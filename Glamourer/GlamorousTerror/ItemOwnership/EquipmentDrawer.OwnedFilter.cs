using System.Numerics;
using Glamourer.Config;
using Glamourer.Unlocks;
using ImSharp;

namespace Glamourer.Gui.Equipment;

public sealed partial class EquipmentDrawer
{
    private const int SourceCount = 6;

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
            var preview = GetSourceSummary(config.OwnedComboFilterSources);
            using var combo = Im.Combo.Begin("##ownedSources"u8, preview, ComboFlags.HeightLargest);
            if (combo)
            {
                if (Im.Button("Select All"u8))
                {
                    config.OwnedComboFilterSources = ItemUnlockManager.ItemSource.All;
                    config.Save();
                }
                Im.Line.Same();
                if (Im.Button("Clear All"u8))
                {
                    config.OwnedComboFilterSources = 0;
                    config.Save();
                }
                Im.Separator();

                DrawSourceToggle(config, "Inventory"u8,           ItemUnlockManager.ItemSource.Inventory);
                DrawSourceToggle(config, "Glamour Dresser"u8,     ItemUnlockManager.ItemSource.GlamourDresser);
                DrawSourceToggle(config, "Armoire"u8,             ItemUnlockManager.ItemSource.Armoire);
                DrawSourceToggle(config, "Saddlebags"u8,          ItemUnlockManager.ItemSource.Saddlebags);
                DrawSourceToggle(config, "Retainers"u8,           ItemUnlockManager.ItemSource.Retainers);
                DrawSourceToggle(config, "Quest / Achievement"u8, ItemUnlockManager.ItemSource.QuestAchievement);
            }
        }
    }

    private static string GetSourceSummary(ItemUnlockManager.ItemSource sources)
    {
        if (sources == ItemUnlockManager.ItemSource.All)
            return "All Sources";

        var count = BitOperations.PopCount((byte)sources);
        return count switch
        {
            0 => "No Sources",
            _ => $"{count} of {SourceCount} Sources",
        };
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
