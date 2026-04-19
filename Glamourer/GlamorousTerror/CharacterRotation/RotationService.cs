using Dalamud.Plugin.Services;
using Luna;
using Penumbra.GameData.Interop;
using Quaternion = FFXIVClientStructs.FFXIV.Common.Math.Quaternion;

namespace Glamourer.Services;

public sealed unsafe class RotationService : IDisposable, IService
{
    private readonly IFramework _framework;
    private readonly Dictionary<nint, RotationOverride> _overrides = [];

    private bool _subscribed;

    public RotationService(IFramework framework)
        => _framework = framework;

    public void SetRotation(Actor actor, Vector3 offsetDegrees)
    {
        if (!actor.Valid || !actor.Model)
            return;

        var original = _overrides.TryGetValue(actor.Address, out var existing)
            ? existing.OriginalRotation
            : actor.Model.AsDrawObject->Object.Rotation;

        var offsetQuat = Quaternion.CreateFromEuler(new FFXIVClientStructs.FFXIV.Common.Math.Vector3(
            offsetDegrees.X, offsetDegrees.Y, offsetDegrees.Z));
        var finalQuat = MultiplyQuaternions(original, offsetQuat);

        _overrides[actor.Address] = new RotationOverride(finalQuat, actor.Model.Address, original, offsetDegrees);
        EnsureSubscribed();
    }

    public void ClearRotation(Actor actor)
    {
        if (actor.Address == nint.Zero)
            return;

        if (_overrides.Remove(actor.Address, out _))
        {
            RestoreGameRotation(actor);

            if (_overrides.Count == 0)
                EnsureUnsubscribed();
        }
    }

    public void ClearAll()
    {
        foreach (var (address, _) in _overrides)
        {
            var actor = (Actor)address;
            RestoreGameRotation(actor);
        }

        _overrides.Clear();
        EnsureUnsubscribed();
    }

    public bool TryGetEuler(Actor actor, out Vector3 euler)
    {
        if (_overrides.TryGetValue(actor.Address, out var ov))
        {
            euler = ov.OffsetDegrees;
            return true;
        }

        euler = Vector3.Zero;
        return false;
    }

    public Vector3 GetActorEuler(Actor actor)
    {
        if (!actor.Valid || !actor.Model)
            return Vector3.Zero;

        var e = actor.Model.AsDrawObject->Object.Rotation.EulerAngles;
        return new Vector3(e.X, e.Y, e.Z);
    }

    public void Dispose()
    {
        ClearAll();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (_overrides.Count == 0)
            return;

        List<nint>? stale = null;
        foreach (var (address, ov) in _overrides)
        {
            var actor = (Actor)address;
            if (!actor.Valid || !actor.Model)
            {
                (stale ??= []).Add(address);
                continue;
            }

            var drawObj = actor.Model.AsDrawObject;
            drawObj->Object.Rotation = ov.Rotation;
            drawObj->Object.IsTransformChanged = true;

            // Rotate child objects (weapons) to match.
            var child = drawObj->Object.ChildObject;
            if (child != null)
            {
                child->Rotation = ov.Rotation;
                child->IsTransformChanged = true;
            }
        }

        if (stale != null)
        {
            foreach (var address in stale)
                _overrides.Remove(address);
            if (_overrides.Count == 0)
                EnsureUnsubscribed();
        }
    }

    private void EnsureSubscribed()
    {
        if (_subscribed)
            return;

        _framework.Update += OnFrameworkUpdate;
        _subscribed = true;
    }

    private void EnsureUnsubscribed()
    {
        if (!_subscribed)
            return;

        _framework.Update -= OnFrameworkUpdate;
        _subscribed = false;
    }

    /// <summary> Restore the draw object's rotation from the game object's current Rotation field (yaw). </summary>
    private static void RestoreGameRotation(Actor actor)
    {
        if (!actor.Valid || !actor.Model)
            return;

        var yaw     = actor.AsObject->Rotation;
        var halfYaw = yaw * 0.5f;
        var gameQuat = new Quaternion
        {
            X = 0f,
            Y = MathF.Sin(halfYaw),
            Z = 0f,
            W = MathF.Cos(halfYaw),
        };

        var drawObj = actor.Model.AsDrawObject;
        drawObj->Object.Rotation           = gameQuat;
        drawObj->Object.IsTransformChanged = true;

        var child = drawObj->Object.ChildObject;
        if (child != null)
        {
            child->Rotation           = gameQuat;
            child->IsTransformChanged = true;
        }
    }

    private static Quaternion MultiplyQuaternions(Quaternion a, Quaternion b)
    {
        var sa = new System.Numerics.Quaternion(a.X, a.Y, a.Z, a.W);
        var sb = new System.Numerics.Quaternion(b.X, b.Y, b.Z, b.W);
        var sr = System.Numerics.Quaternion.Multiply(sa, sb);
        return new Quaternion { X = sr.X, Y = sr.Y, Z = sr.Z, W = sr.W };
    }

    private readonly record struct RotationOverride(Quaternion Rotation, nint LastModelAddress, Quaternion OriginalRotation, Vector3 OffsetDegrees);
}
