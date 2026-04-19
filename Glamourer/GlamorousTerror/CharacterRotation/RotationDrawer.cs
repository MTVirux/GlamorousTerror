using ImSharp;
using Luna;
using Penumbra.GameData.Interop;

namespace Glamourer.Services;

public sealed class RotationDrawer(RotationService rotationService) : IService
{
    private nint _lastActor;
    private Vector3 _euler; // degrees
    private bool _initialized;

    public void Draw(Actor actor)
    {
        if (!actor.Valid || !actor.Model)
            return;

        // When the target actor changes, clear previous override and reinitialize.
        if (actor.Address != _lastActor)
        {
            if (_lastActor != nint.Zero)
                rotationService.ClearRotation((Actor)_lastActor);
            _lastActor = actor.Address;
            _initialized = false;
        }

        if (!_initialized)
        {
            _euler = rotationService.TryGetEuler(actor, out var existing)
                ? existing
                : Vector3.Zero;
            _initialized = true;
        }

        var changed = false;

        Im.Item.SetNextWidthScaled(200);
        if (Im.Drag("##rotYaw"u8, ref _euler.Y, FormatDegrees(_euler.Y), float.MinValue, float.MaxValue, 1f))
            changed = true;
        Im.Line.Same();
        Im.Text("Yaw"u8);
        Im.Tooltip.OnHover("Spin the character left/right."u8);

        Im.Item.SetNextWidthScaled(200);
        if (Im.Drag("##rotPitch"u8, ref _euler.X, FormatDegrees(_euler.X), float.MinValue, float.MaxValue, 1f))
            changed = true;
        Im.Line.Same();
        Im.Text("Pitch"u8);
        Im.Tooltip.OnHover("Tilt the character forward/backward."u8);

        Im.Item.SetNextWidthScaled(200);
        if (Im.Drag("##rotRoll"u8, ref _euler.Z, FormatDegrees(_euler.Z), float.MinValue, float.MaxValue, 1f))
            changed = true;
        Im.Line.Same();
        Im.Text("Roll"u8);
        Im.Tooltip.OnHover("Tilt the character sideways."u8);

        if (changed)
            rotationService.SetRotation(actor, _euler);

        if (Im.Button("Reset Rotation"u8))
        {
            rotationService.ClearRotation(actor);
            _euler = Vector3.Zero;
            _initialized = false;
        }
    }

    private static ReadOnlySpan<byte> FormatDegrees(float degrees)
    {
        var full      = (int)MathF.Truncate(degrees / 360f);
        var remainder = degrees - full * 360f;
        var text      = full == 0
            ? $"{remainder:F0}\u00b0"
            : $"{remainder:F0}\u00b0 {(full > 0 ? "+" : "")}{full}";
        return System.Text.Encoding.UTF8.GetBytes(text);
    }

    public void Reset()
    {
        if (_lastActor != nint.Zero)
            rotationService.ClearRotation((Actor)_lastActor);
        _lastActor = nint.Zero;
        _initialized = false;
    }
}
