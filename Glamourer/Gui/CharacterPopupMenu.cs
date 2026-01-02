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
    private readonly PreviewService     _previewService;
    private readonly Configuration      _config;
    private readonly IUiBuilder         _uiBuilder;

    // Current target info
    private Actor  _lastActor;
    private string _lastCharacterName = string.Empty;
    private bool   _menuOpen;
    private bool   _shouldOpen;
    private Vector2 _menuPosition;

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
        AutoDesignApplier autoDesignApplier,
        PreviewService previewService)
    {
        _items             = items;
        _state             = state;
        _objects           = objects;
        _actors            = actors;
        _designManager     = designManager;
        _designFileSystem  = designFileSystem;
        _designConverter   = designConverter;
        _autoDesignApplier = autoDesignApplier;
        _previewService    = previewService;
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
            if (_previewService.IsPreviewActive)
                _previewService.EndPreview();
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
                if (_previewService.IsPreviewActive && _previewService.State.Type != PreviewType.Equipment)
                    _previewService.EndPreview();
                DrawEquipmentSubmenu(ApplyEquipmentToSelf, toSelf: true);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Appearance to Self submenu
            if (ImGui.BeginMenu("Apply Target's Appearance to Self"))
            {
                // Revert any equipment preview when entering this submenu
                if (_previewService.IsPreviewActive && _previewService.State.Type == PreviewType.Equipment)
                    _previewService.EndPreview();
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
                if (_previewService.IsPreviewActive && _previewService.State.Type != PreviewType.Equipment)
                    _previewService.EndPreview();
                DrawEquipmentSubmenu(ApplyGearToTarget, toSelf: false);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Current Appearance to Target submenu
            if (ImGui.BeginMenu("Apply Current Appearance to Target"))
            {
                // Revert any equipment preview when entering this submenu
                if (_previewService.IsPreviewActive && _previewService.State.Type == PreviewType.Equipment)
                    _previewService.EndPreview();
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
            if (_previewService.IsPreviewActive && _previewService.State.Type != PreviewType.Design)
                _previewService.EndPreview();
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
                if (!_previewService.State.IsSameEquipmentPreview(slot, isBonusItem, toSelf))
                {
                    _previewService.StartEquipmentPreview(_lastActor, slot, isBonusItem, toSelf);
                }
            }
        }

        // Revert preview when not hovering any item (so user can see original by hovering off)
        if (!anyItemHovered && _previewService.IsPreviewActive && _previewService.State.Type == PreviewType.Equipment)
            _previewService.EndPreview();
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
                if (!_previewService.State.IsSameAppearancePreview(flag, toSelf))
                {
                    _previewService.StartAppearancePreview(_lastActor, flag, toSelf);
                }
            }
        }

        // Revert preview when not hovering any item (so user can see original by hovering off)
        if (!anyItemHovered && _previewService.IsPreviewActive && _previewService.State.Type == PreviewType.Appearance)
            _previewService.EndPreview();
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
                if (!_previewService.State.IsSameDesignPreview(design))
                {
                    _previewService.ApplyDesignPreview(design);
                }
            }
        }

        // Revert preview when not hovering any item (so user can see original by hovering off)
        // Only do this at the root level to avoid reverting when navigating subfolders
        if (!anyItemHovered && _previewService.IsPreviewActive && _previewService.State.Type == PreviewType.Design)
            _previewService.EndPreview();
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
    /// Starts a full design to self preview.
    /// </summary>
    private void ApplyFullDesignToSelfPreview()
    {
        _previewService.StartFullDesignToSelfPreview(_lastActor);
    }

    /// <summary>
    /// Starts a full design to target preview.
    /// </summary>
    private void ApplyFullDesignToTargetPreview()
    {
        _previewService.StartFullDesignToTargetPreview(_lastActor);
    }

    /// <summary>
    /// Starts an automation state preview.
    /// </summary>
    private void ApplyAutomationPreview()
    {
        _previewService.StartAutomationPreview(_lastActor);
    }

    /// <summary>
    /// Starts a reset to game state preview.
    /// </summary>
    private void ApplyResetPreview()
    {
        _previewService.StartResetPreview(_lastActor);
    }

    /// <summary>
    /// Checks if preview should be reverted (when no item is hovered).
    /// </summary>
    private void CheckAndEndPreview()
    {
        if (_previewService.IsPreviewActive)
        {
            _previewService.EndPreview();
        }
    }

    /// <summary>
    /// Applies the current preview permanently (with IsFinal=true) without reverting first.
    /// If no preview is active, just runs the action normally.
    /// </summary>
    private void ApplyPreviewPermanently(Action applyAction)
    {
        _previewService.ApplyPreviewPermanently(applyAction);
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
