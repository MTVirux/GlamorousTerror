using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Glamourer.Api.Enums;
using Glamourer.Automation;
using Glamourer.Designs;
using Glamourer.GameData;
using Glamourer.Interop.Material;
using Glamourer.Services;
using Glamourer.State;
using OtterGui.Raii;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;

namespace Glamourer.Gui;

/// <summary>
/// Represents the type of preview being shown.
/// </summary>
public enum PreviewType
{
    None,
    Equipment,
    Appearance,
    Design,
    FullDesignToSelf,
    FullDesignToTarget,
    Automation,
    Reset,
}

/// <summary>
/// Represents the current preview state, tracking what is being previewed and the original state to restore.
/// </summary>
public sealed class PreviewState
{
    /// <summary> Whether a preview is currently active. </summary>
    public bool IsActive { get; private set; }

    /// <summary> The type of preview being shown. </summary>
    public PreviewType Type { get; private set; }

    /// <summary> The actor state being modified by the preview. </summary>
    public ActorState? TargetState { get; private set; }

    /// <summary> The original DesignData before preview was applied. </summary>
    public DesignData OriginalData { get; private set; }

    /// <summary> The original materials before preview was applied. </summary>
    public StateMaterialManager? OriginalMaterials { get; private set; }

    /// <summary> Whether we're applying to self (true) or target (false). </summary>
    public bool ToSelf { get; private set; }

    // Type-specific tracking
    public EquipSlot EquipSlot { get; private set; }
    public bool IsBonusItem { get; private set; }
    public CustomizeFlag AppearanceFlag { get; private set; }
    public Design? Design { get; private set; }

    /// <summary>
    /// Starts a new preview, capturing the original state of the target.
    /// </summary>
    public void Start(ActorState state, PreviewType type, bool toSelf = false)
    {
        // If switching to a different actor, we should have already restored the previous one
        if (IsActive && TargetState != state)
            throw new InvalidOperationException("Cannot start preview on different actor without ending previous preview first");

        if (!IsActive)
        {
            // Capture original state only on first preview
            TargetState = state;
            OriginalData = state.ModelData;
            OriginalMaterials = state.Materials.Clone();
        }

        IsActive = true;
        Type = type;
        ToSelf = toSelf;

        // Clear type-specific fields
        EquipSlot = EquipSlot.Unknown;
        IsBonusItem = false;
        AppearanceFlag = 0;
        Design = null;
    }

    /// <summary>
    /// Starts an equipment preview.
    /// </summary>
    public void StartEquipment(ActorState state, EquipSlot slot, bool isBonusItem, bool toSelf)
    {
        Start(state, PreviewType.Equipment, toSelf);
        EquipSlot = slot;
        IsBonusItem = isBonusItem;
    }

    /// <summary>
    /// Starts an appearance preview.
    /// </summary>
    public void StartAppearance(ActorState state, CustomizeFlag flag, bool toSelf)
    {
        Start(state, PreviewType.Appearance, toSelf);
        AppearanceFlag = flag;
    }

    /// <summary>
    /// Starts a design preview.
    /// </summary>
    public void StartDesign(ActorState state, Design design)
    {
        Start(state, PreviewType.Design, false);
        Design = design;
    }

    /// <summary>
    /// Checks if we're already previewing the same equipment.
    /// </summary>
    public bool IsSameEquipmentPreview(EquipSlot slot, bool isBonusItem, bool toSelf)
        => IsActive && Type == PreviewType.Equipment && EquipSlot == slot && IsBonusItem == isBonusItem && ToSelf == toSelf;

    /// <summary>
    /// Checks if we're already previewing the same appearance.
    /// </summary>
    public bool IsSameAppearancePreview(CustomizeFlag flag, bool toSelf)
        => IsActive && Type == PreviewType.Appearance && AppearanceFlag == flag && ToSelf == toSelf;

    /// <summary>
    /// Checks if we're already previewing the same design.
    /// </summary>
    public bool IsSameDesignPreview(Design design)
        => IsActive && Type == PreviewType.Design && Design == design;

    /// <summary>
    /// Checks if we're already previewing the same type.
    /// </summary>
    public bool IsSameTypePreview(PreviewType type)
        => IsActive && Type == type;

    /// <summary>
    /// Ends the preview, clearing all state. Does NOT restore original data - caller should do that first.
    /// </summary>
    public void End()
    {
        IsActive = false;
        Type = PreviewType.None;
        TargetState = null;
        OriginalData = default;
        OriginalMaterials = null;
        ToSelf = false;
        EquipSlot = EquipSlot.Unknown;
        IsBonusItem = false;
        AppearanceFlag = 0;
        Design = null;
    }
}

/// <summary>
/// Custom ImGui-based popup menu for character context actions.
/// Provides full control over menu positioning and appearance.
/// </summary>
public class CharacterPopupMenu : IDisposable
{
    private readonly ItemManager        _items;
    private readonly StateManager       _state;
    private readonly ActorObjectManager _objects;
    private readonly ActorManager       _actors;
    private readonly DesignManager      _designManager;
    private readonly DesignFileSystem   _designFileSystem;
    private readonly DesignConverter    _designConverter;
    private readonly AutoDesignApplier  _autoDesignApplier;
    private readonly Configuration      _config;
    private readonly IUiBuilder         _uiBuilder;

    // Current target info
    private Actor  _lastActor;
    private string _lastCharacterName = string.Empty;
    private bool   _menuOpen;
    private bool   _shouldOpen;
    private Vector2 _menuPosition;

    // Unified preview state
    private readonly PreviewState _preview = new();

    // Equipment slot definitions (EquipSlot.Unknown used for "All")
    private static readonly (string Name, EquipSlot Slot, bool IsBonusItem)[] EquipmentSlots =
    [
        ("All", EquipSlot.Unknown, false),
        ("Head", EquipSlot.Head, false),
        ("Body", EquipSlot.Body, false),
        ("Hands", EquipSlot.Hands, false),
        ("Legs", EquipSlot.Legs, false),
        ("Feet", EquipSlot.Feet, false),
        ("Ears", EquipSlot.Ears, false),
        ("Neck", EquipSlot.Neck, false),
        ("Wrists", EquipSlot.Wrists, false),
        ("Right Ring", EquipSlot.RFinger, false),
        ("Left Ring", EquipSlot.LFinger, false),
        ("Main Hand", EquipSlot.MainHand, false),
        ("Off Hand", EquipSlot.OffHand, false),
        ("Facewear", EquipSlot.Unknown, true),
    ];

    // Appearance option definitions
    private static readonly (string Name, CustomizeFlag Flag)[] AppearanceOptions =
    [
        ("All", CustomizeFlagExtensions.AllRelevant),
        ("Clan/Race", CustomizeFlag.Clan | CustomizeFlag.Race),
        ("Gender", CustomizeFlag.Gender),
        ("Body Type", CustomizeFlag.BodyType),
        ("Height", CustomizeFlag.Height),
        ("Face", CustomizeFlag.Face),
        ("Hairstyle", CustomizeFlag.Hairstyle | CustomizeFlag.Highlights),
        ("Hair Color", CustomizeFlag.HairColor | CustomizeFlag.HighlightsColor),
        ("Skin Color", CustomizeFlag.SkinColor),
        ("Eye Colors", CustomizeFlag.EyeColorRight | CustomizeFlag.EyeColorLeft | CustomizeFlag.SmallIris),
        ("Eye Shape", CustomizeFlag.EyeShape),
        ("Eyebrows", CustomizeFlag.Eyebrows),
        ("Nose", CustomizeFlag.Nose),
        ("Jaw", CustomizeFlag.Jaw),
        ("Mouth", CustomizeFlag.Mouth | CustomizeFlag.Lipstick | CustomizeFlag.LipColor),
        ("Facial Features", CustomizeFlag.FacialFeature1 | CustomizeFlag.FacialFeature2 | CustomizeFlag.FacialFeature3 | CustomizeFlag.FacialFeature4 | CustomizeFlag.FacialFeature5 | CustomizeFlag.FacialFeature6 | CustomizeFlag.FacialFeature7 | CustomizeFlag.LegacyTattoo),
        ("Tattoo Color", CustomizeFlag.TattooColor),
        ("Body", CustomizeFlag.MuscleMass | CustomizeFlag.BustSize | CustomizeFlag.TailShape),
        ("Face Paint", CustomizeFlag.FacePaint | CustomizeFlag.FacePaintReversed | CustomizeFlag.FacePaintColor),
    ];

    private const string MainPopupId = "GlamorousTerrorPopup";

    public CharacterPopupMenu(
        ItemManager items,
        StateManager state,
        ActorObjectManager objects,
        ActorManager actors,
        Configuration config,
        IUiBuilder uiBuilder,
        DesignManager designManager,
        DesignFileSystem designFileSystem,
        DesignConverter designConverter,
        AutoDesignApplier autoDesignApplier)
    {
        _items             = items;
        _state             = state;
        _objects           = objects;
        _actors            = actors;
        _designManager     = designManager;
        _designFileSystem  = designFileSystem;
        _designConverter   = designConverter;
        _autoDesignApplier = autoDesignApplier;
        _config            = config;
        _uiBuilder         = uiBuilder;

        _uiBuilder.Draw += OnDraw;
    }

    public void Dispose()
    {
        _uiBuilder.Draw -= OnDraw;
    }

    /// <summary>
    /// Opens the popup menu for the specified actor.
    /// Called by ContextMenuService when the "Glamorous Terror" option is clicked.
    /// </summary>
    public void Open(Actor actor, string characterName)
    {
        if (!actor.Valid || string.IsNullOrEmpty(characterName))
            return;

        // Store target info
        _lastActor         = actor;
        _lastCharacterName = characterName;

        // Get mouse position and offset to the right to avoid overlap
        _menuPosition = ImGui.GetMousePos() + new Vector2(15, 0);

        // Mark that we should open the popup on next draw
        _shouldOpen = true;
    }

    private void OnDraw()
    {

        if (_shouldOpen)
        {
            ImGui.OpenPopup(MainPopupId);
            _shouldOpen = false;
            _menuOpen   = true;
        }

        if (!_menuOpen)
            return;

        // Position the popup at the stored mouse position
        ImGui.SetNextWindowPos(_menuPosition, ImGuiCond.Appearing);

        using var popup = ImRaii.Popup(MainPopupId, ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize);
        if (!popup)
        {
            // Popup closed - revert any active preview
            if (_preview.IsActive)
                EndPreview();
            _menuOpen = false;
            return;
        }

        DrawMenuContent();
    }

    private void DrawMenuContent()
    {
        // Header with character name
        using (ImRaii.PushColor(ImGuiCol.Text, 0xFF00CFFF)) // Light blue/gold color
        {
            ImGui.TextUnformatted($"Glamorous Terror - {_lastCharacterName}");
        }
        using (ImRaii.PushColor(ImGuiCol.Text, 0xFF888888))
        {
            ImGui.TextUnformatted("Hold Shift to keep menu open");
        }
        ImGui.Separator();

        // Check if target is the same as self
        var (playerId, playerData) = _objects.PlayerData;
        var isSameActor = playerData.Valid && _lastActor.Valid && playerData.Objects[0] == _lastActor;

        // Import as Design
        if (ImGui.Selectable("Import as Design", false, ImGuiSelectableFlags.DontClosePopups))
        {
            OnImportAsDesign();
            ClosePopupIfNotHoldingShift();
        }

        // Track if any submenu with preview is open
        var anyPreviewSubmenuOpen = false;

        // Only show "Apply to Self" options if target is different from self
        if (!isSameActor)
        {
            ImGui.Separator();

            // Apply Target's Design to Self (full design transfer)
            if (ImGui.Selectable("Apply Target's Design to Self", false, ImGuiSelectableFlags.DontClosePopups))
            {
                ApplyPreviewPermanently(() => OnApplyFullDesignToSelf());
                ClosePopupIfNotHoldingShift();
            }
            if (ImGui.IsItemHovered())
            {
                ApplyFullDesignToSelfPreview();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Equipment to Self submenu
            if (ImGui.BeginMenu("Apply Target's Equipment to Self"))
            {
                // Revert any non-equipment preview when entering this submenu
                if (_preview.IsActive && _preview.Type != PreviewType.Equipment)
                    EndPreview();
                DrawEquipmentSubmenu(ApplyEquipmentToSelf, toSelf: true);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Appearance to Self submenu
            if (ImGui.BeginMenu("Apply Target's Appearance to Self"))
            {
                // Revert any equipment preview when entering this submenu
                if (_preview.IsActive && _preview.Type == PreviewType.Equipment)
                    EndPreview();
                DrawAppearanceSubmenu(ApplyAppearanceToSelf, toSelf: true);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            ImGui.Separator();

            // Apply Current Design to Target (full design transfer)
            if (ImGui.Selectable("Apply Current Design to Target", false, ImGuiSelectableFlags.DontClosePopups))
            {
                ApplyPreviewPermanently(() => OnApplyFullDesignToTarget());
                ClosePopupIfNotHoldingShift();
            }
            if (ImGui.IsItemHovered())
            {
                ApplyFullDesignToTargetPreview();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Current Gear to Target submenu
            if (ImGui.BeginMenu("Apply Current Equipment to Target"))
            {
                // Revert any non-equipment preview when entering this submenu
                if (_preview.IsActive && _preview.Type != PreviewType.Equipment)
                    EndPreview();
                DrawEquipmentSubmenu(ApplyGearToTarget, toSelf: false);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Current Appearance to Target submenu
            if (ImGui.BeginMenu("Apply Current Appearance to Target"))
            {
                // Revert any equipment preview when entering this submenu
                if (_preview.IsActive && _preview.Type == PreviewType.Equipment)
                    EndPreview();
                DrawAppearanceSubmenu(ApplyAppearanceToTarget, toSelf: false);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }
        }

        ImGui.Separator();

        // Apply Design to Target submenu
        if (ImGui.BeginMenu("Apply Design to Target"))
        {
            // Revert any non-design preview when entering this submenu
            if (_preview.IsActive && _preview.Type != PreviewType.Design)
                EndPreview();
            DrawDesignSubmenu(_designFileSystem.Root);
            ImGui.EndMenu();
            anyPreviewSubmenuOpen = true;
        }

        ImGui.Separator();

        // Revert to Automation State
        var automationEnabled = _config.EnableAutoDesigns;
        using (ImRaii.Disabled(!automationEnabled))
        {
            if (ImGui.Selectable("Revert to Automation State", false, ImGuiSelectableFlags.DontClosePopups))
            {
                ApplyPreviewPermanently(() => OnRevertToAutomation());
                ClosePopupIfNotHoldingShift();
            }
        }
        if (ImGui.IsItemHovered() && automationEnabled)
        {
            ApplyAutomationPreview();
            anyPreviewSubmenuOpen = true;
        }

        // Reset to Game State
        if (ImGui.Selectable("Reset to Game State", false, ImGuiSelectableFlags.DontClosePopups))
        {
            ApplyPreviewPermanently(() => OnResetToGameState());
            ClosePopupIfNotHoldingShift();
        }
        if (ImGui.IsItemHovered())
        {
            ApplyResetPreview();
            anyPreviewSubmenuOpen = true;
        }

        // Only revert preview if nothing with preview is being hovered/open
        if (!anyPreviewSubmenuOpen)
        {
            CheckAndEndPreview();
        }
    }

    private void DrawEquipmentSubmenu(Action<EquipSlot, bool> action, bool toSelf)
    {
        var includeWeapons = AreJobsCompatible();

        // Get player and target states for comparison
        var (playerState, targetState) = GetStatesForComparison();

        var anyItemHovered = false;
        foreach (var (name, slot, isBonusItem) in EquipmentSlots)
        {
            // Skip weapons if jobs aren't compatible
            if (!includeWeapons && (slot == EquipSlot.MainHand || slot == EquipSlot.OffHand))
                continue;

            // Check if items match
            var matches = CheckEquipmentMatch(playerState, targetState, slot, isBonusItem);

            if (ImGui.Selectable(name, matches, ImGuiSelectableFlags.DontClosePopups))
            {
                ApplyPreviewPermanently(() => action(slot, isBonusItem));
                ClosePopupIfNotHoldingShift();
            }

            // Handle preview on hover
            if (ImGui.IsItemHovered())
            {
                anyItemHovered = true;
                if (!_preview.IsSameEquipmentPreview(slot, isBonusItem, toSelf))
                {
                    StartEquipmentPreview(slot, isBonusItem, toSelf);
                }
            }
        }

        // Revert preview when not hovering any item (so user can see original by hovering off)
        if (!anyItemHovered && _preview.IsActive && _preview.Type == PreviewType.Equipment)
            EndPreview();
    }

    private void DrawAppearanceSubmenu(Action<CustomizeFlag> action, bool toSelf)
    {
        // Get player and target states for comparison
        var (playerState, targetState) = GetStatesForComparison();

        var anyItemHovered = false;
        foreach (var (name, flag) in AppearanceOptions)
        {
            // Check if appearance matches
            var matches = CheckAppearanceMatch(playerState, targetState, flag);

            if (ImGui.Selectable(name, matches, ImGuiSelectableFlags.DontClosePopups))
            {
                ApplyPreviewPermanently(() => action(flag));
                ClosePopupIfNotHoldingShift();
            }

            // Handle preview on hover
            if (ImGui.IsItemHovered())
            {
                anyItemHovered = true;
                if (!_preview.IsSameAppearancePreview(flag, toSelf))
                {
                    StartAppearancePreview(flag, toSelf);
                }
            }
        }

        // Revert preview when not hovering any item (so user can see original by hovering off)
        if (!anyItemHovered && _preview.IsActive && _preview.Type == PreviewType.Appearance)
            EndPreview();
    }

    private void DrawDesignSubmenu(DesignFileSystem.Folder folder)
    {
        var anyItemHovered = false;

        // First draw subfolders
        foreach (var subFolder in folder.GetSubFolders().OrderBy(f => f.Name))
        {
            if (ImGui.BeginMenu(subFolder.Name))
            {
                DrawDesignSubmenu(subFolder);
                ImGui.EndMenu();
            }
            // Subfolder menus count as hovered when open
            if (ImGui.IsItemHovered())
                anyItemHovered = true;
        }

        // Then draw designs (leaves)
        foreach (var leaf in folder.GetLeaves().OrderBy(l => l.Value.Name.Text))
        {
            var design = leaf.Value;
            if (ImGui.Selectable(design.Name.Text, false, ImGuiSelectableFlags.DontClosePopups))
            {
                ApplyPreviewPermanently(() => OnApplyDesignToTarget(design));
                ClosePopupIfNotHoldingShift();
            }

            // Handle preview on hover
            if (ImGui.IsItemHovered())
            {
                anyItemHovered = true;
                if (!_preview.IsSameDesignPreview(design))
                {
                    StartDesignPreview(design);
                }
            }
        }

        // Revert preview when not hovering any item (so user can see original by hovering off)
        // Only do this at the root level to avoid reverting when navigating subfolders
        if (!anyItemHovered && _preview.IsActive && _preview.Type == PreviewType.Design)
            EndPreview();
    }

    #region Comparison and Preview Helpers

    private (ActorState? playerState, ActorState? targetState) GetStatesForComparison()
    {
        ActorState? playerState = null;
        ActorState? targetState = null;

        try
        {
            var (playerId, playerData) = _objects.PlayerData;
            if (playerData.Valid && _state.TryGetValue(playerId, out var ps))
                playerState = ps;

            if (_lastActor.Valid)
            {
                var identifier = _actors.FromObject(_lastActor, out _, true, false, false);
                if (identifier.IsValid && _state.TryGetValue(identifier, out var ts))
                    targetState = ts;
            }
        }
        catch
        {
            // Ignore errors during comparison
        }

        return (playerState, targetState);
    }

    private bool CheckEquipmentMatch(ActorState? playerState, ActorState? targetState, EquipSlot slot, bool isBonusItem)
    {
        if (playerState == null || targetState == null)
            return false;

        try
        {
            if (isBonusItem)
            {
                var playerItem = playerState.ModelData.BonusItem(BonusItemFlag.Glasses);
                var targetItem = targetState.ModelData.BonusItem(BonusItemFlag.Glasses);
                return playerItem.Id == targetItem.Id;
            }
            else if (slot == EquipSlot.Unknown)
            {
                // "All Equipment" - check if all slots match
                foreach (var s in EquipSlotExtensions.FullSlots)
                {
                    var playerItem = playerState.ModelData.Item(s);
                    var targetItem = targetState.ModelData.Item(s);
                    if (playerItem.Id != targetItem.Id)
                        return false;
                }
                return true;
            }
            else
            {
                var playerItem = playerState.ModelData.Item(slot);
                var targetItem = targetState.ModelData.Item(slot);
                return playerItem.Id == targetItem.Id;
            }
        }
        catch
        {
            return false;
        }
    }

    private bool CheckAppearanceMatch(ActorState? playerState, ActorState? targetState, CustomizeFlag flag)
    {
        if (playerState == null || targetState == null)
            return false;

        try
        {
            if (flag == (CustomizeFlag)0)
            {
                // "All Appearance" - check if all customize values match
                foreach (var cf in Enum.GetValues<CustomizeFlag>())
                {
                    if (cf == 0) continue;
                    // Skip combined flags - only check single flags
                    if (!Enum.IsDefined(cf)) continue;
                    if (!CompareCustomizeFlag(playerState, targetState, cf))
                        return false;
                }
                return true;
            }
            else
            {
                // Handle combined flags by iterating through all individual flags
                foreach (var cf in Enum.GetValues<CustomizeFlag>())
                {
                    if (cf == 0 || !Enum.IsDefined(cf)) continue;
                    if ((flag & cf) == cf)
                    {
                        if (!CompareCustomizeFlag(playerState, targetState, cf))
                            return false;
                    }
                }
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private bool CompareCustomizeFlag(ActorState playerState, ActorState targetState, CustomizeFlag singleFlag)
    {
        try
        {
            var playerValue = playerState.ModelData.Customize[singleFlag.ToIndex()];
            var targetValue = targetState.ModelData.Customize[singleFlag.ToIndex()];
            return playerValue.Value == targetValue.Value;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Preview System

    /// <summary>
    /// Starts an equipment preview with proper state capture.
    /// </summary>
    private void StartEquipmentPreview(EquipSlot slot, bool isBonusItem, bool toSelf)
    {
        try
        {
            // Determine target state first
            ActorState? state = null;
            if (toSelf)
            {
                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                if (!_state.GetOrCreate(playerId, playerData.Objects[0], out state)) return;
            }
            else
            {
                if (!_lastActor.Valid) return;
                if (!_state.GetOrCreate(_lastActor, out state)) return;
            }

            if (state == null) return;

            // IMPORTANT: Restore any active preview BEFORE capturing source data
            // This ensures we capture the original state, not a previewed state
            if (_preview.IsActive && _preview.TargetState != state)
            {
                RestoreToOriginalState();
                _preview.End();
            }
            else if (_preview.IsActive)
            {
                RestoreToOriginalState();
            }

            // Now capture source data after any previews have been reverted
            DesignData sourceData;
            if (toSelf)
            {
                sourceData = _state.FromActor(_lastActor, true, false);
            }
            else
            {
                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                sourceData = _state.FromActor(playerData.Objects[0], true, false);
            }

            ApplicationCollection collection;
            if (isBonusItem)
                collection = new ApplicationCollection(0, BonusItemFlag.Glasses, CustomizeFlag.BodyType, 0, 0, 0);
            else if (slot == EquipSlot.Unknown)
                collection = ApplicationCollection.Equipment;
            else
                collection = new ApplicationCollection(slot.ToBothFlags(), 0, CustomizeFlag.BodyType, 0, 0, 0);

            var tempDesign = _designConverter.Convert(sourceData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            _preview.StartEquipment(state, slot, isBonusItem, toSelf);
            _state.ApplyDesign(state, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview equipment failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts an appearance preview with proper state capture.
    /// </summary>
    private void StartAppearancePreview(CustomizeFlag flag, bool toSelf)
    {
        try
        {
            // Determine target state first
            ActorState? state = null;
            if (toSelf)
            {
                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                if (!_state.GetOrCreate(playerId, playerData.Objects[0], out state)) return;
            }
            else
            {
                if (!_lastActor.Valid) return;
                if (!_state.GetOrCreate(_lastActor, out state)) return;
            }

            if (state == null) return;

            // IMPORTANT: Restore any active preview BEFORE capturing source data
            // This ensures we capture the original state, not a previewed state
            if (_preview.IsActive && _preview.TargetState != state)
            {
                RestoreToOriginalState();
                _preview.End();
            }
            else if (_preview.IsActive)
            {
                RestoreToOriginalState();
            }

            // Now capture source data after any previews have been reverted
            DesignData sourceData;
            if (toSelf)
            {
                sourceData = _state.FromActor(_lastActor, true, false);
            }
            else
            {
                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                sourceData = _state.FromActor(playerData.Objects[0], true, false);
            }

            var collection = flag == 0
                ? ApplicationCollection.Customizations
                : new ApplicationCollection(0, 0, flag, 0, 0, 0);

            var tempDesign = _designConverter.Convert(sourceData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            _preview.StartAppearance(state, flag, toSelf);
            _state.ApplyDesign(state, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview appearance failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts a design preview with proper state capture.
    /// </summary>
    private void StartDesignPreview(Design design)
    {
        try
        {
            if (!_lastActor.Valid) return;
            if (!_state.GetOrCreate(_lastActor, out var state)) return;

            // Handle actor switching: restore previous actor first if needed
            if (_preview.IsActive && _preview.TargetState != state)
            {
                RestoreToOriginalState();
                _preview.End();
            }

            // Restore to original before applying new preview if same actor
            if (_preview.IsActive)
                RestoreToOriginalState();

            _preview.StartDesign(state, design);
            // Use ResetMaterials = true to fully clear materials before applying the design's materials
            // This prevents material conflicts when switching between designs
            _state.ApplyDesign(state, design, ApplySettings.ManualWithLinks with { IsFinal = false, ResetMaterials = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview design failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts a full design to self preview.
    /// </summary>
    private void ApplyFullDesignToSelfPreview()
    {
        if (_preview.IsSameTypePreview(PreviewType.FullDesignToSelf))
            return;

        try
        {
            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid) return;
            if (!_state.GetOrCreate(playerId, playerData.Objects[0], out var playerState)) return;
            if (!_lastActor.Valid) return;

            // IMPORTANT: Restore any active preview BEFORE capturing source data
            // This ensures we capture the original state, not a previewed state
            if (_preview.IsActive && _preview.TargetState != playerState)
            {
                RestoreToOriginalState();
                _preview.End();
            }
            else if (_preview.IsActive)
            {
                RestoreToOriginalState();
            }

            // Now capture source data after any previews have been reverted
            var targetData = _state.FromActor(_lastActor, true, false);

            _preview.Start(playerState, PreviewType.FullDesignToSelf, toSelf: true);
            var tempDesign = _designConverter.Convert(targetData, new StateMaterialManager(), ApplicationRules.All);
            _state.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview full design to self failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts a full design to target preview.
    /// </summary>
    private void ApplyFullDesignToTargetPreview()
    {
        if (_preview.IsSameTypePreview(PreviewType.FullDesignToTarget))
            return;

        try
        {
            if (!_lastActor.Valid) return;
            if (!_state.GetOrCreate(_lastActor, out var targetState)) return;

            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid) return;

            // IMPORTANT: Restore any active preview BEFORE capturing source data
            // This ensures we capture the original state, not a previewed state
            if (_preview.IsActive && _preview.TargetState != targetState)
            {
                RestoreToOriginalState();
                _preview.End();
            }
            else if (_preview.IsActive)
            {
                RestoreToOriginalState();
            }

            // Now capture source data after any previews have been reverted
            var playerDesignData = _state.FromActor(playerData.Objects[0], true, false);

            _preview.Start(targetState, PreviewType.FullDesignToTarget, toSelf: false);
            var tempDesign = _designConverter.Convert(playerDesignData, new StateMaterialManager(), ApplicationRules.All);
            _state.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview full design to target failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts an automation state preview.
    /// </summary>
    private void ApplyAutomationPreview()
    {
        if (_preview.IsSameTypePreview(PreviewType.Automation))
            return;

        try
        {
            if (!_lastActor.Valid) return;
            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var state)) return;

            // Handle actor switching: restore previous actor first if needed
            if (_preview.IsActive && _preview.TargetState != state)
            {
                RestoreToOriginalState();
                _preview.End();
            }

            // Restore to original before applying new preview if same actor
            if (_preview.IsActive)
                RestoreToOriginalState();

            _preview.Start(state, PreviewType.Automation);
            _autoDesignApplier.ReapplyAutomation(_lastActor, targetIdentifier, state, true, false, out var forcedRedraw);
            _state.ReapplyAutomationState(_lastActor, state, forcedRedraw, true, StateSource.Manual);
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview automation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts a reset to game state preview.
    /// </summary>
    private void ApplyResetPreview()
    {
        if (_preview.IsSameTypePreview(PreviewType.Reset))
            return;

        try
        {
            if (!_lastActor.Valid) return;
            if (!_state.GetOrCreate(_lastActor, out var state)) return;

            // Handle actor switching: restore previous actor first if needed
            if (_preview.IsActive && _preview.TargetState != state)
            {
                RestoreToOriginalState();
                _preview.End();
            }

            // Restore to original before applying new preview if same actor
            if (_preview.IsActive)
                RestoreToOriginalState();

            _preview.Start(state, PreviewType.Reset);
            _state.ResetState(state, StateSource.Manual, isFinal: false);
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview reset failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores the character to their original state without clearing the stored original data.
    /// Used when switching between different preview types on the same actor.
    /// </summary>
    private void RestoreToOriginalState()
    {
        if (_preview.TargetState == null || !_preview.IsActive)
            return;

        try
        {
            // Convert the original data back to a design and apply it with ResetMaterials
            var tempDesign = _designConverter.Convert(_preview.OriginalData, _preview.OriginalMaterials ?? new StateMaterialManager(), ApplicationRules.All);
            _state.ApplyDesign(_preview.TargetState, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Restore to original state failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ends the preview, restoring the original state and clearing preview tracking.
    /// </summary>
    private void EndPreview()
    {
        if (!_preview.IsActive)
            return;

        try
        {
            RestoreToOriginalState();
        }
        finally
        {
            _preview.End();
        }
    }

    /// <summary>
    /// Checks if preview should be reverted (when no item is hovered).
    /// </summary>
    private void CheckAndEndPreview()
    {
        if (_preview.IsActive)
        {
            EndPreview();
        }
    }

    /// <summary>
    /// Applies the current preview permanently (with IsFinal=true) without reverting first.
    /// If no preview is active, just runs the action normally.
    /// </summary>
    private void ApplyPreviewPermanently(Action applyAction)
    {
        // Clear the preview state without restoring - the action will apply final changes
        _preview.End();

        // Now run the action which will apply with IsFinal=true
        applyAction();
    }

    /// <summary>
    /// Closes the popup only if the user is not holding shift.
    /// This allows users to apply multiple options without reopening the menu.
    /// </summary>
    private void ClosePopupIfNotHoldingShift()
    {
        if (!ImGui.GetIO().KeyShift)
        {
            ImGui.CloseCurrentPopup();
        }
    }

    #endregion

    #region Menu Actions

    private void OnImportAsDesign()
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

    private void OnApplyFullDesignToSelf()
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

            // Get target's full design data
            var targetData = _state.FromActor(_lastActor, true, false);
            var tempDesign = _designConverter.Convert(targetData, new StateMaterialManager(), ApplicationRules.All);

            _state.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied {_lastCharacterName}'s design to self");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply target's design to self: {ex}");
        }
    }

    private void OnApplyFullDesignToTarget()
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

            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            // Get player's full design data
            var playerDesignData = _state.FromActor(playerData.Objects[0], true, false);
            var tempDesign = _designConverter.Convert(playerDesignData, new StateMaterialManager(), ApplicationRules.All);

            _state.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied current design to {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply current design to target: {ex}");
        }
    }

    private void ApplyEquipmentToSelf(EquipSlot slot, bool isBonusItem)
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
            ApplicationCollection collection;
            string slotName;

            if (isBonusItem)
            {
                collection = new ApplicationCollection(0, BonusItemFlag.Glasses, CustomizeFlag.BodyType, 0, 0, 0);
                slotName = "facewear";
            }
            else if (slot == EquipSlot.Unknown)
            {
                collection = ApplicationCollection.Equipment;
                slotName = "all equipment";
            }
            else
            {
                collection = new ApplicationCollection(slot.ToBothFlags(), 0, CustomizeFlag.BodyType, 0, 0, 0);
                slotName = slot.ToString();
            }

            var tempDesign = _designConverter.Convert(designData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            _state.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied {slotName} from {_lastCharacterName} to self");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply equipment to self: {ex}");
        }
    }

    private void ApplyAppearanceToSelf(CustomizeFlag flags)
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
            var collection = flags == CustomizeFlagExtensions.AllRelevant
                ? ApplicationCollection.Customizations
                : new ApplicationCollection(0, 0, flags | CustomizeFlag.BodyType, 0, 0, 0);
            var tempDesign = _designConverter.Convert(designData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            _state.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied appearance from {_lastCharacterName} to self");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply appearance to self: {ex}");
        }
    }

    private void ApplyGearToTarget(EquipSlot slot, bool isBonusItem)
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

            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            ApplicationCollection collection;
            string slotName;

            if (isBonusItem)
            {
                collection = new ApplicationCollection(0, BonusItemFlag.Glasses, CustomizeFlag.BodyType, 0, 0, 0);
                slotName = "facewear";
            }
            else if (slot == EquipSlot.Unknown)
            {
                collection = ApplicationCollection.Equipment;
                slotName = "all gear";
            }
            else
            {
                collection = new ApplicationCollection(slot.ToBothFlags(), 0, CustomizeFlag.BodyType, 0, 0, 0);
                slotName = slot.ToString();
            }

            var tempDesign = _designConverter.Convert(playerState.ModelData, playerState.Materials,
                new ApplicationRules(collection, false));

            _state.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied current {slotName} to {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply gear to target: {ex}");
        }
    }

    private void ApplyAppearanceToTarget(CustomizeFlag flags)
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

            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            var collection = flags == CustomizeFlagExtensions.AllRelevant
                ? ApplicationCollection.Customizations
                : new ApplicationCollection(0, 0, flags | CustomizeFlag.BodyType, 0, 0, 0);
            var tempDesign = _designConverter.Convert(playerState.ModelData, playerState.Materials,
                new ApplicationRules(collection, false));

            _state.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = true });
            Glamourer.Log.Information($"Applied current appearance to {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply appearance to target: {ex}");
        }
    }

    private void OnApplyDesignToTarget(Design design)
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            _state.ApplyDesign(targetState, design, ApplySettings.ManualWithLinks with { IsFinal = true });
            Glamourer.Log.Information($"Applied design {design.ResolveName(true)} to {_lastCharacterName}");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to apply design to target: {ex}");
        }
    }

    private void OnRevertToAutomation()
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            if (!_config.EnableAutoDesigns)
            {
                Glamourer.Log.Warning("Auto designs are not enabled");
                return;
            }

            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            if (targetState.IsLocked)
            {
                Glamourer.Log.Warning($"Cannot revert {_lastCharacterName} - state is locked");
                return;
            }

            _autoDesignApplier.ReapplyAutomation(_lastActor, targetIdentifier, targetState, true, false, out var forcedRedraw);
            _state.ReapplyAutomationState(_lastActor, forcedRedraw, true, StateSource.Manual);
            Glamourer.Log.Information($"Reverted {_lastCharacterName} to automation state");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to revert to automation state: {ex}");
        }
    }

    private void OnResetToGameState()
    {
        if (!_lastActor.Valid)
            return;

        try
        {
            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var targetState))
                return;

            if (targetState.IsLocked)
            {
                Glamourer.Log.Warning($"Cannot reset {_lastCharacterName} - state is locked");
                return;
            }

            _state.ResetState(targetState, StateSource.Manual, isFinal: true);
            Glamourer.Log.Information($"Reset {_lastCharacterName} to game state");
        }
        catch (Exception ex)
        {
            Glamourer.Log.Error($"Failed to reset to game state: {ex}");
        }
    }

    #endregion

    #region Helpers

    private bool AreJobsCompatible()
    {
        if (!_lastActor.Valid || !_lastActor.IsCharacter)
            return false;

        var (_, playerData) = _objects.PlayerData;
        if (!playerData.Valid)
            return false;

        Actor playerActor = playerData.Objects[0];
        if (!playerActor.Valid || !playerActor.IsCharacter)
            return false;

        return _lastActor.Job == playerActor.Job;
    }

    #endregion
}
