using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Glamourer.Events;
using Glamourer.Services;
using Lumina.Excel.Sheets;
using Luna;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Cabinet = Lumina.Excel.Sheets.Cabinet;

namespace Glamourer.Unlocks;

public sealed class ItemUnlockManager : ISavable, IDisposable, IReadOnlyDictionary<ItemId, long>, IService
{
    private readonly SaveService          _saveService;
    private readonly ItemManager          _items;
    private readonly IClientState         _clientState;
    private readonly IPlayerState         _playerState;
    private readonly IFramework           _framework;
    private readonly ObjectUnlocked       _event;
    private readonly ObjectIdentification _identifier;

    private readonly Dictionary<uint, long> _unlocked = new();
    private readonly Dictionary<uint, ItemSource> _sources = new();

    private ulong _currentContentId;
    private bool _lastArmoireState;
    private bool _lastAchievementState;
    private bool _lastGlamourState;
    private bool _lastPlateState;
    private byte _currentInventory;
    private byte _currentInventoryIndex;

    // Pruning state: tracks items seen during the current full inventory scan cycle.
    private readonly Dictionary<uint, ItemSource> _seenThisCycle = new();
    private ItemSource _fullyScannedSources;

    /// <summary> Sources that are pruned when items are no longer detected in them. </summary>
    private const ItemSource PrunableSources = ItemSource.Inventory | ItemSource.Saddlebags | ItemSource.Retainers;

    [Flags]
    public enum UnlockType : byte
    {
        Quest1      = 0x01,
        Quest2      = 0x02,
        Achievement = 0x04,
        Cabinet     = 0x08,
    }

    [Flags]
    public enum ItemSource : byte
    {
        Inventory        = 0x01,
        GlamourDresser   = 0x02,
        Armoire          = 0x04,
        Saddlebags       = 0x08,
        Retainers        = 0x10,
        QuestAchievement = 0x20,
        All              = Inventory | GlamourDresser | Armoire | Saddlebags | Retainers | QuestAchievement,
    }

    public readonly IReadOnlyDictionary<ItemId, UnlockRequirements> Unlockable;

    public ItemUnlockManager(SaveService saveService, ItemManager items, IClientState clientState, IPlayerState playerState, IDataManager gameData, IFramework framework,
        ObjectUnlocked @event, ObjectIdentification identifier, IGameInteropProvider interop)
    {
        interop.InitializeFromAttributes(this);
        _saveService = saveService;
        _items       = items;
        _clientState = clientState;
        _playerState = playerState;
        _framework   = framework;
        _event       = @event;
        _identifier  = identifier;
        Unlockable   = CreateUnlockData(gameData, items);
        _clientState.Login  += OnLogin;
        _clientState.Logout += OnLogout;
        _framework.Update   += OnFramework;

        // Handle plugin reload while already logged in.
        if (_playerState.ContentId != 0)
            OnLogin();
    }

    private void OnLogin()
    {
        _currentContentId = _playerState.ContentId;
        _unlocked.Clear();
        _sources.Clear();
        ResetScanState();
        Load();
        Scan();
    }

    private void OnLogout(int type, int code)
    {
        Save();
        _unlocked.Clear();
        _sources.Clear();
        ResetScanState();
        _currentContentId = 0;
    }

    private void ResetScanState()
    {
        _currentInventory      = 0;
        _currentInventoryIndex = 0;
        _lastArmoireState      = false;
        _lastAchievementState  = false;
        _lastGlamourState      = false;
        _lastPlateState        = false;
        _seenThisCycle.Clear();
        _fullyScannedSources   = 0;
    }

    //private Achievement.AchievementState _achievementState = Achievement.AchievementState.Invalid;

    private static readonly InventoryType[] ScannableInventories =
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.EquippedItems,
        InventoryType.Mail,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings,
        InventoryType.ArmoryMainHand,
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
        InventoryType.RetainerEquippedItems,
        InventoryType.RetainerMarket,
    };

    private static ItemSource GetInventorySource(InventoryType type)
        => type switch
        {
            InventoryType.SaddleBag1        => ItemSource.Saddlebags,
            InventoryType.SaddleBag2        => ItemSource.Saddlebags,
            InventoryType.PremiumSaddleBag1 => ItemSource.Saddlebags,
            InventoryType.PremiumSaddleBag2 => ItemSource.Saddlebags,
            InventoryType.RetainerPage1          => ItemSource.Retainers,
            InventoryType.RetainerPage2          => ItemSource.Retainers,
            InventoryType.RetainerPage3          => ItemSource.Retainers,
            InventoryType.RetainerPage4          => ItemSource.Retainers,
            InventoryType.RetainerPage5          => ItemSource.Retainers,
            InventoryType.RetainerPage6          => ItemSource.Retainers,
            InventoryType.RetainerPage7          => ItemSource.Retainers,
            InventoryType.RetainerEquippedItems  => ItemSource.Retainers,
            InventoryType.RetainerMarket         => ItemSource.Retainers,
            _ => ItemSource.Inventory,
        };

    private bool AddItem(ItemId itemId, long time, ItemSource source = ItemSource.Inventory)
    {
        itemId = itemId.StripModifiers;
        if (!_items.ItemData.TryGetValue(itemId, EquipSlot.MainHand, out var equip))
            return false;

        var isNew = _unlocked.TryAdd(equip.ItemId.Id, time);

        // Always OR-in the source, even if the item was already unlocked.
        if (_sources.TryGetValue(equip.ItemId.Id, out var existing))
            _sources[equip.ItemId.Id] = existing | source;
        else
            _sources[equip.ItemId.Id] = source;

        if (isNew)
            _event.Invoke(new ObjectUnlocked.Arguments(ObjectUnlocked.Type.Item, equip.ItemId.Id, DateTimeOffset.FromUnixTimeMilliseconds(time)));

        var ident = _identifier.Identify(equip.PrimaryId, equip.SecondaryId, equip.Variant, equip.Type.ToSlot());
        foreach (var item in ident)
        {
            var variantIsNew = _unlocked.TryAdd(item.ItemId.Id, time);

            if (_sources.TryGetValue(item.ItemId.Id, out var variantExisting))
                _sources[item.ItemId.Id] = variantExisting | source;
            else
                _sources[item.ItemId.Id] = source;

            if (variantIsNew)
                _event.Invoke(new ObjectUnlocked.Arguments(ObjectUnlocked.Type.Item, item.ItemId.Id,
                    DateTimeOffset.FromUnixTimeMilliseconds(time)));
        }

        return isNew;
    }

    /// <summary> Record that an item (and its variants) were seen from a given source during this scan cycle. </summary>
    private void MarkSeen(ItemId itemId, ItemSource source)
    {
        itemId = itemId.StripModifiers;
        if (!_items.ItemData.TryGetValue(itemId, EquipSlot.MainHand, out var equip))
            return;

        if (_seenThisCycle.TryGetValue(equip.ItemId.Id, out var existing))
            _seenThisCycle[equip.ItemId.Id] = existing | source;
        else
            _seenThisCycle[equip.ItemId.Id] = source;

        var ident = _identifier.Identify(equip.PrimaryId, equip.SecondaryId, equip.Variant, equip.Type.ToSlot());
        foreach (var item in ident)
        {
            if (_seenThisCycle.TryGetValue(item.ItemId.Id, out var variantExisting))
                _seenThisCycle[item.ItemId.Id] = variantExisting | source;
            else
                _seenThisCycle[item.ItemId.Id] = source;
        }
    }

    private unsafe void OnFramework(IFramework _)
    {
        if (_currentContentId == 0)
            return;

        var uiState = UIState.Instance();
        if (uiState == null)
            return;

        var scan            = false;
        var newArmoireState = uiState->Cabinet.IsCabinetLoaded();
        if (newArmoireState != _lastArmoireState)
        {
            _lastArmoireState =  newArmoireState;
            scan              |= newArmoireState;
        }

        var newAchievementState = uiState->Achievement.IsLoaded();
        if (newAchievementState != _lastAchievementState)
        {
            _lastAchievementState =  newAchievementState;
            scan                  |= newAchievementState;
        }

        if (scan)
            Scan();

        var time          = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var mirageManager = MirageManager.Instance();
        var changes       = false;
        if (mirageManager != null)
        {
            var newGlamourState = mirageManager->PrismBoxLoaded;
            if (newGlamourState != _lastGlamourState)
            {
                _lastGlamourState = newGlamourState;
                if (newGlamourState)
                {
                    // Prune items no longer in the glamour dresser.
                    var currentDresserItems = new HashSet<uint>();
                    var span = mirageManager->PrismBoxItemIds;
                    foreach (var item in span)
                    {
                        var stripped = ((ItemId)item).StripModifiers;
                        if (_items.ItemData.TryGetValue(stripped, EquipSlot.MainHand, out var equip))
                            currentDresserItems.Add(equip.ItemId.Id);
                    }

                    changes |= PruneSource(ItemSource.GlamourDresser, currentDresserItems);

                    foreach (var item in span)
                        changes |= AddItem(item, time, ItemSource.GlamourDresser);
                }
            }

            var newPlateState = mirageManager->GlamourPlatesLoaded;
            if (newPlateState != _lastPlateState)
            {
                _lastPlateState = newPlateState;
                // Plates are additive only — dresser is the authoritative source for the GlamourDresser flag.
                foreach (var plate in mirageManager->GlamourPlates)
                {
                    var span = plate.ItemIds;
                    foreach (var item in span)
                        changes |= AddItem(item, time, ItemSource.GlamourDresser);
                }
            }
        }

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager != null)
        {
            var type      = ScannableInventories[_currentInventory];
            var container = inventoryManager->GetInventoryContainer(type);
            if (container != null && container->IsLoaded && _currentInventoryIndex < container->Size)
            {
                Glamourer.Log.Excessive($"[UnlockScanner] Scanning {_currentInventory} {type} {_currentInventoryIndex}/{container->Size}.");
                var item = container->GetInventorySlot(_currentInventoryIndex++);
                if (item != null)
                {
                    var source = GetInventorySource(type);
                    changes |= AddItem(item->ItemId,    time, source);
                    changes |= AddItem(item->GlamourId, time, source);
                    MarkSeen(item->ItemId,    source);
                    MarkSeen(item->GlamourId, source);
                }
            }
            else
            {
                // Finished scanning this inventory type. If it was loaded, mark its source as fully scanned.
                if (container != null && container->IsLoaded)
                    _fullyScannedSources |= GetInventorySource(type);

                var nextInventory = (byte)(_currentInventory + 1 == ScannableInventories.Length ? 0 : _currentInventory + 1);
                if (nextInventory == 0)
                {
                    // Full cycle complete — prune sources that were fully scanned but items not seen.
                    changes |= PruneInventorySources();
                    _seenThisCycle.Clear();
                    _fullyScannedSources = 0;
                }

                _currentInventory      = nextInventory;
                _currentInventoryIndex = 0;
            }
        }

        if (changes)
            Save();
    }

    /// <summary> Prune inventory sources for items not seen during the current scan cycle. </summary>
    private bool PruneInventorySources()
    {
        var pruneMask = PrunableSources & _fullyScannedSources;
        if (pruneMask == 0)
            return false;

        var changed  = false;
        var toRemove = new List<uint>();
        foreach (var (id, src) in _sources)
        {
            var relevantFlags = src & pruneMask;
            if (relevantFlags == 0)
                continue;

            _seenThisCycle.TryGetValue(id, out var seenFlags);
            var unseenFlags = relevantFlags & ~seenFlags;
            if (unseenFlags == 0)
                continue;

            var newSrc = src & ~unseenFlags;
            if (newSrc == 0)
                toRemove.Add(id);
            else
                _sources[id] = newSrc;

            changed = true;
        }

        foreach (var id in toRemove)
        {
            _sources.Remove(id);
            _unlocked.Remove(id);
        }

        if (changed)
            Glamourer.Log.Debug($"[UnlockScanner] Pruned inventory sources (mask={pruneMask}), removed {toRemove.Count} items entirely.");

        return changed;
    }

    /// <summary> Remove a specific source flag from all items not in the given set. </summary>
    private bool PruneSource(ItemSource flag, HashSet<uint> currentItems)
    {
        var changed  = false;
        var toRemove = new List<uint>();
        foreach (var (id, src) in _sources)
        {
            if ((src & flag) == 0)
                continue;

            if (currentItems.Contains(id))
                continue;

            var newSrc = src & ~flag;
            if (newSrc == 0)
                toRemove.Add(id);
            else
                _sources[id] = newSrc;

            changed = true;
        }

        foreach (var id in toRemove)
        {
            _sources.Remove(id);
            _unlocked.Remove(id);
        }

        if (changed)
            Glamourer.Log.Debug($"[UnlockScanner] Pruned {flag} source, removed {toRemove.Count} items entirely.");

        return changed;
    }

    public bool IsUnlocked(CustomItemId itemId, out DateTimeOffset time)
    {
        // Pseudo items are always unlocked.
        if (itemId.Id >= (uint)_items.ItemSheet.Count)
        {
            time = DateTimeOffset.MinValue;
            return true;
        }

        var id = itemId.Item.Id;
        if (_unlocked.TryGetValue(id, out var t))
        {
            time = DateTimeOffset.FromUnixTimeMilliseconds(t);
            return true;
        }

        if (IsGameUnlocked(id, out var source))
        {
            time = DateTimeOffset.UtcNow;
            if (_unlocked.TryAdd(id, time.ToUnixTimeMilliseconds()))
            {
                _event.Invoke(new ObjectUnlocked.Arguments(ObjectUnlocked.Type.Item, id, time));
                Save();
            }

            if (_sources.TryGetValue(id, out var existing))
                _sources[id] = existing | source;
            else
                _sources[id] = source;

            return true;
        }

        time = DateTimeOffset.MaxValue;
        return false;
    }

    public bool IsGameUnlocked(ItemId itemId)
        => IsGameUnlocked(itemId, out _);

    public bool IsGameUnlocked(ItemId itemId, out ItemSource source)
    {
        if (Unlockable.TryGetValue(itemId, out var req) && req.IsUnlocked(this))
        {
            source = req.Type.HasFlag(UnlockType.Cabinet) ? ItemSource.Armoire : ItemSource.QuestAchievement;
            return true;
        }

        source = default;
        return false;
    }

    /// <summary> Check if an item is owned from any of the specified sources. </summary>
    public bool IsOwnedFromSources(CustomItemId itemId, ItemSource filter)
    {
        // Pseudo items (Nothing, Smallclothes, etc.) are always considered owned.
        // Guard with IsItem so bonus/custom items (whose Id includes high flag bits) don't match.
        if (itemId.IsItem && (itemId.Id is 0 || itemId.Id >= uint.MaxValue - 512))
            return true;

        var id = itemId.Item.Id;
        return _sources.TryGetValue(id, out var src) && (src & filter) != 0;
    }

    /// <summary> Get all sources an item has been seen from. </summary>
    public ItemSource GetSources(CustomItemId itemId)
    {
        var id = itemId.Item.Id;
        return _sources.TryGetValue(id, out var src) ? src : default;
    }

    public void Dispose()
    {
        _clientState.Login  -= OnLogin;
        _clientState.Logout -= OnLogout;
        _framework.Update   -= OnFramework;
    }

    public void Scan()
    {
        var time    = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var changes = false;
        foreach (var (itemId, unlock) in Unlockable)
        {
            if (!unlock.IsUnlocked(this))
                continue;

            var source = unlock.Type.HasFlag(UnlockType.Cabinet) ? ItemSource.Armoire : ItemSource.QuestAchievement;
            if (_unlocked.TryAdd(itemId.Id, time))
            {
                _event.Invoke(new ObjectUnlocked.Arguments(ObjectUnlocked.Type.Item, itemId.Id, DateTimeOffset.FromUnixTimeMilliseconds(time)));
                changes = true;
            }

            // Always OR-in the source even if already unlocked.
            if (_sources.TryGetValue(itemId.Id, out var existing))
                _sources[itemId.Id] = existing | source;
            else
                _sources[itemId.Id] = source;
        }

        // TODO inventories
        if (changes)
            Save();
    }

    public string ToFilePath(FilenameService fileNames)
        => _currentContentId != 0
            ? fileNames.UnlockFileItemsForCharacter(_currentContentId)
            : fileNames.UnlockFileItems;

    public void Save()
        => _saveService.DelaySave(this, TimeSpan.FromSeconds(10));

    public void Save(Stream stream)
    {
        using var writer = new StreamWriter(stream);
        UnlockDictionaryHelpers.Save(writer, _unlocked, _sources);
    }

    private void Load()
    {
        var version = UnlockDictionaryHelpers.Load(ToFilePath(_saveService.FileNames), _unlocked, _sources,
            id => _items.ItemData.TryGetValue(id, EquipSlot.MainHand, out _), "item");
        UpdateModels(version);
    }

    private static Dictionary<ItemId, UnlockRequirements> CreateUnlockData(IDataManager gameData, ItemManager items)
    {
        var ret     = new Dictionary<ItemId, UnlockRequirements>();
        var cabinet = gameData.GetExcelSheet<Cabinet>();
        foreach (var row in cabinet)
        {
            if (items.ItemData.TryGetValue(row.Item.RowId, EquipSlot.MainHand, out var item))
                ret.TryAdd(item.ItemId, new UnlockRequirements(row.RowId, 0, 0, 0, UnlockType.Cabinet));
        }

        var gilShopItem = gameData.GetSubrowExcelSheet<GilShopItem>();
        var gilShop     = gameData.GetExcelSheet<GilShop>();
        foreach (var row in gilShopItem.SelectMany(g => g))
        {
            if (!items.ItemData.TryGetValue(row.Item.RowId, EquipSlot.MainHand, out var item))
                continue;

            var quest1      = row.QuestRequired[0].RowId;
            var quest2      = row.QuestRequired[1].RowId;
            var achievement = row.AchievementRequired.RowId;
            var state       = row.StateRequired;

            if (gilShop.TryGetRow(row.RowId, out var shop) && shop.Quest.RowId != 0)
            {
                if (quest1 == 0)
                    quest1 = shop.Quest.RowId;
                else if (quest2 is 0)
                    quest2 = shop.Quest.RowId;
            }

            var type = (quest1 is not 0 ? UnlockType.Quest1 : 0)
              | (quest2 is not 0 ? UnlockType.Quest2 : 0)
              | (achievement is not 0 ? UnlockType.Achievement : 0);
            ret.TryAdd(item.ItemId, new UnlockRequirements(quest1, quest2, achievement, state, type));
        }

        return ret;
    }

    private void UpdateModels(int version)
    {
        if (version > 1)
            return;

        foreach (var (item, time) in _unlocked.ToArray())
        {
            if (!_items.ItemData.TryGetValue(item, EquipSlot.MainHand, out var equip))
                continue;

            var parentSource = _sources.TryGetValue(item, out var src) ? src : ItemSource.All;
            var ident = _identifier.Identify(equip.PrimaryId, equip.SecondaryId, equip.Variant, equip.Type.ToSlot());
            foreach (var item2 in ident)
            {
                if (_unlocked.TryAdd(item2.ItemId.Id, time))
                    _event.Invoke(new ObjectUnlocked.Arguments(ObjectUnlocked.Type.Item, item2.ItemId.Id, DateTimeOffset.FromUnixTimeMilliseconds(time)));

                if (_sources.TryGetValue(item2.ItemId.Id, out var existingSrc))
                    _sources[item2.ItemId.Id] = existingSrc | parentSource;
                else
                    _sources[item2.ItemId.Id] = parentSource;
            }
        }
    }

    public IEnumerator<KeyValuePair<ItemId, long>> GetEnumerator()
        => _unlocked.Select(kvp => new KeyValuePair<ItemId, long>(kvp.Key, kvp.Value)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    public int Count
        => _unlocked.Count;

    public bool ContainsKey(ItemId key)
        => _unlocked.ContainsKey(key.Id);

    public bool TryGetValue(ItemId key, out long value)
        => _unlocked.TryGetValue(key.Id, out value);

    public long this[ItemId key]
        => _unlocked[key.Id];

    public IEnumerable<ItemId> Keys
        => _unlocked.Keys.Select(i => (ItemId)i);

    public IEnumerable<long> Values
        => _unlocked.Values;
}
