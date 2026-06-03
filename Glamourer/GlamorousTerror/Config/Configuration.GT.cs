using ImSharp;

namespace Glamourer.Config;

/// <summary>
/// Language override for equipment item names.
/// </summary>
public enum EquipmentNameLanguage
{
    /// <summary> Use the game's current language setting. </summary>
    GameDefault,
    /// <summary> Display equipment names in English. </summary>
    English,
    /// <summary> Display equipment names in Japanese. </summary>
    Japanese,
    /// <summary> Display equipment names in German. </summary>
    German,
    /// <summary> Display equipment names in French. </summary>
    French,
}

public sealed partial class Configuration
{
    // --- GlamorousTerror-specific configuration properties ---

    public bool                  UseIconEquipmentDrawer     { get; set; } = false;
    public int                   IconPickerMaxRows          { get; set; } = 10;
    public bool                  GroupIconPickerByModel     { get; set; } = true;
    public bool                  KeepIconPickerOpen         { get; set; } = false;
    public bool                  IconPickerPinned           { get; set; } = false;
    public bool                  RememberIconPickerScroll   { get; set; } = false;
    public bool                  EnableImmersiveDresser      { get; set; } = true;
    public bool                  SingleWindowDresser         { get; set; } = false;
    public bool                  SimplifiedDresserLayout     { get; set; } = false;
    public bool                  OverrideDresserBgColor      { get; set; } = false;
    public Rgba32                ImmersiveDresserBgColor     { get; set; } = default;
    public bool                  AutoHideGameUi              { get; set; } = false;
    public bool                  LockImmersiveDresserPanels { get; set; } = false;
    public float                 ImmersiveDresserCameraY    { get; set; } = 0f;
    public bool                  AllowCameraClipping        { get; set; } = false;
    public bool                  DisableFirstPerson         { get; set; } = false;

    public EquipmentNameLanguage EquipmentNameLanguage        { get; set; } = EquipmentNameLanguage.GameDefault;
    public bool                  CrossLanguageEquipmentSearch { get; set; } = false;

    public bool                                   OwnedOnlyComboFilter    { get; set; } = false;
    public Unlocks.ItemUnlockManager.ItemSource   OwnedComboFilterSources { get; set; } = Unlocks.ItemUnlockManager.ItemSource.All;

    public Services.CodeService.CodeFlag          EnabledCheats           { get; set; } = 0;

    // --- UI Actor Glamour Mirroring (Glamorous Terror) ---
    public bool MirrorUiActors { get; set; } = false;

    public bool MirrorCharacterWindow          { get; set; } = false;
    public bool MirrorCharacterWindowCustomize { get; set; } = true;
    public bool MirrorCharacterWindowGear      { get; set; } = true;

    public bool MirrorExamine          { get; set; } = false;
    public bool MirrorExamineCustomize { get; set; } = true;
    public bool MirrorExamineGear      { get; set; } = true;

    public bool MirrorFittingRoom          { get; set; } = false;
    public bool MirrorFittingRoomCustomize { get; set; } = true;
    public bool MirrorFittingRoomGear      { get; set; } = true;

    public bool MirrorDyePreview          { get; set; } = false;
    public bool MirrorDyePreviewCustomize { get; set; } = true;

    public bool MirrorAdventurerPlate          { get; set; } = false;
    public bool MirrorAdventurerPlateCustomize { get; set; } = true;
    public bool MirrorAdventurerPlateGear      { get; set; } = true;

    public bool MirrorBanner          { get; set; } = false;
    public bool MirrorBannerCustomize { get; set; } = true;
    public bool MirrorBannerGear      { get; set; } = true;
}
