using Glamourer.Config;
using Glamourer.Services;
using Glamourer.Unlocks;
using ImSharp;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.Gui.Equipment;

public sealed class WeaponCombo(FavoriteManager favorites, ItemManager items, Configuration config, FullEquipType slot, ItemNameService itemNameService, ItemUnlockManager itemUnlockManager)
    : BaseItemCombo(favorites, items, config, itemNameService, itemUnlockManager)
{
    public override StringU8      Label { get; } = GetLabel(slot);
    public readonly FullEquipType Slot = slot;

    protected override bool Identify(out EquipItem item)
    {
        if (!Slot.IsUnknown() && !ItemData.ConvertWeaponId(CustomSetId).IsCompatible(CurrentItem.Type))
        {
            item = default;
            return false;
        }
        item = Items.Identify(Slot.ToSlot(), CustomSetId, CustomWeaponId, CustomVariant);
        return true;
    }

    protected override IEnumerable<CacheItem> GetItems()
    {
        // GT: Unknown lists every mainhand, UnknownOffhand every offhand. Both are used by the
        // Unrestricted Weapons setting to offer weapons outside the character's current job.
        if (Slot is FullEquipType.Unknown or FullEquipType.UnknownOffhand)
        {
            var wantedSlot = Slot is FullEquipType.Unknown ? EquipSlot.MainHand : EquipSlot.OffHand;
            var enumerable = Array.Empty<EquipItem>().AsEnumerable();
            foreach (var t in FullEquipType.Values.Where(e => e.ToSlot() == wantedSlot))
            {
                if (Items.ItemData.ByType.TryGetValue(t, out var l))
                    enumerable = enumerable.Concat(l);
            }

            IEnumerable<EquipItem> all = enumerable.OrderByDescending(Favorites.Contains).ThenBy(e => e.Name);
            if (wantedSlot is EquipSlot.OffHand)
                all = all.Prepend(ItemManager.NothingItem(FullEquipType.Shield));
            return all.Select(e => new CacheItem(e));
        }

        if (!Items.ItemData.ByType.TryGetValue(Slot, out var list))
            return [];

        var enumerator = list.AsEnumerable();
        foreach(var compatible in Slot.CompatibleTypes())
            if (Items.ItemData.ByType.TryGetValue(compatible, out var l2))
                enumerator = enumerator.Concat(l2);

        IEnumerable<EquipItem> ret = enumerator.OrderByDescending(Favorites.Contains).ThenBy(e => e.Name);
        if (Slot.AllowsNothing())
            ret = ret.Prepend(ItemManager.NothingItem(Slot));
        return ret.Select(e => new CacheItem(e));
    }

    private static StringU8 GetLabel(FullEquipType type)
        => type switch
        {
            FullEquipType.UnknownOffhand => new StringU8("Offhand"u8),
            _ when type.IsUnknown()      => new StringU8("Mainhand"u8),
            _                            => new StringU8(type.ToName()),
        };
}