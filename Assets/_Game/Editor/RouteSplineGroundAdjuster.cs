using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Splines;
using MCPForUnity.Editor;
using MCPForUnity.Editor.Constants;
using MCPForUnity.Editor.Services.Transport.Transports;

namespace Game.Editor
{
    [InitializeOnLoad]
    public static class RouteSplineGroundAdjuster
    {
        static RouteSplineGroundAdjuster()
        {
            // Auto start Unity MCP bridge for agent / client connection
            EditorApplication.delayCall += InitializeBridgeAndInspect;
        }

        private static void InitializeBridgeAndInspect()
        {
            try
            {
                McpCiBoot.StartStdioForCi();
                Debug.Log("[RouteSplineGroundAdjuster] Unity MCP Stdio bridge auto-started successfully.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RouteSplineGroundAdjuster] Failed to auto-start MCP bridge: {ex.Message}");
            }

            InspectRouteSpline();
        }

        [MenuItem("Tools/DATN/Inspect Route Spline Ground")]
        public static void InspectRouteSpline()
        {
            GameObject routeGo = GameObject.Find("Route_Spline");
            if (routeGo == null)
            {
                Debug.LogWarning("[RouteSplineGroundAdjuster] Could not find GameObject 'Route_Spline' in active scene.");
                return;
            }

            SplineContainer container = routeGo.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null)
            {
                Debug.LogWarning("[RouteSplineGroundAdjuster] SplineContainer or Spline is missing on Route_Spline.");
                return;
            }

            SandBoatRoute sandBoatRoute = routeGo.GetComponent<SandBoatRoute>();
            Transform endpointRef = null;
            if (sandBoatRoute != null)
            {
                SerializedObject soRoute = new SerializedObject(sandBoatRoute);
                SerializedProperty propRef = soRoute.FindProperty("_endpointReference");
                if (propRef != null && propRef.objectReferenceValue is Transform tr)
                {
                    endpointRef = tr;
                }
            }

            Spline spline = container.Spline;
            int totalKnots = spline.Count;
            Debug.Log($"[RouteSplineGroundAdjuster] Route_Spline found with {totalKnots} knots.");

            if (endpointRef != null)
            {
                Vector3 epWorld = endpointRef.position;
                float groundY = GetGroundHeight(epWorld, out string hitName);
                Debug.Log($"[RouteSplineGroundAdjuster] EndpointReference '{endpointRef.name}' worldPos: {epWorld}, groundY: {groundY} (hit: {hitName}), delta: {epWorld.y - groundY:F3}");
            }

            // Inspect knots along the entire route at intervals
            Debug.Log("--- ROUTE HEIGHT PROFILE SAMPLES ---");
            for (int i = 0; i < totalKnots; i += 20)
            {
                BezierKnot knot = spline[i];
                Vector3 worldPos = container.transform.TransformPoint((Vector3)knot.Position);
                float groundY = GetGroundHeight(worldPos, out string hitName);
                float diff = worldPos.y - groundY;
                Debug.Log($"[Profile] Knot[{i}/{totalKnots}] worldY: {worldPos.y:F3}, groundY: {groundY:F3} ({hitName}), diffY: {diff:F3}");
            }

            // Check boat collider/mesh hull offset from pivot
            GameObject boat = GameObject.Find("BoatController");
            if (boat != null)
            {
                Renderer[] rends = boat.GetComponentsInChildren<Renderer>();
                float lowestRendY = float.MaxValue;
                foreach (var r in rends)
                {
                    if (r.bounds.min.y < lowestRendY) lowestRendY = r.bounds.min.y;
                }
                Collider[] cols = boat.GetComponentsInChildren<Collider>();
                float lowestColY = float.MaxValue;
                foreach (var c in cols)
                {
                    if (c.bounds.min.y < lowestColY) lowestColY = c.bounds.min.y;
                }
                Debug.Log($"[BoatProfile] Boat pos: {boat.transform.position}, LowestRendererY: {lowestRendY:F3} (deltaFromPivot: {lowestRendY - boat.transform.position.y:F3}), LowestColliderY: {lowestColY:F3} (deltaFromPivot: {lowestColY - boat.transform.position.y:F3})");
            }

            // Inspect knots from 290 to 315 in detail
            for (int i = 290; i < totalKnots; i++)
            {
                BezierKnot knot = spline[i];
                Vector3 worldPos = container.transform.TransformPoint((Vector3)knot.Position);
                float groundY = GetGroundHeight(worldPos, out string hitName);
                float diff = worldPos.y - groundY;
                Debug.Log($"[EndKnot] Knot[{i}] worldPos: ({worldPos.x:F2}, {worldPos.y:F2}, {worldPos.z:F2}), groundY: {groundY:F3} ({hitName}), diffY: {diff:F3}");
            }
        }

        [MenuItem("Tools/DATN/Adjust Route Spline To Ground")]
        public static void AdjustRouteSplineToGround()
        {
            GameObject routeGo = GameObject.Find("Route_Spline");
            if (routeGo == null)
            {
                Debug.LogError("[RouteSplineGroundAdjuster] Could not find GameObject 'Route_Spline'.");
                return;
            }

            SplineContainer container = routeGo.GetComponent<SplineContainer>();
            if (container == null || container.Spline == null)
            {
                Debug.LogError("[RouteSplineGroundAdjuster] SplineContainer is missing on Route_Spline.");
                return;
            }

            SandBoatRoute sandBoatRoute = routeGo.GetComponent<SandBoatRoute>();
            Transform endpointRef = null;
            if (sandBoatRoute != null)
            {
                SerializedObject soRoute = new SerializedObject(sandBoatRoute);
                SerializedProperty propRef = soRoute.FindProperty("_endpointReference");
                if (propRef != null && propRef.objectReferenceValue is Transform tr)
                {
                    endpointRef = tr;
                }
            }

            Spline spline = container.Spline;
            int totalKnots = spline.Count;
            Undo.RecordObject(container, "Adjust Route Spline To Ground");

            const float DEFAULT_ROUTE_RIDE_HEIGHT = 0.233f;
            const float ENDPOINT_RIDE_HEIGHT = 0.280f;

            int checkRange = Mathf.Min(35, totalKnots);
            int startIdx = totalKnots - checkRange;
            int adjustedCount = 0;

            for (int i = startIdx; i < totalKnots - 1; i++)
            {
                BezierKnot knot = spline[i];
                Vector3 worldPos = container.transform.TransformPoint((Vector3)knot.Position);
                float groundY = GetGroundHeight(worldPos, out string hitName);

                float t = Mathf.InverseLerp(startIdx, totalKnots - 1, i);
                float targetRideHeight = Mathf.Lerp(DEFAULT_ROUTE_RIDE_HEIGHT, ENDPOINT_RIDE_HEIGHT, t);
                float targetWorldY = groundY + targetRideHeight;

                if (Mathf.Abs(worldPos.y - targetWorldY) > 0.005f)
                {
                    Vector3 adjustedWorldPos = new Vector3(worldPos.x, targetWorldY, worldPos.z);
                    Vector3 localAdjustedPos = container.transform.InverseTransformPoint(adjustedWorldPos);
                    knot.Position = localAdjustedPos;
                    spline[i] = knot;
                    adjustedCount++;
                    Debug.Log($"[RouteSplineGroundAdjuster] Adjusted Knot[{i}] world Y from {worldPos.y:F3} to target {targetWorldY:F3} (groundY {groundY:F3} + offset {targetRideHeight:F3}, hit: {hitName})");
                }
            }

            // Also adjust endpointReference if present
            if (endpointRef != null)
            {
                Undo.RecordObject(endpointRef, "Adjust Endpoint Reference");
                Vector3 epWorld = endpointRef.position;
                float epGroundY = GetGroundHeight(epWorld, out string epHitName);
                float epTargetY = epGroundY + ENDPOINT_RIDE_HEIGHT;
                if (Mathf.Abs(epWorld.y - epTargetY) > 0.005f)
                {
                    endpointRef.position = new Vector3(epWorld.x, epTargetY, epWorld.z);
                    EditorUtility.SetDirty(endpointRef);
                    Debug.Log($"[RouteSplineGroundAdjuster] Adjusted endpoint '{endpointRef.name}' from world Y {epWorld.y:F3} to target {epTargetY:F3} (groundY {epGroundY:F3} + offset {ENDPOINT_RIDE_HEIGHT:F3}, hit: {epHitName})");
                }

                // Sync the very last knot to endpoint
                BezierKnot lastKnot = spline[totalKnots - 1];
                lastKnot.Position = container.transform.InverseTransformPoint(endpointRef.position);
                spline[totalKnots - 1] = lastKnot;
            }

            EditorUtility.SetDirty(container);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(routeGo.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(routeGo.scene);

            Debug.Log($"[RouteSplineGroundAdjuster] Successfully adjusted {adjustedCount} knots with ride height offsets and saved scene {routeGo.scene.name}!");
        }

        public static float GetGroundHeight(Vector3 worldPos, out string hitName)
        {
            hitName = "None";
            float highestGroundY = float.MinValue;

            // 1. Raycast down from slightly above the current position to prevent hitting high overhead structures
            Ray ray = new Ray(new Vector3(worldPos.x, worldPos.y + 4f, worldPos.z), Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 20f, ~0, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                // Ignore boat colliders, player colliders, route colliders
                if (hit.collider.name.Contains("Boat") || hit.collider.name.Contains("Player") || hit.collider.name.Contains("Route"))
                    continue;

                if (hit.point.y > highestGroundY)
                {
                    highestGroundY = hit.point.y;
                    hitName = hit.collider.gameObject.name;
                }
            }

            // 2. Terrain check
            if (Terrain.activeTerrain != null)
            {
                float terrainY = Terrain.activeTerrain.SampleHeight(worldPos) + Terrain.activeTerrain.transform.position.y;
                if (terrainY > highestGroundY)
                {
                    highestGroundY = terrainY;
                    hitName = $"Terrain ({Terrain.activeTerrain.name})";
                }
            }

            if (highestGroundY > float.MinValue + 100f)
            {
                return highestGroundY;
            }

            // Fallback if no collider hit: return original Y
            hitName = "Fallback";
            return worldPos.y;
        }
    }
}
