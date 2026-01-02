using Glamourer.Designs;
using Glamourer.Interop.Material;
using Glamourer.State;
using OtterGui.Services;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Interop;

namespace Glamourer.Services;

/// <summary>
/// Service to handle temporary design preview state.
/// This is used when hovering over designs in the UI to preview them on the current target
/// without making permanent changes.
/// </summary>
public sealed class DesignPreviewService(
    StateManager stateManager,
    ActorObjectManager objects,
    ActorManager actorManager,
    DesignConverter designConverter) : IService
{
    // Preview tracking state
    private bool _isPreviewActive;
    private Design? _previewedDesign;
    private ActorState? _previewOriginalState;
    private DesignData _previewOriginalData;
    private StateMaterialManager? _previewOriginalMaterials;
    private bool _hasOriginalData;

    /// <summary>
    /// Gets whether a preview is currently active.
    /// </summary>
    public bool IsPreviewActive => _isPreviewActive;

    /// <summary>
    /// Gets the currently previewed design, if any.
    /// </summary>
    public Design? PreviewedDesign => _previewedDesign;

    /// <summary>
    /// Apply a design preview to the current in-game target.
    /// If the same design is already being previewed, this does nothing.
    /// If a different design is being previewed, the original state is restored first.
    /// </summary>
    /// <param name="design">The design to preview.</param>
    /// <returns>True if the preview was applied successfully.</returns>
    public bool ApplyPreview(Design design)
    {
        // Check if we're already previewing this exact design
        if (_isPreviewActive && _previewedDesign == design)
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

            // If we have original data but for a different actor, restore that actor first then capture new original
            if (_hasOriginalData && _previewOriginalState != state)
            {
                RestoreOriginalState();
                // Now capture the new actor's state
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
            }
            else if (!_hasOriginalData)
            {
                // Capture the original state before first preview
                _previewOriginalState = state;
                _previewOriginalData = state.ModelData;
                _previewOriginalMaterials = state.Materials.Clone();
                _hasOriginalData = true;
            }
            else
            {
                // Same actor, just restore to original before applying new preview
                RestoreOriginalState();
            }

            // Apply the design with IsFinal = false to indicate this is a temporary preview
            stateManager.ApplyDesign(state, design, ApplySettings.ManualWithLinks with { IsFinal = false });
            _isPreviewActive = true;
            _previewedDesign = design;
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
        if (!_isPreviewActive || _previewOriginalState == null || !_hasOriginalData)
            return;

        try
        {
            RestoreOriginalState();
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Revert preview failed: {ex.Message}");
        }
        finally
        {
            ClearPreviewState();
        }
    }

    /// <summary>
    /// Finalize the current preview, making it a permanent change.
    /// Call this when the user clicks to select a design after previewing.
    /// </summary>
    public void FinalizePreview()
    {
        if (!_isPreviewActive || _previewedDesign == null || _previewOriginalState == null)
            return;

        try
        {
            // Apply the design again with IsFinal = true to make it permanent
            stateManager.ApplyDesign(_previewOriginalState, _previewedDesign, ApplySettings.ManualWithLinks with { IsFinal = true });
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Finalize preview failed: {ex.Message}");
        }
        finally
        {
            ClearPreviewState();
        }
    }

    /// <summary>
    /// Restore the original state without clearing the preview tracking.
    /// Used internally when switching between previews.
    /// </summary>
    private void RestoreOriginalState()
    {
        if (_previewOriginalState == null || !_hasOriginalData)
            return;

        try
        {
            // Convert the original data back to a design and apply it
            // Use ResetMaterials = true so ApplyDesign clears existing materials
            var tempDesign = designConverter.Convert(_previewOriginalData, new StateMaterialManager(), ApplicationRules.All);
            stateManager.ApplyDesign(_previewOriginalState, tempDesign, ApplySettings.Manual with { IsFinal = false, ResetMaterials = true });

            // After ApplyDesign clears materials, manually restore the original materials
            // This preserves the full MaterialValueState including Game and DrawData fields
            // which would be lost if we converted through the design system
            if (_previewOriginalMaterials is { } materials)
            {
                foreach (var (key, value) in materials.Values)
                    _previewOriginalState.Materials.AddOrUpdateValue(key, value);
            }
        }
        catch (Exception ex)
        {
            Glamourer.Log.Debug($"Restore original state failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Clear all preview tracking state.
    /// </summary>
    private void ClearPreviewState()
    {
        _isPreviewActive = false;
        _previewedDesign = null;
        _previewOriginalState = null;
        _previewOriginalMaterials = null;
        _hasOriginalData = false;
    }
}
