using UnityEngine;

/// <summary>
/// Pushes HeadZones_Player's mesh vertices outward along their own normals by a small amount,
/// to avoid z-fighting with the underlying head/body mesh it sits almost exactly on top of.
/// Works on a runtime mesh instance only - never modifies the original imported asset.
/// </summary>
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class HeadZoneMeshOffset : MonoBehaviour
{
    [Tooltip("How far to push each vertex outward along its normal, in meters. Keep this very small.")]
    public float outwardOffset = 0.001f;

    private bool applied = false;

    void Awake()
    {
        ApplyOffset();
    }

    public void ApplyOffset()
    {
        if (applied)
            return;

        SkinnedMeshRenderer smr = GetComponent<SkinnedMeshRenderer>();
        if (smr == null || smr.sharedMesh == null)
        {
            Debug.LogWarning("HeadZoneMeshOffset: no SkinnedMeshRenderer/mesh found on " + name);
            return;
        }

        Mesh instanceMesh = Instantiate(smr.sharedMesh);
        instanceMesh.name = smr.sharedMesh.name + "_OffsetInstance";

        Vector3[] vertices = instanceMesh.vertices;
        Vector3[] normals = instanceMesh.normals;

        if (normals == null || normals.Length != vertices.Length)
        {
            instanceMesh.RecalculateNormals();
            normals = instanceMesh.normals;
        }

        for (int i = 0; i < vertices.Length; i++)
            vertices[i] += normals[i].normalized * outwardOffset;

        instanceMesh.vertices = vertices;
        instanceMesh.RecalculateBounds();

        smr.sharedMesh = instanceMesh;
        applied = true;

        Debug.Log("HeadZoneMeshOffset: pushed " + vertices.Length + " vertices outward by " + outwardOffset + "m on " + name);
    }
}
