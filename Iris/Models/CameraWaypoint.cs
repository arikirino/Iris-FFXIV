using System.Numerics;

namespace Iris.Models;

public enum EasingType
{
    Linear,
    EaseIn,
    EaseOut,
    EaseInOut,  // Default — mimics Captura's feel
    Smooth,     // CatmullRom spline through surrounding waypoints
}

public class CameraWaypoint
{
    public int Index { get; set; }
    public Vector3 Position { get; set; }
    public Quaternion Rotation { get; set; }
    public float FoV { get; set; }       // Field of view in radians
    public float Zoom { get; set; }      // Zoom distance
    public float Duration { get; set; }  // Seconds to travel TO this waypoint FROM the previous
    public EasingType Easing { get; set; } = EasingType.EaseInOut;
    public string? Label { get; set; }

    public CameraWaypoint() { }

    public CameraWaypoint(int index, Vector3 position, Quaternion rotation, float fov, float zoom)
    {
        Index    = index;
        Position = position;
        Rotation = rotation;
        FoV      = fov;
        Zoom     = zoom;
        Duration = 2.0f;
        Easing   = EasingType.EaseInOut;
    }

    public CameraWaypoint Clone() => new()
    {
        Index    = Index,
        Position = Position,
        Rotation = Rotation,
        FoV      = FoV,
        Zoom     = Zoom,
        Duration = Duration,
        Easing   = Easing,
        Label    = Label,
    };
}
