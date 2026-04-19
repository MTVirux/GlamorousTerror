using Glamourer.Config;
using Glamourer.Services;
using Glamourer.Unlocks;
using ImSharp;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Equipment;

public abstract partial class BaseItemCombo : FilterComboBase<BaseItemCombo.CacheItem>, IDisposable
{
    private readonly Im.StyleDisposable _style = new();
    public abstract  StringU8           Label { get; }

    protected readonly FavoriteManager Favorites;
    protected readonly ItemManager     Items;
    protected readonly Configuration   Config;
    protected          EquipItem       CurrentItem;
    protected          PrimaryId       CustomSetId;
    protected          SecondaryId     CustomWeaponId;
    protected          Variant         CustomVariant;

    protected BaseItemCombo(FavoriteManager favorites, ItemManager items, Configuration config, ItemNameService itemNameService, ItemUnlockManager itemUnlockManager)
        : base(new ItemFilter(itemNameService, config, itemUnlockManager), ConfigData.Default with
        {
            ComputeWidth = true,
            ClearFilterOnSelection = !config.KeepItemComboFilter,
            ClearFilterOnCacheDisposal = false,
            DirtyCacheOnClose = true,
        })
    {
        Favorites = favorites;
        Items     = items;
        Config    = config;

        Config.KeepItemComboFilterChanged += OnKeepItemComboFilterChanged;
    }

    public void Dispose()
        => Config.KeepItemComboFilterChanged -= OnKeepItemComboFilterChanged;

    public EquipItem? HoveredItem   { get; private set; }
    public bool       IsPopupOpen   { get; private set; }
    public bool       ItemSelected  { get; private set; }

    public void ResetSelection()
        => ItemSelected = false;

    public bool Draw(in EquipItem item, out EquipItem newItem, float width)
    {
        IsPopupOpen = false;
        HoveredItem = null;

        using var id = Im.Id.Push(Label);
        CurrentItem   = item;
        CustomVariant = 0;
        if (Draw(StringU8.Empty, item.Name, StringU8.Empty, width, out var cache))
        {
            newItem      = cache.Item;
            ItemSelected = true;
            return true;
        }

        if (CustomVariant.Id is not 0 && Identify(out newItem))
        {
            ItemSelected = true;
            return true;
        }

        newItem = item;
        return false;
    }

    protected override void PreDrawList()
    {
        IsPopupOpen = true;
        _style.PushY(ImStyleDouble.ItemSpacing, 0)
            .PushY(ImStyleDouble.SelectableTextAlign, 0.5f);
    }

    protected override void PostDrawList()
        => _style.Dispose();

    public readonly struct CacheItem(EquipItem item)
    {
        public readonly EquipItem  Item  = item;
        public readonly StringPair Name  = new(item.Name);
        public readonly StringPair Model = new($"({item.PrimaryId.Id}-{item.Variant.Id})");
    }

    protected sealed partial class ItemFilter(ItemNameService itemNameService, Configuration config, ItemUnlockManager itemUnlockManager) : PartwiseFilterBase<CacheItem>
    {
        // GT partial method declarations (implementations in GlamorousTerror/ partial files)
        private partial bool GTPreFilterItem(in CacheItem item);
        private partial bool GTFallbackNameMatch(in CacheItem item);

        public override bool WouldBeVisible(in CacheItem item, int globalIndex)
        {
            if (!GTPreFilterItem(in item))
                return false;

            return base.WouldBeVisible(in item, globalIndex) || WouldBeVisible(item.Model.Utf16) || GTFallbackNameMatch(in item);
        }

        protected override string ToFilterString(in CacheItem item, int globalIndex)
            => item.Name.Utf16;
    }

    protected override FilterComboBaseCache<CacheItem> CreateCache()
        => new Cache(this);

    protected sealed class Cache(FilterComboBase<CacheItem> parent) : FilterComboBaseCache<CacheItem>(parent)
    {
        private static EquipItem _longestItem;

        protected override void ComputeWidth()
        {
            if (!_longestItem.Valid)
            {
                var data = ((BaseItemCombo)Parent).Items.ItemData;
                _longestItem = data.AllItems(true).Concat(data.AllItems(false))
                    .MaxBy(i => Im.Font.CalculateSize($"{i.Item2.Name} ({i.Item2.ModelString})").X).Item2;
            }

            ComboWidth = Im.Font.CalculateSize($"{_longestItem.Name} ({_longestItem.ModelString})").X
              + Im.Style.FrameHeight
              + Im.Style.ItemSpacing.X * 3;
        }
    }

    protected override float ItemHeight
        => Im.Style.FrameHeight;

    protected override bool DrawItem(in CacheItem item, int globalIndex, bool selected)
    {
        UiHelpers.DrawFavoriteStar(Favorites, item.Item);
        Im.Line.Same();
        Im.Cursor.Y -= Im.Style.FramePadding.Y;
        var ret = Im.Selectable(item.Name.Utf8, selected, SelectableFlags.None, new Vector2(0, Im.Style.FrameHeight));
        if (Im.Item.Hovered())
            HoveredItem = item.Item;
        Im.Line.Same();
        using var color = ImGuiColor.Text.Push(Rgba32.Gray);
        ImEx.TextRightAligned(item.Model.Utf8);
        return ret;
    }

    protected override void EnterPressed()
    {
        if (!Im.Io.KeyControl)
            return;

        var split = ((ItemFilter)Filter).Text.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        switch (split.Length)
        {
            case 2 when ushort.TryParse(split[0], out var setId) && byte.TryParse(split[1], out var variant):
                CustomSetId   = setId;
                CustomVariant = variant;
                break;
            case 3 when ushort.TryParse(split[0], out var setId)
             && ushort.TryParse(split[1],         out var weaponId)
             && byte.TryParse(split[2], out var variant):
                CustomSetId    = setId;
                CustomWeaponId = weaponId;
                CustomVariant  = variant;
                break;
            default: return;
        }
    }

    protected abstract bool Identify(out EquipItem item);

    protected override bool IsSelected(CacheItem item, int globalIndex)
        => item.Item.Id == CurrentItem.Id;

    private void OnKeepItemComboFilterChanged(bool newValue, bool _)
        => ClearFilterOnSelection = !newValue;
}
