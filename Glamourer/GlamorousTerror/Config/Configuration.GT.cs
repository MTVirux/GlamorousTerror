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
    public bool                  EnableGameContextMenu      { get; set; } = true;
    public bool                  EnableImmersiveDresser     { get; set; } = true;
    public bool                  AutoHideGameUi             { get; set; } = false;
    public bool                  LockImmersiveDresserPanels { get; set; } = false;
    public float                 ImmersiveDresserCameraY    { get; set; } = 0f;
    public bool                  AllowCameraClipping        { get; set; } = false;
    public bool                  DisableFirstPerson         { get; set; } = false;

    public EquipmentNameLanguage EquipmentNameLanguage        { get; set; } = EquipmentNameLanguage.GameDefault;
    public bool                  CrossLanguageEquipmentSearch { get; set; } = false;

    public bool                                   OwnedOnlyComboFilter    { get; set; } = false;
    public Unlocks.ItemUnlockManager.ItemSource   OwnedComboFilterSources { get; set; } = Unlocks.ItemUnlockManager.ItemSource.All;
}
