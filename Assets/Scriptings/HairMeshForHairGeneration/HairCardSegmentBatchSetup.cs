using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

// HANDOFF NOTE:
// Editor helper for converting many imported hair cards into segmented cards. Use its context menu in the Inspector.
// This is not gameplay runtime code. It creates _GeneratedHairSegments children and can disable the source renderer.
public class HairCardSegmentBatchSetup : MonoBehaviour
{
    [Header("Search")]
    public Transform HairCardsRoot;
    // Comma-separated prefixes let the user include groups such as Back, Back.001, CrownLeft, etc.
    public string NamePrefixes = "Back, BackMiddle, BackNape, BackUpper, Crown, Fringe, LeftSide, RightSide, Top, TransitionBackLeft, TransitionBackRight";
    public bool IncludeInactive = true;
    public bool MatchCase = false;

    [Header("Segments")]
    [Min(2)]
    // More segments make shorter cuts smoother, but increase renderer/collider count. Keep this modest for VR.
    public int SegmentsPerHairCard = 8;
    // Only removes previously generated segment children, not the original imported hair card mesh.
    public bool RemoveOldGeneratedSegments = true;
    // Source renderer is hidden so only the generated segment pieces are visible/cuttable.
    public bool DisableOriginalRenderer = true;
    public bool DisableOriginalColliders = true;

    [Header("Segment Colliders")]
    public bool AddBoxColliderToSegments = true;
    public bool SegmentCollidersAreTriggers = true;
    public Vector3 ColliderSizeMultiplier = new Vector3(1.03f, 1.03f, 1.03f);

    [Header("HairCardData")]
    public bool AddHairCardData = true;
    public bool UseHairTransformAsRootGizmo = true;
    public bool AutoCalculateGrowthDirection = true;
    public bool SetLengthFromMesh = true;

    // FIX ("Section stuck at Fringe after regenerating cards"): without this, every card this tool creates
    // or re-processes keeps whatever HairCardData.Section value the source object happened to already have
    // (Fringe, the enum's default 0 value, if HairCardData was just added fresh or duplicated from another
    // card) - it was never derived from the card's own name. Mirrors HaircutManager.TryFindSectionInHierarchy
    // / NormalizeSectionName exactly (longest-matching HairSectionType name that is a prefix of the
    // alphanumeric-only card name, walking up to HairCardsRoot if the card's own name has no match) so a
    // card named e.g. "BackMiddle.001" always resolves to HairSectionType.BackMiddle here the same way it
    // would if HaircutManager's own "Assign Sections From Object Or Parent Names" ran on it later - the two
    // systems can never disagree.
    [Tooltip("Derives each card's HairCardData.Section from its own object name (or a parent's, up to HairCardsRoot) - e.g. \"BackMiddle.001\" becomes HairSectionType.BackMiddle. Picks the LONGEST matching HairSectionType name so e.g. \"FringeLeft\" is preferred over \"Fringe\". Runs every time this tool processes a card, so Section can never end up stuck at the enum default (Fringe) after a regenerate.")]
    public bool AssignSectionFromName = true;

    private const string GeneratedSegmentRootName = "_GeneratedHairSegments";
    private const string GeneratedSegmentPrefix = "_HairSegment_";

    // Run-scoped counters for the one-line summary log at the end of GenerateSegmentedHairCards(), instead
    // of spamming a warning per unmatched card.
    private int sectionAssignedCount;
    private int sectionUnmatchedCount;

    [ContextMenu("Generate Segmented Hair Cards")]
    public void GenerateSegmentedHairCards()
    {
        Transform root = HairCardsRoot != null ? HairCardsRoot : transform;
        string[] prefixes = GetPrefixes();
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(IncludeInactive);

        if (RemoveOldGeneratedSegments)
            RemoveOldGeneratedSegmentsFromTargets(meshFilters, prefixes);

        meshFilters = root.GetComponentsInChildren<MeshFilter>(IncludeInactive);
        int processedCount = 0;
        sectionAssignedCount = 0;
        sectionUnmatchedCount = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];

            if (meshFilter == null)
                continue;

            if (meshFilter.sharedMesh == null || !NameMatches(meshFilter.name, prefixes) || IsGeneratedSegment(meshFilter.transform))
                continue;

            HairCardData data = AddOrSetupHairCardData(meshFilter);
            GenerateSegmentsForHairCard(meshFilter, data);
            processedCount++;
        }

        Debug.Log($"[HairCardSegmentBatchSetup] Segmented hair cards generated: {processedCount}");

        if (AssignSectionFromName)
        {
            Debug.Log($"[HairCardSegmentBatchSetup] Sections auto-assigned from names: {sectionAssignedCount}/{processedCount}" +
                (sectionUnmatchedCount > 0 ? $" ({sectionUnmatchedCount} card(s) had no name/parent matching any HairSectionType - see warnings above, Section left unchanged for those)" : ""));
        }
    }

    private void GenerateSegmentsForHairCard(MeshFilter sourceFilter, HairCardData data)
    {
        Transform segmentRoot = CreateSegmentRoot(sourceFilter.transform);
        Mesh sourceMesh = sourceFilter.sharedMesh;
        MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
        List<int>[] segmentTriangles = AssignTrianglesToSegments(sourceFilter, data, sourceMesh);
        HairCardSegment[] segments = new HairCardSegment[SegmentsPerHairCard];

        for (int i = 0; i < SegmentsPerHairCard; i++)
        {
            Mesh segmentMesh = BuildSegmentMesh(sourceMesh, segmentTriangles[i], i);

            if (segmentMesh == null)
                continue;

            MarkDirty(segmentMesh);

            GameObject segmentObject = new GameObject($"{GeneratedSegmentPrefix}{i:00}");
            RegisterCreatedObject(segmentObject);
            segmentObject.transform.SetParent(segmentRoot, false);

            MeshFilter segmentFilter = AddComponentWithUndo<MeshFilter>(segmentObject);
            segmentFilter.sharedMesh = segmentMesh;

            if (sourceRenderer != null)
            {
                MeshRenderer segmentRenderer = AddComponentWithUndo<MeshRenderer>(segmentObject);
                segmentRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
                segmentRenderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                segmentRenderer.receiveShadows = sourceRenderer.receiveShadows;
            }

            if (AddBoxColliderToSegments)
                AddSegmentCollider(segmentObject, segmentMesh);

            HairCardSegment segment = AddComponentWithUndo<HairCardSegment>(segmentObject);
            segment.SegmentIndex = i;
            segments[i] = segment;

            MarkDirty(segmentObject);
        }

        SegmentedHairCard segmentedHairCard = sourceFilter.GetComponent<SegmentedHairCard>();

        if (segmentedHairCard == null)
            segmentedHairCard = AddComponentWithUndo<SegmentedHairCard>(sourceFilter.gameObject);

        segmentedHairCard.SetSegments(segments);

        if (DisableOriginalRenderer && sourceRenderer != null)
        {
            sourceRenderer.enabled = false;
            MarkDirty(sourceRenderer);
        }

        if (DisableOriginalColliders)
            DisableColliders(sourceFilter, segmentRoot);

        MarkDirty(segmentedHairCard);
        MarkDirty(sourceFilter.gameObject);
    }

    private List<int>[] AssignTrianglesToSegments(MeshFilter sourceFilter, HairCardData data, Mesh sourceMesh)
    {
        Vector3[] vertices = sourceMesh.vertices;
        Vector3 rootLocal = data != null ? sourceFilter.transform.InverseTransformPoint(data.GetRootPosition()) : sourceMesh.bounds.center;
        Vector3 growthLocal = data != null ? sourceFilter.transform.InverseTransformDirection(data.GetGrowthDirection()).normalized : Vector3.zero;

        if (growthLocal.sqrMagnitude <= 0.0001f)
            growthLocal = GetLongestBoundsAxis(sourceMesh.bounds);

        float minProjection = float.MaxValue;
        float maxProjection = float.MinValue;

        for (int i = 0; i < vertices.Length; i++)
        {
            float projection = Vector3.Dot(vertices[i] - rootLocal, growthLocal);
            minProjection = Mathf.Min(minProjection, projection);
            maxProjection = Mathf.Max(maxProjection, projection);
        }

        if (maxProjection - minProjection <= 0.0001f)
        {
            minProjection = 0f;
            maxProjection = Mathf.Max(sourceMesh.bounds.size.magnitude, 0.001f);
        }

        List<int>[] segmentTriangles = new List<int>[SegmentsPerHairCard];

        for (int i = 0; i < segmentTriangles.Length; i++)
            segmentTriangles[i] = new List<int>();

        for (int submesh = 0; submesh < sourceMesh.subMeshCount; submesh++)
        {
            int[] triangles = sourceMesh.GetTriangles(submesh);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                Vector3 centroid = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                float projection = Vector3.Dot(centroid - rootLocal, growthLocal);
                float normalized = Mathf.InverseLerp(minProjection, maxProjection, projection);
                int segmentIndex = Mathf.Clamp(Mathf.FloorToInt(normalized * SegmentsPerHairCard), 0, SegmentsPerHairCard - 1);

                segmentTriangles[segmentIndex].Add(a);
                segmentTriangles[segmentIndex].Add(b);
                segmentTriangles[segmentIndex].Add(c);
            }
        }

        return segmentTriangles;
    }

    private Mesh BuildSegmentMesh(Mesh sourceMesh, List<int> sourceTriangleIndices, int segmentIndex)
    {
        if (sourceTriangleIndices == null || sourceTriangleIndices.Count < 3)
            return null;

        Vector3[] sourceVertices = sourceMesh.vertices;
        Vector3[] sourceNormals = sourceMesh.normals;
        Vector2[] sourceUVs = sourceMesh.uv;
        Dictionary<int, int> remappedIndices = new Dictionary<int, int>();
        List<Vector3> newVertices = new List<Vector3>();
        List<Vector3> newNormals = new List<Vector3>();
        List<Vector2> newUVs = new List<Vector2>();
        List<int> newTriangles = new List<int>();

        for (int i = 0; i < sourceTriangleIndices.Count; i++)
        {
            int sourceIndex = sourceTriangleIndices[i];

            if (!remappedIndices.TryGetValue(sourceIndex, out int newIndex))
            {
                newIndex = newVertices.Count;
                remappedIndices.Add(sourceIndex, newIndex);
                newVertices.Add(sourceVertices[sourceIndex]);
                newNormals.Add(sourceNormals != null && sourceNormals.Length > sourceIndex ? sourceNormals[sourceIndex] : Vector3.up);
                newUVs.Add(sourceUVs != null && sourceUVs.Length > sourceIndex ? sourceUVs[sourceIndex] : Vector2.zero);
            }

            newTriangles.Add(newIndex);
        }

        Mesh segmentMesh = new Mesh();
        segmentMesh.name = $"{sourceMesh.name}_Segment_{segmentIndex:00}";
        segmentMesh.SetVertices(newVertices);
        segmentMesh.SetNormals(newNormals);
        segmentMesh.SetUVs(0, newUVs);
        segmentMesh.SetTriangles(newTriangles, 0);
        segmentMesh.RecalculateBounds();

        if (sourceNormals == null || sourceNormals.Length == 0)
            segmentMesh.RecalculateNormals();

        return segmentMesh;
    }

    private HairCardData AddOrSetupHairCardData(MeshFilter meshFilter)
    {
        if (!AddHairCardData)
            return meshFilter.GetComponent<HairCardData>();

        HairCardData data = meshFilter.GetComponent<HairCardData>();

        if (data == null)
            data = AddComponentWithUndo<HairCardData>(meshFilter.gameObject);

        data.UseRootGizmo = UseHairTransformAsRootGizmo;

        if (UseHairTransformAsRootGizmo)
            data.RootGizmo = meshFilter.transform;

        data.AutoCalculateGrowthDirection = AutoCalculateGrowthDirection;
        data.SetLengthFromMesh = SetLengthFromMesh;
        data.RefreshRootPosition();

        if (AutoCalculateGrowthDirection)
            data.AutoCalculateGrowthDirectionFromMesh();

        if (AssignSectionFromName)
            AssignSectionFromNameOrHierarchy(meshFilter.transform, data);

        MarkDirty(data);
        return data;
    }

    // See the AssignSectionFromName field comment above for why this exists. Deliberately mirrors
    // HaircutManager.TryFindSectionInHierarchy/NormalizeSectionName's matching rules exactly (longest
    // HairSectionType name that prefixes the alphanumeric-only object name, walking up parents to
    // HairCardsRoot) so both systems always agree on a given card's section.
    private void AssignSectionFromNameOrHierarchy(Transform hairCardTransform, HairCardData data)
    {
        Transform stopAtRoot = HairCardsRoot != null ? HairCardsRoot : transform;

        if (!TryFindSectionInHierarchy(hairCardTransform, stopAtRoot, out HairSectionType section))
        {
            sectionUnmatchedCount++;
            Debug.LogWarning($"[HairCardSegmentBatchSetup] Could not auto-detect a HairSectionType from the name of '{hairCardTransform.name}' (or its parents up to HairCardsRoot) - leaving Section as '{data.Section}'. Rename it (or a parent) to start with a HairSectionType name, e.g. \"BackMiddle.001\", or assign Section manually.", hairCardTransform);
            return;
        }

        data.Section = section;
        sectionAssignedCount++;
    }

    private bool TryFindSectionInHierarchy(Transform hairCardTransform, Transform stopAtRoot, out HairSectionType section)
    {
        section = default;
        int bestNameLength = -1;
        HairSectionType bestSection = section;
        Transform current = hairCardTransform;
        StringComparison comparison = MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        while (current != null)
        {
            string normalizedName = NormalizeForSectionMatch(current.name);
            HairSectionType[] sections = (HairSectionType[])Enum.GetValues(typeof(HairSectionType));

            for (int i = 0; i < sections.Length; i++)
            {
                string sectionName = sections[i].ToString();

                if (sectionName.Length <= bestNameLength || !normalizedName.StartsWith(sectionName, comparison))
                    continue;

                bestNameLength = sectionName.Length;
                bestSection = sections[i];
            }

            if (current == stopAtRoot)
                break;

            current = current.parent;
        }

        if (bestNameLength < 0)
            return false;

        section = bestSection;
        return true;
    }

    private static string NormalizeForSectionMatch(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
            return string.Empty;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(objectName.Length);

        for (int i = 0; i < objectName.Length; i++)
        {
            char c = objectName[i];

            if (char.IsLetterOrDigit(c))
                builder.Append(c);
        }

        return builder.ToString();
    }

    private Transform CreateSegmentRoot(Transform sourceTransform)
    {
        GameObject segmentRootObject = new GameObject(GeneratedSegmentRootName);
        RegisterCreatedObject(segmentRootObject);
        segmentRootObject.transform.SetParent(sourceTransform, false);
        return segmentRootObject.transform;
    }

    private void AddSegmentCollider(GameObject segmentObject, Mesh segmentMesh)
    {
        BoxCollider boxCollider = AddComponentWithUndo<BoxCollider>(segmentObject);
        Bounds bounds = segmentMesh.bounds;
        boxCollider.center = bounds.center;
        boxCollider.size = Vector3.Scale(new Vector3(
            Mathf.Max(bounds.size.x, 0.005f),
            Mathf.Max(bounds.size.y, 0.005f),
            Mathf.Max(bounds.size.z, 0.005f)
        ), ColliderSizeMultiplier);
        boxCollider.isTrigger = SegmentCollidersAreTriggers;
        MarkDirty(boxCollider);
    }

    private void DisableColliders(MeshFilter sourceFilter, Transform segmentRoot)
    {
        Collider[] colliders = sourceFilter.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            if (segmentRoot != null && colliders[i].transform.IsChildOf(segmentRoot))
                continue;

            colliders[i].enabled = false;
            MarkDirty(colliders[i]);
        }
    }

    private void RemoveGeneratedSegments(Transform hairCardTransform)
    {
        for (int i = hairCardTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = hairCardTransform.GetChild(i);

            if (child.name.Equals(GeneratedSegmentRootName, StringComparison.Ordinal))
                DestroyObject(child.gameObject);
        }
    }

    private void RemoveOldGeneratedSegmentsFromTargets(MeshFilter[] meshFilters, string[] prefixes)
    {
        if (meshFilters == null)
            return;

        HashSet<Transform> cleanedTransforms = new HashSet<Transform>();

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];

            if (meshFilter == null || IsGeneratedSegment(meshFilter.transform))
                continue;

            if (!NameMatches(meshFilter.name, prefixes))
                continue;

            if (cleanedTransforms.Add(meshFilter.transform))
                RemoveGeneratedSegments(meshFilter.transform);
        }
    }

    private bool IsGeneratedSegment(Transform target)
    {
        Transform current = target;

        while (current != null)
        {
            if (current.name.Equals(GeneratedSegmentRootName, StringComparison.Ordinal) || current.name.StartsWith(GeneratedSegmentPrefix, StringComparison.Ordinal))
                return true;

            current = current.parent;
        }

        return false;
    }

    private Vector3 GetLongestBoundsAxis(Bounds bounds)
    {
        Vector3 size = bounds.size;

        if (size.x >= size.y && size.x >= size.z)
            return Vector3.right;

        if (size.y >= size.z)
            return Vector3.up;

        return Vector3.forward;
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
            Undo.RegisterCreatedObjectUndo(target, "Create segmented hair card");
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
