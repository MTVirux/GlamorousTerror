using Glamourer.Config;
using ImSharp;
using Penumbra.GameData.Enums;

namespace Glamourer.GlamorousTerror.WeaponUnlock;

/// <summary>
/// Weapon type compatibility checks that can be bypassed by the Unrestricted Weapons setting.
/// Upstream only allows swapping a weapon for one of the same type, which keeps every weapon
/// tied to the job that can equip it. These wrappers replace the upstream calls at every point
/// where that restriction is enforced.
/// </summary>
public static class WeaponRestrictions
{
    /// <inheritdoc cref="FullEquipTypeExtensions.IsCompatible"/>
    public static bool IsCompatibleGT(this FullEquipType type, FullEquipType other, Configuration config)
        => config.UnrestrictedWeapons || type.IsCompatible(other);

    /// <inheritdoc cref="FullEquipTypeExtensions.IsOffhandCompatible"/>
    public static bool IsOffhandCompatibleGT(this FullEquipType type, FullEquipType gameMainhand, FullEquipType actualMainhand,
        FullEquipType gameOffhand, Configuration config)
        => config.UnrestrictedWeapons || type.IsOffhandCompatible(gameMainhand, actualMainhand, gameOffhand);

    /// <summary>
    /// The equipment types listed by an unrestricted weapon combo.
    /// <see cref="FullEquipType.Unknown"/> keys the mainhand list, <see cref="FullEquipType.UnknownOffhand"/> the offhand one.
    /// The offhand also offers every mainhand, since the game happily loads a mainhand model into the offhand slot.
    /// </summary>
    public static IEnumerable<FullEquipType> UnrestrictedTypes(FullEquipType comboType)
        => comboType is FullEquipType.UnknownOffhand
            ? FullEquipType.Values.Where(e => e.ToSlot() is EquipSlot.OffHand)
                .Concat(FullEquipType.Values.Where(e => e.ToSlot() is EquipSlot.MainHand))
            : FullEquipType.Values.Where(e => e.ToSlot() is EquipSlot.MainHand);
}
