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
    private ActorState? _gtUiState;

    // _creatingIdentifier is left as the Special id so upstream's Reduce/UpdateBaseData never runs
    // against — and corrupts — the real character's state; the look-up here is read-only.
    private partial void GTResolveUiActor()
    {
        _gtUiActive      = false;
        _gtUiGearAllowed = false;
        _gtUiState       = null;

        if (!_uiActorMirror.TryResolve(_creatingIdentifier, out var realId, out _, out var mask))
            return;

        if (!_manager.TryGetValue(realId, out var state))
            return;

        _gtUiState       = state;
        _gtUiMask        = mask;
        _gtUiGearAllowed = mask.Gear;
        _gtUiActive      = true;
    }

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
                armor[idx] = _gtUiState.ModelData.ArmorWithState(slot);
            }
        }
    }

    // Special UI actors reload gear per slot after creation, overwriting the creation-time mirror.
    private partial void GTMirrorUiEquipSlot(Actor actor, EquipSlot slot, ref CharacterArmor armor)
    {
        if (!actor.Valid || actor.Index.Index < (ushort)ScreenActor.CharacterScreen)
            return;

        if (!_uiActorMirror.TryResolve(actor.GetIdentifier(_actors), out var realId, out _, out var mask) || !mask.Gear)
            return;

        if (_manager.TryGetValue(realId, out var state))
            armor = state.ModelData.ArmorWithState(slot);
    }

    private partial void GTMirrorUiBonusSlot(Actor actor, BonusItemFlag slot, ref CharacterArmor armor)
    {
        if (!actor.Valid || actor.Index.Index < (ushort)ScreenActor.CharacterScreen)
            return;

        if (!_uiActorMirror.TryResolve(actor.GetIdentifier(_actors), out var realId, out _, out var mask) || !mask.Gear)
            return;

        if (_manager.TryGetValue(realId, out var state))
            armor = state.ModelData.BonusItem(slot).Armor();
    }

    private partial void GTMirrorUiWeapon(Actor actor, EquipSlot slot, ref CharacterWeapon weapon)
    {
        if (!actor.Valid || actor.Index.Index < (ushort)ScreenActor.CharacterScreen)
            return;

        if (!_uiActorMirror.TryResolve(actor.GetIdentifier(_actors), out var realId, out _, out var mask) || !mask.Gear)
            return;

        if (_manager.TryGetValue(realId, out var state))
            weapon = state.ModelData.Weapon(slot);
    }
}
