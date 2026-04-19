using System.Numerics;
using Glamourer.Config;
using Glamourer.Services;
using Glamourer.Unlocks;
using ImSharp;
using Luna;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Equipment;

public enum IconPickerSortMode : byte
{
    AlphabeticalAsc,
    AlphabeticalDesc,
    IdAsc,
    IdDesc,
}

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

    // Filter & sort state (session-scoped, not persisted)
    private string             _iconPickerNameFilter       = string.Empty;
    private bool               _iconPickerFavoritesOnly;
    private JobFlag            _iconPickerJobFilter        = JobFlag.All;
    private int                _iconPickerDyeChannelFilter = -1;
    private IconPickerSortMode _iconPickerSortMode         = IconPickerSortMode.AlphabeticalAsc;

    private partial void GTResetIconState()
    {
        _iconPickerPopupOpen = false;
    }

    private static int GetDyeChannelCount(in EquipItem item)
    {
        var flags = item.Flags;
        if ((flags & ItemFlags.IsDyable2) != 0)
            return 2;
        if ((flags & ItemFlags.IsDyable1) != 0)
            return 1;
        return 0;
    }

    private bool FilterIconPickerItem(in EquipItem item)
    {
        if (_iconPickerFavoritesOnly && !_favoriteManager.Contains(item))
            return false;

        if (_iconPickerNameFilter.Length > 0
            && !item.Name.Contains(_iconPickerNameFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (_iconPickerJobFilter != JobFlag.All
            && _jobService.JobGroups.TryGetValue(item.JobRestrictions, out var jg)
            && (jg.Flags & _iconPickerJobFilter) == 0)
            return false;

        if (_iconPickerDyeChannelFilter >= 0 && GetDyeChannelCount(in item) != _iconPickerDyeChannelFilter)
            return false;

        return true;
    }

    private IEnumerable<EquipItem> SortIconPickerItems(IEnumerable<EquipItem> items)
        => _iconPickerSortMode switch
        {
            IconPickerSortMode.AlphabeticalAsc  => items.OrderBy(i => i.Name),
            IconPickerSortMode.AlphabeticalDesc  => items.OrderByDescending(i => i.Name),
            IconPickerSortMode.IdAsc             => items.OrderBy(i => i.ItemId.Id),
            IconPickerSortMode.IdDesc            => items.OrderByDescending(i => i.ItemId.Id),
            _                                    => items,
        };

    private void DrawIconPickerFilterBar()
    {
        // Row 1: Name filter + Favorites toggle
        if (Im.Window.Appearing)
            Im.Keyboard.SetFocusHere();

        var starWidth   = Im.Style.FrameHeight + Im.Style.ItemSpacing.X;
        var inputWidth  = Im.ContentRegion.Available.X - starWidth;
        Im.Item.SetNextWidth(inputWidth);
        Im.Input.Text("##IconPickerName"u8, ref _iconPickerNameFilter, "Search..."u8);

        Im.Line.Same();
        using (var color = _iconPickerFavoritesOnly
                   ? ImGuiColor.Text.Push(0xFF00CFFFu)
                   : ImGuiColor.Text.Push(ImGuiColor.TextDisabled.Get()))
        {
            if (Im.Button("\u2605##IconPickerFav"u8, new Vector2(Im.Style.FrameHeight)))
                _iconPickerFavoritesOnly = !_iconPickerFavoritesOnly;
        }
        Im.Tooltip.OnHover("Toggle favorites-only filter."u8);

        // Row 2: Job filter | Dye channel filter | Sort
        var comboWidth = (Im.ContentRegion.Available.X - 2 * Im.Style.ItemSpacing.X) / 3f;

        // Job filter combo
        var jobCount   = BitOperations.PopCount((ulong)_iconPickerJobFilter);
        var totalJobs  = _jobService.Jobs.Ordered.Count;
        var jobPreview = jobCount >= totalJobs ? "All Jobs" : $"{jobCount} Jobs";
        Im.Item.SetNextWidth(comboWidth);
        using (var combo = Im.Combo.Begin("##IconPickerJobFilter"u8, jobPreview, ComboFlags.HeightLargest))
        {
            if (combo)
            {
                if (Im.Button("Select All##jobs"u8))
                    _iconPickerJobFilter = _jobService.Jobs.AllAvailableJobs;
                Im.Line.Same();
                if (Im.Button("Clear All##jobs"u8))
                    _iconPickerJobFilter = 0;
                Im.Separator();

                DrawIconPickerJobCategory("Tanks"u8,           Job.JobRole.Tank);
                DrawIconPickerJobCategory("Healers"u8,         Job.JobRole.Healer);
                DrawIconPickerJobCategory("Melee DPS"u8,       Job.JobRole.Melee);
                DrawIconPickerJobCategory("Physical Ranged"u8, Job.JobRole.RangedPhysical);
                DrawIconPickerJobCategory("Magical Ranged"u8,  Job.JobRole.RangedMagical);
                DrawIconPickerJobCategory("Crafters"u8,        Job.JobRole.Crafter);
                DrawIconPickerJobCategory("Gatherers"u8,       Job.JobRole.Gatherer);
            }
        }

        // Dye channel filter combo
        Im.Line.Same();
        var dyePreview = _iconPickerDyeChannelFilter switch
        {
            0 => "0 Dyes",
            1 => "1 Dye",
            2 => "2 Dyes",
            _ => "Any Dyes",
        };
        Im.Item.SetNextWidth(comboWidth);
        using (var combo = Im.Combo.Begin("##IconPickerDyeFilter"u8, dyePreview))
        {
            if (combo)
            {
                if (Im.Selectable("Any"u8, _iconPickerDyeChannelFilter < 0))
                    _iconPickerDyeChannelFilter = -1;
                if (Im.Selectable("0"u8, _iconPickerDyeChannelFilter == 0))
                    _iconPickerDyeChannelFilter = 0;
                if (Im.Selectable("1"u8, _iconPickerDyeChannelFilter == 1))
                    _iconPickerDyeChannelFilter = 1;
                if (Im.Selectable("2"u8, _iconPickerDyeChannelFilter == 2))
                    _iconPickerDyeChannelFilter = 2;
            }
        }

        // Sort combo
        Im.Line.Same();
        var sortPreview = _iconPickerSortMode switch
        {
            IconPickerSortMode.AlphabeticalAsc  => "A \u2192 Z",
            IconPickerSortMode.AlphabeticalDesc  => "Z \u2192 A",
            IconPickerSortMode.IdAsc             => "ID \u2191",
            IconPickerSortMode.IdDesc            => "ID \u2193",
            _                                    => "Sort",
        };
        Im.Item.SetNextWidth(comboWidth);
        using (var combo = Im.Combo.Begin("##IconPickerSort"u8, sortPreview))
        {
            if (combo)
            {
                if (Im.Selectable("A \u2192 Z"u8, _iconPickerSortMode == IconPickerSortMode.AlphabeticalAsc))
                    _iconPickerSortMode = IconPickerSortMode.AlphabeticalAsc;
                if (Im.Selectable("Z \u2192 A"u8, _iconPickerSortMode == IconPickerSortMode.AlphabeticalDesc))
                    _iconPickerSortMode = IconPickerSortMode.AlphabeticalDesc;
                if (Im.Selectable("ID \u2191"u8, _iconPickerSortMode == IconPickerSortMode.IdAsc))
                    _iconPickerSortMode = IconPickerSortMode.IdAsc;
                if (Im.Selectable("ID \u2193"u8, _iconPickerSortMode == IconPickerSortMode.IdDesc))
                    _iconPickerSortMode = IconPickerSortMode.IdDesc;
            }
        }

        Im.Separator();
    }

    private void DrawIconPickerJobCategory(ReadOnlySpan<byte> label, Job.JobRole role)
    {
        var roleJobs = _jobService.Jobs.Ordered.Where(j => j.Role == role).ToList();
        if (roleJobs.Count == 0)
            return;

        var roleFlag  = roleJobs.Aggregate((JobFlag)0, (f, j) => f | j.Flag);
        var allSet    = (_iconPickerJobFilter & roleFlag) == roleFlag;
        var noneSet   = (_iconPickerJobFilter & roleFlag) == 0;

        using var tree = Im.Tree.Node(label, TreeNodeFlags.DefaultOpen);
        if (!tree)
            return;

        // Role-level toggle: clicking checks/unchecks all jobs in the category
        if (!noneSet && Im.Checkbox($"All {Encoding.UTF8.GetString(label)}", allSet))
            _iconPickerJobFilter = allSet
                ? _iconPickerJobFilter & ~roleFlag
                : _iconPickerJobFilter | roleFlag;
        else if (noneSet && Im.Checkbox($"All {Encoding.UTF8.GetString(label)}", false))
            _iconPickerJobFilter |= roleFlag;

        foreach (var job in roleJobs)
        {
            var enabled = (_iconPickerJobFilter & job.Flag) != 0;
            if (Im.Checkbox(job.Abbreviation, enabled))
                _iconPickerJobFilter ^= job.Flag;
            Im.Tooltip.OnHover(job.Name);
        }
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
        var lineHeight     = Im.Style.FrameHeight + Im.Style.ItemSpacing.Y;
        var filterBarHeight = 2 * lineHeight + Im.Style.ItemSpacing.Y;
        var maxHeight      = maxRows * (buttonHeight + reducedSpacing.Y) + 2 * Im.Style.WindowPadding.Y + filterBarHeight;

        var minWidth = IconPickerColumns * (_iconSize.X + 2 * Im.Style.FramePadding.X + reducedSpacing.X)
            + 2 * Im.Style.WindowPadding.X;

        var viewportSize = Im.Viewport.Main.Size;
        var anchorY      = Math.Min(_iconPickerClickY, viewportSize.Y - maxHeight);
        anchorX          = Math.Clamp(anchorX, 0, viewportSize.X);

        Im.Window.SetNextPosition(
            new Vector2(anchorX, anchorY),
            Condition.Appearing,
            new Vector2(openLeft ? 1 : 0, 0));

        Im.Window.SetNextSizeConstraints(new Vector2(minWidth, 0), new Vector2(float.MaxValue, maxHeight));
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
                _iconPickerSlot       = data.Slot;
                _iconPickerIsWeapon   = false;
                _iconPickerIsBonus    = false;
                _iconPickerClickY     = Im.Item.UpperLeftCorner.Y;
                _iconPickerNameFilter = string.Empty;
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

        DrawIconPickerFilterBar();

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
            foreach (var equipItem in SortIconPickerItems(list))
            {
                if (_config.OwnedOnlyComboFilter
                    && !_itemUnlockManager.IsOwnedFromSources(equipItem.ItemId, _config.OwnedComboFilterSources))
                    continue;

                if (modelSet != null && !modelSet.Add((equipItem.Type, equipItem.PrimaryId, equipItem.SecondaryId, equipItem.Variant)))
                    continue;

                if (!FilterIconPickerItem(in equipItem))
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
                _iconPickerBonusSlot  = data.Slot;
                _iconPickerIsWeapon   = false;
                _iconPickerIsBonus    = true;
                _iconPickerClickY     = Im.Item.UpperLeftCorner.Y;
                _iconPickerNameFilter = string.Empty;
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

        DrawIconPickerFilterBar();

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
            foreach (var equipItem in SortIconPickerItems(list))
            {
                if (_config.OwnedOnlyComboFilter
                    && !_itemUnlockManager.IsOwnedFromSources(equipItem.ItemId, _config.OwnedComboFilterSources))
                    continue;

                if (modelSet != null && !modelSet.Add((equipItem.Type, equipItem.PrimaryId, equipItem.SecondaryId, equipItem.Variant)))
                    continue;

                if (!FilterIconPickerItem(in equipItem))
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
                _iconPickerSlot       = slot;
                _iconPickerIsWeapon   = true;
                _iconPickerClickY     = Im.Item.UpperLeftCorner.Y;
                _iconPickerNameFilter = string.Empty;
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

        DrawIconPickerFilterBar();

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

                foreach (var item in SortIconPickerItems(l))
                {
                    if (_config.OwnedOnlyComboFilter
                        && !_itemUnlockManager.IsOwnedFromSources(item.ItemId, _config.OwnedComboFilterSources))
                        continue;

                    if (modelSet != null && !modelSet.Add((item.Type, item.PrimaryId, item.SecondaryId, item.Variant)))
                        continue;

                    if (!FilterIconPickerItem(in item))
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
            foreach (var item in SortIconPickerItems(list))
            {
                if (_config.OwnedOnlyComboFilter
                    && !_itemUnlockManager.IsOwnedFromSources(item.ItemId, _config.OwnedComboFilterSources))
                    continue;

                if (modelSet != null && !modelSet.Add((item.Type, item.PrimaryId, item.SecondaryId, item.Variant)))
                    continue;

                if (!FilterIconPickerItem(in item))
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
