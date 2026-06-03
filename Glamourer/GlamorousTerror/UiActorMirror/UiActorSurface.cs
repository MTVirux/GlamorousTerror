namespace Glamourer.Services;

/// <summary> The distinct UI/menu contexts a special screen actor (object index 440-447) can represent. </summary>
public enum UiActorSurface
{
    None,
    CharacterWindow,
    Examine,
    FittingRoom,
    DyePreview,
    AdventurerPlate,
    Banner,
}

/// <summary> Which aspects of a glamour to mirror onto a UI actor for a given surface. </summary>
public readonly record struct UiActorMask(bool Customize, bool Gear);
