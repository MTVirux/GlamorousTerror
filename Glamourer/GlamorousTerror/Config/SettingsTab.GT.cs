using Glamourer.Config;
using Glamourer.Gui.Equipment;
using Glamourer.Interop;
using Glamourer.Services;
using ImSharp;
using Luna;

namespace Glamourer.Gui.Tabs.SettingsTab;

public sealed partial class SettingsTab
{
    // GT-specific constructor dependencies are resolved via DI (already injected in primary constructor).
    // These are: ContextMenuService contextMenuService, ItemNameService itemNameService

    private void DrawGlamorousTerrorSettings()
    {
        if (!Im.Tree.Header("Glamorous Terror"u8))
            return;

        Checkbox("Enable Game Context Menus"u8,
            "Whether to show a Glamorous Terror submenu on character right-click context menus."u8,
            config.EnableGameContextMenu, v =>
            {
                config.EnableGameContextMenu = v;
                if (v)
                    contextMenuService.Enable();
                else
                    contextMenuService.Disable();
            });

        Checkbox("Enable Immersive Dresser"u8,
            "Whether to show an Immersive Dresser option when right-clicking your own character. Opens a fullscreen equipment overlay with the game UI hidden."u8,
            config.EnableImmersiveDresser, v =>
            {
                config.EnableImmersiveDresser = v;
            });

        Im.Dummy(Vector2.Zero);
        Im.Separator();
        Im.Dummy(Vector2.Zero);

        var currentLang  = config.EquipmentNameLanguage;
        var currentLabel = _equipmentLanguages.FirstOrDefault(l => l.Language == currentLang).Label ?? currentLang.ToString();

        Im.Item.SetNextWidthScaled(300);
        using (var combo = Im.Combo.Begin("##gtEquipLangCombo"u8, currentLabel))
        {
            if (combo)
                foreach (var (lang, label) in _equipmentLanguages)
                {
                    if (Im.Selectable(label, lang == currentLang))
                    {
                        config.EquipmentNameLanguage = lang;
                        config.Save();
                        itemNameService.ClearCache();
                    }
                }
        }

        LunaStyle.DrawAlignedHelpMarkerLabel("Equipment Name Language"u8,
            "Override the display language used for equipment item names. Requires a UI reload to take full effect."u8);

        Checkbox("Cross-Language Equipment Search"u8,
            "When enabled, equipment combo searches will match item names in all available languages, not just the selected display language."u8,
            config.CrossLanguageEquipmentSearch, v =>
            {
                config.CrossLanguageEquipmentSearch = v;
                itemNameService.ClearCache();
            });

        Im.Dummy(Vector2.Zero);
        Im.Separator();
        Im.Dummy(Vector2.Zero);

        EquipmentDrawer.DrawOwnedOnlyFilter(config);

        Im.Dummy(Vector2.Zero);
        Im.Separator();
        Im.Dummy(Vector2.Zero);

        Checkbox("Icon Equipment Drawer"u8,
            "Display equipment slots as a compact icon grid instead of name-based combo dropdowns.\nClick an icon to open the item selector. Right-click to clear or revert."u8,
            config.UseIconEquipmentDrawer, v => config.UseIconEquipmentDrawer = v);

        if (config.UseIconEquipmentDrawer)
        {
            Checkbox("Group by Model"u8,
                "When enabled, items that share the same visual model are grouped under a single icon in the picker."u8,
                config.GroupIconPickerByModel, v => config.GroupIconPickerByModel = v);

            Checkbox("Keep Picker Open"u8,
                "When enabled, the icon picker popup stays open after selecting an item instead of closing automatically."u8,
                config.KeepIconPickerOpen, v => config.KeepIconPickerOpen = v);

            var maxRows = config.IconPickerMaxRows;
            Im.Item.SetNextWidthScaled(200);
            if (Im.Slider("##iconPickerMaxRows"u8, ref maxRows, "%i"u8, 1, 20, SliderFlags.AlwaysClamp))
            {
                config.IconPickerMaxRows = maxRows;
                config.Save();
            }

            LunaStyle.DrawAlignedHelpMarkerLabel("Icon Picker Max Rows"u8,
                "Maximum number of rows visible in the icon equipment picker popup before scrolling."u8);
        }

        Im.Line.New();
    }

    private void DrawEquipmentLanguageSettings()
    {
        if (!Im.Tree.Header("Equipment Language Settings"u8))
            return;

        var currentLang = config.EquipmentNameLanguage;
        var currentLabel = _equipmentLanguages.FirstOrDefault(l => l.Language == currentLang).Label ?? currentLang.ToString();

        Im.Item.SetNextWidthScaled(300);
        using (var combo = Im.Combo.Begin("##equipLangCombo"u8, currentLabel))
        {
            if (combo)
                foreach (var (lang, label) in _equipmentLanguages)
                {
                    if (Im.Selectable(label, lang == currentLang))
                    {
                        config.EquipmentNameLanguage = lang;
                        config.Save();
                        itemNameService.ClearCache();
                    }
                }
        }

        LunaStyle.DrawAlignedHelpMarkerLabel("Equipment Name Language"u8,
            "Override the display language used for equipment item names. Requires a UI reload to take full effect."u8);

        Checkbox("Cross-Language Equipment Search"u8,
            "When enabled, equipment combo searches will match item names in all available languages, not just the selected display language."u8,
            config.CrossLanguageEquipmentSearch, v =>
            {
                config.CrossLanguageEquipmentSearch = v;
                itemNameService.ClearCache();
            });
    }

    private static readonly (EquipmentNameLanguage Language, string Label)[] _equipmentLanguages =
    [
        (EquipmentNameLanguage.GameDefault, "Game Default"),
        (EquipmentNameLanguage.English,     "English"),
        (EquipmentNameLanguage.German,      "German"),
        (EquipmentNameLanguage.French,      "French"),
        (EquipmentNameLanguage.Japanese,    "Japanese"),
    ];
}
