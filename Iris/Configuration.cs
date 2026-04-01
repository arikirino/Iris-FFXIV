using System;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace Iris;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    // ── Controller settings ─────────────────────────────────────
    public float DeadZone { get; set; } = 0.15f;
    public SensitivityCurve SensitivityCurve { get; set; } = SensitivityCurve.Quadratic;
    public bool InvertY { get; set; } = false;
    public float MoveSpeed { get; set; } = 1.0f;
    public float RotateSpeed { get; set; } = 1.0f;
    public float ZoomSpeed { get; set; } = 1.0f;

    // ── UI settings ─────────────────────────────────────────────
    public bool ShowIrisWindow { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}

public enum SensitivityCurve
{
    Linear,
    Quadratic,
    Cubic,
}
