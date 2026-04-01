using System;
using System.Collections.Generic;
using System.Linq;

namespace Iris.Models;

public class CameraPath
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "New Path";
    public bool Loop { get; set; } = false;
    public float GlobalSpeedMultiplier { get; set; } = 1.0f;  // 0.25x to 4.0x
    public List<CameraWaypoint> Waypoints { get; set; } = new();

    public float TotalDuration => Waypoints.Sum(w => w.Duration);

    /// <summary>Re-numbers all waypoint indices to match their list position.</summary>
    public void RenumberWaypoints()
    {
        for (var i = 0; i < Waypoints.Count; i++)
            Waypoints[i].Index = i;
    }

    public void AddWaypoint(CameraWaypoint wp)
    {
        wp.Index = Waypoints.Count;
        Waypoints.Add(wp);
    }

    public void DeleteWaypoint(int index)
    {
        if (index < 0 || index >= Waypoints.Count) return;
        Waypoints.RemoveAt(index);
        RenumberWaypoints();
    }

    /// <summary>Insert a waypoint AFTER the given index.</summary>
    public void InsertWaypointAfter(int afterIndex, CameraWaypoint wp)
    {
        var insertAt = Math.Clamp(afterIndex + 1, 0, Waypoints.Count);
        Waypoints.Insert(insertAt, wp);
        RenumberWaypoints();
    }

    public void MoveWaypoint(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        if (fromIndex < 0 || fromIndex >= Waypoints.Count) return;
        toIndex = Math.Clamp(toIndex, 0, Waypoints.Count - 1);

        var wp = Waypoints[fromIndex];
        Waypoints.RemoveAt(fromIndex);
        Waypoints.Insert(toIndex, wp);
        RenumberWaypoints();
    }
}
