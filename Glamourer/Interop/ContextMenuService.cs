using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Glamourer.Config;
using Glamourer.Designs;
using Glamourer.Gui;
using Glamourer.Services;
using Glamourer.State;
using Luna;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Structs;

namespace Glamourer.Interop;

public sealed class ContextMenuService : IDisposable, IRequiredService
{
    public const int ChatLogContextItemId = 0x958;

    private readonly ItemManager              _items;
    private readonly IContextMenu             _contextMenu;
    private readonly StateManager             _state;
    private readonly ActorObjectManager       _objects;
    private readonly CharacterPopupMenu        _popupMenu;
    private readonly ImmersiveDresserManager   _immersiveDresser;
    private readonly Configuration             _config;
    private          EquipItem                _lastItem;
    private readonly StainId[]                _lastStains = new StainId[StainId.NumStains];
    private          Actor                    _lastCharacterActor;
    private          string                   _lastCharacterName = string.Empty;

    private readonly MenuItem _inventoryItem;
    private readonly MenuItem _characterItem;
    private readonly MenuItem _immersiveDresserItem;

    public ContextMenuService(ItemManager items, StateManager state, ActorObjectManager objects, Configuration config,
        IContextMenu context, CharacterPopupMenu popupMenu, ImmersiveDresserManager immersiveDresser)
    {
        _contextMenu      = context;
        _items            = items;
        _state            = state;
        _objects          = objects;
        _popupMenu        = popupMenu;
        _immersiveDresser = immersiveDresser;
        _config           = config;
        if (config.EnableGameContextMenu)
            Enable();

        _inventoryItem = new MenuItem
        {
            IsEnabled   = true,
            IsReturn    = false,
            PrefixChar  = 'G',
            Name        = "Try On",
            OnClicked   = OnClick,
            IsSubmenu   = false,
            PrefixColor = 541,
        };
        _characterItem = new MenuItem
        {
            IsEnabled   = true,
            IsReturn    = false,
            PrefixChar  = 'G',
            Name        = "Glamorous Terror",
            OnClicked   = OnCharacterClick,
            IsSubmenu   = false,
            PrefixColor = 541,
        };
        _immersiveDresserItem = new MenuItem
        {
            IsEnabled   = true,
            IsReturn    = false,
            PrefixChar  = 'G',
            Name        = "Immersive Dresser",
            OnClicked   = OnImmersiveDresserClick,
            IsSubmenu   = false,
            PrefixColor = 541,
        };
    }

    private unsafe void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType is ContextMenuType.Inventory)
        {
            var arg = (MenuTargetInventory)args.Target;
            if (arg.TargetItem.HasValue && HandleItem(arg.TargetItem.Value.ItemId))
            {
                for (var i = 0; i < arg.TargetItem.Value.Stains.Length; ++i)
                    _lastStains[i] = arg.TargetItem.Value.Stains[i];
                args.AddMenuItem(_inventoryItem);
            }
        }
        else
        {
            var target = (MenuTargetDefault)args.Target;

            // Character context menu: show "Glamorous Terror" entry when targeting a player character.
            if (target.TargetObjectId != 0 && target.TargetObject is { } gameObject && gameObject.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Player)
            {
                _lastCharacterActor   = (nint)gameObject.Address;
                _lastCharacterName    = target.TargetName;
                args.AddMenuItem(_characterItem);

                // Immersive Dresser: only for the local player.
                if (_config.EnableImmersiveDresser && _objects.Player.Valid && (nint)gameObject.Address == (nint)_objects.Player)
                    args.AddMenuItem(_immersiveDresserItem);
            }

            switch (args.AddonName)
            {
                case "ItemSearch" when args.AgentPtr != nint.Zero:
                {
                    if (HandleItem((ItemId)AgentContext.Instance()->UpdateCheckerParam))
                        args.AddMenuItem(_inventoryItem);

                    break;
                }
                case "ChatLog":
                {
                    var agent = AgentChatLog.Instance();
                    if (agent == null || !ValidateChatLogContext(agent))
                        return;

                    if (HandleItem(*(ItemId*)(agent + ChatLogContextItemId)))
                    {
                        for (var i = 0; i < _lastStains.Length; ++i)
                            _lastStains[i] = 0;
                        args.AddMenuItem(_inventoryItem);
                    }

                    break;
                }
                case "RecipeNote":
                {
                    var agent = AgentRecipeNote.Instance();
                    if (agent == null)
                        return;

                    if (HandleItem(agent->ContextMenuResultItemId))
                    {
                        for (var i = 0; i < _lastStains.Length; ++i)
                            _lastStains[i] = 0;
                        args.AddMenuItem(_inventoryItem);
                    }

                    break;
                }
                case "InclusionShop":
                {
                    var agent = AgentRecipeItemContext.Instance();
                    if (agent == null)
                        return;

                    if (HandleItem(agent->ResultItemId))
                    {
                        for (var i = 0; i < _lastStains.Length; ++i)
                            _lastStains[i] = 0;
                        args.AddMenuItem(_inventoryItem);
                    }

                    break;
                }
            }
        }
    }

    public void Enable()
        => _contextMenu.OnMenuOpened += OnMenuOpened;

    public void Disable()
        => _contextMenu.OnMenuOpened -= OnMenuOpened;

    public void Dispose()
        => Disable();

    private void OnCharacterClick(IMenuItemClickedArgs _)
        => _popupMenu.Open(_lastCharacterActor, _lastCharacterName);

    private void OnImmersiveDresserClick(IMenuItemClickedArgs _)
        => _immersiveDresser.Open();

    private void OnClick(IMenuItemClickedArgs _)
    {
        var (id, playerData) = _objects.PlayerData;
        if (!playerData.Valid)
            return;

        if (!_state.GetOrCreate(id, playerData.Objects[0], out var state))
            return;

        var slot = _lastItem.Type.ToSlot();
        _state.ChangeEquip(state, slot, _lastItem, _lastStains[0], ApplySettings.Manual);
        if (!_lastItem.Type.ValidOffhand().IsOffhandType())
            return;

        if (_lastItem.PrimaryId.Id is > 1600 and < 1651
         && _items.ItemData.TryGetValue(_lastItem.ItemId, EquipSlot.Hands, out var gauntlets))
            _state.ChangeEquip(state, EquipSlot.Hands, gauntlets, _lastStains[0], ApplySettings.Manual);
        if (_items.ItemData.TryGetValue(_lastItem.ItemId, EquipSlot.OffHand, out var offhand))
            _state.ChangeEquip(state, EquipSlot.OffHand, offhand, _lastStains[0], ApplySettings.Manual);
    }

    private bool HandleItem(ItemId id)
    {
        var itemId = id.StripModifiers;
        return _items.ItemData.TryGetValue(itemId, EquipSlot.MainHand, out _lastItem);
    }

    private static unsafe bool ValidateChatLogContext(AgentChatLog* agent)
        => *(&agent->ContextItemId + 8) == 3;
}
