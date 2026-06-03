using Glamourer.Services;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Structs;

namespace Glamourer.State;

public sealed partial class StateListener
{
    private bool        _gtUiActive;
    private UiActorMask _gtUiMask;
    private bool        _gtUiGearAllowed;
    private ushort      _gtUiPreviewMask;
    private ActorState? _gtUiState;

    /// <summary>
    /// Glamorous Terror: detect a special UI/menu actor and look up (read-only) the real character's
    /// glamour state. Deliberately does NOT touch <see cref="_creatingIdentifier"/>, so the upstream
    /// Reduce/UpdateBaseData path stays skipped for the special actor and the real character's state
    /// is never mutated — only the UI actor's transient draw buffers are written, in GTApplyUiActor.
    /// </summary>
    private partial void GTResolveUiActor()
    {
        _gtUiActive      = false;
        _gtUiGearAllowed = false;
        _gtUiPreviewMask = 0;
        _gtUiState       = null;

        if (!_uiActorMirror.TryResolve(_creatingIdentifier, out var realId, out var surface, out var mask))
            return;

        if (!_manager.TryGetValue(realId, out var state))
            return;

        _gtUiState       = state;
        _gtUiMask        = mask;
        _gtUiGearAllowed = mask.Gear;

        if (mask.Gear && surface is UiActorSurface.FittingRoom or UiActorSurface.DyePreview)
        {
            // Respect the window's "show other gear" toggle and preserve the previewed slot. If the
            // agent cannot be read, fall back to customizations-only rather than clobbering the preview.
            if (_uiActorMirror.TryGetPreviewState(out _gtUiPreviewMask, out var displayGear))
                _gtUiGearAllowed = displayGear;
            else
                _gtUiGearAllowed = false;
        }

        _gtUiActive = true;
    }

    /// <summary>
    /// Glamorous Terror: write the resolved glamour into the special UI actor's draw buffers for the
    /// enabled aspects only. Disabled aspects and previewed slots keep the game's original values,
    /// since the upstream apply path is skipped for these actors and leaves the buffers untouched.
    /// </summary>
    private unsafe partial void GTApplyUiActor(nint customizePtr, nint equipDataPtr)
    {
        if (!_gtUiActive || _gtUiState is null)
            return;

        if (_gtUiMask.Customize)
            *(CustomizeArray*)customizePtr = _gtUiState.ModelData.Customize;

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
                armor[idx] = _gtUiState.ModelData.ArmorWithState(slot);
            }
        }
    }

    /// <summary>
    /// Glamorous Terror: special UI actors (fitting room, dye preview, banners, …) reload their
    /// equipment per slot through this hook after creation, which would overwrite the creation-time
    /// mirror. Re-apply the glamoured armor here for the enabled, non-previewed slots, read-only.
    /// </summary>
    private partial void GTMirrorUiEquipSlot(Actor actor, EquipSlot slot, ref CharacterArmor armor)
    {
        if (!actor.Valid || actor.Index.Index < (ushort)ScreenActor.CharacterScreen)
            return;

        if (!_uiActorMirror.TryResolve(actor.GetIdentifier(_actors), out var realId, out var surface, out var mask) || !mask.Gear)
            return;

        if (surface is UiActorSurface.FittingRoom or UiActorSurface.DyePreview)
        {
            // Honour the "show other gear" toggle; leave the slot the window is previewing. Bail if
            // the agent is unreadable or other gear is hidden, so the game's own display stands.
            if (!_uiActorMirror.TryGetPreviewState(out var previewMask, out var displayGear) || !displayGear)
                return;

            var idx = (int)slot.ToIndex();
            if (idx is >= 0 and < 10 && (previewMask & (1 << idx)) != 0)
                return;
        }

        if (_manager.TryGetValue(realId, out var state))
            armor = state.ModelData.ArmorWithState(slot);
    }

    /// <summary>
    /// Glamorous Terror: mirror the glamour's bonus item (e.g. glasses/eyewear) onto special UI actors,
    /// so their eyewear show/hide matches the glamour rather than the game's stored appearance.
    /// </summary>
    private partial void GTMirrorUiBonusSlot(Actor actor, BonusItemFlag slot, ref CharacterArmor armor)
    {
        if (!actor.Valid || actor.Index.Index < (ushort)ScreenActor.CharacterScreen)
            return;

        if (!_uiActorMirror.TryResolve(actor.GetIdentifier(_actors), out var realId, out var surface, out var mask) || !mask.Gear)
            return;

        // Try-on/dye windows have their own gear handling; leave their bonus slots to the game.
        if (surface is UiActorSurface.FittingRoom or UiActorSurface.DyePreview)
            return;

        if (_manager.TryGetValue(realId, out var state))
            armor = state.ModelData.BonusItem(slot).Armor();
    }
}
