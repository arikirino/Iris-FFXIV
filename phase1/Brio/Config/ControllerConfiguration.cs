using System.ComponentModel;

namespace Brio.Config;

/// <summary>
/// Configuration for controller (gamepad) analog input in the free camera.
/// Phase 1 of the Iris camera extension.
/// </summary>
public class ControllerConfiguration
{
    /// <summary>Master toggle — if false, all controller camera input is ignored.</summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    /// Stick axis values below this threshold are treated as zero.
    /// Eliminates drift from off-center resting position. Range: 0.0–0.5.
    /// </summary>
    public float DeadZone { get; set; } = 0.15f;

    /// <summary>Response curve applied after dead zone normalization.</summary>
    public SensitivityCurve SensitivityCurve { get; set; } = SensitivityCurve.Quadratic;

    /// <summary>Invert horizontal (pan) axis of the right stick.</summary>
    public bool InvertX { get; set; } = false;

    /// <summary>Invert vertical (tilt) axis of the right stick.</summary>
    public bool InvertY { get; set; } = false;

    /// <summary>Multiplier applied to left-stick translation speed. Scales with FreeCamValues.MovementSpeed.</summary>
    public float MoveSpeed { get; set; } = 1.0f;

    /// <summary>Multiplier applied to right-stick rotation speed.</summary>
    public float RotateSpeed { get; set; } = 1.0f;

    /// <summary>Multiplier applied to trigger zoom speed.</summary>
    public float ZoomSpeed { get; set; } = 1.0f;

    /// <summary>Speed multiplier applied when the precision modifier button (L1) is held.</summary>
    public float PrecisionModifier { get; set; } = 0.25f;
}

public enum SensitivityCurve
{
    [Description("Linear")]    Linear,
    [Description("Quadratic")] Quadratic,
    [Description("Cubic")]     Cubic
}
