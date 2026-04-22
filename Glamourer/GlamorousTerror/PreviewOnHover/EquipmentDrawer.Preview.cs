using Glamourer.Services;
using Glamourer.State;
using ImSharp;
using Penumbra.GameData.Enums;

namespace Glamourer.Gui.Equipment;

public sealed partial class EquipmentDrawer
{
    // Stain hover preview tracking
    private EquipSlot _stainPreviewSlot;
    private int       _stainPreviewIndex;
    private bool      _stainPreviewValid;
    private bool      _allStainPreviewValid;

    private partial void GTResetPreviewState()
    {
        _stainPreviewValid    = false;
        _allStainPreviewValid = false;
    }

    private partial void GTCaptureStainSlot(EquipSlot slot, int index)
    {
        _stainPreviewSlot  = slot;
        _stainPreviewIndex = index;
        _stainPreviewValid = true;
    }

    private partial void GTCaptureAllStain()
    {
        _allStainPreviewValid = true;
    }

    public void ApplyHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        // Equipment combos
        for (var i = 0; i < _equipCombo.Length; i++)
        {
            var combo = _equipCombo[i];
            if (combo.IsPopupOpen)
            {
                var slot = EquipSlotExtensions.EqdpSlots[i];
                if (combo.HoveredItem is { } hoveredItem)
                    _previewService.PreviewSingleItem(state, slot, hoveredItem);
                else
                    _previewService.StartSingleItemPreview(state, slot);

                if (combo.ItemSelected)
                {
                    _previewService.EndSingleValuePreview(wasSelectionMade: true);
                    combo.ResetSelection();
                }

                return;
            }

            if (combo.ItemSelected)
            {
                _previewService.EndSingleValuePreview(wasSelectionMade: true);
                combo.ResetSelection();
            }
        }

        // Bonus item combos
        foreach (var (combo, slot) in _bonusItemCombo.Zip(BonusExtensions.AllFlags))
        {
            if (combo.IsPopupOpen)
            {
                if (combo.HoveredItem is { } hoveredItem)
                    _previewService.PreviewSingleBonusItem(state, slot, hoveredItem);
                else
                    _previewService.StartSingleBonusItemPreview(state, slot);

                if (combo.ItemSelected)
                {
                    _previewService.EndSingleValuePreview(wasSelectionMade: true);
                    combo.ResetSelection();
                }

                return;
            }

            if (combo.ItemSelected)
            {
                _previewService.EndSingleValuePreview(wasSelectionMade: true);
                combo.ResetSelection();
            }
        }

        // Weapon combos
        foreach (var (type, combo) in _weaponCombo)
        {
            if (combo.IsPopupOpen)
            {
                var slot = type.ToSlot();
                if (combo.HoveredItem is { } hoveredItem)
                    _previewService.PreviewSingleItem(state, slot, hoveredItem);
                else
                    _previewService.StartSingleItemPreview(state, slot);

                if (combo.ItemSelected)
                {
                    _previewService.EndSingleValuePreview(wasSelectionMade: true);
                    combo.ResetSelection();
                }

                return;
            }

            if (combo.ItemSelected)
            {
                _previewService.EndSingleValuePreview(wasSelectionMade: true);
                combo.ResetSelection();
            }
        }

        // Stain combo (per-slot)
        if (_stainCombo.IsPopupOpen && _stainPreviewValid)
        {
            _previewService.StartSingleStainPreview(state, _stainPreviewSlot, _stainPreviewIndex);

            if (_stainCombo.HoveredStain is { } hoveredStain)
                _previewService.PreviewSingleStain(state, _stainPreviewSlot, _stainPreviewIndex, hoveredStain);

            if (_stainCombo.StainSelected)
            {
                _previewService.EndSingleValuePreview(wasSelectionMade: true);
                _stainCombo.ResetSelection();
            }

            return;
        }

        if (_stainCombo.StainSelected)
        {
            _previewService.EndSingleValuePreview(wasSelectionMade: true);
            _stainCombo.ResetSelection();
        }

        // Icon picker popup (requires CTRL for preview, like customization popups)
        if (_iconPickerPopupOpen)
        {
            if (_iconPickerIsBonus)
                _previewService.StartSingleBonusItemPreview(state, _iconPickerBonusSlot);
            else
                _previewService.StartSingleItemPreview(state, _iconPickerSlot);

            if (_iconPickerSelectionMade)
            {
                _previewService.EndSingleValuePreview(wasSelectionMade: true);
                _iconPickerSelectionMade = false;
                return;
            }

            if (Im.Io.KeyControl && _iconPickerHoveredItem is { } hovered)
            {
                if (_iconPickerIsBonus)
                    _previewService.PreviewSingleBonusItem(state, _iconPickerBonusSlot, hovered);
                else
                    _previewService.PreviewSingleItem(state, _iconPickerSlot, hovered);
            }
            else
            {
                _previewService.RestoreSingleValuePreview();
            }

            return;
        }

        // No equipment popup open — end only if the active preview is equipment-related.
        // AllSlotsStain is handled separately in ApplyAllStainHoverPreview (called from panels
        // that actually contain DrawAllStain) — don't end it here.
        if (_previewService.State.IsActive &&
            _previewService.State.Type is PreviewType.SingleItem or PreviewType.SingleStain)
            _previewService.EndSingleValuePreview();
    }

    /// <summary>
    /// Hover preview dispatcher for the "Dye All Slots" combo. Must be called from the same
    /// panel that drew <see cref="DrawAllStain"/> (after the draw), because that combo lives
    /// in a panel separate from the per-slot equipment draws (e.g., ImmersiveDresser's
    /// OptionsPanel vs. EquipmentPanel).
    /// </summary>
    public void ApplyAllStainHoverPreview(State.StateManager stateManager, State.ActorState state)
    {
        if (_stainCombo.IsPopupOpen && _allStainPreviewValid)
        {
            _previewService.StartAllSlotsStainPreview(state);

            if (_stainCombo.HoveredStain is { } hoveredAllStain)
                _previewService.PreviewAllSlotsStain(state, hoveredAllStain);

            if (_stainCombo.StainSelected)
            {
                _previewService.EndSingleValuePreview(wasSelectionMade: true);
                _stainCombo.ResetSelection();
            }

            return;
        }

        // Popup closed without selection — restore and end.
        if (_previewService.State.IsActive && _previewService.State.Type == PreviewType.AllSlotsStain)
            _previewService.EndSingleValuePreview();
    }
}
