using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Game.Config;
using Dalamud.Hooking;
using Dalamud.Interface;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Common.Component.BGCollision;
using Dalamud.Interface.ImGuiNotification;
using Glamourer.Config;
using Glamourer.Designs;
using Glamourer.Designs.History;
using Glamourer.Gui.Customization;
using Glamourer.Gui.Equipment;
using Glamourer.Gui.Materials;
using Glamourer.Services;
using Glamourer.State;

using ImSharp;
using Luna;
using Penumbra.GameData.Actors;
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
    private readonly IGameConfig       _gameConfig;
    private readonly Configuration     _config;
    private readonly PreviewService    _previewService;
    private readonly RotationDrawer    _rotationDrawer;

    public readonly  EquipmentPanel    Left;
    public readonly  AccessoryPanel    Right;
    public readonly  OptionsPanel      Options;

    internal DresserMode _currentMode = DresserMode.Equipment;
    internal bool _showParameters;
    internal int  _advancedDyesDrawnFrame = -1;
    internal ActorIdentifier _targetIdentifier = ActorIdentifier.Invalid;

    internal readonly ActorObjectManager _objects;

    private bool  _wasUiVisible = true;
    private bool  _savedDisableUserUiHide;
    private bool? _savedAutoChangePointOfView;
    private bool  _isOpen;
    private bool  _didHideUi;
    internal bool _cammyFreeCamActive;

    private unsafe delegate void CameraUpdateDelegate(CameraBase* self);

    private readonly IGameInteropProvider _interop;
    private Hook<CameraUpdateDelegate>?   _cameraUpdateHook;

    internal readonly AdvancedDyePopup _advancedDyes;

    public unsafe ImmersiveDresserManager(EquipmentDrawer equipmentDrawer, CustomizationDrawer customizationDrawer,
        CustomizeParameterDrawer parameterDrawer, PreviewService previewService, StateManager stateManager,
        ActorObjectManager objects, Configuration config, IUiBuilder uiBuilder, IKeyState keyState, IFramework framework,
        ICommandManager commandManager, IGameConfig gameConfig, IGameInteropProvider interop, RotationDrawer rotationDrawer,
        DesignConverter designConverter, DesignManager designManager, EditorHistory editorHistory,
        AdvancedDyePopup advancedDyes)
    {
        _uiBuilder      = uiBuilder;
        _keyState       = keyState;
        _framework      = framework;
        _commandManager = commandManager;
        _gameConfig     = gameConfig;
        _config         = config;
        _previewService = previewService;
        _rotationDrawer = rotationDrawer;
        _interop        = interop;
        _advancedDyes   = advancedDyes;
        _objects        = objects;
        Left            = new EquipmentPanel(this, equipmentDrawer, customizationDrawer, stateManager);
        Right           = new AccessoryPanel(this, equipmentDrawer, parameterDrawer, stateManager);
        Options         = new OptionsPanel(this, equipmentDrawer, stateManager, objects, config, commandManager, designConverter, designManager, editorHistory);
    }

    /// <summary>
    /// Lazily install the camera update hook on the *active in-world* camera's vtable.
    /// Must run after login because the lobby camera has a different vtable from the world camera —
    /// hooking it at plugin construction time silently misses the world camera once the player zones in.
    /// </summary>
    private unsafe void EnsureCameraHook()
    {
        if (_cameraUpdateHook != null)
            return;
        var camera = CameraManager.Instance()->GetActiveCamera();
        if (camera == null)
            return;
        var vtable = *(nint**)(&camera->CameraBase);
        _cameraUpdateHook = _interop.HookFromAddress<CameraUpdateDelegate>(vtable[3], CameraUpdateDetour);
    }

    public void Open(ActorIdentifier identifier = default)
    {
        _targetIdentifier = identifier.IsValid ? identifier.CreatePermanent() : ActorIdentifier.Invalid;

        if (_isOpen)
            return;

        _isOpen                      = true;
        _config.ImmersiveDresserCameraY = 0f;
        _savedDisableUserUiHide      = _uiBuilder.DisableUserUiHide;
        _uiBuilder.DisableUserUiHide = true;
        _framework.Update           += OnFrameworkUpdate;
        EnsureCameraHook();
        _cameraUpdateHook?.Enable();
        if (_config.DisableFirstPerson)
            ApplyFirstPersonOverride();
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
        _targetIdentifier = ActorIdentifier.Invalid;
        _framework.Update -= OnFrameworkUpdate;
        _rotationDrawer.Reset();
        if (_cammyFreeCamActive)
        {
            _commandManager.ProcessCommand("/cammy freecam");
            _cammyFreeCamActive = false;
        }

        _cameraUpdateHook?.Disable();
        Left.IsOpen    = false;
        Right.IsOpen   = false;
        Options.IsOpen = false;
        if (_didHideUi)
        {
            RestoreGameUi();
            _didHideUi = false;
        }

        RestoreFirstPersonOverride();
        _uiBuilder.DisableUserUiHide = _savedDisableUserUiHide;
    }

    public void Dispose()
    {
        if (_isOpen)
            Close();
        _cameraUpdateHook?.Dispose();
    }

    /// <summary>
    /// Resolves the actor currently being edited. Falls back to the local player when no explicit
    /// target is set, when the explicit target is no longer present in the object table, or when the
    /// resolved data has no valid objects.
    /// </summary>
    internal (ActorIdentifier Identifier, ActorData Data) ResolveTarget()
    {
        if (_targetIdentifier.IsValid && _objects.TryGetValue(_targetIdentifier, out var data) && data.Valid)
            return (_targetIdentifier, data);

        return _objects.PlayerData;
    }

    /// <summary>
    /// Switches the dresser to edit a different actor. Pass <see cref="ActorIdentifier.Invalid"/>
    /// (or <c>default</c>) to fall back to the local player. Any in-flight preview is restored
    /// before the swap so the previous actor's state is not left dirty.
    /// </summary>
    internal void SetTarget(ActorIdentifier identifier)
    {
        var resolved = identifier.IsValid ? identifier.CreatePermanent() : ActorIdentifier.Invalid;
        if (_targetIdentifier.Equals(resolved))
            return;

        _previewService.EndPreview();
        _rotationDrawer.Reset();
        _targetIdentifier = resolved;
    }

    private unsafe void OnFrameworkUpdate(IFramework _)
    {
        if (!_isOpen)
            return;

        if (_keyState[VirtualKey.ESCAPE])
        {
            _keyState[VirtualKey.ESCAPE] = false;
            Close();
            return;
        }
    }

    private unsafe void CameraUpdateDetour(CameraBase* self)
    {
        _cameraUpdateHook!.Original(self);

        var offset = _config.ImmersiveDresserCameraY;
        if (offset == 0f)
            return;

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

    /// <summary>
    /// Snapshot the player's "Switch to 1st person view when fully zoomed in" game-config value
    /// (UiControl: AutoChangePointOfView) and override it to 0 (auto-switch off) for the duration
    /// of the dresser session. Also performs a one-shot snap to third-person if the camera was
    /// already in first-person — AutoChangePointOfView only governs future zoom-driven transitions.
    /// Must be called on the framework thread (Open/Close and the Options checkbox already run there).
    /// </summary>
    internal unsafe void ApplyFirstPersonOverride()
    {
        if (_savedAutoChangePointOfView is not null)
            return;

        if (!_gameConfig.TryGet(UiControlOption.AutoChangePointOfView, out bool current))
            return;

        _savedAutoChangePointOfView = current;
        _gameConfig.Set(UiControlOption.AutoChangePointOfView, false);

        var cam = CameraManager.Instance()->GetActiveCamera();
        if (cam != null && cam->ZoomMode == CameraZoomMode.FirstPerson)
        {
            cam->ZoomMode      = CameraZoomMode.ThirdPerson;
            cam->ControlMode   = CameraControlMode.ThirdPersonFixed;
            cam->Distance      = cam->MinDistance > 0 ? cam->MinDistance : 1.5f;
            cam->InterpDistance = cam->Distance;
        }
    }

    /// <summary>
    /// Restore the player's original AutoChangePointOfView value. No-op if no override is in
    /// flight. Must be called on the framework thread.
    /// </summary>
    internal void RestoreFirstPersonOverride()
    {
        if (_savedAutoChangePointOfView is not { } original)
            return;

        _gameConfig.Set(UiControlOption.AutoChangePointOfView, original);
        _savedAutoChangePointOfView = null;
    }

    private static WindowFlags PanelFlags
        => WindowFlags.NoTitleBar | WindowFlags.NoDocking | WindowFlags.AlwaysAutoResize | WindowFlags.NoCollapse;

    /// <summary>
    /// Shared PreDraw styling for the Equipment and Accessory panels: equipment-mode padding/border,
    /// optional background override, and opaque frame backgrounds. Each panel sets its own
    /// position anchor, window name, and flags before calling this.
    /// </summary>
    private static void ApplyPanelStyle(ImmersiveDresserManager manager, Im.ColorStyleDisposable style)
    {
        if (manager._currentMode is DresserMode.Equipment)
            style.Push(ImStyleDouble.WindowPadding, new Vector2(Im.Style.GlobalScale * 4))
                .Push(ImStyleSingle.WindowBorderThickness, 0);

        if (manager._config.OverrideDresserBgColor)
            style.Push(ImGuiColor.WindowBackground, manager._config.ImmersiveDresserBgColor.Color);

        // Force frame backgrounds opaque so checkboxes/inputs remain readable when the
        // window background is translucent.
        style.Push(ImGuiColor.FrameBackground,        ImGuiColor.FrameBackground.Get().Color        | 0xFF000000)
             .Push(ImGuiColor.FrameBackgroundHovered, ImGuiColor.FrameBackgroundHovered.Get().Color | 0xFF000000)
             .Push(ImGuiColor.FrameBackgroundActive,  ImGuiColor.FrameBackgroundActive.Get().Color  | 0xFF000000);
    }

    /// <summary>
    /// Draws the floating advanced-dye popup once per frame, guarded so the Equipment and Accessory
    /// panels (both of which call this) only draw it a single time.
    /// </summary>
    private void DrawAdvancedDyesOnce(Actor gameObject, ActorState state)
    {
        if (_advancedDyesDrawnFrame == Im.State.FrameCount)
            return;

        _advancedDyesDrawnFrame = Im.State.FrameCount;
        _advancedDyes.Draw(gameObject, state, false, forceFloating: true);
    }

    /// <summary> Draws the per-slot equipment icon loop shared by the Equipment and Accessory panels. </summary>
    private static void DrawSlotIcons(EquipmentDrawer equipmentDrawer, StateManager stateManager, ActorState state,
        IReadOnlyList<EquipSlot> slots, bool simplified)
    {
        foreach (var slot in slots)
        {
            var data = EquipDrawData.FromState(stateManager, state, slot);
            using var slotId    = Im.Id.Push((int)slot);
            using var slotStyle = ImStyleDouble.ItemSpacing.PushX(Im.Style.ItemInnerSpacing.X);
            equipmentDrawer.DrawEquipIcon(data, stainsBeside: true, simplified: simplified);
        }
    }

    /// <summary> Left panel: Equipment icons in Equipment mode, CustomizationDrawer in Appearance mode. </summary>
    public sealed class EquipmentPanel(
        ImmersiveDresserManager manager,
        EquipmentDrawer equipmentDrawer,
        CustomizationDrawer customizationDrawer,
        StateManager stateManager)
        : Window("Equipment###ImmersiveDresserLeft", PanelFlags)
    {
        private readonly Im.ColorStyleDisposable _style = new();

        public override bool DrawConditions()
            => manager.ResolveTarget().Data.Valid;

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
            if (manager._currentMode is DresserMode.Appearance)
                Flags &= ~(WindowFlags.NoTitleBar | WindowFlags.NoCollapse);
            if (manager._config.LockImmersiveDresserPanels)
                Flags |= WindowFlags.NoMove;
            RespectCloseHotkey  = true;
            DisableWindowSounds = true;

            ApplyPanelStyle(manager, _style);
        }

        public override void PostDraw()
            => _style.Dispose();

        public override void Draw()
        {
            var (id, playerData) = manager.ResolveTarget();
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
                equipmentDrawer.Prepare(false);

                var mainhand = EquipDrawData.FromState(stateManager, state, EquipSlot.MainHand);
                var offhand  = EquipDrawData.FromState(stateManager, state, EquipSlot.OffHand);
                var hasOffhand = offhand.CurrentItem.Type is not FullEquipType.Unknown;

                var simplified = manager._config.SimplifiedDresserLayout;

                equipmentDrawer.DrawSingleWeaponIcon(ref mainhand, ref offhand, false, true,
                    stainsBeside: true, simplified: simplified);

                var slots = manager._config.SingleWindowDresser
                    ? EquipSlotExtensions.EqdpSlots
                    : EquipSlotExtensions.EquipmentSlots;
                DrawSlotIcons(equipmentDrawer, stateManager, state, slots, simplified);

                if (manager._config.SingleWindowDresser && hasOffhand)
                    equipmentDrawer.DrawSingleWeaponIcon(ref mainhand, ref offhand, false, false,
                        stainsBeside: true, simplified: simplified);

                foreach (var slot in BonusExtensions.AllFlags)
                {
                    var data = BonusDrawData.FromState(stateManager, state, slot);
                    using var slotId    = Im.Id.Push(100 + (int)slot);
                    using var slotStyle = ImStyleDouble.ItemSpacing.PushX(Im.Style.ItemInnerSpacing.X);
                    equipmentDrawer.DrawBonusItemIcon(data, stainsBeside: true, simplified: simplified);
                }

                equipmentDrawer.ApplyHoverPreview(stateManager, state);
            }

            manager.DrawAdvancedDyesOnce(playerData.Objects[0], state);
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
        StateManager stateManager)
        : Window("Accessories###ImmersiveDresserRight", PanelFlags)
    {
        private readonly Im.ColorStyleDisposable _style = new();

        public override bool DrawConditions()
            => manager.ResolveTarget().Data.Valid
             && (manager._currentMode is DresserMode.Appearance
                 ? manager._showParameters
                 : !manager._config.SingleWindowDresser);

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

            ApplyPanelStyle(manager, _style);
        }

        public override void PostDraw()
            => _style.Dispose();

        public override void Draw()
        {
            var (id, playerData) = manager.ResolveTarget();
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
                equipmentDrawer.Prepare(false);

                var mainhand = EquipDrawData.FromState(stateManager, state, EquipSlot.MainHand);
                var offhand  = EquipDrawData.FromState(stateManager, state, EquipSlot.OffHand);

                var hasOffhand = offhand.CurrentItem.Type is not FullEquipType.Unknown;
                var simplified = manager._config.SimplifiedDresserLayout;

                if (hasOffhand)
                    equipmentDrawer.DrawSingleWeaponIcon(ref mainhand, ref offhand, false, false,
                        stainsBeside: true, simplified: simplified);

                DrawSlotIcons(equipmentDrawer, stateManager, state, EquipSlotExtensions.AccessorySlots, simplified);

                equipmentDrawer.ApplyHoverPreview(stateManager, state);
            }

            manager.DrawAdvancedDyesOnce(playerData.Objects[0], state);
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
        ICommandManager commandManager,
        DesignConverter converter,
        DesignManager designManager,
        EditorHistory editorHistory)
        : Window("Options###ImmersiveDresserOptions", PanelFlags)
    {
        private static readonly AwesomeIcon EyeIcon      = FontAwesomeIcon.Eye;
        private static readonly AwesomeIcon EyeSlashIcon = FontAwesomeIcon.EyeSlash;
        private static readonly AwesomeIcon LockIcon     = FontAwesomeIcon.Lock;
        private static readonly AwesomeIcon UnlockIcon   = FontAwesomeIcon.LockOpen;
        private static readonly AwesomeIcon FreeCamIcon  = FontAwesomeIcon.Video;

        private bool        _cammyAvailable;
        private string      _newName   = string.Empty;
        private DesignBase? _newDesign;

        public override bool DrawConditions()
            => manager.ResolveTarget().Data.Valid;

        public override void PreDraw()
        {
            var center = Im.Viewport.Main.Center;
            Im.Window.SetNextPosition(center + new Vector2(0, Im.Viewport.Main.Size.Y * 0.25f),
                Condition.FirstUseEver, new Vector2(0.5f, 0f));
            Im.Window.SetNextSize(Vector2.Zero, Condition.Always);
            Flags               = PanelFlags & ~(WindowFlags.NoTitleBar | WindowFlags.NoCollapse);
            if (config.LockImmersiveDresserPanels)
                Flags |= WindowFlags.NoMove;
            RespectCloseHotkey  = true;
            DisableWindowSounds = true;
        }

        public override void Draw()
        {
            var (id, playerData) = manager.ResolveTarget();
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

            // Design action buttons (clipboard, save, undo)
            DrawDesignActions(state, id);

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

            DrawTargetPicker(id);

            if (manager._currentMode is DresserMode.Equipment)
            {
                Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

                // Dye All
                equipmentDrawer.Prepare(false);
                if (equipmentDrawer.DrawAllStain(out var newAllStain, state.IsLocked))
                {
                    foreach (var slot in EquipSlotExtensions.EqdpSlots)
                        stateManager.ChangeStains(state, slot, newAllStain, ApplySettings.Manual);
                }
                equipmentDrawer.ApplyAllStainHoverPreview(stateManager, state);

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
            }

            Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

            // Dresser settings (equipment mode only)
            if (manager._currentMode is DresserMode.Equipment && Im.Tree.Header("Dresser Settings"u8))
            {
                if (Im.Checkbox("Single Window Layout"u8, config.SingleWindowDresser))
                {
                    config.SingleWindowDresser ^= true;
                    config.Save();
                }
                Im.Tooltip.OnHover(
                    "When enabled, equipment and accessories are shown together in a single window instead of split into two side-by-side panels."u8);

                if (Im.Checkbox("Simplified Layout"u8, config.SimplifiedDresserLayout))
                {
                    config.SimplifiedDresserLayout ^= true;
                    config.Save();
                }
                Im.Tooltip.OnHover(
                    "Hide the advanced dye buttons and stack the dye channels vertically beside the item icon."u8);

                if (Im.Checkbox("##overrideBg"u8, config.OverrideDresserBgColor))
                {
                    config.OverrideDresserBgColor ^= true;
                    config.Save();
                }

                if (config.OverrideDresserBgColor)
                {
                    Im.Line.Same();
                    var bgColor = config.ImmersiveDresserBgColor;
                    if (Im.Color.Editor("##dresserBgColor"u8, ref bgColor,
                            ColorEditorFlags.AlphaBar | ColorEditorFlags.AlphaPreviewHalf | ColorEditorFlags.NoInputs))
                    {
                        config.ImmersiveDresserBgColor = bgColor;
                        config.Save();
                    }
                }

                Im.Line.Same();
                Im.Text("Override Window Background"u8);
                Im.Tooltip.OnHover(
                    "Replace the equipment and accessory window backgrounds with a custom color."u8);

                Im.Line.New();
            }

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
                if (config.ImmersiveDresserCameraY != 0f && Im.Button("Reset##cameraYReset"u8))
                {
                    config.ImmersiveDresserCameraY = 0f;
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

                    if (manager._isOpen)
                    {
                        if (config.DisableFirstPerson)
                            manager.ApplyFirstPersonOverride();
                        else
                            manager.RestoreFirstPersonOverride();
                    }
                }

                Im.Tooltip.OnHover("Disables auto-switch to first-person on full zoom-in (game config: 'Switch to 1st person view when fully zoomed in'). Restores original on dresser close."u8);

                Im.Line.New();
            }

            // Character rotation
            if (Im.Tree.Header("Character Rotation"u8))
            {
                manager._rotationDrawer.Draw(playerData.Objects[0]);
                Im.Line.New();
            }

            if (manager._currentMode is DresserMode.Appearance)
            {
                Im.Dummy(new Vector2(0, Im.Style.ItemSpacing.Y));

                if (Im.Checkbox("Show Color Customization"u8, manager._showParameters))
                    manager._showParameters = !manager._showParameters;
            }

            // Save as Design popup (must be drawn every frame while open)
            using (Im.Style.PushDefault())
            {
                if (InputPopup.OpenName("Save as Design"u8, _newName, out var newName))
                {
                    if (_newDesign is not null && newName.Length > 0)
                        designManager.CreateClone(_newDesign, newName, true);
                    _newDesign = null;
                    _newName   = string.Empty;
                }
            }
        }

        private void DrawDesignActions(ActorState state, Penumbra.GameData.Actors.ActorIdentifier id)
        {
            var iconSize = Im.Style.FrameHeight;

            // Set from clipboard
            if (ImEx.Icon.Button(LunaStyle.FromClipboardIcon,
                    "Try to apply a design from your clipboard.\nHold Control to only apply gear.\nHold Shift to only apply customizations."u8,
                    disabled: state.IsLocked,
                    size: new Vector2(iconSize, iconSize)))
            {
                try
                {
                    var (applyGear, applyCustomize) = UiHelpers.ConvertKeysToBool();
                    var text   = Im.Clipboard.GetUtf16();
                    var design = converter.FromBase64(text, applyCustomize, applyGear, out _)
                     ?? throw new Exception("The clipboard did not contain valid data.");
                    stateManager.ApplyDesign(state, design, ApplySettings.ManualWithLinks with { IsFinal = true });
                }
                catch (Exception ex)
                {
                    Glamourer.Messager.NotificationMessage(ex, $"Could not apply clipboard to {id}.",
                        $"Could not apply clipboard to design.", NotificationType.Error, false);
                }
            }

            Im.Line.Same();

            // Export to clipboard
            if (ImEx.Icon.Button(LunaStyle.ToClipboardIcon,
                    "Copy the current design to your clipboard.\nHold Control to disable applying of customizations.\nHold Shift to disable applying of gear."u8,
                    disabled: state.ModelData.ModelId is not 0,
                    size: new Vector2(iconSize, iconSize)))
            {
                try
                {
                    var text = converter.ShareBase64(state, ApplicationRules.FromModifiers(state));
                    Im.Clipboard.Set(text);
                }
                catch (Exception ex)
                {
                    Glamourer.Messager.NotificationMessage(ex, $"Could not copy {id} data to clipboard.",
                        $"Could not copy data to clipboard.", NotificationType.Error);
                }
            }

            Im.Line.Same();

            // Save as design
            if (ImEx.Icon.Button(LunaStyle.SaveIcon,
                    "Save the current state as a design.\nHold Control to disable applying of customizations.\nHold Shift to disable applying of gear."u8,
                    disabled: state.ModelData.ModelId is not 0,
                    size: new Vector2(iconSize, iconSize)))
            {
                Im.Popup.Open("Save as Design"u8);
                _newName   = id.ToName();
                _newDesign = converter.Convert(state, ApplicationRules.FromModifiers(state));
            }

            Im.Line.Same();

            // Undo
            if (ImEx.Icon.Button(LunaStyle.UndoIcon,
                    "Undo the last change."u8,
                    disabled: state.IsLocked || !editorHistory.CanUndo(state),
                    size: new Vector2(iconSize, iconSize)))
                editorHistory.Undo(state);
        }

        private void DrawTargetPicker(ActorIdentifier currentId)
        {
            var hasOverride = manager._targetIdentifier.IsValid;
            var preview     = hasOverride ? currentId.ToName() : $"{currentId.ToName()} (Self)";

            Im.Item.SetNextWidthScaled(220);
            using (var combo = Im.Combo.Begin("##dresserTarget"u8, preview))
            {
                if (combo)
                {
                    var playerId = objects.PlayerData.Identifier;
                    if (Im.Selectable("Player (Self)"u8, !hasOverride))
                        manager.SetTarget(default);

                    foreach (var pair in objects.Where(p => p.Value.Objects.Any(a => a.Model)))
                    {
                        if (pair.Key.Equals(playerId))
                            continue;

                        var selected = hasOverride && pair.Key.Equals(manager._targetIdentifier);
                        if (Im.Selectable(pair.Value.Label, selected))
                            manager.SetTarget(pair.Key);
                    }
                }
            }

            var (targetId, targetData) = objects.TargetData;
            var canUseTarget = targetData.Valid
             && targetData.Objects.Any(a => a.Model)
             && !targetId.Equals(manager._targetIdentifier)
             && !(hasOverride is false && targetId.Equals(objects.PlayerData.Identifier));

            if (canUseTarget)
            {
                Im.Line.Same();
                if (ImEx.Icon.Button(FontAwesomeIcon.HandPointer.Icon(),
                        "Switch the dresser to your current in-game target."u8,
                        size: new Vector2(Im.Style.FrameHeight, Im.Style.FrameHeight)))
                    manager.SetTarget(targetId);
                Im.Tooltip.OnHover($"Switch dresser target to: {targetId.ToName()}");
            }

            if (hasOverride)
            {
                Im.Line.Same();
                if (Im.Button("Return to Self"u8))
                    manager.SetTarget(default);
            }
        }

        public override void OnClose()
        {
            manager.Close();
            base.OnClose();
        }
    }
}
