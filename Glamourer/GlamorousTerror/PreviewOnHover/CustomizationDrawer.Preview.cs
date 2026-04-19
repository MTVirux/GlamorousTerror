using ImSharp;

namespace Glamourer.Gui.Customization;

public sealed partial class CustomizationDrawer
{
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

            if (_iconHoveredValue.Value != 0)
                previewService.HandleCustomizationPopupFrame(state, _iconPopupIndex, (int)_iconHoveredValue.Value, _iconHoveredValue, Im.Io.KeyControl);
            else
                previewService.HandleCustomizationPopupFrame(state, _iconPopupIndex, null, default, Im.Io.KeyControl);

            if (_iconSelectionMade)
            {
                previewService.MarkPopupSelectionMade();
                previewService.EndSingleValuePreview(wasSelectionMade: true);
                _iconSelectionMade = false;
            }
        }
    }

    private void ApplyListHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        if (_listPopupOpen)
        {
            previewService.StartSingleCustomizationPreview(state, _listPopupIndex, requiresCtrl: true);

            if (_listHoveredValue.Value != 0)
                previewService.HandleCustomizationPopupFrame(state, _listPopupIndex, (int)_listHoveredValue.Value, _listHoveredValue, Im.Io.KeyControl);
            else
                previewService.HandleCustomizationPopupFrame(state, _listPopupIndex, null, default, Im.Io.KeyControl);

            if (_listSelectionMade)
            {
                previewService.MarkPopupSelectionMade();
                previewService.EndSingleValuePreview(wasSelectionMade: true);
                _listSelectionMade = false;
            }
        }
    }

    private void ApplyColorHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        if (_colorPopupOpen)
        {
            previewService.StartSingleCustomizationPreview(state, _colorPopupIndex, requiresCtrl: true);

            if (_colorHoveredValue.Value != 0)
                previewService.HandleCustomizationPopupFrame(state, _colorPopupIndex, (int)_colorHoveredValue.Value, _colorHoveredValue, Im.Io.KeyControl);
            else
                previewService.HandleCustomizationPopupFrame(state, _colorPopupIndex, null, default, Im.Io.KeyControl);

            if (_colorSelectionMade)
            {
                previewService.MarkPopupSelectionMade();
                previewService.EndSingleValuePreview(wasSelectionMade: true);
                _colorSelectionMade = false;
            }
        }
    }
}
