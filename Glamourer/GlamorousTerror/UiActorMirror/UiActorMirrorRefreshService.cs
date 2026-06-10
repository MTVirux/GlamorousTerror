using Glamourer.Events;
using Glamourer.Interop;
using Glamourer.State;
using Luna;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Interop;
using Penumbra.GameData.Structs;

namespace Glamourer.Services;

// The draw-object hooks in StateListener.UiActor.cs only re-mirror a UI actor when the game itself
// reloads its slots, so a state change made after the window refreshed (equipping/unequipping gear,
// editing the glamour) would never reach the menu actor. This service pushes gear changes onto live
// UI actors directly. Like the hooks, it reads the resolved character's state strictly read-only and
// writes only into the UI actor's transient draw data.
public sealed class UiActorMirrorRefreshService : IDisposable, IRequiredService
{
    private readonly UiActorMirrorService _uiActorMirror;
    private readonly StateManager         _stateManager;
    private readonly ActorObjectManager   _objects;
    private readonly UpdateSlotService    _updateSlot;
    private readonly WeaponService        _weapons;
    private readonly StateApplier         _applier;
    private readonly StateChanged         _stateChanged;
    private readonly StateFinalized       _stateFinalized;

    public UiActorMirrorRefreshService(UiActorMirrorService uiActorMirror, StateManager stateManager, ActorObjectManager objects,
        UpdateSlotService updateSlot, WeaponService weapons, StateApplier applier, StateChanged stateChanged, StateFinalized stateFinalized)
    {
        _uiActorMirror  = uiActorMirror;
        _stateManager   = stateManager;
        _objects        = objects;
        _updateSlot     = updateSlot;
        _weapons        = weapons;
        _applier        = applier;
        _stateChanged   = stateChanged;
        _stateFinalized = stateFinalized;
        _stateChanged.Subscribe(OnStateChanged, (StateChanged.Priority)(-2000));
        _stateFinalized.Subscribe(OnStateFinalized, (StateFinalized.Priority)(-2000));
    }

    public void Dispose()
    {
        _stateChanged.Unsubscribe(OnStateChanged);
        _stateFinalized.Unsubscribe(OnStateFinalized);
    }

    private void OnStateChanged(in StateChanged.Arguments arguments)
        => Refresh(arguments.State);

    private void OnStateFinalized(in StateFinalized.Arguments _)
        => Refresh(null);

    private void Refresh(ActorState? only)
    {
        for (var idx = (ushort)ScreenActor.CharacterScreen; idx <= (ushort)ScreenActor.Card8; ++idx)
        {
            var actor = _objects.Objects[new ObjectIndex(idx)];
            if (!actor.Valid || !actor.IsCharacter)
                continue;

            var model = actor.Model;
            if (!model.IsHuman)
                continue;

            if (!_uiActorMirror.TryResolve(actor.GetIdentifier(_objects.Actors), out var realId, out _, out var mask) || !mask.Gear)
                continue;

            if (!_stateManager.TryGetValue(realId, out var state))
                continue;

            if (only != null && !ReferenceEquals(state, only))
                continue;

            PushGear(actor, model, state);
            PushMaterials(actor, state);
        }
    }

    // Advanced dyes (material colour-table edits) are not part of a gear-slot reload, so the draw-object hooks
    // never carry them onto a window that is already open. ChangeMaterialValues reads the mirrored state
    // read-only and writes only into the UI actor's live colour-table textures, so nothing is persisted onto
    // the mirrored character. Gated by the caller on the surface's Gear mask, like the gear push.
    private void PushMaterials(Actor actor, ActorState state)
    {
        if (state.Materials.Values.Count == 0)
            return;

        _applier.ChangeMaterialValues(new ActorData(actor, "UI Actor Mirror"), state.Materials);
    }

    // Equality-guarded so redundant events cause no slot reloads; all writes go through the
    // hooks' .Original paths, so none of this re-enters StateListener.
    private void PushGear(Actor actor, Model model, ActorState state)
    {
        foreach (var slot in EquipSlotExtensions.EqdpSlots)
        {
            var armor = state.ModelData.ArmorWithState(slot);
            if (model.GetArmor(slot).Value != armor.Value)
                _updateSlot.UpdateEquipSlot(model, slot, armor);
        }

        foreach (var slot in BonusExtensions.AllFlags)
        {
            var armor = state.ModelData.BonusItem(slot).Armor();
            if (model.GetBonus(slot).Value != armor.Value)
                _updateSlot.UpdateBonusSlot(model, slot, armor);
        }

        var (_, _, mainData, offData) = model.GetWeapons(actor);
        PushWeapon(actor, EquipSlot.MainHand, mainData, state.ModelData.Weapon(EquipSlot.MainHand));
        PushWeapon(actor, EquipSlot.OffHand,  offData,  state.ModelData.Weapon(EquipSlot.OffHand));
    }

    private void PushWeapon(Actor actor, EquipSlot slot, CharacterWeapon current, CharacterWeapon target)
    {
        // Stains on an empty weapon crash the game.
        if (target.Skeleton.Id is 0)
            target = target.With(StainIds.None);

        if (current.Value != target.Value)
            _weapons.LoadWeapon(actor, slot, target);
    }
}
