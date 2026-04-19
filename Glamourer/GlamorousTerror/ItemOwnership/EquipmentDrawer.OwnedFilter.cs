using Glamourer.Config;
using Glamourer.Unlocks;
using ImSharp;

namespace Glamourer.Gui.Equipment;

public sealed partial class EquipmentDrawer
{
    public static void DrawOwnedOnlyFilter(Configuration config)
    {
        if (Im.Checkbox("Show Only Owned Items in Combos"u8, config.OwnedOnlyComboFilter))
        {
            config.OwnedOnlyComboFilter ^= true;
            config.Save();
        }
        Im.Tooltip.OnHover(
            "When enabled, equipment, weapon, and bonus item combo dropdowns will only show items you own.\nUse the source toggles below to control which sources count."u8);

        if (config.OwnedOnlyComboFilter)
        {
            using var indent = Im.Indent();
            DrawSourceToggle(config, "Inventory"u8,         ItemUnlockManager.ItemSource.Inventory);
            DrawSourceToggle(config, "Glamour Dresser"u8,   ItemUnlockManager.ItemSource.GlamourDresser);
            DrawSourceToggle(config, "Armoire"u8,           ItemUnlockManager.ItemSource.Armoire);
            DrawSourceToggle(config, "Saddlebags"u8,        ItemUnlockManager.ItemSource.Saddlebags);
            DrawSourceToggle(config, "Retainers"u8,         ItemUnlockManager.ItemSource.Retainers);
            DrawSourceToggle(config, "Quest / Achievement"u8, ItemUnlockManager.ItemSource.QuestAchievement);
        }
    }

    private static void DrawSourceToggle(Configuration config, ReadOnlySpan<byte> label, ItemUnlockManager.ItemSource flag)
    {
        var enabled = (config.OwnedComboFilterSources & flag) != 0;
        if (Im.Checkbox(label, enabled))
        {
            config.OwnedComboFilterSources ^= flag;
            config.Save();
        }
    }
}
