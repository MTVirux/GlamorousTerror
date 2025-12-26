using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Glamourer.GameData;
using Dalamud.Bindings.ImGui;
using OtterGui;
using OtterGui.Raii;
using OtterGui.Text;
using OtterGui.Text.EndObjects;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using System;

namespace Glamourer.Gui.Customization;

public partial class CustomizationDrawer
{
    private const string         ColorPickerPopupName = "ColorPicker";
    private       CustomizeValue _draggedColorValue;
    private       CustomizeIndex _draggedColorType;

    // State for color picker popup hover preview
    private bool           _colorPopupOpen;
    private bool           _colorPopupActiveThisFrame;
    private bool           _colorPopupSelectionMade;
    private CustomizeIndex _colorPopupIndex;
    private CustomizeValue _colorPopupOriginalValue;
    private int?           _colorPopupHoveredIndex;
    private CustomizeValue _colorPopupHoveredValue;


    private void DrawDragDropSource(CustomizeIndex index, CustomizeData custom)
    {
        using var dragDropSource = ImUtf8.DragDropSource();
        if (!dragDropSource)
            return;

        if (!DragDropSource.SetPayload("##colorDragDrop"u8))
            _draggedColorValue = _customize[index];
        ImUtf8.Text(
            $"Dragging {(custom.Color == 0 ? $"{_currentOption} (NPC)" : _currentOption)} #{_draggedColorValue.Value}...");
        _draggedColorType = index;
    }

    private void DrawDragDropTarget(CustomizeIndex index)
    {
        using var dragDropTarget = ImUtf8.DragDropTarget();
        if (!dragDropTarget.Success || !dragDropTarget.IsDropping("##colorDragDrop"u8))
            return;
        
        var idx       = _set.DataByValue(_draggedColorType, _draggedColorValue, out var draggedData, _customize.Face);
        var bestMatch = _draggedColorValue;
        if (draggedData.HasValue)
        {
            var draggedColor = draggedData.Value.Color;
            var targetData   = _set.Data(index, idx);
            if (targetData.Color != draggedColor)
            {
                var bestDiff = Diff(targetData.Color, draggedColor);
                var count    = _set.Count(index);
                for (var i = 0; i < count; ++i)
                {
                    targetData = _set.Data(index, i);
                    if (targetData.Color == draggedColor)
                    {
                        UpdateValue(_draggedColorValue);
                        return;
                    }

                    var diff = Diff(targetData.Color, draggedColor);
                    if (diff >= bestDiff)
                        continue;

                    bestDiff  = diff;
                    bestMatch = (CustomizeValue)i;
                }
            }
        }

        UpdateValue(bestMatch);
        return;

        static uint Diff(uint color1, uint color2)
        {
            var r = (color1 & 0xFF) - (color2 & 0xFF);
            var g = ((color1 >> 8) & 0xFF) - ((color2 >> 8) & 0xFF);
            var b = ((color1 >> 16) & 0xFF) - ((color2 >> 16) & 0xFF);
            return 30 * r * r + 59 * g * g + 11 * b * b;
        }
    }

    private void DrawColorPicker(CustomizeIndex index)
    {
        using var id = SetId(index);
        var (current, custom) = GetCurrentCustomization(index);

        var color = ImGui.ColorConvertU32ToFloat4(current < 0 ? ImGui.GetColorU32(ImGuiCol.FrameBg) : custom.Color);

        using (_ = ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 2 * ImGuiHelpers.GlobalScale, current < 0))
        {
            if (ImGui.ColorButton($"{_customize[index].Value}##color", color, ImGuiColorEditFlags.NoDragDrop, _framedIconSize))
            {
                ImGui.OpenPopup(ColorPickerPopupName);
            }
            else if (current >= 0 && !_locked && CaptureMouseWheel(ref current, 0, _currentCount))
            {
                var data = _set.Data(_currentIndex, current, _customize.Face);
                UpdateValue(data.Value);
            }

            DrawDragDropSource(index, custom);
            DrawDragDropTarget(index);
        }

        var npc = false;
        if (current < 0)
        {
            using var font = ImRaii.PushFont(UiBuilder.IconFont);
            var       size = ImGui.CalcTextSize(FontAwesomeIcon.Question.ToIconString());
            var       pos  = ImGui.GetItemRectMin() + (ImGui.GetItemRectSize() - size) / 2;
            ImGui.GetWindowDrawList().AddText(pos, ImGui.GetColorU32(ImGuiCol.Text), FontAwesomeIcon.Question.ToIconString());
            current = 0;
            npc     = true;
        }

        ImGui.SameLine();

        using (_ = ImRaii.Group())
        {
            DataInputInt(current, npc);
            if (_withApply)
            {
                ApplyCheckbox();
                ImGui.SameLine();
            }

            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(custom.Color == 0 ? $"{_currentOption} (NPC)" : _currentOption);
        }

        DrawColorPickerPopup(current);
    }

    private void DrawColorPickerPopup(int current)
    {
        using var popup = ImRaii.Popup(ColorPickerPopupName, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
            return;

        // Mark popup as active this frame and initialize open state.
        _colorPopupActiveThisFrame = true;
        if (!_colorPopupOpen)
        {
            _colorPopupOpen = true;
            _colorPopupIndex = _currentIndex;
            _colorPopupOriginalValue = _customize[_currentIndex];
            _colorPopupSelectionMade = false;
        }

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero)
            .Push(ImGuiStyleVar.FrameRounding, 0);
        var prevValue = _customize[_currentIndex];
        for (var i = 0; i < _currentCount; ++i)
        {
            var custom = _set.Data(_currentIndex, i, _customize[CustomizeIndex.Face]);
            if (ImGui.ColorButton(custom.Value.ToString(), ImGui.ColorConvertU32ToFloat4(custom.Color)) && !_locked)
            {
                // If the clicked option is already selected, close the popup.
                if (custom.Value == prevValue)
                {
                    ImGui.CloseCurrentPopup();
                }
                else
                {
                    UpdateValue(custom.Value);
                    // Mark that a selection was made inside the popup so we don't revert on close.
                    _colorPopupSelectionMade = true;
                    ImGui.CloseCurrentPopup();
                }
            }

            // Track hovered option for previewing.
            if (ImGui.IsItemHovered())
            {
                _colorPopupHoveredIndex = i;
                _colorPopupHoveredValue = custom.Value;
                ImGuiUtil.HoverTooltip("Hold CTRL to preview on actor.");
            }
            else if (_colorPopupHoveredIndex == i)
            {
                _colorPopupHoveredIndex = null;
            }

            if (i == current)
            {
                var size = ImGui.GetItemRectSize();
                ImGui.GetWindowDrawList()
                    .AddCircleFilled(ImGui.GetItemRectMin() + size / 2, size.X / 4, ImGuiUtil.ContrastColorBw(custom.Color));
            }

            if (i % 8 != 7)
                ImGui.SameLine();
        }
    }

    // Obtain the current customization and print a warning if it is not known.
    private (int, CustomizeData) GetCurrentCustomization(CustomizeIndex index)
    {
        var current = _set.DataByValue(index, _customize[index], out var custom, _customize.Face);
        if (_set.IsAvailable(index) && current < 0)
            return (current, new CustomizeData(index, _customize[index]));

        return (current, custom!.Value);
    }

    /// <summary>
    /// Apply hover preview changes for color picker popups (hair color, lip color, etc.).
    /// This will preview the hovered color while holding Control, and restore
    /// the original value when the popup closes or when not hovering.
    /// </summary>
    private void ApplyColorHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        // If popup was active this frame, handle preview or restoration while open.
        if (_colorPopupActiveThisFrame)
        {
            // If hovering an option and Ctrl is held, apply preview.
            if (_colorPopupHoveredIndex.HasValue && ImGui.GetIO().KeyCtrl)
            {
                var current = state.ModelData.Customize[_colorPopupIndex];
                if (current != _colorPopupHoveredValue)
                    stateManager.ChangeCustomize(state, _colorPopupIndex, _colorPopupHoveredValue, Designs.ApplySettings.Manual);
            }
            else
            {
                // Not hovering or Ctrl not held: restore original while popup open if no selection was made.
                if (!_colorPopupSelectionMade)
                {
                    var current = state.ModelData.Customize[_colorPopupIndex];
                    if (current != _colorPopupOriginalValue)
                        stateManager.ChangeCustomize(state, _colorPopupIndex, _colorPopupOriginalValue, Designs.ApplySettings.Manual);
                }
            }

            // Reset per-frame active marker for next frame.
            _colorPopupActiveThisFrame = false;
            return;
        }

        // Popup was open previously but not active this frame -> it has been closed.
        if (_colorPopupOpen)
        {
            // If no selection was made inside the popup, restore original value.
            if (!_colorPopupSelectionMade)
            {
                var current = state.ModelData.Customize[_colorPopupIndex];
                if (current != _colorPopupOriginalValue)
                    stateManager.ChangeCustomize(state, _colorPopupIndex, _colorPopupOriginalValue, Designs.ApplySettings.Manual);
            }

            // Clear open state and hovered index.
            _colorPopupOpen = false;
            _colorPopupHoveredIndex = null;
            _colorPopupSelectionMade = false;
        }
    }
}
