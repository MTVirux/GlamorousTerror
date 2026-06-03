using Glamourer.Services;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.State;

public sealed partial class StateListener
{
    private bool                      _gtUiActive;
    private UiActorMask               _gtUiMask;
    private bool                      _gtUiGearAllowed;
    private ushort                    _gtUiPreviewMask;
    private CustomizeArray            _gtUiCustomizeSnapshot;
    private readonly CharacterArmor[] _gtUiEquipSnapshot = new CharacterArmor[10];

    /// <summary>
    /// Glamorous Terror: if the actor being created is a special UI/menu actor and its surface is
    /// enabled, snapshot the original buffers and remap <see cref="_creatingIdentifier"/> to the
    /// real character so the normal Reduce + apply path resolves that character's glamour state.
    /// </summary>
    private unsafe partial void GTRemapUiActor(nint customizePtr, nint equipDataPtr)
    {
        _gtUiActive      = false;
        _gtUiGearAllowed = false;
        _gtUiPreviewMask = 0;

        if (!_uiActorMirror.TryResolve(_creatingIdentifier, out var realId, out var surface, out var mask))
            return;

        _gtUiMask        = mask;
        _gtUiGearAllowed = mask.Gear;

        if (mask.Gear && surface is UiActorSurface.FittingRoom or UiActorSurface.DyePreview)
        {
            // Preserve the slot(s) the game is actively previewing. If the agent cannot be read,
            // fall back to customizations-only for these surfaces rather than clobbering the preview.
            if (!_uiActorMirror.TryGetPreviewedSlotMask(out _gtUiPreviewMask))
                _gtUiGearAllowed = false;
        }

        // Snapshot the game's original buffers before the apply path overwrites them, so disabled
        // aspects and previewed slots can be restored deterministically in GTApplyUiActor.
        _gtUiCustomizeSnapshot = *(CustomizeArray*)customizePtr;
        var srcArmor = (CharacterArmor*)equipDataPtr;
        for (var i = 0; i < 10; ++i)
            _gtUiEquipSnapshot[i] = srcArmor[i];

        _gtUiActive         = true;
        _creatingIdentifier = realId;
    }

    /// <summary>
    /// Glamorous Terror: author every aspect of the UI actor's customize/equip buffers — glamour
    /// from the resolved state for enabled, non-previewed aspects, and the original snapshot for
    /// disabled aspects and previewed slots. Deterministic regardless of the upstream branch taken.
    /// </summary>
    private unsafe partial void GTApplyUiActor(nint customizePtr, nint equipDataPtr)
    {
        if (!_gtUiActive || _creatingState is null)
            return;

        *(CustomizeArray*)customizePtr =
            _gtUiMask.Customize ? _creatingState.ModelData.Customize : _gtUiCustomizeSnapshot;

        var armor = (CharacterArmor*)equipDataPtr;
        foreach (var slot in EquipSlotExtensions.EqdpSlots)
        {
            var idx = (int)slot.ToIndex();
            if (idx is < 0 or >= 10)
                continue;

            var previewed = (_gtUiPreviewMask & (1 << idx)) != 0;
            armor[idx] = _gtUiGearAllowed && !previewed
                ? _creatingState.ModelData.ArmorWithState(slot)
                : _gtUiEquipSnapshot[idx];
        }
    }
}
