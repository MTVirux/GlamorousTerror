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
    private readonly Configuration      _config;
    private readonly IUiBuilder         _uiBuilder;

    // Current target info
    private Actor  _lastActor;
    private string _lastCharacterName = string.Empty;
    private bool   _menuOpen;
    private bool   _shouldOpen;
    private Vector2 _menuPosition;

    // Preview tracking
    private bool _isPreviewActive;
    private EquipSlot _previewEquipSlot;
    private bool _previewIsBonusItem;
    private CustomizeFlag _previewAppearanceFlag;
    private bool _previewIsEquipment;
    private bool _previewToSelf;
    private bool _previewIsAutomation;
    private bool _previewIsReset;
    private bool _previewIsFullDesignToSelf;
    private bool _previewIsFullDesignToTarget;
    private Design? _previewDesign;
    private ActorState? _previewOriginalState;
    private DesignData _previewOriginalData;
    private StateMaterialManager? _previewOriginalMaterials;
    private bool _hasOriginalData;

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
            if (_isPreviewActive)
                RevertPreview();
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
                RevertPreview();
                OnApplyFullDesignToSelf();
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
                if (_isPreviewActive && !_previewIsEquipment)
                    RevertPreview();
                DrawEquipmentSubmenu(ApplyEquipmentToSelf, toSelf: true);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Appearance to Self submenu
            if (ImGui.BeginMenu("Apply Target's Appearance to Self"))
            {
                // Revert any equipment preview when entering this submenu
                if (_isPreviewActive && _previewIsEquipment)
                    RevertPreview();
                DrawAppearanceSubmenu(ApplyAppearanceToSelf, toSelf: true);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            ImGui.Separator();

            // Apply Current Design to Target (full design transfer)
            if (ImGui.Selectable("Apply Current Design to Target", false, ImGuiSelectableFlags.DontClosePopups))
            {
                RevertPreview();
                OnApplyFullDesignToTarget();
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
                if (_isPreviewActive && !_previewIsEquipment)
                    RevertPreview();
                DrawEquipmentSubmenu(ApplyGearToTarget, toSelf: false);
                ImGui.EndMenu();
                anyPreviewSubmenuOpen = true;
            }

            // Apply Current Appearance to Target submenu
            if (ImGui.BeginMenu("Apply Current Appearance to Target"))
            {
                // Revert any equipment preview when entering this submenu
                if (_isPreviewActive && _previewIsEquipment)
                    RevertPreview();
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
            if (_isPreviewActive && _previewDesign == null)
                RevertPreview();
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
                RevertPreview();
                OnRevertToAutomation();
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
            RevertPreview();
            OnResetToGameState();
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
            CheckAndRevertPreview();
        }
    }

    private void DrawEquipmentSubmenu(Action<EquipSlot, bool> action, bool toSelf)
    {
        var includeWeapons = AreJobsCompatible();

        // Get player and target states for comparison
        var (playerState, targetState) = GetStatesForComparison();

        foreach (var (name, slot, isBonusItem) in EquipmentSlots)
        {
            // Skip weapons if jobs aren't compatible
            if (!includeWeapons && (slot == EquipSlot.MainHand || slot == EquipSlot.OffHand))
                continue;

            // Check if items match
            var matches = CheckEquipmentMatch(playerState, targetState, slot, isBonusItem);

            if (ImGui.Selectable(name, matches, ImGuiSelectableFlags.DontClosePopups))
            {
                RevertPreview();
                action(slot, isBonusItem);
                ClosePopupIfNotHoldingShift();
            }

            // Handle preview on hover
            if (ImGui.IsItemHovered())
            {
                if (!_isPreviewActive || !_previewIsEquipment || _previewEquipSlot != slot || _previewIsBonusItem != isBonusItem || _previewToSelf != toSelf)
                {
                    RevertPreview();
                    ApplyEquipmentPreview(slot, isBonusItem, toSelf);
                    _isPreviewActive = true;
                    _previewEquipSlot = slot;
                    _previewIsBonusItem = isBonusItem;
                    _previewIsEquipment = true;
                    _previewToSelf = toSelf;
                }
            }
        }
    }

    private void DrawAppearanceSubmenu(Action<CustomizeFlag> action, bool toSelf)
    {
        // Get player and target states for comparison
        var (playerState, targetState) = GetStatesForComparison();

        foreach (var (name, flag) in AppearanceOptions)
        {
            // Check if appearance matches
            var matches = CheckAppearanceMatch(playerState, targetState, flag);

            if (ImGui.Selectable(name, matches, ImGuiSelectableFlags.DontClosePopups))
            {
                RevertPreview();
                action(flag);
                ClosePopupIfNotHoldingShift();
            }

            // Handle preview on hover
            if (ImGui.IsItemHovered())
            {
                if (!_isPreviewActive || _previewAppearanceFlag != flag || _previewToSelf != toSelf || _previewIsEquipment)
                {
                    RevertPreview();
                    ApplyAppearancePreview(flag, toSelf);
                    _isPreviewActive = true;
                    _previewAppearanceFlag = flag;
                    _previewIsEquipment = false;
                    _previewToSelf = toSelf;
                }
            }
        }
    }

    private void DrawDesignSubmenu(DesignFileSystem.Folder folder)
    {
        // First draw subfolders
        foreach (var subFolder in folder.GetSubFolders().OrderBy(f => f.Name))
        {
            if (ImGui.BeginMenu(subFolder.Name))
            {
                DrawDesignSubmenu(subFolder);
                ImGui.EndMenu();
            }
        }

        // Then draw designs (leaves)
        foreach (var leaf in folder.GetLeaves().OrderBy(l => l.Value.Name.Text))
        {
            var design = leaf.Value;
            if (ImGui.Selectable(design.Name.Text, false, ImGuiSelectableFlags.DontClosePopups))
            {
                RevertPreview();
                OnApplyDesignToTarget(design);
                ClosePopupIfNotHoldingShift();
            }

            // Handle preview on hover
            if (ImGui.IsItemHovered())
            {
                ApplyDesignPreview(design);
            }
        }
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

    private void ApplyEquipmentPreview(EquipSlot slot, bool isBonusItem, bool toSelf)
    {
        try
        {
            ActorState? state = null;
            DesignData sourceData;

            if (toSelf)
            {
                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                if (!_state.GetOrCreate(playerId, playerData.Objects[0], out state)) return;
                sourceData = _state.FromActor(_lastActor, true, false);
            }
            else
            {
                if (!_lastActor.Valid) return;
                if (!_state.GetOrCreate(_lastActor, out state)) return;

                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                sourceData = _state.FromActor(playerData.Objects[0], true, false);
            }

            if (state == null) return;

            ApplicationCollection collection;
            if (isBonusItem)
                collection = new ApplicationCollection(0, BonusItemFlag.Glasses, CustomizeFlag.BodyType, 0, 0, 0);
            else if (slot == EquipSlot.Unknown)
                collection = ApplicationCollection.Equipment;
            else
                collection = new ApplicationCollection(slot.ToBothFlags(), 0, CustomizeFlag.BodyType, 0, 0, 0);

            var tempDesign = _designConverter.Convert(sourceData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != state)
            {
                RestoreToOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreToOriginalState();
            }

            _state.ApplyDesign(state, tempDesign, ApplySettings.Manual with { IsFinal = false });
            _isPreviewActive = true;
            _previewIsEquipment = true;
            _previewToSelf = toSelf;
            _previewEquipSlot = slot;
            _previewIsBonusItem = isBonusItem;
            _previewDesign = null;
            _previewIsAutomation = false;
            _previewIsReset = false;
            _previewIsFullDesignToSelf = false;
            _previewIsFullDesignToTarget = false;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview equipment failed: {ex.Message}");
        }
    }

    private void ApplyAppearancePreview(CustomizeFlag flag, bool toSelf)
    {
        try
        {
            ActorState? state = null;
            DesignData sourceData;

            if (toSelf)
            {
                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                if (!_state.GetOrCreate(playerId, playerData.Objects[0], out state)) return;
                sourceData = _state.FromActor(_lastActor, true, false);
            }
            else
            {
                if (!_lastActor.Valid) return;
                if (!_state.GetOrCreate(_lastActor, out state)) return;

                var (playerId, playerData) = _objects.PlayerData;
                if (!playerData.Valid) return;
                sourceData = _state.FromActor(playerData.Objects[0], true, false);
            }

            if (state == null) return;

            var collection = flag == 0
                ? ApplicationCollection.Customizations
                : new ApplicationCollection(0, 0, flag, 0, 0, 0);

            var tempDesign = _designConverter.Convert(sourceData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != state)
            {
                RestoreToOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreToOriginalState();
            }

            _state.ApplyDesign(state, tempDesign, ApplySettings.Manual with { IsFinal = false });
            _isPreviewActive = true;
            _previewIsEquipment = false;
            _previewAppearanceFlag = flag;
            _previewToSelf = toSelf;
            _previewDesign = null;
            _previewIsAutomation = false;
            _previewIsReset = false;
            _previewIsFullDesignToSelf = false;
            _previewIsFullDesignToTarget = false;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview appearance failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Restores the character to their original state without clearing the stored original data.
    /// Used when switching between different preview types.
    /// </summary>
    private void RestoreToOriginalState()
    {
        if (_previewOriginalState == null || !_hasOriginalData)
            return;

        try
        {
            // Restore materials first
            if (_previewOriginalMaterials is { } materials)
            {
                _previewOriginalState.Materials.Clear();
                foreach (var (key, value) in materials.Values)
                    _previewOriginalState.Materials.AddOrUpdateValue(key, value);
            }

            var tempDesign = _designConverter.Convert(_previewOriginalData, _previewOriginalMaterials ?? new StateMaterialManager(), ApplicationRules.All);
            _state.ApplyDesign(_previewOriginalState, tempDesign, ApplySettings.Manual with { IsFinal = false });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Restore to original state failed: {ex.Message}");
        }
    }

    private void RevertPreview()
    {
        if (!_isPreviewActive || _previewOriginalState == null || !_hasOriginalData)
            return;

        try
        {
            // Restore materials first
            if (_previewOriginalMaterials is { } materials)
            {
                _previewOriginalState.Materials.Clear();
                foreach (var (key, value) in materials.Values)
                    _previewOriginalState.Materials.AddOrUpdateValue(key, value);
            }

            var tempDesign = _designConverter.Convert(_previewOriginalData, _previewOriginalMaterials ?? new StateMaterialManager(), ApplicationRules.All);
            _state.ApplyDesign(_previewOriginalState, tempDesign, ApplySettings.Manual with { IsFinal = false });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Revert preview failed: {ex.Message}");
        }
        finally
        {
            _isPreviewActive = false;
            _previewOriginalState = null;
            _hasOriginalData = false;
            _previewOriginalMaterials = null;
            _previewDesign = null;
            _previewIsAutomation = false;
            _previewIsReset = false;
            _previewIsFullDesignToSelf = false;
            _previewIsFullDesignToTarget = false;
            _previewIsEquipment = false;
        }
    }

    private void CheckAndRevertPreview()
    {
        // Called when no item is hovered - revert preview if active
        if (_isPreviewActive)
        {
            RevertPreview();
        }
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

    private void ApplyFullDesignToSelfPreview()
    {
        // Check if we're already previewing this
        if (_isPreviewActive && _previewIsFullDesignToSelf)
            return;

        try
        {
            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid) return;
            if (!_state.GetOrCreate(playerId, playerData.Objects[0], out var playerState)) return;
            if (!_lastActor.Valid) return;

            // Get target's full design data
            var targetData = _state.FromActor(_lastActor, true, false);

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != playerState)
            {
                RestoreToOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = playerState;
                _previewOriginalData = playerState.ModelData;
                _previewOriginalMaterials = playerState.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                _previewOriginalState = playerState;
                _previewOriginalData = playerState.ModelData;
                _previewOriginalMaterials = playerState.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreToOriginalState();
            }

            var tempDesign = _designConverter.Convert(targetData, new StateMaterialManager(), ApplicationRules.All);
            _state.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = false });
            _isPreviewActive = true;
            _previewIsFullDesignToSelf = true;
            _previewIsFullDesignToTarget = false;
            _previewDesign = null;
            _previewIsAutomation = false;
            _previewIsReset = false;
            _previewIsEquipment = false;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview full design to self failed: {ex.Message}");
        }
    }

    private void ApplyFullDesignToTargetPreview()
    {
        // Check if we're already previewing this
        if (_isPreviewActive && _previewIsFullDesignToTarget)
            return;

        try
        {
            if (!_lastActor.Valid) return;
            if (!_state.GetOrCreate(_lastActor, out var targetState)) return;

            var (playerId, playerData) = _objects.PlayerData;
            if (!playerData.Valid) return;

            // Get player's full design data
            var playerData2 = _state.FromActor(playerData.Objects[0], true, false);

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != targetState)
            {
                RestoreToOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = targetState;
                _previewOriginalData = targetState.ModelData;
                _previewOriginalMaterials = targetState.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                _previewOriginalState = targetState;
                _previewOriginalData = targetState.ModelData;
                _previewOriginalMaterials = targetState.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreToOriginalState();
            }

            var tempDesign = _designConverter.Convert(playerData2, new StateMaterialManager(), ApplicationRules.All);
            _state.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = false });
            _isPreviewActive = true;
            _previewIsFullDesignToTarget = true;
            _previewIsFullDesignToSelf = false;
            _previewDesign = null;
            _previewIsAutomation = false;
            _previewIsReset = false;
            _previewIsEquipment = false;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview full design to target failed: {ex.Message}");
        }
    }

    private void ApplyDesignPreview(Design design)
    {
        // Check if we're already previewing this exact design
        if (_isPreviewActive && _previewDesign == design)
            return;

        try
        {
            if (!_lastActor.Valid) return;
            if (!_state.GetOrCreate(_lastActor, out var state)) return;

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != state)
            {
                RestoreToOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreToOriginalState();
            }

            _state.ApplyDesign(state, design, ApplySettings.ManualWithLinks with { IsFinal = false });
            _isPreviewActive = true;
            _previewDesign = design;
            _previewIsAutomation = false;
            _previewIsReset = false;
            _previewIsEquipment = false;
            _previewIsFullDesignToSelf = false;
            _previewIsFullDesignToTarget = false;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview design failed: {ex.Message}");
        }
    }

    private void ApplyAutomationPreview()
    {
        // Check if we're already previewing automation
        if (_isPreviewActive && _previewIsAutomation)
            return;

        try
        {
            if (!_lastActor.Valid) return;
            var targetIdentifier = _lastActor.GetIdentifier(_objects.Actors);
            if (!_state.GetOrCreate(targetIdentifier, _lastActor, out var state)) return;

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != state)
            {
                RestoreToOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreToOriginalState();
            }

            _autoDesignApplier.ReapplyAutomation(_lastActor, targetIdentifier, state, true, false, out var forcedRedraw);
            // Apply the state to actually render the changes including materials/colors
            _state.ReapplyAutomationState(_lastActor, state, forcedRedraw, true, StateSource.Manual);
            _isPreviewActive = true;
            _previewIsAutomation = true;
            _previewDesign = null;
            _previewIsReset = false;
            _previewIsEquipment = false;
            _previewIsFullDesignToSelf = false;
            _previewIsFullDesignToTarget = false;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview automation failed: {ex.Message}");
        }
    }

    private void ApplyResetPreview()
    {
        // Check if we're already previewing reset
        if (_isPreviewActive && _previewIsReset)
            return;

        try
        {
            if (!_lastActor.Valid) return;
            if (!_state.GetOrCreate(_lastActor, out var state)) return;

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != state)
            {
                RestoreToOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreToOriginalState();
            }

            // Use ResetState with isFinal=false to preview - this properly clears materials
            _state.ResetState(state, StateSource.Manual, isFinal: false);
            _isPreviewActive = true;
            _previewIsReset = true;
            _previewDesign = null;
            _previewIsAutomation = false;
            _previewIsEquipment = false;
            _previewIsFullDesignToSelf = false;
            _previewIsFullDesignToTarget = false;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview reset failed: {ex.Message}");
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
