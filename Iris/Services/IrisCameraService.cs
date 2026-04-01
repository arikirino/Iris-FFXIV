using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Iris.Models;

namespace Iris.Services;

/// <summary>
/// Core service: manages the active CameraPath, records waypoints from the
/// live camera, runs the per-frame interpolation playback engine, and handles
/// import/export of .json (and later .xcp) files.
/// </summary>
public sealed unsafe class IrisCameraService : IDisposable
{
    private readonly IPluginLog _log;

    // ── Active path ──────────────────────────────────────────────
    public CameraPath Path { get; private set; } = new();

    // ── Playback state ───────────────────────────────────────────
    public PlaybackState State { get; private set; } = PlaybackState.Stopped;
    public float PlayheadTime { get; private set; }   // seconds into the path
    public float SpeedMultiplier { get; private set; } = 1.0f;

    // ── Selected waypoint (UI) ───────────────────────────────────
    public int SelectedIndex { get; set; } = -1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented               = true,
        PropertyNameCaseInsensitive = true,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    public IrisCameraService(IPluginLog log)
    {
        _log = log;
    }

    public void Dispose() { }

    // ── Waypoint recording ───────────────────────────────────────

    /// <summary>Capture current camera state and append as a new waypoint.</summary>
    public void PlantWaypoint()
    {
        if (!TryGetCameraState(out var pos, out var rot, out var fov, out var zoom))
        {
            _log.Warning("[Iris] PlantWaypoint: could not read camera state.");
            return;
        }
        var wp = new CameraWaypoint(Path.Waypoints.Count, pos, rot, fov, zoom);
        Path.AddWaypoint(wp);
        SelectedIndex = wp.Index;
        _log.Debug($"[Iris] Planted waypoint #{wp.Index} at {pos}");
    }

    /// <summary>Overwrite the selected waypoint's camera data with the current camera state.</summary>
    public void UpdateSelectedToCurrentCamera()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Path.Waypoints.Count) return;
        if (!TryGetCameraState(out var pos, out var rot, out var fov, out var zoom)) return;

        var wp      = Path.Waypoints[SelectedIndex];
        wp.Position = pos;
        wp.Rotation = rot;
        wp.FoV      = fov;
        wp.Zoom     = zoom;
    }

    public void DeleteWaypoint(int index)
    {
        Path.DeleteWaypoint(index);
        SelectedIndex = Math.Clamp(SelectedIndex, -1, Path.Waypoints.Count - 1);
    }

    public void InsertWaypointAfter(int afterIndex)
    {
        if (!TryGetCameraState(out var pos, out var rot, out var fov, out var zoom)) return;
        var wp = new CameraWaypoint(0, pos, rot, fov, zoom);
        Path.InsertWaypointAfter(afterIndex, wp);
        SelectedIndex = wp.Index;
    }

    public void MoveWaypoint(int fromIndex, int toIndex)
    {
        Path.MoveWaypoint(fromIndex, toIndex);
        SelectedIndex = toIndex < fromIndex ? toIndex : Math.Max(0, toIndex);
    }

    // ── Playback control ─────────────────────────────────────────

    public void Play()
    {
        if (Path.Waypoints.Count < 2)
        {
            _log.Warning("[Iris] Need at least 2 waypoints to play.");
            return;
        }
        if (State == PlaybackState.Stopped) PlayheadTime = 0f;
        State = PlaybackState.Playing;
    }

    public void Pause()
    {
        if (State == PlaybackState.Playing) State = PlaybackState.Paused;
    }

    public void Stop()
    {
        State        = PlaybackState.Stopped;
        PlayheadTime = 0f;
    }

    public void SetSpeed(float multiplier)
    {
        SpeedMultiplier = Math.Clamp(multiplier, 0.25f, 4.0f);
    }

    // ── Per-frame tick (called by Plugin.cs via IFramework.Update) ──

    public void Tick(float deltaTime)
    {
        if (State != PlaybackState.Playing) return;
        if (Path.Waypoints.Count < 2) { Stop(); return; }

        PlayheadTime += deltaTime * SpeedMultiplier;

        float total = Path.TotalDuration;
        if (PlayheadTime >= total)
        {
            if (Path.Loop)
                PlayheadTime %= total;
            else
            {
                PlayheadTime = total;
                ApplyWaypointDirect(Path.Waypoints[^1]);
                Stop();
                return;
            }
        }

        ApplyInterpolated(PlayheadTime);
    }

    // ── Controller camera input (called by IrisControllerService) ──

    /// <summary>
    /// Applies analog stick deltas to the live game camera.
    /// move.X = strafe, move.Z = forward/back, move.Y = up/down.
    /// rotate.X = pan (yaw), rotate.Y = tilt (pitch).
    /// </summary>
    public void ApplyControllerInput(Vector3 moveDelta, Vector2 rotateDelta, float zoomDelta)
    {
        var cam = GetGameCamera();
        if (cam == null) return;

        // Build forward/right vectors from current yaw (H rotation) only —
        // vertical component is excluded so strafing/walking stays on the XZ plane.
        var forward = new Vector3(-MathF.Sin(cam->HRotation), 0f, -MathF.Cos(cam->HRotation));
        var right   = new Vector3( MathF.Cos(cam->HRotation), 0f, -MathF.Sin(cam->HRotation));

        var translation = right          * moveDelta.X
                        + forward        * moveDelta.Z
                        + Vector3.UnitY  * moveDelta.Y;

        // Move the look-at point; the game recomputes eye position from LookAt + Zoom + angles.
        // Writing to Position directly is ignored on the next game frame.
        cam->LookAt += translation;

        // Yaw and pitch — pitch is clamped to avoid gimbal lock / camera flip.
        cam->HRotation += rotateDelta.X;
        cam->VRotation  = Math.Clamp(
            cam->VRotation - rotateDelta.Y,
            -1.483f,   // minVRotation default
             0.785f);  // maxVRotation default (π/4)

        // Zoom — clamped to the game's own min/max.
        cam->Zoom = Math.Clamp(cam->Zoom + zoomDelta, cam->MinZoom, cam->MaxZoom);
    }

    // ── Save / Load ──────────────────────────────────────────────

    public void SaveJson(string filePath)
    {
        try
        {
            var json = JsonSerializer.Serialize(Path, JsonOptions);
            File.WriteAllText(filePath, json);
            _log.Information($"[Iris] Saved path to {filePath}");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[Iris] SaveJson failed");
        }
    }

    public void LoadJson(string filePath)
    {
        try
        {
            var json   = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<CameraPath>(json, JsonOptions);
            if (loaded == null) throw new InvalidDataException("Deserialized path was null.");
            Path          = loaded;
            SelectedIndex = Path.Waypoints.Count > 0 ? 0 : -1;
            Stop();
            _log.Information($"[Iris] Loaded path '{Path.Name}' ({Path.Waypoints.Count} waypoints)");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "[Iris] LoadJson failed");
        }
    }

    // ── Phase 5 stubs (XCP) ──────────────────────────────────────

    public void ImportXcp(string filePath)
    {
        // TODO Phase 5: parse XAT .xcp format into CameraPath
        _log.Warning("[Iris] .xcp import not yet implemented (Phase 5).");
    }

    public void ExportXcp(string filePath)
    {
        // TODO Phase 5: serialize CameraPath to XAT .xcp format
        _log.Warning("[Iris] .xcp export not yet implemented (Phase 5).");
    }

    // ── Interpolation engine ─────────────────────────────────────

    private void ApplyInterpolated(float playhead)
    {
        float accumulated = 0f;
        for (int i = 1; i < Path.Waypoints.Count; i++)
        {
            var   wpA     = Path.Waypoints[i - 1];
            var   wpB     = Path.Waypoints[i];
            float segEnd  = accumulated + wpB.Duration;

            if (playhead <= segEnd || i == Path.Waypoints.Count - 1)
            {
                float t = wpB.Duration > 0f
                    ? Math.Clamp((playhead - accumulated) / wpB.Duration, 0f, 1f)
                    : 1f;

                t = ApplyEasing(t, wpB.Easing);

                var prev = i > 1                         ? Path.Waypoints[i - 2] : null;
                var next = i < Path.Waypoints.Count - 1  ? Path.Waypoints[i + 1] : null;

                var pos  = InterpolatePosition(prev, wpA, wpB, next, t);
                var rot  = Quaternion.Slerp(wpA.Rotation, wpB.Rotation, t);
                var fov  = Lerp(wpA.FoV,  wpB.FoV,  t);
                var zoom = Lerp(wpA.Zoom, wpB.Zoom, t);

                ApplyCameraState(pos, rot, fov, zoom);
                return;
            }

            accumulated = segEnd;
        }
    }

    private static Vector3 InterpolatePosition(
        CameraWaypoint? prev, CameraWaypoint wpA, CameraWaypoint wpB, CameraWaypoint? next, float t)
    {
        if (prev != null && next != null &&
            (wpB.Easing == EasingType.Smooth || wpA.Easing == EasingType.Smooth))
        {
            return CatmullRom(prev.Position, wpA.Position, wpB.Position, next.Position, t);
        }
        return Vector3.Lerp(wpA.Position, wpB.Position, t);
    }

    private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * (
              (2f * p1)
            + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3)      * t3
        );
    }

    private static float ApplyEasing(float t, EasingType easing) => easing switch
    {
        EasingType.Linear    => t,
        EasingType.EaseIn    => t * t,
        EasingType.EaseOut   => t * (2f - t),
        EasingType.EaseInOut => t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t,
        EasingType.Smooth    => t * t * (3f - 2f * t),   // smoothstep; position uses CatmullRom on top
        _                    => t,
    };

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // ── Camera struct access (FFXIVClientStructs) ────────────────

    private static IrisGameCamera* GetGameCamera()
    {
        var mgr = CameraManager.Instance();
        if (mgr == null) return null;
        return (IrisGameCamera*)mgr->GetActiveCamera();
    }

    /// <summary>
    /// Read camera state for waypoint recording.
    /// Returns LookAt (not eye Position) as pos — this is stable across zoom changes
    /// and is the correct anchor point to record and replay.
    /// Rotation is reconstructed as a Quaternion from the two angle floats for use
    /// in the interpolation engine; it is converted back to angles on write.
    /// </summary>
    private bool TryGetCameraState(out Vector3 pos, out Quaternion rot, out float fov, out float zoom)
    {
        pos  = default;
        rot  = Quaternion.Identity;
        fov  = 0f;
        zoom = 0f;

        var cam = GetGameCamera();
        if (cam == null) return false;

        pos  = cam->LookAt;
        rot  = QuaternionFromAngles(cam->HRotation, cam->VRotation);
        fov  = cam->FoV;
        zoom = cam->Zoom;
        return true;
    }

    private static void ApplyCameraState(Vector3 lookAt, Quaternion rot, float fov, float zoom)
    {
        var cam = GetGameCamera();
        if (cam == null) return;

        cam->LookAt = lookAt;
        AnglesFromQuaternion(rot, out cam->HRotation, out cam->VRotation);
        cam->FoV  = Math.Clamp(fov,  cam->MinFoV,  cam->MaxFoV);
        cam->Zoom = Math.Clamp(zoom, cam->MinZoom,  cam->MaxZoom);
    }

    private static void ApplyWaypointDirect(CameraWaypoint wp) =>
        ApplyCameraState(wp.Position, wp.Rotation, wp.FoV, wp.Zoom);

    // ── Angle ↔ Quaternion helpers ───────────────────────────────

    /// <summary>Reconstruct a Quaternion from the camera's yaw (H) and pitch (V) floats.</summary>
    private static Quaternion QuaternionFromAngles(float h, float v)
    {
        var yaw   = Quaternion.CreateFromAxisAngle(Vector3.UnitY,  h);
        var pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -v);
        return Quaternion.Normalize(yaw * pitch);
    }

    /// <summary>
    /// Decompose a Quaternion back to yaw (H) and pitch (V) to write into the game struct.
    /// The game clamps V itself, but we stay within safe range to avoid surprises.
    /// </summary>
    private static void AnglesFromQuaternion(Quaternion q, out float h, out float v)
    {
        var forward = Vector3.Transform(-Vector3.UnitZ, q);
        h = MathF.Atan2(forward.X, forward.Z);
        v = -MathF.Asin(Math.Clamp(forward.Y, -1f, 1f));
    }

    // ── IrisGameCamera overlay ───────────────────────────────────
    //
    // Thin unsafe struct overlaying the game's GameCamera in memory.
    // Offsets verified against UnknownX7/Hypostasis GameCamera.cs:
    //   https://github.com/UnknownX7/Hypostasis/blob/master/Game/Structures/GameCamera.cs
    // Update here if a game patch shifts offsets (check Hypostasis or FFXIVClientStructs).
    //
    // NOTE: There is NO Quaternion field in the game struct. Rotation is stored as two
    // floats: HRotation (yaw) and VRotation (pitch). Iris converts between them and
    // Quaternion internally for interpolation purposes only.

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Explicit)]
    private struct IrisGameCamera
    {
        // Eye position — computed by the game each frame from LookAt + Zoom + angles.
        // Read-only useful for debug; prefer writing to LookAt for movement.
        [System.Runtime.InteropServices.FieldOffset(0x60)] public Vector3 Position;   // x/y/z

        // Focal point — the anchor the camera orbits. Write here to move the camera.
        [System.Runtime.InteropServices.FieldOffset(0x90)] public Vector3 LookAt;     // lookAtX/Y/Z

        // Rotation stored as two floats, NOT a matrix or quaternion.
        [System.Runtime.InteropServices.FieldOffset(0x140)] public float HRotation;  // yaw,   -π..π, default π
        [System.Runtime.InteropServices.FieldOffset(0x144)] public float VRotation;  // pitch, default -0.349066

        // Zoom / distance
        [System.Runtime.InteropServices.FieldOffset(0x124)] public float Zoom;       // currentZoom, default 6
        [System.Runtime.InteropServices.FieldOffset(0x128)] public float MinZoom;    // default 1.5
        [System.Runtime.InteropServices.FieldOffset(0x12C)] public float MaxZoom;    // default 20

        // Field of view (radians)
        [System.Runtime.InteropServices.FieldOffset(0x130)] public float FoV;        // currentFoV, default 0.78
        [System.Runtime.InteropServices.FieldOffset(0x134)] public float MinFoV;     // default 0.69
        [System.Runtime.InteropServices.FieldOffset(0x138)] public float MaxFoV;     // default 0.78

        // Camera mode — useful for GPose detection / guard checks
        [System.Runtime.InteropServices.FieldOffset(0x180)] public int Mode;         // 0 = 1st person, 1 = 3rd
    }
}

public enum PlaybackState { Stopped, Playing, Paused }
