using Glamourer.Services;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Glamourer.State;

public sealed partial class StateListener
{
    private bool         _gtUiActive;
    private UiActorMask  _gtUiMask;
    private bool         _gtUiGearAllowed;
    private ushort       _gtUiPreviewMask;

    /// <summary>
    /// Glamorous Terror: if the actor being created is a special UI/menu actor and its surface is
    /// enabled, remap <see cref="_creatingIdentifier"/> to the real character so the normal
    /// Reduce + apply path resolves that character's glamour state.
    /// </summary>
    private partial void GTRemapUiActor()
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
            if (_uiActorMirror.TryGetPreviewedSlotMask(out _gtUiPreviewMask))
            { /* keep gear; previewed slots are skipped in GTApplyUiActor */ }
            else
                _gtUiGearAllowed = false;
        }

        _gtUiActive         = true;
        _creatingIdentifier = realId;
    }

    /// <summary>
    /// Glamorous Terror: authoritatively write the resolved glamour state into the UI actor's
    /// customize/equip buffers for the enabled aspects, skipping any previewed slots.
    /// </summary>
    private unsafe partial void GTApplyUiActor(nint customizePtr, nint equipDataPtr)
    {
        if (!_gtUiActive || _creatingState is null)
            return;

        if (_gtUiMask.Customize)
            *(CustomizeArray*)customizePtr = _creatingState.ModelData.Customize;

        if (_gtUiGearAllowed)
        {
            var armor = (CharacterArmor*)equipDataPtr;
            foreach (var slot in EquipSlotExtensions.EqdpSlots)
            {
                var idx = (int)slot.ToIndex();
                if (idx is < 0 or >= 10)
                    continue;
                if ((_gtUiPreviewMask & (1 << idx)) != 0)
                    continue;
                armor[idx] = _creatingState.ModelData.ArmorWithState(slot);
            }
        }
    }
}
