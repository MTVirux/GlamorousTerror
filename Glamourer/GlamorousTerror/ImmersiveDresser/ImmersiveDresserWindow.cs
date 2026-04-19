using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Glamourer.Config;
using Glamourer.Designs;
using Glamourer.Gui.Customization;
using Glamourer.Gui.Equipment;
using Glamourer.Services;
using Glamourer.State;

using ImSharp;
using Luna;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;

namespace Glamourer.Gui;

public enum DresserMode
{
    Equipment,
    Appearance,
}

public sealed class ImmersiveDresserManager : IDisposable, IService
{
    private readonly IUiBuilder        _uiBuilder;
    private readonly IKeyState          _keyState;
    private readonly IFramework         _framework;
    private readonly ICommandManager   _commandManager;
    private readonly Configuration     _config;
    private readonly PreviewService    _previewService;
    private readonly RotationDrawer    _rotationDrawer;

    public readonly  EquipmentPanel    Left;
    public readonly  AccessoryPanel    Right;
    public readonly  OptionsPanel      Options;

    internal DresserMode _currentMode = DresserMode.Equipment;
    internal bool _showParameters;

    private bool _wasUiVisible = true;
    private bool _savedDisableUserUiHide;
    private bool _isOpen;
    private bool _didHideUi;
    internal bool _cammyFreeCamActive;
    internal float _lastValidCameraY;

    private unsafe delegate void CameraUpdateDelegate(CameraBase* self);
    private unsafe delegate byte CanChangePerspectiveDelegate();

    private readonly Hook<CameraUpdateDelegate>?        _cameraUpdateHook;
    private readonly Hook<CanChangePerspectiveDelegate>? _canChangePerspectiveHook;

    public unsafe ImmersiveDresserManager(EquipmentDrawer equipmentDrawer, CustomizationDrawer customizationDrawer,
        CustomizeParameterDrawer parameterDrawer, PreviewService previewService, StateManager stateManager,
        ActorObjectManager objects, Configuration config, IUiBuilder uiBuilder, IKeyState keyState, IFramework framework,
        ICommandManager commandManager, IGameInteropProvider interop, RotationDrawer rotationDrawer)
    {
        _uiBuilder      = uiBuilder;
        _keyState       = keyState;
        _framework      = framework;
        _commandManager = commandManager;
        _config         = config;
        _previewService = previewService;
        _rotationDrawer = rotationDrawer;
        Left            = new EquipmentPanel(this, equipmentDrawer, customizationDrawer, stateManager, objects);
        Right           = new AccessoryPanel(this, equipmentDrawer, parameterDrawer, stateManager, objects);
        Options         = new OptionsPanel(this, equipmentDrawer, stateManager, objects, config, commandManager);

        var camera = CameraManager.Instance()->GetActiveCamera();
        if (camera != null)
        {
            var vtable = *(nint**)(&camera->CameraBase);
            _cameraUpdateHook           = interop.HookFromAddress<CameraUpdateDelegate>(vtable[3], CameraUpdateDetour);
            _canChangePerspectiveHook   = interop.HookFromAddress<CanChangePerspectiveDelegate>(vtable[22], CanChangePerspectiveDetour);
        }
    }

    public void Open()
    {
        if (_isOpen)
            return;

        _isOpen                      = true;
        _config.ImmersiveDresserCameraY = 0f;
        _savedDisableUserUiHide      = _uiBuilder.DisableUserUiHide;
        _uiBuilder.DisableUserUiHide = true;
        _framework.Update           += OnFrameworkUpdate;
        _cameraUpdateHook?.Enable();
        _canChangePerspectiveHook?.Enable();
        if (_config.AutoHideGameUi)
        {
            HideGameUi();
            _didHideUi = true;
        }
        else
        {
            _didHideUi = false;
        }
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
        _rotationDrawer.Reset();
        if (_cammyFreeCamActive)
        {
            _commandManager.ProcessCommand("/cammy freecam");
            _cammyFreeCamActive = false;
        }

        _cameraUpdateHook?.Disable();
        _canChangePerspectiveHook?.Disable();
        Left.IsOpen    = false;
        Right.IsOpen   = false;
        Options.IsOpen = false;
        if (_didHideUi)
        {
            RestoreGameUi();
            _didHideUi = false;
        }

        _uiBuilder.DisableUserUiHide = _savedDisableUserUiHide;
    }

    public void Dispose()
    {
        if (_isOpen)
            Close();
        _cameraUpdateHook?.Dispose();
        _canChangePerspectiveHook?.Dispose();
    }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        if (_isOpen && _keyState[VirtualKey.ESCAPE])
        {
            _keyState[VirtualKey.ESCAPE] = false;
            Close();
            return;
        }

    }

    private unsafe byte CanChangePerspectiveDetour()
    {
        if (_config.DisableFirstPerson)
            return 0;
        return _canChangePerspectiveHook!.Original();
    }

    private unsafe void CameraUpdateDetour(CameraBase* self)
    {
        _cameraUpdateHook!.Original(self);

        var offset = _config.ImmersiveDresserCameraY;
        if (offset == 0f)
        {
            _lastValidCameraY = 0f;
            return;
        }

        // Compute candidate position after offset.
        var candidateY = self->SceneCamera.Position.Y + offset;

        // Cast a ray downward from the original camera position to find the ground.
        if (!_config.AllowCameraClipping)
        {
            const float minHeightAboveGround = 0.5f;
            var         origin               = self->SceneCamera.Position;
            var         direction            = new Vector3(0, -1, 0);
            if (BGCollisionModule.RaycastMaterialFilter(origin, direction, out var hit))
            {
                var groundY = hit.Point.Y + minHeightAboveGround;
                if (candidateY < groundY)
                    candidateY = groundY;
            }
        }

        var appliedOffset = candidateY - self->SceneCamera.Position.Y;
        _lastValidCameraY = appliedOffset;

        // Snap config back to the clamped value so the slider reflects reality.
        if (Math.Abs(appliedOffset - offset) > 0.001f)
            _config.ImmersiveDresserCameraY = appliedOffset;

        self->SceneCamera.LookAtVector.Y += appliedOffset;
        self->SceneCamera.Position.Y      = candidateY;
    }

    internal unsafe void SetAutoHideUi(bool hide)
    {
        var module = RaptureAtkModule.Instance();
        if (module == null)
            return;

        if (hide && module->IsUiVisible)
        {
            _wasUiVisible       = true;
            module->IsUiVisible = false;
            _didHideUi          = true;
        }
        else if (!hide && !module->IsUiVisible)
        {
            module->IsUiVisible = true;
            _didHideUi          = false;
        }
        else
        {
            _didHideUi = hide;
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
        if (module != null && !module->IsUiVisible)
            module->IsUiVisible = _wasUiVisible;
    }

    private static WindowFlags PanelFlags
        => WindowFlags.NoTitleBar | WindowFlags.NoDocking | WindowFlags.AlwaysAutoResize | WindowFlags.NoCollapse;

    /// <summary> Left panel: Equipment icons in Equipment mode, CustomizationDrawer in Appearance mode. </summary>
    public sealed class EquipmentPanel(
        ImmersiveDresserManager manager,
        EquipmentDrawer equipmentDrawer,
        CustomizationDrawer customizationDrawer,
        StateManager stateManager,
        ActorObjectManager objects)
        : Window("Equipment###ImmersiveDresserLeft", PanelFlags)
    {
        public override bool DrawConditions()
            => objects.Player.Valid;

        public override void PreDraw()
        {
            WindowName = manager._currentMode is DresserMode.Appearance
                ? "Customization###ImmersiveDresserLeftApp"
                : "Equipment###ImmersiveDresserLeft";

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

            if (manager._currentMode is DresserMode.Appearance)
            {
                // Customization drawer
                if (customizationDrawer.Draw(state.ModelData.Customize, state.IsLocked, false))
                    stateManager.ChangeEntireCustomize(state, customizationDrawer.Customize,
                        customizationDrawer.Changed, ApplySettings.Manual);

                customizationDrawer.ApplyHoverPreview(stateManager, state);
            }
            else
            {
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
        }

        public override void OnClose()
        {
            manager.Close();
            base.OnClose();
        }
    }

    /// <summary> Right panel: Accessories in Equipment mode, CustomizeParameterDrawer in Appearance mode. </summary>
    public sealed class AccessoryPanel(
        ImmersiveDresserManager manager,
        EquipmentDrawer equipmentDrawer,
        CustomizeParameterDrawer parameterDrawer,
        StateManager stateManager,
        ActorObjectManager objects)
        : Window("Accessories###ImmersiveDresserRight", PanelFlags)
    {
        public override bool DrawConditions()
            => objects.Player.Valid && (manager._currentMode is DresserMode.Equipment || manager._showParameters);

        public override void PreDraw()
        {
            WindowName = manager._currentMode is DresserMode.Appearance
                ? "Parameters###ImmersiveDresserRightApp"
                : "Accessories###ImmersiveDresserRight";

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

            if (manager._currentMode is DresserMode.Appearance)
            {
                parameterDrawer.Draw(stateManager, state);
            }
            else
            {
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
        }

        public override void OnClose()
        {
            manager.Close();
            base.OnClose();
        }
    }

    /// <summary> Options panel: Meta toggles, dye all, source filter, controls (equipment mode only). </summary>
    public sealed class OptionsPanel(
        ImmersiveDresserManager manager,
        EquipmentDrawer equipmentDrawer,
        StateManager stateManager,
        ActorObjectManager objects,
        Configuration config,
        ICommandManager commandManager)
        : Window("Options###ImmersiveDresserOptions", PanelFlags)
    {
        private static readonly AwesomeIcon EyeIcon      = FontAwesomeIcon.Eye;
        private static readonly AwesomeIcon EyeSlashIcon = FontAwesomeIcon.EyeSlash;
        private static readonly AwesomeIcon LockIcon     = FontAwesomeIcon.Lock;
        private static readonly AwesomeIcon UnlockIcon   = FontAwesomeIcon.LockOpen;
        private static readonly AwesomeIcon FreeCamIcon  = FontAwesomeIcon.Video;

        private bool _cammyAvailable;

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

            // Mode toggle
            if (manager._currentMode is DresserMode.Equipment)
            {
                if (Im.Button("Switch to Appearance"u8))
                    manager._currentMode = DresserMode.Appearance;
            }
            else
            {
                if (Im.Button("Switch to Equipment"u8))
                {
                    manager._previewService.EndCustomizationPopupFrame(state);
                    manager._currentMode = DresserMode.Equipment;
                }
            }

            Im.Line.Same();
            if (Im.Button("Reset to Game State"u8))
                stateManager.ResetState(state, StateSource.Manual, isFinal: true);

            // Detect Cammy availability and free cam state each frame.
            _cammyAvailable = commandManager.Commands.ContainsKey("/cammy");
            unsafe
            {
                var cam = CameraManager.Instance()->GetActiveCamera();
                manager._cammyFreeCamActive = _cammyAvailable && cam != null && cam->MaxDistance <= 0.1f;
            }

            // Right-aligned icon buttons on the same line
            Im.Line.Same();
            var available = Im.ContentRegion.Available.X;
            var iconSize  = Im.Style.FrameHeight;
            var spacing   = Im.Style.ItemSpacing.X;
            var needed    = iconSize * 3 + spacing * 2;
            Im.Cursor.X += available - needed;

            bool isGameUiHidden;
            unsafe
            {
                var module = RaptureAtkModule.Instance();
                isGameUiHidden = module != null && !module->IsUiVisible;
            }

            var eyeIcon = isGameUiHidden ? EyeSlashIcon : EyeIcon;
            if (ImEx.Icon.Button(eyeIcon, isGameUiHidden
                    ? "Game UI is hidden. Click to show."u8
                    : "Game UI is visible. Click to hide."u8,
                size: new Vector2(iconSize, iconSize)))
            {
                config.AutoHideGameUi = !isGameUiHidden;
                config.Save();
                manager.SetAutoHideUi(config.AutoHideGameUi);
            }

            Im.Line.Same();
            var lockIcon = config.LockImmersiveDresserPanels ? LockIcon : UnlockIcon;
            if (ImEx.Icon.Button(lockIcon, config.LockImmersiveDresserPanels
                    ? "Panels are locked. Click to unlock."u8
                    : "Panels are unlocked. Click to lock."u8,
                size: new Vector2(iconSize, iconSize)))
            {
                config.LockImmersiveDresserPanels ^= true;
                config.Save();
            }

            Im.Line.Same();
            if (ImEx.Icon.Button(FreeCamIcon, _cammyAvailable
                    ? manager._cammyFreeCamActive
                        ? "Free camera is active. Click to disable."u8
                        : "Click to enable free camera (Cammy)."u8
                    : "Freecan requires the Cammy plugin to be installed."u8,
                disabled: !_cammyAvailable,
                buttonColor: manager._cammyFreeCamActive ? (ColorParameter)0xFF44AA44u : default,
                size: new Vector2(iconSize, iconSize)))
            {
                commandManager.ProcessCommand("/cammy freecam");
            }

            if (manager._currentMode is DresserMode.Equipment)
            {
                Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

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

                // Icon Drawer settings
                if (Im.Tree.Header("Icon Drawer"u8))
                {
                    EquipmentDrawer.DrawOwnedOnlyFilter(config);

                    if (Im.Checkbox("Group by Model"u8, config.GroupIconPickerByModel))
                    {
                        config.GroupIconPickerByModel ^= true;
                        config.Save();
                    }

                    Im.Tooltip.OnHover(
                        "When enabled, items that share the same visual model are grouped under a single icon in the picker."u8);

                    Im.Line.New();
                }
            }

            Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

            // Camera settings (hidden while free cam is active since all controls are irrelevant)
            if (!manager._cammyFreeCamActive && Im.Tree.Header("Camera"u8))
            {
                var cameraY = config.ImmersiveDresserCameraY;
                Im.Item.SetNextWidthScaled(200);
                if (Im.Slider("##cameraYOffset"u8, ref cameraY, "%.2f"u8, -2f, 2f, SliderFlags.AlwaysClamp))
                {
                    config.ImmersiveDresserCameraY = cameraY;
                    config.Save();
                }

                Im.Line.Same();
                Im.Text("Camera Height"u8);
                Im.Tooltip.OnHover("Adjusts the camera vertical position while the immersive dresser is open."u8);

                if (Im.Checkbox("Allow Camera Clipping"u8, config.AllowCameraClipping))
                {
                    config.AllowCameraClipping ^= true;
                    config.Save();
                }

                Im.Tooltip.OnHover("When enabled, the camera can clip through the ground."u8);

                if (Im.Checkbox("Disable First Person"u8, config.DisableFirstPerson))
                {
                    config.DisableFirstPerson ^= true;
                    config.Save();

                    // If toggled on while already in first person, force back to third person.
                    unsafe
                    {
                        if (config.DisableFirstPerson)
                        {
                            var cam = CameraManager.Instance()->GetActiveCamera();
                            if (cam != null && cam->ZoomMode == CameraZoomMode.FirstPerson)
                            {
                                cam->ZoomMode      = CameraZoomMode.ThirdPerson;
                                cam->ControlMode   = CameraControlMode.ThirdPersonFixed;
                                cam->Distance      = cam->MinDistance > 0 ? cam->MinDistance : 1.5f;
                                cam->InterpDistance = cam->Distance;
                            }
                        }
                    }
                }

                Im.Tooltip.OnHover("Prevents the camera from entering first-person mode when zooming in."u8);

                Im.Line.New();
            }

            // Character rotation
            if (Im.Tree.Header("Character Rotation"u8))
            {
                manager._rotationDrawer.Draw(objects.Player);
                Im.Line.New();
            }

            if (manager._currentMode is DresserMode.Appearance)
            {
                Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

                if (Im.Checkbox("Show Color Customization"u8, manager._showParameters))
                    manager._showParameters = !manager._showParameters;
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
