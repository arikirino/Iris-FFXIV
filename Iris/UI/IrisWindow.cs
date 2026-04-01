using System;
using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Iris.Models;
using Iris.Services;

namespace Iris.UI;

/// <summary>
/// Main Iris window — modelled after Captura's Advanced Camera Controls panel.
/// Layout (top to bottom):
///   1. Quick actions  – Plant Waypoint / Cinematic Mode
///   2. Waypoint list  – scrollable, reorderable, per-item Edit inline
///   3. Selected waypoint editor  – duration, easing, label, Update button
///   4. Playback transport        – play/pause/stop, speed, loop, total time
///   5. File I/O                  – Load/Save JSON, Import/Export XCP
///   6. Controller settings       – dead zone, sensitivity, invert-Y, speeds
/// </summary>
public class IrisWindow : Window, IDisposable
{
    private readonly Plugin _plugin;
    private readonly IrisCameraService _camera;
    private readonly Configuration _config;

    // ── Editor state ─────────────────────────────────────────────
    private int _editingIndex = -1;    // which waypoint row is expanded

    // Speed dropdown options
    private static readonly float[] SpeedOptions = { 0.25f, 0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 2.0f, 3.0f, 4.0f };
    private static readonly string[] SpeedLabels = { "0.25x", "0.5x", "0.75x", "1x", "1.25x", "1.5x", "2x", "3x", "4x" };

    private static readonly string[] EasingLabels =
        Enum.GetNames<EasingType>();

    public IrisWindow(Plugin plugin, IrisCameraService camera, Configuration config)
        : base("Iris##IrisMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        _plugin = plugin;
        _camera = camera;
        _config = config;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 480),
            MaximumSize = new Vector2(700, float.MaxValue),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawQuickActions();
        ImGui.Separator();
        DrawWaypointList();
        ImGui.Separator();
        DrawSelectedEditor();
        ImGui.Separator();
        DrawPlaybackControls();
        ImGui.Separator();
        DrawFileIO();
        ImGui.Separator();
        DrawControllerSettings();
    }

    // ── 1. Quick Actions ─────────────────────────────────────────

    private void DrawQuickActions()
    {
        if (ImGui.Button("Plant Waypoint"))
            _camera.PlantWaypoint();

        ImGui.SameLine();

        if (ImGui.Button("Cinematic Mode"))
        {
            // TODO Phase 2: hide all Dalamud UI and start playback
            _camera.Play();
        }
    }

    // ── 2. Waypoint List ─────────────────────────────────────────

    private void DrawWaypointList()
    {
        ImGui.Text("Waypoints");
        var waypoints = _camera.Path.Waypoints;

        using var child = ImRaii.Child("WaypointList", new Vector2(0, 180), true);
        if (!child.Success) return;

        for (int i = 0; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            bool isSelected = _camera.SelectedIndex == i;

            // Highlight selected row
            if (isSelected)
                ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.3f, 0.5f, 0.8f, 0.4f));

            bool clicked = ImGui.Selectable(
                $"##wp{i}",
                isSelected,
                ImGuiSelectableFlags.None,
                new Vector2(0, 0));

            if (isSelected)
                ImGui.PopStyleColor();

            if (clicked)
            {
                _camera.SelectedIndex = i;
                _editingIndex = -1;
            }

            ImGui.SameLine();
            string label = string.IsNullOrWhiteSpace(wp.Label) ? $"Waypoint {i + 1}" : wp.Label;
            ImGui.Text($"#{i + 1}  {label,-24}  → {wp.Duration:F1}s");

            ImGui.SameLine();
            if (ImGui.SmallButton($"Edit##e{i}"))
            {
                _camera.SelectedIndex = i;
                _editingIndex = _editingIndex == i ? -1 : i;
            }

            ImGui.SameLine();
            if (ImGui.SmallButton($"X##d{i}"))
            {
                _camera.DeleteWaypoint(i);
                if (_editingIndex >= i) _editingIndex = -1;
                break;  // list mutated — restart loop next frame
            }

            // Inline expanded editor
            if (_editingIndex == i)
                DrawInlineWaypointEditor(wp);
        }
    }

    private void DrawInlineWaypointEditor(CameraWaypoint wp)
    {
        ImGui.Indent(16f);

        float dur = wp.Duration;
        if (ImGui.SliderFloat($"Duration##d{wp.Index}", ref dur, 0f, 30f, "%.1f s"))
            wp.Duration = dur;

        int easingIdx = (int)wp.Easing;
        if (ImGui.Combo($"Easing##e{wp.Index}", ref easingIdx, EasingLabels, EasingLabels.Length))
            wp.Easing = (EasingType)easingIdx;

        string lbl = wp.Label ?? string.Empty;
        if (ImGui.InputText($"Label##l{wp.Index}", ref lbl, 64))
            wp.Label = lbl;

        if (ImGui.Button($"Update to Current Camera##u{wp.Index}"))
            _camera.UpdateSelectedToCurrentCamera();

        ImGui.Unindent(16f);
    }

    // Insert / Move buttons below the list

    private void DrawWaypointActions()
    {
        int sel = _camera.SelectedIndex;
        int count = _camera.Path.Waypoints.Count;

        if (ImGui.Button("+ Insert Here") && sel >= 0)
            _camera.InsertWaypointAfter(sel);

        ImGui.SameLine();

        using (ImRaii.Disabled(sel <= 0))
        {
            if (ImGui.Button("↑ Move Up") && sel > 0)
                _camera.MoveWaypoint(sel, sel - 1);
        }

        ImGui.SameLine();

        using (ImRaii.Disabled(sel < 0 || sel >= count - 1))
        {
            if (ImGui.Button("↓ Move Down") && sel >= 0 && sel < count - 1)
                _camera.MoveWaypoint(sel, sel + 1);
        }
    }

    // ── 3. Selected Waypoint Summary ─────────────────────────────

    private void DrawSelectedEditor()
    {
        int sel = _camera.SelectedIndex;
        var waypoints = _camera.Path.Waypoints;

        if (sel < 0 || sel >= waypoints.Count)
        {
            ImGui.TextDisabled("No waypoint selected.");
            DrawWaypointActions();
            return;
        }

        var wp = waypoints[sel];
        string wpName = string.IsNullOrWhiteSpace(wp.Label)
            ? $"Waypoint {sel + 1}"
            : $"#{sel + 1} — {wp.Label}";

        ImGui.Text($"Selected: {wpName}");

        float dur = wp.Duration;
        ImGui.SetNextItemWidth(120);
        if (ImGui.SliderFloat("Duration##sel", ref dur, 0f, 30f, "%.1f s"))
            wp.Duration = dur;

        ImGui.SameLine();

        int easingIdx = (int)wp.Easing;
        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo("Easing##sel", ref easingIdx, EasingLabels, EasingLabels.Length))
            wp.Easing = (EasingType)easingIdx;

        string lbl = wp.Label ?? string.Empty;
        ImGui.SetNextItemWidth(240);
        if (ImGui.InputText("Label##sel", ref lbl, 64))
            wp.Label = lbl;

        if (ImGui.Button("Update to Current Camera"))
            _camera.UpdateSelectedToCurrentCamera();

        DrawWaypointActions();
    }

    // ── 4. Playback Controls ─────────────────────────────────────

    private void DrawPlaybackControls()
    {
        ImGui.Text("Playback");

        var state = _camera.State;

        if (ImGui.Button("|<"))  // Go to start
        {
            _camera.Stop();
        }

        ImGui.SameLine();

        bool isPlaying = state == PlaybackState.Playing;
        if (ImGui.Button(isPlaying ? "||" : "> Play"))
        {
            if (isPlaying) _camera.Pause();
            else           _camera.Play();
        }

        ImGui.SameLine();

        if (ImGui.Button("[]"))
            _camera.Stop();

        ImGui.SameLine();

        // Speed dropdown
        float currentSpeed = _camera.SpeedMultiplier;
        int speedIdx = Array.FindIndex(SpeedOptions, s => MathF.Abs(s - currentSpeed) < 0.01f);
        if (speedIdx < 0) speedIdx = 3;  // default to 1x

        ImGui.SetNextItemWidth(80);
        if (ImGui.Combo("Speed##playback", ref speedIdx, SpeedLabels, SpeedLabels.Length))
            _camera.SetSpeed(SpeedOptions[speedIdx]);

        // Loop toggle + total duration
        bool loop = _camera.Path.Loop;
        if (ImGui.Checkbox("Loop", ref loop))
            _camera.Path.Loop = loop;

        ImGui.SameLine();
        ImGui.TextDisabled($"Total: {_camera.Path.TotalDuration:F1}s");
    }

    // ── 5. File I/O ──────────────────────────────────────────────

    private void DrawFileIO()
    {
        // Phase 4 will replace these with a proper file picker.
        // For now, hard-code to the plugin's config directory.

        string dir = Plugin.PluginInterface.GetPluginConfigDirectory();

        if (ImGui.Button("Load .json"))
        {
            var path = System.IO.Path.Combine(dir, $"{_camera.Path.Name}.json");
            _camera.LoadJson(path);
        }

        ImGui.SameLine();

        if (ImGui.Button("Save .json"))
        {
            var path = System.IO.Path.Combine(dir, $"{_camera.Path.Name}.json");
            _camera.SaveJson(path);
        }

        ImGui.SameLine();

        if (ImGui.Button("Import .xcp"))
        {
            var path = System.IO.Path.Combine(dir, $"{_camera.Path.Name}.xcp");
            _camera.ImportXcp(path);
        }

        ImGui.SameLine();

        if (ImGui.Button("Export .xcp"))
        {
            var path = System.IO.Path.Combine(dir, $"{_camera.Path.Name}.xcp");
            _camera.ExportXcp(path);
        }
    }

    // ── 6. Controller Settings ───────────────────────────────────

    private void DrawControllerSettings()
    {
        if (!ImGui.CollapsingHeader("Controller Settings")) return;

        float dz = _config.DeadZone;
        if (ImGui.SliderFloat("Dead Zone", ref dz, 0f, 0.5f, "%.2f"))
        {
            _config.DeadZone = dz;
            _config.Save();
        }

        int curveIdx = (int)_config.SensitivityCurve;
        string[] curveLabels = Enum.GetNames<SensitivityCurve>();
        if (ImGui.Combo("Sensitivity Curve", ref curveIdx, curveLabels, curveLabels.Length))
        {
            _config.SensitivityCurve = (SensitivityCurve)curveIdx;
            _config.Save();
        }

        bool invertY = _config.InvertY;
        if (ImGui.Checkbox("Invert Y", ref invertY))
        {
            _config.InvertY = invertY;
            _config.Save();
        }

        float ms = _config.MoveSpeed;
        if (ImGui.SliderFloat("Move Speed", ref ms, 0.1f, 5f, "%.2f"))
        {
            _config.MoveSpeed = ms;
            _config.Save();
        }

        float rs = _config.RotateSpeed;
        if (ImGui.SliderFloat("Rotate Speed", ref rs, 0.1f, 5f, "%.2f"))
        {
            _config.RotateSpeed = rs;
            _config.Save();
        }

        float zs = _config.ZoomSpeed;
        if (ImGui.SliderFloat("Zoom Speed", ref zs, 0.1f, 5f, "%.2f"))
        {
            _config.ZoomSpeed = zs;
            _config.Save();
        }
    }
}
