using UnityEngine;

public class Level04FlightPath : MonoBehaviour
{
    [SerializeField] private Transform[] _waypoints;

    public int WaypointCount => _waypoints?.Length ?? 0;

    public int FindClosestWaypointIndex(Vector3 position)
    {
        if (WaypointCount == 0) return -1;

        int closestIndex = 0;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < _waypoints.Length; i++)
        {
            if (_waypoints[i] == null) continue;
            float distance = (position - _waypoints[i].position).sqrMagnitude;
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    public Vector3 GetGuidanceDirection(
        Vector3 position,
        ref int waypointIndex,
        float reachDistance)
    {
        if (WaypointCount == 0) return Vector3.zero;
        waypointIndex = Mathf.Clamp(waypointIndex, 0, _waypoints.Length - 1);

        while (waypointIndex < _waypoints.Length - 1)
        {
            Transform waypoint = _waypoints[waypointIndex];
            Transform next = _waypoints[waypointIndex + 1];
            if (waypoint == null || next == null)
            {
                waypointIndex++;
                continue;
            }

            Vector3 segment = next.position - waypoint.position;
            bool reached = Vector3.Distance(position, waypoint.position) <= reachDistance;
            bool passed = segment.sqrMagnitude > 0.01f
                && Vector3.Dot(position - waypoint.position, segment.normalized) > 0f;
            if (!reached && !passed) break;
            waypointIndex++;
        }

        Transform target = _waypoints[waypointIndex];
        if (target == null) return Vector3.zero;
        return (target.position - position).normalized;
    }

    public Vector3 GetWaypointPosition(int index)
    {
        if (WaypointCount == 0) return transform.position;
        index = Mathf.Clamp(index, 0, _waypoints.Length - 1);
        return _waypoints[index] != null ? _waypoints[index].position : transform.position;
    }

    public float GetDistanceToPath(Vector3 position, int waypointIndex)
    {
        if (WaypointCount == 0) return 0f;
        waypointIndex = Mathf.Clamp(waypointIndex, 0, _waypoints.Length - 1);

        float distance = Vector3.Distance(position, GetWaypointPosition(waypointIndex));
        if (waypointIndex > 0)
        {
            distance = Mathf.Min(
                distance,
                DistanceToSegment(
                    position,
                    GetWaypointPosition(waypointIndex - 1),
                    GetWaypointPosition(waypointIndex)));
        }
        if (waypointIndex < _waypoints.Length - 1)
        {
            distance = Mathf.Min(
                distance,
                DistanceToSegment(
                    position,
                    GetWaypointPosition(waypointIndex),
                    GetWaypointPosition(waypointIndex + 1)));
        }
        return distance;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        if (segment.sqrMagnitude < 0.001f) return Vector3.Distance(point, start);
        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude);
        return Vector3.Distance(point, start + segment * t);
    }

    private void OnDrawGizmosSelected()
    {
        if (_waypoints == null || _waypoints.Length < 2) return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < _waypoints.Length - 1; i++)
        {
            if (_waypoints[i] == null || _waypoints[i + 1] == null) continue;
            Gizmos.DrawLine(_waypoints[i].position, _waypoints[i + 1].position);
            Gizmos.DrawWireSphere(_waypoints[i].position, 4f);
        }
        if (_waypoints[^1] != null)
        {
            Gizmos.DrawWireSphere(_waypoints[^1].position, 4f);
        }
    }
}
