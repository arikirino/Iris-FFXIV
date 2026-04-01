using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Plugin.Services;

namespace Iris.Services;

/// <summary>
/// Reads controller analog input and drives the free camera in GPose.
/// Phase 1 focus: smooth analog stick camera movement with dead zone,
/// sensitivity curve, precision / fast mode modifiers.
/// </summary>
public sealed class IrisControllerService : IDisposable
{
    private readonly ICondition _condition;
    private readonly IGamepadState _gamepad;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly Configuration _config;
    private readonly IrisCameraService _camera;

    // ── Speed modifiers ──────────────────────────────────────────
    private const float PrecisionMultiplier = 0.1f;   // L1 held
    private const float FastMultiplier      = 3.0f;   // R1 held

    public IrisControllerService(
        ICondition condition,
        IGamepadState gamepad,
        IFramework framework,
        IPluginLog log,
        Configuration config,
        IrisCameraService camera)
    {
        _condition = condition;
        _gamepad   = gamepad;
        _framework = framework;
        _log       = log;
        _config    = config;
        _camera    = camera;

        _framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        _framework.Update -= OnFrameworkUpdate;
    }

    // ── Framework tick ───────────────────────────────────────────

    private void OnFrameworkUpdate(IFramework framework)
    {
        // Only run controller camera in GPose
        if (!IsInGPose()) return;

        var dt = (float)framework.UpdateDelta.TotalSeconds;
        ProcessInput(dt);
    }

    private bool IsInGPose() =>
        _condition[ConditionFlag.WatchingCutscene];

    // ── Input processing ─────────────────────────────────────────

    private void ProcessInput(float dt)
    {
        // Read raw stick values (-1..+1)
        var leftRaw  = new Vector2(_gamepad.LeftStick.X,  _gamepad.LeftStick.Y);
        var rightRaw = new Vector2(_gamepad.RightStick.X, _gamepad.RightStick.Y);

        // Triggers: L2 = zoom out, R2 = zoom in
        float zoomAxis = _gamepad.Raw(GamepadButtons.R2) - _gamepad.Raw(GamepadButtons.L2);

        // Speed modifier buttons
        bool precision = _gamepad.Raw(GamepadButtons.L1) > 0.5f;
        bool fast      = _gamepad.Raw(GamepadButtons.R1) > 0.5f;
        float speedMod = precision ? PrecisionMultiplier : (fast ? FastMultiplier : 1.0f);

        // Apply dead zone and sensitivity curve
        var left  = ApplyCurve(ApplyDeadZone(leftRaw,  _config.DeadZone), _config.SensitivityCurve);
        var right = ApplyCurve(ApplyDeadZone(rightRaw, _config.DeadZone), _config.SensitivityCurve);
        float zoom = ApplyCurve(ApplyDeadZone(zoomAxis, _config.DeadZone), _config.SensitivityCurve);

        if (_config.InvertY) right.Y = -right.Y;

        // Scale by config speeds and dt
        var move   = new Vector3(left.X,  0f, -left.Y) * _config.MoveSpeed   * speedMod * dt;
        var rotate = new Vector2(right.X, right.Y)     * _config.RotateSpeed * speedMod * dt;
        float zoomDelta = zoom * _config.ZoomSpeed * speedMod * dt;

        _camera.ApplyControllerInput(move, rotate, zoomDelta);
    }

    // ── Dead zone ────────────────────────────────────────────────

    private static Vector2 ApplyDeadZone(Vector2 v, float deadZone)
    {
        float mag = v.Length();
        if (mag < deadZone) return Vector2.Zero;
        // Rescale so output starts at 0 just outside the dead zone
        return v / mag * ((mag - deadZone) / (1f - deadZone));
    }

    private static float ApplyDeadZone(float v, float deadZone)
    {
        float abs = MathF.Abs(v);
        if (abs < deadZone) return 0f;
        return MathF.Sign(v) * ((abs - deadZone) / (1f - deadZone));
    }

    // ── Sensitivity curve ────────────────────────────────────────

    private static Vector2 ApplyCurve(Vector2 v, SensitivityCurve curve) =>
        v.Length() < 0.001f ? Vector2.Zero :
        v / v.Length() * ApplyCurve(v.Length(), curve);

    private static float ApplyCurve(float t, SensitivityCurve curve) => curve switch
    {
        SensitivityCurve.Linear    => t,
        SensitivityCurve.Quadratic => t * t * MathF.Sign(t),
        SensitivityCurve.Cubic     => t * t * t,
        _                          => t,
    };
}
