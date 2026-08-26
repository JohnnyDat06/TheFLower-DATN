using UnityEngine;

/// <summary>
/// Runtime-only red landing marker for a storm rock. It uses a LineRenderer so
/// Phase 14 does not require a new material or scene asset.
/// </summary>
[DisallowMultipleComponent]
public sealed class SandstormRockTelegraph : MonoBehaviour
{
    private const int RingSegmentCount = 48;

    private LineRenderer _lineRenderer;
    private Mesh _fillMesh;
    private Material _lineMaterial;
    private Material _fillMaterial;

    /// <summary>Creates a red filled landing marker at the deterministic storm-rock landing point.</summary>
    public static SandstormRockTelegraph Create(Vector3 position, float radius, float lineWidth)
    {
        GameObject markerObject = new GameObject("StormRock_LandingWarning");
        markerObject.transform.position = position;
        SandstormRockTelegraph marker = markerObject.AddComponent<SandstormRockTelegraph>();
        marker.BuildRing(Mathf.Max(0.1f, radius), Mathf.Max(0.005f, lineWidth));
        return marker;
    }

    private void BuildRing(float radius, float lineWidth)
    {
        _lineRenderer = gameObject.AddComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.loop = true;
        _lineRenderer.positionCount = RingSegmentCount;
        _lineRenderer.startWidth = lineWidth;
        _lineRenderer.endWidth = lineWidth;
        _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lineRenderer.receiveShadows = false;

        Shader lineShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        if (lineShader != null)
        {
            _lineMaterial = new Material(lineShader)
            {
                color = new Color(1f, 0.02f, 0.02f, 0.95f)
            };
            _lineRenderer.sharedMaterial = _lineMaterial;
        }

        CreateFilledMarker(radius);
        for (int segmentIndex = 0; segmentIndex < RingSegmentCount; segmentIndex++)
        {
            float angle = segmentIndex / (float)RingSegmentCount * Mathf.PI * 2f;
            _lineRenderer.SetPosition(
                segmentIndex,
                transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
        }
    }

    private void CreateFilledMarker(float radius)
    {
        GameObject fillObject = new GameObject("StormRock_LandingWarning_Fill");
        fillObject.transform.SetParent(transform, false);
        fillObject.transform.localPosition = Vector3.up * 0.015f;

        MeshFilter meshFilter = fillObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = fillObject.AddComponent<MeshRenderer>();
        _fillMesh = new Mesh
        {
            name = "StormRockLandingWarningDisk"
        };

        Vector3[] vertices = new Vector3[RingSegmentCount + 1];
        vertices[0] = Vector3.zero;
        for (int segmentIndex = 0; segmentIndex < RingSegmentCount; segmentIndex++)
        {
            float angle = segmentIndex / (float)RingSegmentCount * Mathf.PI * 2f;
            vertices[segmentIndex + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
        }

        int[] triangles = new int[RingSegmentCount * 3];
        for (int segmentIndex = 0; segmentIndex < RingSegmentCount; segmentIndex++)
        {
            int nextVertex = (segmentIndex + 1) % RingSegmentCount;
            int triangleIndex = segmentIndex * 3;
            triangles[triangleIndex] = 0;
            triangles[triangleIndex + 1] = nextVertex + 1;
            triangles[triangleIndex + 2] = segmentIndex + 1;
        }

        _fillMesh.vertices = vertices;
        _fillMesh.triangles = triangles;
        _fillMesh.RecalculateNormals();
        meshFilter.sharedMesh = _fillMesh;

        Shader fillShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (fillShader != null)
        {
            _fillMaterial = new Material(fillShader)
            {
                color = new Color(1f, 0f, 0f, 0.45f)
            };
            meshRenderer.sharedMaterial = _fillMaterial;
        }
    }

    private void OnDestroy()
    {
        if (_lineMaterial != null)
        {
            Destroy(_lineMaterial);
        }

        if (_fillMaterial != null)
        {
            Destroy(_fillMaterial);
        }

        if (_fillMesh != null)
        {
            Destroy(_fillMesh);
        }
    }
}
