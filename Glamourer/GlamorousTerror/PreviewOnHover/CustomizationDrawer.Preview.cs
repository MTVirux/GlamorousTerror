using ImSharp;

namespace Glamourer.Gui.Customization;

public sealed partial class CustomizationDrawer
{
    // Popup flag latching — see CLAUDE.md preview-on-hover invariant #1.
    // Im.Popup.Begin returns false once a popup closes, so the popup body never runs to set the
    // flag back to false. Without this reset, _xxxPopupOpen latches true and ApplyHoverPreview
    // keeps a SingleCustomization preview alive — its fall-through then re-applies the captured
    // OriginalCustomizeValue over external mutations like "Revert to Game".
    private partial void GTResetPopupFlags()
    {
        _iconPopupOpen  = false;
        _listPopupOpen  = false;
        _colorPopupOpen = false;
    }

    public void ApplyHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        if (_iconPopupOpen)
            ApplyIconHoverPreview(stateManager, state);
        else if (_listPopupOpen)
            ApplyListHoverPreview(stateManager, state);
        else if (_colorPopupOpen)
            ApplyColorHoverPreview(stateManager, state);
        else
            previewService.EndCustomizationPopupFrame(state);
    }

    private void ApplyIconHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        if (_iconPopupOpen)
        {
            previewService.StartSingleCustomizationPreview(state, _iconPopupIndex, requiresCtrl: true);

            if (_iconSelectionMade)
            {
                previewService.MarkPopupSelectionMade();
                previewService.EndSingleValuePreview(wasSelectionMade: true);
                _iconSelectionMade = false;
                return;
            }

            if (_iconHoveredValue.Value != 0)
                previewService.HandleCustomizationPopupFrame(state, _iconPopupIndex, (int)_iconHoveredValue.Value, _iconHoveredValue, Im.Io.KeyControl);
            else
                previewService.HandleCustomizationPopupFrame(state, _iconPopupIndex, null, default, Im.Io.KeyControl);
        }
    }

    private void ApplyListHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        if (_listPopupOpen)
        {
            previewService.StartSingleCustomizationPreview(state, _listPopupIndex, requiresCtrl: true);

            if (_listSelectionMade)
            {
                previewService.MarkPopupSelectionMade();
                previewService.EndSingleValuePreview(wasSelectionMade: true);
                _listSelectionMade = false;
                return;
            }

            if (_listHoveredValue.Value != 0)
                previewService.HandleCustomizationPopupFrame(state, _listPopupIndex, (int)_listHoveredValue.Value, _listHoveredValue, Im.Io.KeyControl);
            else
                previewService.HandleCustomizationPopupFrame(state, _listPopupIndex, null, default, Im.Io.KeyControl);
        }
    }

    private void ApplyColorHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        if (_colorPopupOpen)
        {
            previewService.StartSingleCustomizationPreview(state, _colorPopupIndex, requiresCtrl: true);

            if (_colorSelectionMade)
            {
                previewService.MarkPopupSelectionMade();
                previewService.EndSingleValuePreview(wasSelectionMade: true);
                _colorSelectionMade = false;
                return;
            }

            if (_colorHoveredValue.Value != 0)
                previewService.HandleCustomizationPopupFrame(state, _colorPopupIndex, (int)_colorHoveredValue.Value, _colorHoveredValue, Im.Io.KeyControl);
            else
                previewService.HandleCustomizationPopupFrame(state, _colorPopupIndex, null, default, Im.Io.KeyControl);
        }
    }
}
