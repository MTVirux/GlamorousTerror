using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Glamourer.Designs;
using Glamourer.Gui;
using Glamourer.Interop.Material;
using Glamourer.Services;
using Glamourer.State;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Structs;

namespace Glamourer.Interop;

public class ContextMenuService : IDisposable
{
    public const int ChatLogContextItemId = 0x958;

    private readonly ItemManager        _items;
    private readonly IContextMenu       _contextMenu;
    private readonly StateManager       _state;
    private readonly ActorObjectManager _objects;
    private readonly DesignManager      _designManager;
    private readonly DesignConverter    _designConverter;
    private readonly QuickDesignCombo   _quickDesignCombo;
    private readonly Configuration      _config;
    private          EquipItem          _lastItem;
    private readonly StainId[]          _lastStains = new StainId[StainId.NumStains];

    // For character context menu
    private Actor  _lastActor;
    private string _lastCharacterName = string.Empty;

    // Inventory item menu
    private readonly MenuItem _inventoryItem;

    // Character submenu and items
    private readonly MenuItem _glamorousTerrorSubmenu;
    private readonly MenuItem _importAsDesign;
    private readonly MenuItem _applyEquipmentToSelf;
    private readonly MenuItem _applyAppearanceToSelf;
    private readonly MenuItem _applyGearToTarget;
    private readonly MenuItem _applyAppearanceToTarget;
    private readonly MenuItem _applyQuickDesignToTarget;

    public ContextMenuService(ItemManager items, StateManager state, ActorObjectManager objects, Configuration config,
        IContextMenu context, DesignManager designManager, DesignConverter designConverter, QuickDesignCombo quickDesignCombo)
    {
        _contextMenu      = context;
        _items            = items;
        _state            = state;
        _objects          = objects;
        _designManager    = designManager;
        _designConverter  = designConverter;
        _quickDesignCombo = quickDesignCombo;
        _config           = config;

        if (config.EnableGameContextMenu || config.EnableImportCharacterContextMenu)
            Enable();

        // Inventory item menu
        _inventoryItem = new MenuItem
        {
            IsEnabled   = true,
            IsReturn    = false,
            PrefixChar  = 'G',
            Name        = "Try On",
            OnClicked   = OnInventoryItemClick,
            IsSubmenu   = false,
            PrefixColor = 541,
        };

        // Character submenu items
        _importAsDesign = new MenuItem
        {
            IsEnabled  = true,
            IsReturn   = false,
            Name       = "Import as Design",
            OnClicked  = OnImportAsDesignClick,
            IsSubmenu  = false,
        };

        _applyEquipmentToSelf = new MenuItem
        {
            IsEnabled  = true,
            IsReturn   = false,
            Name       = "Apply Equipment to Self",
            OnClicked  = OnApplyEquipmentToSelfClick,
            IsSubmenu  = false,
        };

        _applyAppearanceToSelf = new MenuItem
        {
            IsEnabled  = true,
            IsReturn   = false,
            Name       = "Apply Appearance to Self",
            OnClicked  = OnApplyAppearanceToSelfClick,
            IsSubmenu  = false,
        };

        _applyGearToTarget = new MenuItem
        {
            IsEnabled  = true,
            IsReturn   = false,
            Name       = "Apply Current Gear to Target",
            OnClicked  = OnApplyGearToTargetClick,
            IsSubmenu  = false,
        };

        _applyAppearanceToTarget = new MenuItem
        {
            IsEnabled  = true,
            IsReturn   = false,
            Name       = "Apply Current Appearance to Target",
            OnClicked  = OnApplyAppearanceToTargetClick,
            IsSubmenu  = false,
        };

        _applyQuickDesignToTarget = new MenuItem
        {
            IsEnabled  = true,
            IsReturn   = false,
            Name       = "Apply Selected Quick Design to Target",
            OnClicked  = OnApplyQuickDesignToTargetClick,
            IsSubmenu  = false,
        };

        // Main submenu
        _glamorousTerrorSubmenu = new MenuItem
        {
            IsEnabled   = true,
            IsReturn    = false,
            PrefixChar  = 'G',
            Name        = "Glamorous Terror",
            IsSubmenu   = true,
            PrefixColor = 541,
            OnClicked   = OnGlamorousTerrorSubmenuClick,
        };
    }

    private void OnGlamorousTerrorSubmenuClick(IMenuItemClickedArgs args)
    {
        args.OpenSubmenu([
            _importAsDesign,
            _applyEquipmentToSelf,
            _applyAppearanceToSelf,
            _applyGearToTarget,
            _applyAppearanceToTarget,
            _applyQuickDesignToTarget,
        ]);
    }

    private unsafe void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType is ContextMenuType.Inventory)
        {
            if (!_config.EnableGameContextMenu)
                return;

            var arg = (MenuTargetInventory)args.Target;
            if (arg.TargetItem.HasValue && HandleItem(arg.TargetItem.Value.ItemId))
            {
                for (var i = 0; i < arg.TargetItem.Value.Stains.Length; ++i)
                    _lastStains[i] = arg.TargetItem.Value.Stains[i];
                args.AddMenuItem(_inventoryItem);
            }
        }
        else if (args.MenuType is ContextMenuType.Default)
        {
            // Handle character/player context menus
            if (_config.EnableImportCharacterContextMenu)
            {
                var target = (MenuTargetDefault)args.Target;
                if (target.TargetObject != null && HandleCharacter(target))
                    args.AddMenuItem(_glamorousTerrorSubmenu);
            }

            // Handle item-related addons
            if (_config.EnableGameContextMenu)
                HandleItemAddons(args);
        }
    }

    private unsafe void HandleItemAddons(IMenuOpenedArgs args)
    {
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

    public void Enable()
        => _contextMenu.OnMenuOpened += OnMenuOpened;

    public void Disable()
        => _contextMenu.OnMenuOpened -= OnMenuOpened;

    public void Dispose()
        => Disable();

    #region Click Handlers

    private void OnInventoryItemClick(IMenuItemClickedArgs _)
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

    private void OnImportAsDesignClick(IMenuItemClickedArgs _)
    {
        if (!_lastActor.Valid || string.IsNullOrEmpty(_lastCharacterName))
            return;

        try
        {
            var designData = _state.FromActor(_lastActor, true, false);
            var tempDesign = _designConverter.Convert(designData, new StateMaterialManager(), ApplicationRules.All);
            _designManager.CreateClone(tempDesign, _lastCharacterName, true);
            Glamourer.Log.Information($"Imported design from character: {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to import character design: {ex}");
        }
    }

    private void OnApplyEquipmentToSelfClick(IMenuItemClickedArgs _)
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid)
                return;

            if (!_state.GetOrCreate(playerId, playerData.Objects[0], out var playerState))
                return;

            var designData = _state.FromActor(_lastActor, true, false);
            var tempDesign = _designConverter.Convert(designData, new StateMaterialManager(),
                new ApplicationRules(ApplicationCollection.Equipment, false));

            _state.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied equipment from {_lastCharacterName} to self");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply equipment to self: {ex}");
        }
    }

    private void OnApplyAppearanceToSelfClick(IMenuItemClickedArgs _)
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid)
                return;

            if (!_state.GetOrCreate(playerId, playerData.Objects[0], out var playerState))
                return;

            var designData = _state.FromActor(_lastActor, true, false);
            var tempDesign = _designConverter.Convert(designData, new StateMaterialManager(),
                new ApplicationRules(ApplicationCollection.Customizations, false));

            _state.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied appearance from {_lastCharacterName} to self");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply appearance to self: {ex}");
        }
    }

    private void OnApplyGearToTargetClick(IMenuItemClickedArgs _)
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            // Get player's current state
            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid)
                return;

            if (!_state.GetOrCreate(playerId, playerData.Objects[0], out var playerState))
                return;

            // Get or create target state
            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            // Convert player's equipment to a design and apply to target
            var tempDesign = _designConverter.Convert(playerState.ModelData, playerState.Materials,
                new ApplicationRules(ApplicationCollection.Equipment, false));

            _state.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied current gear to {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply gear to target: {ex}");
        }
    }

    private void OnApplyAppearanceToTargetClick(IMenuItemClickedArgs _)
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            // Get player's current state
            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid)
                return;

            if (!_state.GetOrCreate(playerId, playerData.Objects[0], out var playerState))
                return;

            // Get or create target state
            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            // Convert player's appearance to a design and apply to target
            var tempDesign = _designConverter.Convert(playerState.ModelData, playerState.Materials,
                new ApplicationRules(ApplicationCollection.Customizations, false));

            _state.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied current appearance to {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply appearance to target: {ex}");
        }
    }

    private void OnApplyQuickDesignToTargetClick(IMenuItemClickedArgs _)
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            var selectedDesign = _quickDesignCombo.Design as Design;
            if (selectedDesign == null)
            {
                Glamourer.Log.Warning("No quick design selected");
                return;
            }

            // Get or create target state
            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            _state.ApplyDesign(targetState, selectedDesign, ApplySettings.ManualWithLinks with { IsFinal = true });
            Glamourer.Log.Information($"Applied quick design {selectedDesign.ResolveName(true)} to {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply quick design to target: {ex}");
        }
    }

    #endregion

    #region Helpers

    private bool HandleCharacter(MenuTargetDefault target)
    {
        var gameObject = target.TargetObject;
        if (gameObject == null)
            return false;

        Actor actor = gameObject.Address;
        if (!actor.Valid || !actor.IsCharacter)
            return false;

        _lastActor         = actor;
        _lastCharacterName = target.TargetName;
        return true;
    }

    private bool HandleItem(ItemId id)
    {
        var itemId = id.StripModifiers;
        return _items.ItemData.TryGetValue(itemId, EquipSlot.MainHand, out _lastItem);
    }

    private static unsafe bool ValidateChatLogContext(AgentChatLog* agent)
        => *(&agent->ContextItemId + 8) == 3;

    #endregion
}
