using Glamourer.Config;
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
}
