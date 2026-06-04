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

        DrawUiActorMirrorSettings();

        Im.Line.New();
    }

    private void DrawUiActorMirrorSettings()
    {
        Im.Dummy(Vector2.Zero);
        Im.Separator();
        Im.Dummy(Vector2.Zero);

        using var tree = Im.Tree.Node("UI Actors"u8);
        if (!tree)
            return;

        Checkbox("Mirror Glamour onto UI Actors"u8,
            "Master switch. When enabled, the character models shown in menus (character window, examine, fitting room, dye preview, adventurer plate, party/PvP banners) reflect your glamoured appearance instead of your real gear."u8,
            config.MirrorUiActors, v => config.MirrorUiActors = v);

        if (!config.MirrorUiActors)
            return;

        DrawSurfaceRow("Character Window"u8,
            "Your own gear/character window."u8,
            config.MirrorCharacterWindow, v => config.MirrorCharacterWindow = v,
            config.MirrorCharacterWindowCustomize, v => config.MirrorCharacterWindowCustomize = v,
            config.MirrorCharacterWindowGear, v => config.MirrorCharacterWindowGear = v);

        DrawSurfaceRow("Examine"u8,
            "Other players you inspect. Only applies a design if you have automation matching that player."u8,
            config.MirrorExamine, v => config.MirrorExamine = v,
            config.MirrorExamineCustomize, v => config.MirrorExamineCustomize = v,
            config.MirrorExamineGear, v => config.MirrorExamineGear = v);

        DrawSurfaceRow("Fitting Room"u8,
            "The try-on window. Only customizations are mirrored here so the gear you are trying on shows normally."u8,
            config.MirrorFittingRoom, v => config.MirrorFittingRoom = v,
            config.MirrorFittingRoomCustomize, v => config.MirrorFittingRoomCustomize = v,
            false, null);

        DrawSurfaceRow("Dye Preview"u8,
            "The dye preview window. Only customizations are mirrored here — the window shows the item set being dyed, so its gear is left untouched."u8,
            config.MirrorDyePreview, v => config.MirrorDyePreview = v,
            config.MirrorDyePreviewCustomize, v => config.MirrorDyePreviewCustomize = v,
            false, null);

        DrawSurfaceRow("Adventurer Plate"u8,
            "Your own portrait shown on the adventurer plate / banner."u8,
            config.MirrorAdventurerPlate, v => config.MirrorAdventurerPlate = v,
            config.MirrorAdventurerPlateCustomize, v => config.MirrorAdventurerPlateCustomize = v,
            config.MirrorAdventurerPlateGear, v => config.MirrorAdventurerPlateGear = v);

        DrawSurfaceRow("Party / PvP Banners"u8,
            "Group banner/card portraits. Mirrors your look for yourself and applies matching automation designs for other players shown."u8,
            config.MirrorBanner, v => config.MirrorBanner = v,
            config.MirrorBannerCustomize, v => config.MirrorBannerCustomize = v,
            config.MirrorBannerGear, v => config.MirrorBannerGear = v);
    }

    private void DrawSurfaceRow(ReadOnlySpan<byte> label, ReadOnlySpan<byte> tooltip,
        bool enabled, Action<bool> setEnabled,
        bool customize, Action<bool> setCustomize,
        bool gear, Action<bool>? setGear)
    {
        using var id = Im.Id.Push(label);
        Checkbox(label, tooltip, enabled, setEnabled);
        if (!enabled)
            return;

        using var indent = Im.Indent();
        Checkbox("Customizations"u8, "Mirror body/face customizations (skin, hair, etc.) for this surface."u8,
            customize, setCustomize);
        if (setGear != null)
            Checkbox("Gear"u8, "Mirror equipment for this surface."u8, gear, setGear);
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
