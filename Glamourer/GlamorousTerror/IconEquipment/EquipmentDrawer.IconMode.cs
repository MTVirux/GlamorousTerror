using Glamourer.Config;
using Glamourer.Services;
using Glamourer.Unlocks;
using ImSharp;
using Luna;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Equipment;

public sealed partial class EquipmentDrawer
{
    #region Icon Mode

    private const int IconPickerColumns = 8;

    private static ReadOnlySpan<byte> IconPickerPopup
        => "##IconPicker"u8;

    private EquipSlot     _iconPickerSlot;
    private BonusItemFlag _iconPickerBonusSlot;
    private bool          _iconPickerIsWeapon;
    private bool          _iconPickerIsBonus;
    private bool          _iconPickerPopupOpen;
    private EquipItem?    _iconPickerHoveredItem;
    private bool          _iconPickerSelectionMade;
    private float         _iconPickerClickY;

    private partial void GTResetIconState()
    {
        _iconPickerPopupOpen = false;
    }

    private partial bool GTTryDrawEquipIcon(EquipDrawData data)
    {
        if (!_config.UseIconEquipmentDrawer)
            return false;

        DrawEquipIcon(in data);
        return true;
    }

    private partial bool GTTryDrawBonusItemIcon(BonusDrawData data)
    {
        if (!_config.UseIconEquipmentDrawer)
            return false;

        DrawBonusItemIcon(in data);
        return true;
    }

    private partial bool GTTryDrawWeaponsIcon(EquipDrawData mainhand, EquipDrawData offhand, bool allWeapons)
    {
        if (!_config.UseIconEquipmentDrawer)
            return false;

        DrawWeaponsIcon(mainhand, offhand, allWeapons);
        return true;
    }

    private void PositionIconPickerPopup()
    {
        var windowPos     = Im.Window.Position;
        var windowSize    = Im.Window.Size;
        var windowCenterX = windowPos.X + windowSize.X * 0.5f;
        var viewportCenterX = Im.Viewport.Main.Center.X;
        var openLeft      = windowCenterX < viewportCenterX;
        var anchorX       = openLeft ? windowPos.X : windowPos.X + windowSize.X;

        var maxRows        = Math.Max(1, _config.IconPickerMaxRows);
        var reducedSpacing = Im.Style.ItemSpacing * 0.25f;
        var buttonHeight   = _iconSize.Y + 2 * Im.Style.FramePadding.Y;
        var maxHeight      = maxRows * (buttonHeight + reducedSpacing.Y) + 2 * Im.Style.WindowPadding.Y;

        var viewportSize = Im.Viewport.Main.Size;
        var anchorY      = Math.Min(_iconPickerClickY, viewportSize.Y - maxHeight);
        anchorX          = Math.Clamp(anchorX, 0, viewportSize.X);

        Im.Window.SetNextPosition(
            new Vector2(anchorX, anchorY),
            Condition.Appearing,
            new Vector2(openLeft ? 1 : 0, 0));

        Im.Window.SetNextSizeConstraints(Vector2.Zero, new Vector2(float.MaxValue, maxHeight));
    }

    internal void DrawEquipIcon(in EquipDrawData data)
    {
        var combo = _equipCombo[data.Slot.ToIndex()];

        using (Im.Group())
        {
            data.CurrentItem.DrawIcon(_textures, _iconSize, data.Slot);
            var clicked      = Im.Item.Clicked();
            var rightClicked = Im.Item.RightClicked();

            if (Im.Item.Hovered())
            {
                using var tt = Im.Tooltip.Begin();
                Im.Text(combo.Label);
                Im.Text(data.CurrentItem.Name);
                if (VerifyRestrictedGear(data))
                    Im.Text("(Restricted)"u8);
            }

            if (clicked && !data.Locked)
            {
                _iconPickerSlot     = data.Slot;
                _iconPickerIsWeapon = false;
                _iconPickerIsBonus  = false;
                _iconPickerClickY   = Im.Item.UpperLeftCorner.Y;
                Im.Popup.Open(IconPickerPopup);
            }

            if (ResetOrClear(data.Locked, rightClicked, data.AllowRevert, true, data.CurrentItem, data.GameItem,
                    ItemManager.NothingItem(data.Slot), out var item))
                data.SetItem(item);

            DrawGearDragDrop(data);
            DrawIconStainIndicators(data);
        }

        if (data.DisplayApplication)
        {
            Im.Line.Same();
            DrawApply(data);
            Im.Line.Same();
            DrawApplyStain(data);
        }

        DrawEquipIconPickerPopup(data);
    }

    private void DrawEquipIconPickerPopup(in EquipDrawData data)
    {
        if (_iconPickerSlot != data.Slot || _iconPickerIsWeapon)
            return;

        PositionIconPickerPopup();
        using var popup = Im.Popup.Begin(IconPickerPopup, WindowFlags.NoMove);
        if (!popup)
            return;

        _iconPickerPopupOpen   = true;
        _iconPickerHoveredItem = null;

        using var style = ImStyleDouble.ItemSpacing.Push(Im.Style.ItemSpacing * 0.25f)
            .Push(ImStyleSingle.FrameRounding, 0);

        var nothing = ItemManager.NothingItem(data.Slot);
        DrawIconPickerItem(nothing, data.CurrentItem, data, 0);

        var hasItems = false;
        if (_items.ItemData.ByType.TryGetValue(data.Slot.ToEquipType(), out var list))
        {
            var i = 1;
            HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>? modelSet = _config.GroupIconPickerByModel
                ? new HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>()
                : null;
            foreach (var equipItem in list)
            {
                if (_config.OwnedOnlyComboFilter
                    && !_itemUnlockManager.IsOwnedFromSources(equipItem.ItemId, _config.OwnedComboFilterSources))
                    continue;

                if (modelSet != null && !modelSet.Add((equipItem.Type, equipItem.PrimaryId, equipItem.SecondaryId, equipItem.Variant)))
                    continue;

                hasItems = true;
                if (i % IconPickerColumns is not 0)
                    Im.Line.Same();
                DrawIconPickerItem(equipItem, data.CurrentItem, data, i);
                ++i;
            }
        }

        if (!hasItems)
            Im.Text("No items match the current filter."u8);
    }

    private void DrawIconPickerItem(in EquipItem item, in EquipItem current, in EquipDrawData data, int index)
    {
        using var id         = Im.Id.Push(index);
        using var frameColor = item.Id == current.Id
            ? ImGuiColor.Button.Push(Colors.SelectedRed)
            : ImGuiColor.Button.Push(ImGuiColor.Button.Get());

        var (ptr, textureSize, empty) = _textures.GetIcon(item, data.Slot);
        if (Im.Image.Button(ptr, _iconSize))
        {
            data.SetItem(item);
            _iconPickerSelectionMade = true;
            Im.Popup.CloseCurrent();
        }

        if (Im.Item.Hovered())
        {
            _iconPickerHoveredItem = item;
            using var tt = Im.Tooltip.Begin();
            Im.Text(item.Name);
            if (!empty)
                Im.Image.Draw(ptr, textureSize);
        }
    }

    internal void DrawBonusItemIcon(in BonusDrawData data)
    {
        var combo = _bonusItemCombo[data.Slot.ToIndex()];

        using (Im.Group())
        {
            data.CurrentItem.DrawIcon(_textures, _iconSize, data.Slot);
            var clicked      = Im.Item.Clicked();
            var rightClicked = Im.Item.RightClicked();

            if (Im.Item.Hovered())
            {
                using var tt = Im.Tooltip.Begin();
                Im.Text(combo.Label);
                Im.Text(data.CurrentItem.Name);
            }

            if (clicked && !data.Locked)
            {
                _iconPickerBonusSlot = data.Slot;
                _iconPickerIsWeapon  = false;
                _iconPickerIsBonus   = true;
                _iconPickerClickY    = Im.Item.UpperLeftCorner.Y;
                Im.Popup.Open(IconPickerPopup);
            }

            if (ResetOrClear(data.Locked, rightClicked, data.AllowRevert, true, data.CurrentItem, data.GameItem,
                    EquipItem.BonusItemNothing(data.Slot), out var item))
                data.SetItem(item);
        }

        if (data.DisplayApplication)
        {
            Im.Line.Same();
            DrawApply(data);
        }

        DrawBonusIconPickerPopup(data);
    }

    private void DrawBonusIconPickerPopup(in BonusDrawData data)
    {
        if (_iconPickerBonusSlot != data.Slot || _iconPickerIsWeapon)
            return;

        PositionIconPickerPopup();
        using var popup = Im.Popup.Begin(IconPickerPopup, WindowFlags.NoMove);
        if (!popup)
            return;

        _iconPickerPopupOpen   = true;
        _iconPickerHoveredItem = null;

        using var style = ImStyleDouble.ItemSpacing.Push(Im.Style.ItemSpacing * 0.25f)
            .Push(ImStyleSingle.FrameRounding, 0);

        var nothing = EquipItem.BonusItemNothing(data.Slot);
        DrawBonusIconPickerItem(nothing, data.CurrentItem, data, 0);

        var hasItems = false;
        if (_items.ItemData.ByType.TryGetValue(data.Slot.ToEquipType(), out var list))
        {
            var i = 1;
            HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>? modelSet = _config.GroupIconPickerByModel
                ? new HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>()
                : null;
            foreach (var equipItem in list)
            {
                if (_config.OwnedOnlyComboFilter
                    && !_itemUnlockManager.IsOwnedFromSources(equipItem.ItemId, _config.OwnedComboFilterSources))
                    continue;

                if (modelSet != null && !modelSet.Add((equipItem.Type, equipItem.PrimaryId, equipItem.SecondaryId, equipItem.Variant)))
                    continue;

                hasItems = true;
                if (i % IconPickerColumns is not 0)
                    Im.Line.Same();
                DrawBonusIconPickerItem(equipItem, data.CurrentItem, data, i);
                ++i;
            }
        }

        if (!hasItems)
            Im.Text("No items match the current filter."u8);
    }

    private void DrawBonusIconPickerItem(in EquipItem item, in EquipItem current, in BonusDrawData data, int index)
    {
        using var id         = Im.Id.Push(index);
        using var frameColor = item.Id == current.Id
            ? ImGuiColor.Button.Push(Colors.SelectedRed)
            : ImGuiColor.Button.Push(ImGuiColor.Button.Get());

        var (ptr, textureSize, empty) = _textures.GetIcon(item, data.Slot);
        if (Im.Image.Button(ptr, _iconSize))
        {
            data.SetItem(item);
            _iconPickerSelectionMade = true;
            Im.Popup.CloseCurrent();
        }

        if (Im.Item.Hovered())
        {
            _iconPickerHoveredItem = item;
            using var tt = Im.Tooltip.Begin();
            Im.Text(item.Name);
            if (!empty)
                Im.Image.Draw(ptr, textureSize);
        }
    }

    private void DrawWeaponsIcon(EquipDrawData mainhand, EquipDrawData offhand, bool allWeapons)
    {
        DrawWeaponSlotIcon(ref mainhand, ref offhand, allWeapons, true);

        if (offhand.CurrentItem.Type is not FullEquipType.Unknown)
        {
            Im.Line.Same();
            DrawWeaponSlotIcon(ref mainhand, ref offhand, allWeapons, false);
        }
    }

    /// <summary> Draw a single weapon slot icon without the DrawWeapons wrapper, for custom panel layouts. </summary>
    public void DrawSingleWeaponIcon(ref EquipDrawData mainhand, ref EquipDrawData offhand, bool allWeapons, bool isMainhand)
    {
        if (_config.HideApplyCheckmarks)
        {
            mainhand.DisplayApplication = false;
            offhand.DisplayApplication  = false;
        }

        using var id    = Im.Id.Push(isMainhand ? "WeaponMH"u8 : "WeaponOH"u8);
        using var style = ImStyleDouble.ItemSpacing.PushX(Im.Style.ItemInnerSpacing.X);
        DrawWeaponSlotIcon(ref mainhand, ref offhand, allWeapons, isMainhand);
    }

    private void DrawWeaponSlotIcon(ref EquipDrawData mainhand, ref EquipDrawData offhand, bool allWeapons, bool isMainhand)
    {
        ref var data = ref isMainhand ? ref mainhand : ref offhand;
        var comboType = isMainhand
            ? (allWeapons ? FullEquipType.Unknown : mainhand.CurrentItem.Type)
            : offhand.CurrentItem.Type;

        if (!_weaponCombo.TryGetValue(comboType, out var combo))
            return;

        var slot = isMainhand ? EquipSlot.MainHand : EquipSlot.OffHand;

        using (Im.Group())
        {
            data.CurrentItem.DrawIcon(_textures, _iconSize, slot);
            var clicked = Im.Item.Clicked();

            if (Im.Item.Hovered())
            {
                using var tt = Im.Tooltip.Begin();
                Im.Text(combo.Label);
                Im.Text(data.CurrentItem.Name);
                if (allWeapons && isMainhand)
                    Im.Text($"({data.CurrentItem.Type.ToName()})");
            }

            if (clicked && !data.Locked)
            {
                _iconPickerSlot     = slot;
                _iconPickerIsWeapon = true;
                _iconPickerClickY   = Im.Item.UpperLeftCorner.Y;
                Im.Popup.Open(IconPickerPopup);
            }

            DrawGearDragDrop(data);
            DrawIconStainIndicators(data);
        }

        if (data.DisplayApplication)
        {
            Im.Line.Same();
            DrawApply(data);
            Im.Line.Same();
            DrawApplyStain(data);
        }

        DrawWeaponIconPickerPopup(ref mainhand, ref offhand, isMainhand, comboType, slot);
    }

    private void DrawWeaponIconPickerPopup(ref EquipDrawData mainhand, ref EquipDrawData offhand, bool isMainhand,
        FullEquipType comboType, EquipSlot slot)
    {
        if (_iconPickerSlot != slot || !_iconPickerIsWeapon)
            return;

        PositionIconPickerPopup();
        using var popup = Im.Popup.Begin(IconPickerPopup, WindowFlags.NoMove);
        if (!popup)
            return;

        _iconPickerPopupOpen   = true;
        _iconPickerHoveredItem = null;

        using var style = ImStyleDouble.ItemSpacing.Push(Im.Style.ItemSpacing * 0.25f)
            .Push(ImStyleSingle.FrameRounding, 0);

        ref var data    = ref isMainhand ? ref mainhand : ref offhand;
        var     current = data.CurrentItem;
        var     i       = 0;
        HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>? modelSet = _config.GroupIconPickerByModel
            ? new HashSet<(FullEquipType, PrimaryId, SecondaryId, Variant)>()
            : null;

        if (comboType is FullEquipType.Unknown)
        {
            foreach (var t in FullEquipType.Values.Where(e => e.ToSlot() is EquipSlot.MainHand))
            {
                if (!_items.ItemData.ByType.TryGetValue(t, out var l))
                    continue;

                foreach (var item in l)
                {
                    if (_config.OwnedOnlyComboFilter
                        && !_itemUnlockManager.IsOwnedFromSources(item.ItemId, _config.OwnedComboFilterSources))
                        continue;

                    if (modelSet != null && !modelSet.Add((item.Type, item.PrimaryId, item.SecondaryId, item.Variant)))
                        continue;

                    if (i > 0 && i % IconPickerColumns is not 0)
                        Im.Line.Same();
                    DrawWeaponIconPickerItem(item, current, slot, ref mainhand, ref offhand, i);
                    ++i;
                }
            }
        }
        else if (_items.ItemData.ByType.TryGetValue(comboType, out var list))
        {
            foreach (var item in list)
            {
                if (_config.OwnedOnlyComboFilter
                    && !_itemUnlockManager.IsOwnedFromSources(item.ItemId, _config.OwnedComboFilterSources))
                    continue;

                if (modelSet != null && !modelSet.Add((item.Type, item.PrimaryId, item.SecondaryId, item.Variant)))
                    continue;

                if (i > 0 && i % IconPickerColumns is not 0)
                    Im.Line.Same();
                DrawWeaponIconPickerItem(item, current, slot, ref mainhand, ref offhand, i);
                ++i;
            }
        }

        if (i == 0)
            Im.Text("No items match the current filter."u8);
    }

    private void DrawWeaponIconPickerItem(in EquipItem item, in EquipItem current, EquipSlot slot,
        ref EquipDrawData mainhand, ref EquipDrawData offhand, int index)
    {
        using var id         = Im.Id.Push(index);
        using var frameColor = item.Id == current.Id
            ? ImGuiColor.Button.Push(Colors.SelectedRed)
            : ImGuiColor.Button.Push(ImGuiColor.Button.Get());

        var (ptr, textureSize, empty) = _textures.GetIcon(item, slot);
        if (Im.Image.Button(ptr, _iconSize))
        {
            if (slot is EquipSlot.MainHand)
            {
                mainhand.SetItem(item);
                if (item.Type.ValidOffhand() != mainhand.CurrentItem.Type.ValidOffhand())
                {
                    offhand.CurrentItem = _items.GetDefaultOffhand(item);
                    offhand.SetItem(offhand.CurrentItem);
                }

                mainhand.CurrentItem = item;
            }
            else
            {
                offhand.SetItem(item);
            }

            _iconPickerSelectionMade = true;
            Im.Popup.CloseCurrent();
        }

        if (Im.Item.Hovered())
        {
            _iconPickerHoveredItem = item;
            using var tt = Im.Tooltip.Begin();
            Im.Text(item.Name);
            if (!empty)
                Im.Image.Draw(ptr, textureSize);
        }
    }

    private void DrawIconStainIndicators(in EquipDrawData data)
    {
        var stainSize = new Vector2(Im.Style.FrameHeight * 0.6f);
        foreach (var (index, stainId) in data.CurrentStains.Index())
        {
            if (index > 0)
                Im.Line.SameInner();

            if (_stainData.TryGetValue(stainId, out var stain) && stain.RowIndex.Id is not 0)
            {
                var pos = Im.Cursor.ScreenPosition;
                Im.Window.DrawList.Shape.RectangleFilled(pos, pos + stainSize, stain.RgbaColor, 3 * Im.Style.GlobalScale);
                Im.Dummy(stainSize);
            }
            else
            {
                var pos = Im.Cursor.ScreenPosition;
                Im.Window.DrawList.Shape.RectangleFilled(pos, pos + stainSize, ImGuiColor.FrameBackground.Get(),
                    3 * Im.Style.GlobalScale);
                Im.Dummy(stainSize);
            }

            // Stain tooltip on hover.
            if (Im.Item.Hovered())
            {
                using var tt = Im.Tooltip.Begin();
                if (_stainData.TryGetValue(stainId, out var s) && s.RowIndex.Id is not 0)
                    Im.Text(s.Name);
                else
                    Im.Text("No Dye"u8);
            }
        }
    }

    #endregion
}
