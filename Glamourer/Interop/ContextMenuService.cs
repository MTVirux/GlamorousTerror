using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Glamourer.Designs;
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
    private readonly Configuration      _config;
    private          EquipItem          _lastItem;
    private readonly StainId[]          _lastStains = new StainId[StainId.NumStains];

    // For character import
    private Actor  _lastActor;
    private string _lastCharacterName = string.Empty;

    private readonly MenuItem _inventoryItem;
    private readonly MenuItem _importCharacterItem;

    public ContextMenuService(ItemManager items, StateManager state, ActorObjectManager objects, Configuration config,
        IContextMenu context, DesignManager designManager, DesignConverter designConverter)
    {
        _contextMenu     = context;
        _items           = items;
        _state           = state;
        _objects         = objects;
        _designManager   = designManager;
        _designConverter = designConverter;
        _config          = config;
        if (config.EnableGameContextMenu || config.EnableImportCharacterContextMenu)
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

        _importCharacterItem = new MenuItem
        {
            IsEnabled   = true,
            IsReturn    = false,
            PrefixChar  = 'G',
            Name        = "Import Design to Glamorous",
            OnClicked   = OnImportCharacterClick,
            IsSubmenu   = false,
            PrefixColor = 541,
        };
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
            // Handle character/player context menus for import
            if (_config.EnableImportCharacterContextMenu)
            {
                var target = (MenuTargetDefault)args.Target;
                if (target.TargetObject != null && HandleCharacter(target))
                    args.AddMenuItem(_importCharacterItem);
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

    private void OnImportCharacterClick(IMenuItemClickedArgs _)
    {
        if (!_lastActor.Valid || string.IsNullOrEmpty(_lastCharacterName))
            return;

        try
        {
            // Get the design data from the actor
            var designData = _state.FromActor(_lastActor, true, false);

            // Create a design from the actor data using the converter with ApplicationRules.All
            var tempDesign = _designConverter.Convert(designData, new StateMaterialManager(), ApplicationRules.All);

            // Create a new design with the character's name
            _designManager.CreateClone(tempDesign, _lastCharacterName, true);

            Glamourer.Log.Information($"Imported design from character: {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to import character design: {ex}");
        }
    }

    private bool HandleCharacter(MenuTargetDefault target)
    {
        // Check if we have a valid game object that is a character
        var gameObject = target.TargetObject;
        if (gameObject == null)
            return false;

        // Create an Actor from the game object address
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
}
