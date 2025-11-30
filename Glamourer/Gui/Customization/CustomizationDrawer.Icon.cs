using Dalamud.Interface.Textures.TextureWraps;
using Glamourer.GameData;
using Glamourer.Unlocks;
using Glamourer.Designs;
using Dalamud.Bindings.ImGui;
using OtterGui;
using OtterGui.Extensions;
using OtterGui.Raii;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Customization;

public partial class CustomizationDrawer
{
    private const string IconSelectorPopup = "Style Picker";

    // State for icon popup hover preview
    private bool           _iconPopupOpen;
    private bool           _iconPopupActiveThisFrame;
    private bool           _iconPopupSelectionMade;
    private CustomizeIndex _iconPopupIndex;
    private CustomizeValue _iconPopupOriginalValue;
    private int?           _iconPopupHoveredIndex;
    private CustomizeValue _iconPopupHoveredValue;

    private void DrawIconSelector(CustomizeIndex index)
    {
        using var id       = SetId(index);
        using var bigGroup = ImRaii.Group();
        var       label    = _currentOption;

        var current         = _set.DataByValue(index, _currentByte, out var custom, _customize.Face);
        var originalCurrent = current;
        var npc             = false;
        if (current < 0)
        {
            label   = $"{_currentOption} (NPC)";
            current = 0;
            custom  = _set.Data(index, 0);
            npc     = true;
        }

        var icon    = _service.Manager.GetIcon(custom!.Value.IconId);
        var hasIcon = icon.TryGetWrap(out var wrap, out _);
        using (_ = ImRaii.Disabled(_locked || _currentIndex is CustomizeIndex.Face && _lockedRedraw))
        {
            if (ImGui.ImageButton(wrap?.Handle ?? icon.GetWrapOrEmpty().Handle, _iconSize))
            {
                ImGui.OpenPopup(IconSelectorPopup);
            }
            else if (originalCurrent >= 0 && CaptureMouseWheel(ref current, 0, _currentCount))
            {
                var data = _set.Data(_currentIndex, current, _customize.Face);
                UpdateValue(data.Value);
            }
        }

        if (hasIcon)
            ImGuiUtil.HoverIconTooltip(wrap!, _iconSize);

        ImGui.SameLine();
        using (_ = ImRaii.Group())
        {
            DataInputInt(current, npc);
            if (_lockedRedraw && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(
                    "The face can not be changed as this requires a redraw of the character, which is not supported for this actor.");

            if (_withApply)
            {
                ApplyCheckbox();
                ImGui.SameLine();
            }

            ImGui.TextUnformatted(label);
        }

        DrawIconPickerPopup(current);
    }

    private void DrawIconPickerPopup(int current)
    {
        using var popup = ImRaii.Popup(IconSelectorPopup, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup)
            return;

        // Mark popup as active this frame and initialize open state.
        _iconPopupActiveThisFrame = true;
        if (!_iconPopupOpen)
        {
            _iconPopupOpen = true;
            _iconPopupIndex = _currentIndex;
            _iconPopupOriginalValue = _customize[_currentIndex];
            _iconPopupSelectionMade = false;
        }

        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, Vector2.Zero)
            .Push(ImGuiStyleVar.FrameRounding, 0);
        var prevValue = _customize[_currentIndex];
        for (var i = 0; i < _currentCount; ++i)
        {
            var custom = _set.Data(_currentIndex, i, _customize.Face);
            var icon   = _service.Manager.GetIcon(custom.IconId);
            using (var _ = ImRaii.Group())
            {
                var isFavorite = _favorites.Contains(_set.Gender, _set.Clan, _currentIndex, custom.Value);
                using var frameColor = current == i
                    ? ImRaii.PushColor(ImGuiCol.Button, Colors.SelectedRed)
                    : ImRaii.PushColor(ImGuiCol.Button, ColorId.FavoriteStarOn.Value(), isFavorite);
                var hasIcon = icon.TryGetWrap(out var wrap, out var _);

                if (ImGui.ImageButton(wrap?.Handle ?? icon.GetWrapOrEmpty().Handle, _iconSize))
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
                        _iconPopupSelectionMade = true;
                        // For non-face/hairstyle/facepaint selectors, close the popup after selecting
                        // a new option. For face/hairstyle/facepaint, keep it open to allow multiple picks.
                        if (_currentIndex is not (Penumbra.GameData.Enums.CustomizeIndex.Face
                                                 or Penumbra.GameData.Enums.CustomizeIndex.Hairstyle
                                                 or Penumbra.GameData.Enums.CustomizeIndex.FacePaint))
                            ImGui.CloseCurrentPopup();
                    }
                }
                // Track hovered option for previewing.
                if (ImGui.IsItemHovered())
                {
                    _iconPopupHoveredIndex = i;
                    _iconPopupHoveredValue = custom.Value;
                }
                else if (_iconPopupHoveredIndex == i)
                {
                    _iconPopupHoveredIndex = null;
                }
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    if (isFavorite)
                        _favorites.Remove(_set.Gender, _set.Clan, _currentIndex, custom.Value);
                    else
                        _favorites.TryAdd(_set.Gender, _set.Clan, _currentIndex, custom.Value);

                if (hasIcon)
                {
                    var parts = new List<string>();
                    if (FavoriteManager.TypeAllowed(_currentIndex))
                        parts.Add("Right-Click to toggle favorite.");

                    if (_currentIndex is Penumbra.GameData.Enums.CustomizeIndex.Face
                        or Penumbra.GameData.Enums.CustomizeIndex.Hairstyle
                        or Penumbra.GameData.Enums.CustomizeIndex.FacePaint)
                        parts.Add("Hold CTRL to preview on actor.");

                    var tip = parts.Count > 0 ? string.Join("\n", parts) : string.Empty;
                    ImGuiUtil.HoverIconTooltip(wrap!, _iconSize, tip);
                }

                var text      = custom.Value.ToString();
                var textWidth = ImGui.CalcTextSize(text).X;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (_iconSize.X - textWidth + 2 * ImGui.GetStyle().FramePadding.X) / 2);
                ImGui.TextUnformatted(text);
            }

            if (i % 8 != 7)
                ImGui.SameLine();
        }
    }


    // Only used for facial features, so fixed ID.
    private void DrawMultiIconSelector()
    {
        using var bigGroup = ImRaii.Group();
        using var disabled = ImRaii.Disabled(_locked);
        DrawMultiIcons();
        ImGui.SameLine();
        using var group = ImRaii.Group();

        _currentCount = 256;
        if (_withApply)
        {
            ApplyCheckbox(CustomizeIndex.FacialFeature1);
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + _spacing.X);
            ApplyCheckbox(CustomizeIndex.FacialFeature2);
            ImGui.SameLine();
            ApplyCheckbox(CustomizeIndex.FacialFeature3);
            ImGui.SameLine();
            ApplyCheckbox(CustomizeIndex.FacialFeature4);
        }
        else
        {
            ImGui.Dummy(new Vector2(ImGui.GetFrameHeight()));
        }

        var oldValue = _customize.AtIndex(_currentIndex.ToByteAndMask().ByteIdx);
        var tmp      = (int)oldValue.Value;
        ImGui.SetNextItemWidth(_inputIntSize);
        if (ImGui.InputInt("##text", ref tmp, 1, 1))
        {
            tmp = Math.Clamp(tmp, 0, byte.MaxValue);
            if (tmp != oldValue.Value)
            {
                _customize.SetByIndex(_currentIndex.ToByteAndMask().ByteIdx, (CustomizeValue)tmp);
                var changes = (byte)tmp ^ oldValue.Value;
                Changed |= ((changes & 0x01) == 0x01 ? CustomizeFlag.FacialFeature1 : 0)
                  | ((changes & 0x02) == 0x02 ? CustomizeFlag.FacialFeature2 : 0)
                  | ((changes & 0x04) == 0x04 ? CustomizeFlag.FacialFeature3 : 0)
                  | ((changes & 0x08) == 0x08 ? CustomizeFlag.FacialFeature4 : 0)
                  | ((changes & 0x10) == 0x10 ? CustomizeFlag.FacialFeature5 : 0)
                  | ((changes & 0x20) == 0x20 ? CustomizeFlag.FacialFeature6 : 0)
                  | ((changes & 0x40) == 0x40 ? CustomizeFlag.FacialFeature7 : 0)
                  | ((changes & 0x80) == 0x80 ? CustomizeFlag.LegacyTattoo : 0);
            }
        }

        if (_set.DataByValue(CustomizeIndex.Face, _customize.Face, out _, _customize.Face) < 0)
        {
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            using var _ = ImRaii.Enabled();
            ImGui.TextUnformatted("(Using Face 1)");
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + _spacing.Y);
        ImGui.AlignTextToFramePadding();
        using (var _ = ImRaii.Enabled())
        {
            ImGui.TextUnformatted("Facial Features & Tattoos");
        }

        if (_withApply)
        {
            ApplyCheckbox(CustomizeIndex.FacialFeature5);
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + _spacing.X);
            ApplyCheckbox(CustomizeIndex.FacialFeature6);
            ImGui.SameLine();
            ApplyCheckbox(CustomizeIndex.FacialFeature7);
            ImGui.SameLine();
            ApplyCheckbox(CustomizeIndex.LegacyTattoo);
        }
    }

    private void DrawMultiIcons()
    {
        var options = _set.Order[MenuType.IconCheckmark];
        using var group = ImRaii.Group();
        var face = _set.DataByValue(CustomizeIndex.Face, _customize.Face, out _, _customize.Face) < 0 ? _set.Faces[0].Value : _customize.Face;
        foreach (var (featureIdx, idx) in options.WithIndex())
        {
            using var            id      = SetId(featureIdx);
            var                  enabled = _customize.Get(featureIdx) != CustomizeValue.Zero;
            var                  feature = _set.Data(featureIdx, 0, face);
            bool                 hasIcon;
            IDalamudTextureWrap? wrap;
            var                  icon = _service.Manager.GetIcon(feature.IconId);
            if (featureIdx is CustomizeIndex.LegacyTattoo)
            {
                wrap    = _legacyTattoo;
                hasIcon = wrap != null;
            }
            else
            {
                hasIcon = icon.TryGetWrap(out wrap, out _);
            }

            if (ImGui.ImageButton(wrap?.Handle ?? icon.GetWrapOrEmpty().Handle, _iconSize, Vector2.Zero, Vector2.One,
                    (int)ImGui.GetStyle().FramePadding.X, Vector4.Zero, enabled ? Vector4.One : _redTint))
            {
                _customize.Set(featureIdx, enabled ? CustomizeValue.Zero : CustomizeValue.Max);
                Changed |= _currentFlag;
            }

            if (hasIcon)
                ImGuiUtil.HoverIconTooltip(wrap!, _iconSize);
            if (idx % 4 != 3)
                ImGui.SameLine();
        }
    }

    /// <summary>
    /// Apply hover preview changes to the provided actor state. This will preview the hovered
    /// customization option while holding Control for face/hairstyle/facepaint, and restore
    /// the original value when the popup closes or when not hovering.
    /// </summary>
    public void ApplyHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        // If popup was active this frame, handle preview or restoration while open.
        if (_iconPopupActiveThisFrame)
        {
            // If hovering an option and Ctrl is held, and this is a previewable index, apply it.
            if (_iconPopupHoveredIndex.HasValue && ImGui.GetIO().KeyCtrl
                && (_iconPopupIndex is CustomizeIndex.Face or CustomizeIndex.Hairstyle or CustomizeIndex.FacePaint))
            {
                var current = state.ModelData.Customize[_iconPopupIndex];
                if (current != _iconPopupHoveredValue)
                    stateManager.ChangeCustomize(state, _iconPopupIndex, _iconPopupHoveredValue, ApplySettings.Manual);
            }
            else
            {
                // Not hovering or Ctrl not held: restore original while popup open if no selection was made.
                if (!_iconPopupSelectionMade)
                {
                    var current = state.ModelData.Customize[_iconPopupIndex];
                    if (current != _iconPopupOriginalValue)
                        stateManager.ChangeCustomize(state, _iconPopupIndex, _iconPopupOriginalValue, ApplySettings.Manual);
                }
            }

            // Reset per-frame active marker for next frame.
            _iconPopupActiveThisFrame = false;
            return;
        }

        // Popup was open previously but not active this frame -> it has been closed.
        if (_iconPopupOpen)
        {
            // If no selection was made inside the popup, restore original value.
            if (!_iconPopupSelectionMade)
            {
                var current = state.ModelData.Customize[_iconPopupIndex];
                if (current != _iconPopupOriginalValue)
                    stateManager.ChangeCustomize(state, _iconPopupIndex, _iconPopupOriginalValue, ApplySettings.Manual);
            }

            // Clear open state and hovered index.
            _iconPopupOpen = false;
            _iconPopupHoveredIndex = null;
            _iconPopupSelectionMade = false;
        }
    }
}
