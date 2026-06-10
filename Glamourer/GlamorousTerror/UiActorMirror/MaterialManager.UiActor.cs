using Glamourer.Services;
using Penumbra.GameData.Interop;

namespace Glamourer.Interop.Material;

public sealed unsafe partial class MaterialManager
{
    // Advanced dyes (material colour-table edits) are applied per draw object in OnPrepareColorSet by looking up
    // the actor's own state. UI/menu actors (IdentifierType.Special) have no state of their own, so they would
    // never receive the player's advanced dyes. This resolves the mirrored character read-only and applies its
    // stored rows to a local copy of the colour table — it never reconciles against or writes back to that state
    // (unlike UpdateMaterialValues), keeping the mirror render-only. Gated on the surface's Gear mask.
    private bool GTApplyUiActorColorSet(Actor actor, MaterialValueIndex.DrawObjectType type, byte slotId, byte materialId,
        in PrepareColorSet.Arguments arguments)
    {
        if (!actor.Valid
         || !_uiActorMirror.TryResolve(actor.GetIdentifier(_actors), out var realId, out _, out var mask)
         || !mask.Gear
         || !_stateManager.TryGetValue(realId, out var state))
            return false;

        var min    = MaterialValueIndex.Min(type, slotId, materialId);
        var max    = MaterialValueIndex.Max(type, slotId, materialId);
        var values = state.Materials.GetValues(min, max);
        if (values.Length > 0 && PrepareColorSet.TryGetColorTable(arguments.Handle, arguments.Ids, out var baseColorSet))
        {
            var mode = PrepareColorSet.GetMode(arguments.Handle);
            for (var i = 0; i < values.Length; ++i)
            {
                var     idx = MaterialValueIndex.FromKey(values[i].Key);
                ref var row = ref baseColorSet[idx.RowIndex];
                values[i].Value.Model.Apply(ref row, mode);
            }

            if (MaterialService.GenerateNewColorTable(baseColorSet, out var texture))
                arguments.ReturnValue = (nint)texture;
        }

        // The actor is a mirrored UI surface; the upstream per-actor path must not run for it (it would find no
        // state and, for any other resolution, could mutate the wrong state), so claim it regardless.
        return true;
    }
}
