using Brio.Config;
using Brio.Game.Camera;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Plugin.Services;
using System;
using System.Numerics;

namespace Brio.Input;

/// <summary>
/// Reads Dalamud's IGamepadState each frame and converts raw axis values into
/// camera-ready deltas (translation, rotation, zoom) with dead zone, sensitivity
/// curve, inversion, and speed-modifier support.
///
/// Intended integration point: VirtualCameraManager.Update() calls
/// AnalogInputProvider.Sample() and folds the result into its movement vectors.
/// </summary>
public class AnalogInputProvider
{
    private readonly IGamepadState _gamepadState;
    private readonly ConfigurationService _configurationService;

    private ControllerConfiguration Config => _configurationService.Configuration.Controller;

    public AnalogInputProvider(IGamepadState gamepadState, ConfigurationService configurationService)
    {
        _gamepadState = gamepadState;
        _configurationService = configurationService;
    }

    /// <summary>
    /// Sample the gamepad this frame and return processed camera input axes.
    /// Call once per Update() tick; values are not cached between frames.
    /// </summary>
    /// <param name="freeCamValues">
    /// FreeCamValues from the current virtual camera, used to scale translation
    /// by the camera's own MovementSpeed setting.
    /// </param>
    public AnalogCameraInput Sample(FreeCamValues freeCamValues)
    {
        if (!Config.Enable || !_gamepadState.GamepadId.HasValue)
            return AnalogCameraInput.Zero;

        // ── Raw axis reconstruction ──────────────────────────────────────────
        // IGamepadState exposes per-direction floats (0-1). Reconstruct signed
        // [-1, 1] axes.
        float leftX  =  _gamepadState.LeftStickRight - _gamepadState.LeftStickLeft;
        float leftY  =  _gamepadState.LeftStickUp    - _gamepadState.LeftStickDown;
        float rightX =  _gamepadState.RightStickRight - _gamepadState.RightStickLeft;
        float rightY =  _gamepadState.RightStickUp    - _gamepadState.RightStickDown;
        float triggerZoom = _gamepadState.RightTrigger - _gamepadState.LeftTrigger; // +1 = zoom out, -1 = zoom in

        // ── Dead zone ────────────────────────────────────────────────────────
        leftX  = ApplyDeadZone(leftX,  Config.DeadZone);
        leftY  = ApplyDeadZone(leftY,  Config.DeadZone);
        rightX = ApplyDeadZone(rightX, Config.DeadZone);
        rightY = ApplyDeadZone(rightY, Config.DeadZone);
        triggerZoom = ApplyDeadZone(triggerZoom, 0.05f); // triggers have a tighter dead zone

        // ── Sensitivity curve ────────────────────────────────────────────────
        leftX  = ApplyCurve(leftX,  Config.SensitivityCurve);
        leftY  = ApplyCurve(leftY,  Config.SensitivityCurve);
        rightX = ApplyCurve(rightX, Config.SensitivityCurve);
        rightY = ApplyCurve(rightY, Config.SensitivityCurve);

        // ── Inversion ────────────────────────────────────────────────────────
        if (Config.InvertX) rightX = -rightX;
        if (Config.InvertY) rightY = -rightY;

        // ── Precision (L1) modifier ──────────────────────────────────────────
        bool precisionMode = (_gamepadState.Raw() & GamepadButtons.L1) != 0;
        float speedMod = precisionMode ? Config.PrecisionModifier : 1.0f;

        // ── Speed scaling ────────────────────────────────────────────────────
        // Translation is further scaled by the camera's own MovementSpeed so
        // the controller feels consistent with whatever the user set for WASD.
        float camMoveSpeed = freeCamValues.MovementSpeed;

        float forwardBackward = -leftY * Config.MoveSpeed * speedMod * camMoveSpeed; // -Y = forward
        float leftRight       =  leftX * Config.MoveSpeed * speedMod * camMoveSpeed;
        float upDown          =  0f; // left stick is lateral; vertical is triggers in standard layout

        // Rotation: right stick → pan (X), tilt (Y)
        // Scale chosen so a full-deflection sweep feels like ~90° at default speed.
        const float rotationScale = 0.02f;
        float rotateX = rightX * Config.RotateSpeed * speedMod * rotationScale;
        float rotateY = rightY * Config.RotateSpeed * speedMod * rotationScale;

        // Zoom: analog triggers
        const float zoomScale = 0.05f;
        float zoomDelta = triggerZoom * Config.ZoomSpeed * speedMod * zoomScale;

        return new AnalogCameraInput(
            forwardBackward,
            leftRight,
            upDown,
            new Vector2(rotateX, rotateY),
            zoomDelta,
            precisionMode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Rescale a signed axis value so the dead zone region maps to 0 and the
    /// remaining range is linearly normalized back to [-1, 1].
    /// </summary>
    private static float ApplyDeadZone(float value, float deadZone)
    {
        float abs = MathF.Abs(value);
        if (abs < deadZone) return 0f;
        float normalized = (abs - deadZone) / (1f - deadZone);
        return MathF.Sign(value) * Math.Clamp(normalized, 0f, 1f);
    }

    /// <summary>Apply a power curve to a normalized [-1, 1] value.</summary>
    private static float ApplyCurve(float value, SensitivityCurve curve)
    {
        if (value == 0f) return 0f;
        float abs = MathF.Abs(value);
        float curved = curve switch
        {
            SensitivityCurve.Linear    => abs,
            SensitivityCurve.Quadratic => abs * abs,
            SensitivityCurve.Cubic     => abs * abs * abs,
            _                          => abs
        };
        return MathF.Sign(value) * curved;
    }
}

/// <summary>Processed analog camera input for one frame.</summary>
public readonly struct AnalogCameraInput
{
    /// <summary>Forward (-) / backward (+) translation delta. Already speed-scaled.</summary>
    public readonly float ForwardBackward;
    /// <summary>Left (-) / right (+) translation delta. Already speed-scaled.</summary>
    public readonly float LeftRight;
    /// <summary>Down (-) / up (+) translation delta. Always 0 in this layout — reserved.</summary>
    public readonly float UpDown;
    /// <summary>
    /// Rotation delta to add to <c>_lastMousePosition</c> in VirtualCameraManager.
    /// X = yaw (pan), Y = pitch (tilt). Already sensitivity-scaled.
    /// </summary>
    public readonly Vector2 Rotation;
    /// <summary>Zoom distance change. Positive = zoom out, negative = zoom in.</summary>
    public readonly float ZoomDelta;
    /// <summary>True if the precision modifier (L1) is held this frame.</summary>
    public readonly bool PrecisionMode;

    public AnalogCameraInput(float forwardBackward, float leftRight, float upDown,
        Vector2 rotation, float zoomDelta, bool precisionMode)
    {
        ForwardBackward = forwardBackward;
        LeftRight       = leftRight;
        UpDown          = upDown;
        Rotation        = rotation;
        ZoomDelta       = zoomDelta;
        PrecisionMode   = precisionMode;
    }

    public static readonly AnalogCameraInput Zero = new(0, 0, 0, Vector2.Zero, 0, false);
}
