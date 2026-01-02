using Glamourer.Automation;
using Glamourer.Designs;
using Glamourer.Interop.Material;
using Glamourer.State;
using OtterGui.Services;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Structs;

namespace Glamourer.Services;

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
    /// <summary> Single item preview in equipment drawer combos. </summary>
    SingleItem,
    /// <summary> Single customization preview in customization drawer popups. </summary>
    SingleCustomization,
    /// <summary> Single stain preview in stain combos. </summary>
    SingleStain,
}

/// <summary>
/// Identifies which customization popup type is currently active.
/// </summary>
public enum PopupType
{
    None,
    Icon,
    Color,
    List,
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

    // Single-value preview tracking
    public EquipItem? OriginalItem { get; private set; }
    public EquipSlot? OriginalItemSlot { get; private set; }
    public BonusItemFlag? OriginalBonusSlot { get; private set; }
    public CustomizeIndex OriginalCustomizeIndex { get; private set; }
    public CustomizeValue OriginalCustomizeValue { get; private set; }
    public StainIds OriginalStain { get; set; }
    public EquipSlot OriginalStainSlot { get; set; }
    public int OriginalStainIndex { get; set; }
    public bool StainSelectionMade { get; set; }

    // Popup preview tracking (for CustomizationDrawer popups)
    public bool PopupActiveThisFrame { get; set; }
    public PopupType ActivePopupType { get; set; }
    public bool PopupSelectionMade { get; set; }
    public int? PopupHoveredIndex { get; set; }
    public CustomizeValue PopupHoveredValue { get; set; }
    public bool RequiresCtrl { get; private set; }

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
    /// Starts a single item preview, saving the original item value.
    /// </summary>
    public void StartSingleItem(ActorState state, EquipSlot slot, EquipItem originalItem)
    {
        Start(state, PreviewType.SingleItem, false);
        OriginalItem = originalItem;
        OriginalItemSlot = slot;
        OriginalBonusSlot = null;
    }

    /// <summary>
    /// Starts a single bonus item preview, saving the original bonus item value.
    /// </summary>
    public void StartSingleBonusItem(ActorState state, BonusItemFlag slot, EquipItem originalItem)
    {
        Start(state, PreviewType.SingleItem, false);
        OriginalItem = originalItem;
        OriginalItemSlot = null;
        OriginalBonusSlot = slot;
    }

    /// <summary>
    /// Starts a single customization preview, saving the original customize value.
    /// </summary>
    /// <param name="requiresCtrl">If true, preview only applies when CTRL is held.</param>
    public void StartSingleCustomization(ActorState state, CustomizeIndex index, CustomizeValue originalValue, bool requiresCtrl = false)
    {
        Start(state, PreviewType.SingleCustomization, false);
        OriginalCustomizeIndex = index;
        OriginalCustomizeValue = originalValue;
        RequiresCtrl = requiresCtrl;
        PopupActiveThisFrame = false;
        PopupSelectionMade = false;
        PopupHoveredIndex = null;
    }

    /// <summary>
    /// Starts a single stain preview, saving the original stain value.
    /// </summary>
    public void StartSingleStain(ActorState state, EquipSlot slot, int stainIndex, StainIds originalStain)
    {
        Start(state, PreviewType.SingleStain, false);
        OriginalStainSlot = slot;
        OriginalStainIndex = stainIndex;
        OriginalStain = originalStain;
        StainSelectionMade = false;
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
    /// Checks if we're previewing a single item in the given slot.
    /// </summary>
    public bool IsSingleItemPreview(EquipSlot slot)
        => IsActive && Type == PreviewType.SingleItem && OriginalItemSlot == slot;

    /// <summary>
    /// Checks if we're previewing a single bonus item in the given slot.
    /// </summary>
    public bool IsSingleBonusItemPreview(BonusItemFlag slot)
        => IsActive && Type == PreviewType.SingleItem && OriginalBonusSlot == slot;

    /// <summary>
    /// Checks if we're previewing a single customization at the given index.
    /// </summary>
    public bool IsSingleCustomizationPreview(CustomizeIndex index)
        => IsActive && Type == PreviewType.SingleCustomization && OriginalCustomizeIndex == index;

    /// <summary>
    /// Checks if we're previewing a single stain in the given slot and index.
    /// </summary>
    public bool IsSingleStainPreview(EquipSlot slot, int stainIndex)
        => OriginalStainSlot == slot && OriginalStainIndex == stainIndex && OriginalStainSlot != EquipSlot.Unknown;

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
        // Clear single-value tracking
        OriginalItem = null;
        OriginalItemSlot = null;
        OriginalBonusSlot = null;
        OriginalCustomizeIndex = default;
        OriginalCustomizeValue = default;
        OriginalStain = default;
        OriginalStainSlot = default;
        OriginalStainIndex = default;
        StainSelectionMade = false;
        // Clear popup tracking
        PopupActiveThisFrame = false;
        ActivePopupType = PopupType.None;
        PopupSelectionMade = false;
        PopupHoveredIndex = null;
        PopupHoveredValue = default;
        RequiresCtrl = false;
    }
}

/// <summary>
/// Unified service for handling design previews across the application.
/// Provides standardized methods for starting, ending, and managing preview states.
/// </summary>
public sealed class PreviewService(
    StateManager stateManager,
    ActorObjectManager objects,
    DesignConverter designConverter,
    ActorManager actorManager,
    AutoDesignApplier autoDesignApplier) : IService
{
    /// <summary> The current preview state. </summary>
    public PreviewState State { get; } = new();

    /// <summary> Whether a preview is currently active. </summary>
    public bool IsPreviewActive => State.IsActive;

    /// <summary> The currently previewed design, if any. </summary>
    public Design? PreviewedDesign => State.Design;

    #region Design Preview (for DesignFileSystemSelector compatibility)

    /// <summary>
    /// Apply a design preview to the current in-game target.
    /// If the same design is already being previewed, this does nothing.
    /// If a different design is being previewed, the original state is restored first.
    /// </summary>
    /// <param name="design">The design to preview.</param>
    /// <returns>True if the preview was applied successfully.</returns>
    public bool ApplyDesignPreview(Design design)
    {
        // Check if we're already previewing this exact design
        if (State.IsSameDesignPreview(design))
            return true;

        try
        {
            // Get the current in-game target
            var target = objects.Target;
            if (!target.Valid)
                return false;

            var identifier = actorManager.FromObject(target, out _, true, false, false);
            if (!identifier.IsValid)
                return false;

            if (!stateManager.GetOrCreate(identifier, target, out var state))
                return false;

            // Handle actor switching: restore previous actor first if needed
            if (State.IsActive && State.TargetState != state)
            {
                RestoreToOriginalState();
                State.End();
            }

            // Restore to original before applying new preview if same actor
            if (State.IsActive)
                RestoreToOriginalState();

            State.StartDesign(state, design);
            stateManager.ApplyDesign(state, design, ApplySettings.ManualWithLinks with { IsFinal = false, ResetMaterials = true });
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview design failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Revert any active preview and restore the original state.
    /// </summary>
    public void RevertPreview()
    {
        if (!State.IsActive || State.TargetState == null)
            return;

        try
        {
            RestoreToOriginalState();
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Revert preview failed: {ex.Message}");
        }
        finally
        {
            State.End();
        }
    }

    /// <summary>
    /// Finalize the current preview, making it a permanent change.
    /// Call this when the user clicks to select a design after previewing.
    /// </summary>
    public void FinalizePreview()
    {
        if (!State.IsActive || State.TargetState == null)
            return;

        try
        {
            // Apply the final action based on preview type
            switch (State.Type)
            {
                case PreviewType.Design when State.Design != null:
                    stateManager.ApplyDesign(State.TargetState, State.Design, ApplySettings.ManualWithLinks with { IsFinal = true });
                    break;
                // Other types can be added as needed
            }
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Finalize preview failed: {ex.Message}");
        }
        finally
        {
            State.End();
        }
    }

    #endregion

    #region Equipment Preview

    /// <summary>
    /// Starts an equipment preview with proper state capture.
    /// </summary>
    /// <param name="sourceActor">The actor to copy equipment from.</param>
    /// <param name="slot">The equipment slot to preview (EquipSlot.Unknown for all).</param>
    /// <param name="isBonusItem">Whether this is a bonus item (e.g., glasses).</param>
    /// <param name="toSelf">True to apply to player, false to apply to target.</param>
    /// <returns>True if preview started successfully.</returns>
    public bool StartEquipmentPreview(Actor sourceActor, EquipSlot slot, bool isBonusItem, bool toSelf)
    {
        if (State.IsSameEquipmentPreview(slot, isBonusItem, toSelf))
            return true;

        try
        {
            // Determine target state first
            ActorState? state = null;
            if (toSelf)
            {
                var (playerId, playerData) = objects.PlayerData;
                if (!playerData.Valid) return false;
                if (!stateManager.GetOrCreate(playerId, playerData.Objects[0], out state)) return false;
            }
            else
            {
                if (!sourceActor.Valid) return false;
                var identifier = actorManager.FromObject(sourceActor, out _, true, false, false);
                if (!identifier.IsValid) return false;
                if (!stateManager.GetOrCreate(identifier, sourceActor, out state)) return false;
            }

            if (state == null) return false;

            // Handle actor switching
            if (State.IsActive && State.TargetState != state)
            {
                RestoreToOriginalState();
                State.End();
            }
            else if (State.IsActive)
            {
                RestoreToOriginalState();
            }

            // Capture source data after any previews have been reverted
            DesignData sourceData;
            if (toSelf)
            {
                sourceData = stateManager.FromActor(sourceActor, true, false);
            }
            else
            {
                var (_, playerData) = objects.PlayerData;
                if (!playerData.Valid) return false;
                sourceData = stateManager.FromActor(playerData.Objects[0], true, false);
            }

            ApplicationCollection collection;
            if (isBonusItem)
                collection = new ApplicationCollection(0, BonusItemFlag.Glasses, CustomizeFlag.BodyType, 0, 0, 0);
            else if (slot == EquipSlot.Unknown)
                collection = ApplicationCollection.Equipment;
            else
                collection = new ApplicationCollection(slot.ToBothFlags(), 0, CustomizeFlag.BodyType, 0, 0, 0);

            var tempDesign = designConverter.Convert(sourceData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            State.StartEquipment(state, slot, isBonusItem, toSelf);
            stateManager.ApplyDesign(state, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview equipment failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Appearance Preview

    /// <summary>
    /// Starts an appearance preview with proper state capture.
    /// </summary>
    /// <param name="sourceActor">The actor to copy appearance from.</param>
    /// <param name="flag">The customize flags to preview.</param>
    /// <param name="toSelf">True to apply to player, false to apply to target.</param>
    /// <returns>True if preview started successfully.</returns>
    public bool StartAppearancePreview(Actor sourceActor, CustomizeFlag flag, bool toSelf)
    {
        if (State.IsSameAppearancePreview(flag, toSelf))
            return true;

        try
        {
            // Determine target state first
            ActorState? state = null;
            if (toSelf)
            {
                var (playerId, playerData) = objects.PlayerData;
                if (!playerData.Valid) return false;
                if (!stateManager.GetOrCreate(playerId, playerData.Objects[0], out state)) return false;
            }
            else
            {
                if (!sourceActor.Valid) return false;
                var identifier = actorManager.FromObject(sourceActor, out _, true, false, false);
                if (!identifier.IsValid) return false;
                if (!stateManager.GetOrCreate(identifier, sourceActor, out state)) return false;
            }

            if (state == null) return false;

            // Handle actor switching
            if (State.IsActive && State.TargetState != state)
            {
                RestoreToOriginalState();
                State.End();
            }
            else if (State.IsActive)
            {
                RestoreToOriginalState();
            }

            // Capture source data after any previews have been reverted
            DesignData sourceData;
            if (toSelf)
            {
                sourceData = stateManager.FromActor(sourceActor, true, false);
            }
            else
            {
                var (_, playerData) = objects.PlayerData;
                if (!playerData.Valid) return false;
                sourceData = stateManager.FromActor(playerData.Objects[0], true, false);
            }

            var collection = flag == 0
                ? ApplicationCollection.Customizations
                : new ApplicationCollection(0, 0, flag, 0, 0, 0);

            var tempDesign = designConverter.Convert(sourceData, new StateMaterialManager(),
                new ApplicationRules(collection, false));

            State.StartAppearance(state, flag, toSelf);
            stateManager.ApplyDesign(state, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview appearance failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Full Design Preview

    /// <summary>
    /// Starts a full design to self preview (copy target's appearance to player).
    /// </summary>
    /// <param name="targetActor">The actor to copy from.</param>
    /// <returns>True if preview started successfully.</returns>
    public bool StartFullDesignToSelfPreview(Actor targetActor)
    {
        if (State.IsSameTypePreview(PreviewType.FullDesignToSelf))
            return true;

        try
        {
            var (playerId, playerData) = objects.PlayerData;
            if (!playerData.Valid) return false;
            if (!stateManager.GetOrCreate(playerId, playerData.Objects[0], out var playerState)) return false;
            if (!targetActor.Valid) return false;

            // Handle actor switching
            if (State.IsActive && State.TargetState != playerState)
            {
                RestoreToOriginalState();
                State.End();
            }
            else if (State.IsActive)
            {
                RestoreToOriginalState();
            }

            var targetData = stateManager.FromActor(targetActor, true, false);

            State.Start(playerState, PreviewType.FullDesignToSelf, toSelf: true);
            var tempDesign = designConverter.Convert(targetData, new StateMaterialManager(), ApplicationRules.All);
            stateManager.ApplyDesign(playerState, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview full design to self failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a full design to target preview (copy player's appearance to target).
    /// </summary>
    /// <param name="targetActor">The actor to apply to.</param>
    /// <returns>True if preview started successfully.</returns>
    public bool StartFullDesignToTargetPreview(Actor targetActor)
    {
        if (State.IsSameTypePreview(PreviewType.FullDesignToTarget))
            return true;

        try
        {
            if (!targetActor.Valid) return false;
            var identifier = actorManager.FromObject(targetActor, out _, true, false, false);
            if (!identifier.IsValid) return false;
            if (!stateManager.GetOrCreate(identifier, targetActor, out var targetState)) return false;

            var (_, playerData) = objects.PlayerData;
            if (!playerData.Valid) return false;

            // Handle actor switching
            if (State.IsActive && State.TargetState != targetState)
            {
                RestoreToOriginalState();
                State.End();
            }
            else if (State.IsActive)
            {
                RestoreToOriginalState();
            }

            var playerDesignData = stateManager.FromActor(playerData.Objects[0], true, false);

            State.Start(targetState, PreviewType.FullDesignToTarget, toSelf: false);
            var tempDesign = designConverter.Convert(playerDesignData, new StateMaterialManager(), ApplicationRules.All);
            stateManager.ApplyDesign(targetState, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview full design to target failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Automation Preview

    /// <summary>
    /// Starts an automation state preview.
    /// </summary>
    /// <param name="targetActor">The actor to preview automation on.</param>
    /// <returns>True if preview started successfully.</returns>
    public bool StartAutomationPreview(Actor targetActor)
    {
        if (State.IsSameTypePreview(PreviewType.Automation))
            return true;

        try
        {
            if (!targetActor.Valid) return false;
            var targetIdentifier = targetActor.GetIdentifier(objects.Actors);
            if (!stateManager.GetOrCreate(targetIdentifier, targetActor, out var state)) return false;

            // Handle actor switching
            if (State.IsActive && State.TargetState != state)
            {
                RestoreToOriginalState();
                State.End();
            }
            else if (State.IsActive)
            {
                RestoreToOriginalState();
            }

            State.Start(state, PreviewType.Automation);
            autoDesignApplier.ReapplyAutomation(targetActor, targetIdentifier, state, true, false, out var forcedRedraw);
            stateManager.ReapplyAutomationState(targetActor, state, forcedRedraw, true, StateSource.Manual);
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview automation failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Reset Preview

    /// <summary>
    /// Starts a reset to game state preview.
    /// </summary>
    /// <param name="targetActor">The actor to preview reset on.</param>
    /// <returns>True if preview started successfully.</returns>
    public bool StartResetPreview(Actor targetActor)
    {
        if (State.IsSameTypePreview(PreviewType.Reset))
            return true;

        try
        {
            if (!targetActor.Valid) return false;
            var identifier = actorManager.FromObject(targetActor, out _, true, false, false);
            if (!identifier.IsValid) return false;
            if (!stateManager.GetOrCreate(identifier, targetActor, out var state)) return false;

            // Handle actor switching
            if (State.IsActive && State.TargetState != state)
            {
                RestoreToOriginalState();
                State.End();
            }
            else if (State.IsActive)
            {
                RestoreToOriginalState();
            }

            State.Start(state, PreviewType.Reset);
            stateManager.ResetState(state, StateSource.Manual, isFinal: false);
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Preview reset failed: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Single-Value Preview (for Equipment/Customization Drawer combos)

    /// <summary>
    /// Starts a single item preview. Call when combo opens to save original item.
    /// </summary>
    /// <param name="state">The actor state to preview on.</param>
    /// <param name="slot">The equipment slot.</param>
    /// <returns>True if preview was started successfully.</returns>
    public bool StartSingleItemPreview(ActorState state, EquipSlot slot)
    {
        if (State.IsSingleItemPreview(slot))
            return true;

        try
        {
            // If another preview is active, end it first
            if (State.IsActive)
            {
                RestoreSingleValuePreview();
                State.End();
            }

            var originalItem = state.ModelData.Item(slot);
            State.StartSingleItem(state, slot, originalItem);
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Start single item preview failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a single bonus item preview. Call when combo opens to save original item.
    /// </summary>
    /// <param name="state">The actor state to preview on.</param>
    /// <param name="slot">The bonus item slot.</param>
    /// <returns>True if preview was started successfully.</returns>
    public bool StartSingleBonusItemPreview(ActorState state, BonusItemFlag slot)
    {
        if (State.IsSingleBonusItemPreview(slot))
            return true;

        try
        {
            // If another preview is active, end it first
            if (State.IsActive)
            {
                RestoreSingleValuePreview();
                State.End();
            }

            var originalItem = state.ModelData.BonusItem(slot);
            State.StartSingleBonusItem(state, slot, originalItem);
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Start single bonus item preview failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a single customization preview. Call when popup opens to save original value.
    /// </summary>
    /// <param name="state">The actor state to preview on.</param>
    /// <param name="index">The customization index.</param>
    /// <param name="requiresCtrl">If true, preview only applies when CTRL is held.</param>
    /// <returns>True if preview was started successfully.</returns>
    public bool StartSingleCustomizationPreview(ActorState state, CustomizeIndex index, bool requiresCtrl = false)
    {
        if (State.IsSingleCustomizationPreview(index))
            return true;

        try
        {
            // If another preview is active, end it first
            if (State.IsActive)
            {
                RestoreSingleValuePreview();
                State.End();
            }

            var originalValue = state.ModelData.Customize[index];
            State.StartSingleCustomization(state, index, originalValue, requiresCtrl);
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Start single customization preview failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a single stain preview. Call when combo opens to save original stain.
    /// </summary>
    /// <param name="state">The actor state to preview on.</param>
    /// <param name="slot">The equipment slot.</param>
    /// <param name="stainIndex">The index in the stain array (for multi-stain items).</param>
    /// <returns>True if preview was started successfully.</returns>
    public bool StartSingleStainPreview(ActorState state, EquipSlot slot, int stainIndex)
    {
        if (State.IsSingleStainPreview(slot, stainIndex))
            return true;

        try
        {
            // If another preview is active, end it first
            if (State.IsActive)
            {
                RestoreSingleValuePreview();
                State.End();
            }

            var originalStain = state.ModelData.Stain(slot);
            State.StartSingleStain(state, slot, stainIndex, originalStain);
            return true;
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Start single stain preview failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Applies a previewed item change. Call when hovering an item in the combo.
    /// </summary>
    /// <param name="state">The actor state.</param>
    /// <param name="slot">The equipment slot.</param>
    /// <param name="item">The item to preview.</param>
    public void PreviewSingleItem(ActorState state, EquipSlot slot, EquipItem item)
    {
        if (!State.IsSingleItemPreview(slot))
            return;

        var currentItem = state.ModelData.Item(slot);
        if (currentItem.ItemId != item.ItemId)
            stateManager.ChangeItem(state, slot, item, ApplySettings.Manual);
    }

    /// <summary>
    /// Applies a previewed bonus item change. Call when hovering an item in the combo.
    /// </summary>
    /// <param name="state">The actor state.</param>
    /// <param name="slot">The bonus item slot.</param>
    /// <param name="item">The item to preview.</param>
    public void PreviewSingleBonusItem(ActorState state, BonusItemFlag slot, EquipItem item)
    {
        if (!State.IsSingleBonusItemPreview(slot))
            return;

        var currentItem = state.ModelData.BonusItem(slot);
        if (currentItem.Id != item.Id)
            stateManager.ChangeBonusItem(state, slot, item, ApplySettings.Manual);
    }

    /// <summary>
    /// Applies a previewed customization change. Call when hovering an option in the popup.
    /// </summary>
    /// <param name="state">The actor state.</param>
    /// <param name="index">The customization index.</param>
    /// <param name="value">The value to preview.</param>
    public void PreviewSingleCustomization(ActorState state, CustomizeIndex index, CustomizeValue value)
    {
        if (!State.IsSingleCustomizationPreview(index))
            return;

        var current = state.ModelData.Customize[index];
        if (current != value)
            stateManager.ChangeCustomize(state, index, value, ApplySettings.Manual);
    }

    /// <summary>
    /// Applies a previewed stain change at a specific index. Call when hovering a stain in the combo.
    /// </summary>
    /// <param name="state">The actor state.</param>
    /// <param name="slot">The equipment slot.</param>
    /// <param name="stainIndex">The stain index in the StainIds array.</param>
    /// <param name="stainValue">The stain value to preview.</param>
    public void PreviewSingleStain(ActorState state, EquipSlot slot, int stainIndex, StainId stainValue)
    {
        if (!State.IsSingleStainPreview(slot, stainIndex))
            return;

        var currentStains = state.ModelData.Stain(slot);
        if (currentStains[stainIndex] != stainValue)
        {
            var newStains = currentStains.With(stainIndex, stainValue);
            stateManager.ChangeStains(state, slot, newStains, ApplySettings.Manual);
        }
    }

    /// <summary>
    /// Restores the original value for single-value previews.
    /// </summary>
    public void RestoreSingleValuePreview()
    {
        if (State.TargetState == null || !State.IsActive)
            return;

        try
        {
            switch (State.Type)
            {
                case PreviewType.SingleItem when State.OriginalItem.HasValue && State.OriginalItemSlot.HasValue:
                    var currentItem = State.TargetState.ModelData.Item(State.OriginalItemSlot.Value);
                    if (currentItem.ItemId != State.OriginalItem.Value.ItemId)
                        stateManager.ChangeItem(State.TargetState, State.OriginalItemSlot.Value, State.OriginalItem.Value, ApplySettings.Manual);
                    break;

                case PreviewType.SingleItem when State.OriginalItem.HasValue && State.OriginalBonusSlot.HasValue:
                    var currentBonus = State.TargetState.ModelData.BonusItem(State.OriginalBonusSlot.Value);
                    if (currentBonus.Id != State.OriginalItem.Value.Id)
                        stateManager.ChangeBonusItem(State.TargetState, State.OriginalBonusSlot.Value, State.OriginalItem.Value, ApplySettings.Manual);
                    break;

                case PreviewType.SingleCustomization:
                    var currentCustomize = State.TargetState.ModelData.Customize[State.OriginalCustomizeIndex];
                    if (currentCustomize != State.OriginalCustomizeValue)
                        stateManager.ChangeCustomize(State.TargetState, State.OriginalCustomizeIndex, State.OriginalCustomizeValue, ApplySettings.Manual);
                    break;

                case PreviewType.SingleStain:
                    var currentStain = State.TargetState.ModelData.Stain(State.OriginalStainSlot);
                    if (currentStain != State.OriginalStain)
                        stateManager.ChangeStains(State.TargetState, State.OriginalStainSlot, State.OriginalStain, ApplySettings.Manual);
                    break;
            }
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Restore single value preview failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ends a single-value preview, restoring the original value.
    /// Call when combo/popup closes without selection.
    /// </summary>
    /// <param name="wasSelectionMade">If true, don't restore - the selection is the new value.</param>
    public void EndSingleValuePreview(bool wasSelectionMade = false)
    {
        if (!State.IsActive)
            return;

        if (State.Type is not (PreviewType.SingleItem or PreviewType.SingleCustomization or PreviewType.SingleStain))
            return;

        try
        {
            if (!wasSelectionMade)
                RestoreSingleValuePreview();
        }
        finally
        {
            State.End();
        }
    }

    /// <summary>
    /// Handles per-frame popup preview logic for customization popups.
    /// Call this each frame while the popup is open.
    /// </summary>
    /// <param name="state">The actor state.</param>
    /// <param name="index">The customization index.</param>
    /// <param name="hoveredIndex">The currently hovered item index, or null if not hovering.</param>
    /// <param name="hoveredValue">The customization value of the hovered item.</param>
    /// <param name="ctrlHeld">Whether CTRL is currently held.</param>
    public void HandleCustomizationPopupFrame(ActorState state, CustomizeIndex index, int? hoveredIndex, CustomizeValue hoveredValue, bool ctrlHeld)
    {
        if (!State.IsSingleCustomizationPreview(index))
            return;

        State.PopupActiveThisFrame = true;
        State.PopupHoveredIndex = hoveredIndex;
        State.PopupHoveredValue = hoveredValue;

        // If hovering and (CTRL held or CTRL not required), apply preview
        if (hoveredIndex.HasValue && (!State.RequiresCtrl || ctrlHeld))
        {
            var current = state.ModelData.Customize[index];
            if (current != hoveredValue)
                stateManager.ChangeCustomize(state, index, hoveredValue, ApplySettings.Manual);
        }
        else
        {
            // Not hovering or CTRL not held when required: restore original if no selection made
            if (!State.PopupSelectionMade)
            {
                var current = state.ModelData.Customize[index];
                if (current != State.OriginalCustomizeValue)
                    stateManager.ChangeCustomize(state, index, State.OriginalCustomizeValue, ApplySettings.Manual);
            }
        }
    }

    /// <summary>
    /// Call at the end of each frame to check if popup closed.
    /// </summary>
    /// <param name="state">The actor state.</param>
    public void EndCustomizationPopupFrame(ActorState state)
    {
        if (!State.IsActive || State.Type != PreviewType.SingleCustomization)
            return;

        // If popup was not active this frame, it has closed
        if (!State.PopupActiveThisFrame)
        {
            // Restore original if no selection was made
            if (!State.PopupSelectionMade)
            {
                var current = state.ModelData.Customize[State.OriginalCustomizeIndex];
                if (current != State.OriginalCustomizeValue)
                    stateManager.ChangeCustomize(state, State.OriginalCustomizeIndex, State.OriginalCustomizeValue, ApplySettings.Manual);
            }
            State.End();
        }
        else
        {
            // Reset for next frame
            State.PopupActiveThisFrame = false;
        }
    }

    /// <summary>
    /// Marks that a selection was made in the popup.
    /// </summary>
    public void MarkPopupSelectionMade()
    {
        if (State.IsActive)
            State.PopupSelectionMade = true;
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Restores the character to their original state without clearing the stored original data.
    /// Used when switching between different preview types on the same actor.
    /// </summary>
    public void RestoreToOriginalState()
    {
        if (State.TargetState == null || !State.IsActive)
            return;

        try
        {
            var tempDesign = designConverter.Convert(State.OriginalData, State.OriginalMaterials ?? new StateMaterialManager(), ApplicationRules.All);
            stateManager.ApplyDesign(State.TargetState, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Restore to original state failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ends the preview, restoring the original state and clearing preview tracking.
    /// </summary>
    public void EndPreview()
    {
        if (!State.IsActive)
            return;

        try
        {
            RestoreToOriginalState();
        }
        finally
        {
            State.End();
        }
    }

    /// <summary>
    /// Ends the preview only if it matches the specified type.
    /// </summary>
    /// <param name="type">The preview type to check.</param>
    public void EndPreviewIfType(PreviewType type)
    {
        if (State.IsActive && State.Type == type)
            EndPreview();
    }

    /// <summary>
    /// Applies the current preview permanently (with IsFinal=true) without reverting first.
    /// If no preview is active, just runs the action normally.
    /// </summary>
    /// <param name="applyAction">The action to apply permanently.</param>
    public void ApplyPreviewPermanently(Action applyAction)
    {
        // Clear the preview state without restoring - the action will apply final changes
        State.End();

        // Now run the action which will apply with IsFinal=true
        applyAction();
    }

    #endregion
}
