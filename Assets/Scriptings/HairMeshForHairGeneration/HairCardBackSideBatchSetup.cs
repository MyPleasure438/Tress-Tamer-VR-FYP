using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class HairCardBackSideBatchSetup : MonoBehaviour
{
    [Header("Search")]
    public Transform HairCardsRoot;
    public string NamePrefixes = "Back, BackMiddle, BackNape, BackUpper, Crown, Fringe, LeftSide, RightSide, Top, TransitionBackLeft, TransitionBackRight";
    public bool IncludeInactive = true;
    public bool MatchCase = false;

    [Header("Generated Back Side")]
    public bool RemoveOldGeneratedBackSides = true;
    public bool DisableBackSideColliders = true;
    public float BackSideOffset = 0.0002f;

    private const string GeneratedBackSideName = "_GeneratedHairBackSide";

    [ContextMenu("Generate Back Sides For Matching Hair Cards")]
    public void GenerateBackSidesForMatchingHairCards()
    {
        Transform root = HairCardsRoot != null ? HairCardsRoot : transform;
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(IncludeInactive);
        string[] prefixes = GetPrefixes();
        int processedCount = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter sourceFilter = meshFilters[i];

            if (sourceFilter.sharedMesh == null || IsGeneratedBackSide(sourceFilter.transform) || !NameMatches(sourceFilter.name, prefixes))
                continue;

            GenerateBackSide(sourceFilter);
            processedCount++;
        }

        Debug.Log($"[HairCardBackSideBatchSetup] Generated back sides: {processedCount}");
    }

    private void GenerateBackSide(MeshFilter sourceFilter)
    {
        if (RemoveOldGeneratedBackSides)
            RemoveGeneratedBackSide(sourceFilter.transform);

        MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();

        GameObject backSideObject = new GameObject(GeneratedBackSideName);
        RegisterCreatedObject(backSideObject);
        backSideObject.transform.SetParent(sourceFilter.transform, false);

        Mesh backSideMesh = CreateBackSideMesh(sourceFilter.sharedMesh);
        MarkDirty(backSideMesh);

        MeshFilter backSideFilter = AddComponentWithUndo<MeshFilter>(backSideObject);
        backSideFilter.sharedMesh = backSideMesh;

        if (sourceRenderer != null)
        {
            MeshRenderer backSideRenderer = AddComponentWithUndo<MeshRenderer>(backSideObject);
            backSideRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            backSideRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            backSideRenderer.receiveShadows = sourceRenderer.receiveShadows;
        }

        if (DisableBackSideColliders)
        {
            Collider[] colliders = backSideObject.GetComponents<Collider>();

            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        MarkDirty(backSideObject);
    }

    private Mesh CreateBackSideMesh(Mesh sourceMesh)
    {
        Mesh mesh = new Mesh();
        mesh.name = sourceMesh.name + "_BackSide";

        Vector3[] vertices = sourceMesh.vertices;
        Vector3[] normals = sourceMesh.normals;
        Vector4[] tangents = sourceMesh.tangents;

        if (BackSideOffset != 0f && normals != null && normals.Length == vertices.Length)
        {
            vertices = (Vector3[])vertices.Clone();

            for (int i = 0; i < vertices.Length; i++)
                vertices[i] -= normals[i].normalized * BackSideOffset;
        }

        mesh.vertices = vertices;
        mesh.uv = sourceMesh.uv;
        mesh.uv2 = sourceMesh.uv2;
        mesh.colors = sourceMesh.colors;

        if (normals != null && normals.Length == sourceMesh.vertexCount)
        {
            Vector3[] flippedNormals = new Vector3[normals.Length];

            for (int i = 0; i < normals.Length; i++)
                flippedNormals[i] = -normals[i];

            mesh.normals = flippedNormals;
        }

        if (tangents != null && tangents.Length == sourceMesh.vertexCount)
        {
            Vector4[] flippedTangents = new Vector4[tangents.Length];

            for (int i = 0; i < tangents.Length; i++)
            {
                Vector4 tangent = tangents[i];
                flippedTangents[i] = new Vector4(tangent.x, tangent.y, tangent.z, -tangent.w);
            }

            mesh.tangents = flippedTangents;
        }

        mesh.subMeshCount = sourceMesh.subMeshCount;

        for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            int[] triangles = sourceMesh.GetTriangles(submesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int temp = triangles[i + 1];
                triangles[i + 1] = triangles[i + 2];
                triangles[i + 2] = temp;
            }

            mesh.SetTriangles(triangles, submesh);
        }

        mesh.RecalculateBounds();

        if (mesh.normals == null || mesh.normals.Length == 0)
            mesh.RecalculateNormals();

        return mesh;
    }

    private void RemoveGeneratedBackSide(Transform hairCardTransform)
    {
        for (int i = hairCardTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = hairCardTransform.GetChild(i);

            if (child.name.Equals(GeneratedBackSideName, StringComparison.Ordinal))
                DestroyObject(child.gameObject);
        }
    }

    private bool IsGeneratedBackSide(Transform target)
    {
        Transform current = target;

        while (current != null)
        {
            if (current.name.Equals(GeneratedBackSideName, StringComparison.Ordinal))
                return true;

            current = current.parent;
        }

        return false;
    }

    private string[] GetPrefixes()
    {
        return NamePrefixes.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private bool NameMatches(string objectName, string[] prefixes)
    {
        StringComparison comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        for (int i = 0; i < prefixes.Length; i++)
        {
            string prefix = prefixes[i].Trim();

            if (string.IsNullOrEmpty(prefix))
                continue;

            if (objectName.Equals(prefix, comparison) || objectName.StartsWith(prefix + ".", comparison))
                return true;
        }

        return false;
    }

    private static T AddComponentWithUndo<T>(GameObject target) where T : Component
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return Undo.AddComponent<T>(target);
#endif

        return target.AddComponent<T>();
    }

    private static void RegisterCreatedObject(GameObject target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            Undo.RegisterCreatedObjectUndo(target, "Create generated hair back side");
#endif
    }

    private static void DestroyObject(GameObject target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.DestroyObjectImmediate(target);
            return;
        }
#endif

        Destroy(target);
    }

    private static void MarkDirty(UnityEngine.Object target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && target != null)
            EditorUtility.SetDirty(target);
#endif
    }
}
