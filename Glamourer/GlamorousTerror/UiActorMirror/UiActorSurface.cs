namespace Glamourer.Services;

// The distinct UI/menu contexts a special screen actor (object index 440-447) can represent.
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

public readonly record struct UiActorMask(bool Customize, bool Gear);
