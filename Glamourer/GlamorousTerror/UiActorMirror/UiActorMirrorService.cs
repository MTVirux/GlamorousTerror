using Glamourer.Config;
using Luna;
using Penumbra.GameData.Actors;
using Penumbra.GameData.Enums;

namespace Glamourer.Services;

/// <summary>
/// Maps a special UI/menu actor (IdentifierType.Special, object index 440-447) to the real
/// character it represents and the configured mirroring mask for that surface.
/// </summary>
public sealed class UiActorMirrorService(ActorManager actors, Configuration config, UiActorPreviewSlots previewSlots) : IService
{
    /// <summary> Try to resolve a special identifier to a real character + enabled surface mask. </summary>
    public bool TryResolve(ActorIdentifier specialId, out ActorIdentifier realId, out UiActorSurface surface, out UiActorMask mask)
    {
        realId  = ActorIdentifier.Invalid;
        surface = UiActorSurface.None;
        mask    = default;

        if (!config.MirrorUiActors || specialId.Type != IdentifierType.Special)
            return false;

        surface = DetermineSurface(specialId.Special, out realId);
        if (surface == UiActorSurface.None || !realId.IsValid)
        {
            surface = UiActorSurface.None;
            return false;
        }

        if (!TryGetMask(surface, out mask))
            return false;

        return true;
    }

    /// <summary> Compute the previewed-slot bitmask for try-on / dye surfaces. </summary>
    /// <returns> True if detection succeeded; false if the try-on agent is unavailable. </returns>
    public bool TryGetPreviewState(out ushort previewMask, out bool displayGear)
        => previewSlots.TryGetPreviewState(out previewMask, out displayGear);

    private UiActorSurface DetermineSurface(ScreenActor screen, out ActorIdentifier realId)
    {
        // Banner/card contexts can occupy CharacterScreen..Card8; resolve them first.
        if (actors.ResolvePartyBannerPlayer(screen, out realId) && realId.IsValid)
            return UiActorSurface.Banner;
        if (actors.ResolvePvPBannerPlayer(screen, out realId) && realId.IsValid)
            return UiActorSurface.Banner;

        switch (screen)
        {
            case ScreenActor.ExamineScreen:
                realId = actors.GetInspectPlayer();
                return UiActorSurface.Examine;
            case ScreenActor.FittingRoom:
                realId = actors.GetCurrentPlayer();
                return UiActorSurface.FittingRoom;
            case ScreenActor.DyePreview:
                realId = actors.GetCurrentPlayer();
                return UiActorSurface.DyePreview;
            case ScreenActor.Portrait:
                realId = actors.GetCurrentPlayer();
                return UiActorSurface.AdventurerPlate;
            case ScreenActor.CharacterScreen:
                realId = actors.GetCurrentPlayer();
                return UiActorSurface.CharacterWindow;
            default:
                realId = ActorIdentifier.Invalid;
                return UiActorSurface.None;
        }
    }

    private bool TryGetMask(UiActorSurface surface, out UiActorMask mask)
    {
        var (enabled, customize, gear) = surface switch
        {
            UiActorSurface.CharacterWindow => (config.MirrorCharacterWindow, config.MirrorCharacterWindowCustomize, config.MirrorCharacterWindowGear),
            UiActorSurface.Examine         => (config.MirrorExamine,         config.MirrorExamineCustomize,         config.MirrorExamineGear),
            UiActorSurface.FittingRoom     => (config.MirrorFittingRoom,     config.MirrorFittingRoomCustomize,     config.MirrorFittingRoomGear),
            UiActorSurface.DyePreview      => (config.MirrorDyePreview,      config.MirrorDyePreviewCustomize,      false),
            UiActorSurface.AdventurerPlate => (config.MirrorAdventurerPlate, config.MirrorAdventurerPlateCustomize, config.MirrorAdventurerPlateGear),
            UiActorSurface.Banner          => (config.MirrorBanner,          config.MirrorBannerCustomize,          config.MirrorBannerGear),
            _                              => (false, false, false),
        };

        mask = new UiActorMask(customize, gear);
        return enabled;
    }
}
