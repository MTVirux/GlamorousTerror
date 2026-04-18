using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Glamourer.Config;
using Glamourer.Designs;
using Glamourer.Gui.Equipment;
using Glamourer.State;
using ImSharp;
using Luna;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;

namespace Glamourer.Gui;

public sealed class ImmersiveDresserManager : IDisposable, IService
{
    private readonly IUiBuilder      _uiBuilder;
    private readonly IKeyState        _keyState;
    private readonly IFramework       _framework;
    private readonly Configuration   _config;

    public readonly  EquipmentPanel  Left;
    public readonly  AccessoryPanel  Right;
    public readonly  OptionsPanel    Options;

    private bool _wasUiVisible = true;
    private bool _savedDisableUserUiHide;
    private bool _isOpen;

    public ImmersiveDresserManager(EquipmentDrawer equipmentDrawer, StateManager stateManager,
        ActorObjectManager objects, Configuration config, IUiBuilder uiBuilder, IKeyState keyState, IFramework framework)
    {
        _uiBuilder = uiBuilder;
        _keyState  = keyState;
        _framework = framework;
        _config    = config;
        Left       = new EquipmentPanel(this, equipmentDrawer, stateManager, objects);
        Right      = new AccessoryPanel(this, equipmentDrawer, stateManager, objects);
        Options    = new OptionsPanel(this, equipmentDrawer, stateManager, objects, config);
    }

    public void Open()
    {
        if (_isOpen)
            return;

        _isOpen                      = true;
        _savedDisableUserUiHide      = _uiBuilder.DisableUserUiHide;
        _uiBuilder.DisableUserUiHide = true;
        _framework.Update           += OnFrameworkUpdate;
        HideGameUi();
        Left.IsOpen    = true;
        Right.IsOpen   = true;
        Options.IsOpen = true;
    }

    public void Close()
    {
        if (!_isOpen)
            return;

        _isOpen        = false;
        _framework.Update -= OnFrameworkUpdate;
        Left.IsOpen    = false;
        Right.IsOpen   = false;
        Options.IsOpen = false;
        RestoreGameUi();
        _uiBuilder.DisableUserUiHide = _savedDisableUserUiHide;
    }

    public void Dispose()
    {
        if (_isOpen)
            Close();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (_isOpen && _keyState[VirtualKey.ESCAPE])
        {
            _keyState[VirtualKey.ESCAPE] = false;
            Close();
        }
    }

    private unsafe void HideGameUi()
    {
        var module = RaptureAtkModule.Instance();
        if (module != null)
        {
            _wasUiVisible        = module->IsUiVisible;
            module->IsUiVisible = false;
        }
    }

    private unsafe void RestoreGameUi()
    {
        var module = RaptureAtkModule.Instance();
        if (module != null)
            module->IsUiVisible = _wasUiVisible;
    }

    private static WindowFlags PanelFlags
        => WindowFlags.NoTitleBar | WindowFlags.NoDocking | WindowFlags.AlwaysAutoResize | WindowFlags.NoCollapse;

    /// <summary> Left panel: MainHand, Head, Body, Hands, Legs, Feet. </summary>
    public sealed class EquipmentPanel(
        ImmersiveDresserManager manager,
        EquipmentDrawer equipmentDrawer,
        StateManager stateManager,
        ActorObjectManager objects)
        : Window("Equipment###ImmersiveDresserLeft", PanelFlags)
    {
        public override bool DrawConditions()
            => objects.Player.Valid;

        public override void PreDraw()
        {
            var center = Im.Viewport.Main.Center;
            Im.Window.SetNextPosition(center - new Vector2(Im.Style.ItemSpacing.X * 0.5f, 0),
                Condition.FirstUseEver, new Vector2(1f, 0.5f));
            Im.Window.SetNextSize(Vector2.Zero, Condition.Always);
            Flags               = PanelFlags;
            if (manager._config.LockImmersiveDresserPanels)
                Flags |= WindowFlags.NoMove;
            RespectCloseHotkey  = true;
            DisableWindowSounds = true;
        }

        public override void Draw()
        {
            var (id, playerData) = objects.PlayerData;
            if (!playerData.Valid)
                return;

            if (!stateManager.GetOrCreate(id, playerData.Objects[0], out var state))
                return;

            equipmentDrawer.Prepare();

            var mainhand = EquipDrawData.FromState(stateManager, state, EquipSlot.MainHand);
            var offhand  = EquipDrawData.FromState(stateManager, state, EquipSlot.OffHand);

            equipmentDrawer.DrawSingleWeaponIcon(ref mainhand, ref offhand, false, true);
            foreach (var slot in EquipSlotExtensions.EquipmentSlots)
            {
                var data = EquipDrawData.FromState(stateManager, state, slot);
                using var slotId    = Im.Id.Push((int)slot);
                using var slotStyle = ImStyleDouble.ItemSpacing.PushX(Im.Style.ItemInnerSpacing.X);
                equipmentDrawer.DrawEquipIcon(data);
            }

            foreach (var slot in BonusExtensions.AllFlags)
            {
                var data = BonusDrawData.FromState(stateManager, state, slot);
                using var slotId    = Im.Id.Push(100 + (int)slot);
                using var slotStyle = ImStyleDouble.ItemSpacing.PushX(Im.Style.ItemInnerSpacing.X);
                equipmentDrawer.DrawBonusItemIcon(data);
            }

            equipmentDrawer.ApplyHoverPreview(stateManager, state);
        }

        public override void OnClose()
        {
            manager.Close();
            base.OnClose();
        }
    }

    /// <summary> Right panel: OffHand, Ears, Neck, Wrists, RFinger, LFinger, Bonus items. </summary>
    public sealed class AccessoryPanel(
        ImmersiveDresserManager manager,
        EquipmentDrawer equipmentDrawer,
        StateManager stateManager,
        ActorObjectManager objects)
        : Window("Accessories###ImmersiveDresserRight", PanelFlags)
    {
        public override bool DrawConditions()
            => objects.Player.Valid;

        public override void PreDraw()
        {
            var center = Im.Viewport.Main.Center;
            Im.Window.SetNextPosition(center + new Vector2(Im.Style.ItemSpacing.X * 0.5f, 0),
                Condition.FirstUseEver, new Vector2(0f, 0.5f));
            Im.Window.SetNextSize(Vector2.Zero, Condition.Always);
            Flags               = PanelFlags;
            if (manager._config.LockImmersiveDresserPanels)
                Flags |= WindowFlags.NoMove;
            RespectCloseHotkey  = true;
            DisableWindowSounds = true;
        }

        public override void Draw()
        {
            var (id, playerData) = objects.PlayerData;
            if (!playerData.Valid)
                return;

            if (!stateManager.GetOrCreate(id, playerData.Objects[0], out var state))
                return;

            equipmentDrawer.Prepare();

            var mainhand = EquipDrawData.FromState(stateManager, state, EquipSlot.MainHand);
            var offhand  = EquipDrawData.FromState(stateManager, state, EquipSlot.OffHand);

            var hasOffhand = offhand.CurrentItem.Type is not FullEquipType.Unknown;
            if (hasOffhand)
                equipmentDrawer.DrawSingleWeaponIcon(ref mainhand, ref offhand, false, false);

            foreach (var slot in EquipSlotExtensions.AccessorySlots)
            {
                var data = EquipDrawData.FromState(stateManager, state, slot);
                using var slotId    = Im.Id.Push((int)slot);
                using var slotStyle = ImStyleDouble.ItemSpacing.PushX(Im.Style.ItemInnerSpacing.X);
                equipmentDrawer.DrawEquipIcon(data);
            }

            equipmentDrawer.ApplyHoverPreview(stateManager, state);
        }

        public override void OnClose()
        {
            manager.Close();
            base.OnClose();
        }
    }

    /// <summary> Options panel: Meta toggles (hat, visor, weapon, ears, crests), dye all, source filter, close button. </summary>
    public sealed class OptionsPanel(
        ImmersiveDresserManager manager,
        EquipmentDrawer equipmentDrawer,
        StateManager stateManager,
        ActorObjectManager objects,
        Configuration config)
        : Window("Options###ImmersiveDresserOptions", PanelFlags)
    {
        public override bool DrawConditions()
            => objects.Player.Valid;

        public override void PreDraw()
        {
            var center = Im.Viewport.Main.Center;
            Im.Window.SetNextPosition(center + new Vector2(0, Im.Viewport.Main.Size.Y * 0.25f),
                Condition.FirstUseEver, new Vector2(0.5f, 0f));
            Im.Window.SetNextSize(Vector2.Zero, Condition.Always);
            Flags               = PanelFlags;
            if (config.LockImmersiveDresserPanels)
                Flags |= WindowFlags.NoMove;
            RespectCloseHotkey  = true;
            DisableWindowSounds = true;
        }

        public override void Draw()
        {
            var (id, playerData) = objects.PlayerData;
            if (!playerData.Valid)
                return;

            if (!stateManager.GetOrCreate(id, playerData.Objects[0], out var state))
                return;

            // Dye All
            equipmentDrawer.Prepare();
            if (equipmentDrawer.DrawAllStain(out var newAllStain, state.IsLocked))
            {
                foreach (var slot in EquipSlotExtensions.EqdpSlots)
                    stateManager.ChangeStains(state, slot, newAllStain, ApplySettings.Manual);
            }

            Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

            // Meta toggles: Hat + Head Crest | Visor + Body Crest | Weapon + Shield Crest | Ears
            using (Im.Group())
            {
                EquipmentDrawer.DrawMetaToggle(ToggleDrawData.FromState(MetaIndex.HatState, stateManager, state));
                EquipmentDrawer.DrawMetaToggle(ToggleDrawData.CrestFromState(CrestFlag.Head, stateManager, state));
            }

            Im.Line.Same();
            using (Im.Group())
            {
                EquipmentDrawer.DrawMetaToggle(ToggleDrawData.FromState(MetaIndex.VisorState, stateManager, state));
                EquipmentDrawer.DrawMetaToggle(ToggleDrawData.CrestFromState(CrestFlag.Body, stateManager, state));
            }

            Im.Line.Same();
            using (Im.Group())
            {
                EquipmentDrawer.DrawMetaToggle(ToggleDrawData.FromState(MetaIndex.WeaponState, stateManager, state));
                EquipmentDrawer.DrawMetaToggle(ToggleDrawData.CrestFromState(CrestFlag.OffHand, stateManager, state));
            }

            Im.Line.Same();
            using (Im.Group())
            {
                EquipmentDrawer.DrawMetaToggle(ToggleDrawData.FromState(MetaIndex.EarState, stateManager, state));
            }

            Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

            // Source filter
            EquipmentDrawer.DrawOwnedOnlyFilter(config);

            Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

            // Lock panels toggle
            if (Im.Checkbox("Lock Panels"u8, config.LockImmersiveDresserPanels))
            {
                config.LockImmersiveDresserPanels ^= true;
                config.Save();
            }

            Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

            // Close button
            var buttonWidth = Im.Font.CalculateSize("Close Dresser"u8).X + Im.Style.FramePadding.X * 2;
            var totalWidth  = Im.ContentRegion.Available.X;
            Im.Cursor.X += (totalWidth - buttonWidth) * 0.5f;
            if (Im.Button("Close Dresser"u8))
                manager.Close();
        }

        public override void OnClose()
        {
            manager.Close();
            base.OnClose();
        }
    }
}
