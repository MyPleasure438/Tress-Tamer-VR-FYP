using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class HairCardBatchSetup : MonoBehaviour
{
    [Header("Search")]
    [Tooltip("Parent containing the hair card mesh objects. If empty, this object's children are searched.")]
    public Transform HairCardsRoot;

    [Tooltip("Comma-separated name prefixes. Example: Back, RightSide, LeftSide")]
    public string NamePrefixes = "Back, BackMiddle, BackNape, BackUpper, Crown, Fringe, LeftSide, RightSide, Top, TransitionBackLeft, TransitionBackRight";

    public bool IncludeInactive = true;
    public bool MatchCase = false;

    [Header("Generated Colliders")]
    [Min(1)]
    public int BoxesPerHairCard = 3;

    [Tooltip("Extra multiplier applied to each generated box collider size.")]
    public Vector3 ColliderSizeMultiplier = new Vector3(1.05f, 1.05f, 1.05f);

    [Tooltip("Extra overlap along the split direction so there are no gaps between boxes.")]
    [Range(0f, 0.5f)]
    public float SegmentOverlap = 0.08f;

    public bool IsTrigger = true;
    public bool RemoveOldGeneratedColliders = true;

    [Header("HairCardData")]
    public bool AddHairCardData = true;
    public bool UseHairTransformAsRootGizmo = true;
    public bool AutoCalculateGrowthDirection = true;
    public bool SetLengthFromMesh = true;

    private const string GeneratedColliderPrefix = "_AutoHairBoxCollider_";

    [ContextMenu("Setup Matching Hair Cards")]
    public void SetupMatchingHairCards()
    {
        Transform root = HairCardsRoot != null ? HairCardsRoot : transform;
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(IncludeInactive);
        string[] prefixes = GetPrefixes();
        int setupCount = 0;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter meshFilter = meshFilters[i];

            if (meshFilter.sharedMesh == null || !NameMatches(meshFilter.name, prefixes))
                continue;

            HairCardData data = AddOrSetupHairCardData(meshFilter);

            if (RemoveOldGeneratedColliders)
                RemoveGeneratedColliderChildren(meshFilter.transform);

            AddGeneratedBoxColliders(meshFilter, data);
            setupCount++;
        }

        Debug.Log($"[HairCardBatchSetup] Setup completed. Hair cards processed: {setupCount}");
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

        MarkDirty(data);
        return data;
    }

    private void AddGeneratedBoxColliders(MeshFilter meshFilter, HairCardData data)
    {
        Mesh mesh = meshFilter.sharedMesh;
        Bounds bounds = mesh.bounds;
        int splitAxis = GetSplitAxis(meshFilter, data);
        int boxCount = Mathf.Max(1, BoxesPerHairCard);

        for (int i = 0; i < boxCount; i++)
        {
            Transform colliderTransform = meshFilter.transform;
            BoxCollider boxCollider;

            if (boxCount == 1)
            {
                boxCollider = meshFilter.GetComponent<BoxCollider>();

                if (boxCollider == null)
                    boxCollider = AddComponentWithUndo<BoxCollider>(meshFilter.gameObject);
            }
            else
            {
                GameObject colliderObject = new GameObject($"{GeneratedColliderPrefix}{i:00}");
                RegisterCreatedObject(colliderObject);
                colliderObject.transform.SetParent(meshFilter.transform, false);
                colliderTransform = colliderObject.transform;
                boxCollider = AddComponentWithUndo<BoxCollider>(colliderObject);
            }

            Vector3 center = bounds.center;
            Vector3 size = bounds.size;
            float fullAxisSize = Mathf.Max(GetAxis(size, splitAxis), 0.001f);
            float segmentSize = fullAxisSize / boxCount;
            float segmentCenter = -fullAxisSize * 0.5f + segmentSize * (i + 0.5f);

            SetAxis(ref center, splitAxis, GetAxis(bounds.center, splitAxis) + segmentCenter);
            SetAxis(ref size, splitAxis, segmentSize * (1f + SegmentOverlap));

            boxCollider.center = center;
            boxCollider.size = Vector3.Scale(size, ColliderSizeMultiplier);
            boxCollider.isTrigger = IsTrigger;

            MarkDirty(boxCollider);
            MarkDirty(colliderTransform);
        }
    }

    private int GetSplitAxis(MeshFilter meshFilter, HairCardData data)
    {
        if (data != null && data.GetGrowthDirection().sqrMagnitude > 0.0001f)
        {
            Vector3 localDirection = meshFilter.transform.InverseTransformDirection(data.GetGrowthDirection()).normalized;
            Vector3 absolute = new Vector3(Mathf.Abs(localDirection.x), Mathf.Abs(localDirection.y), Mathf.Abs(localDirection.z));

            if (absolute.x >= absolute.y && absolute.x >= absolute.z)
                return 0;

            if (absolute.y >= absolute.z)
                return 1;

            return 2;
        }

        Vector3 size = meshFilter.sharedMesh.bounds.size;

        if (size.x >= size.y && size.x >= size.z)
            return 0;

        if (size.y >= size.z)
            return 1;

        return 2;
    }

    private void RemoveGeneratedColliderChildren(Transform hairCardTransform)
    {
        for (int i = hairCardTransform.childCount - 1; i >= 0; i--)
        {
            Transform child = hairCardTransform.GetChild(i);

            if (!child.name.StartsWith(GeneratedColliderPrefix, StringComparison.Ordinal))
                continue;

            DestroyObject(child.gameObject);
        }
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

    private static float GetAxis(Vector3 value, int axis)
    {
        if (axis == 0)
            return value.x;

        if (axis == 1)
            return value.y;

        return value.z;
    }

    private static void SetAxis(ref Vector3 value, int axis, float axisValue)
    {
        if (axis == 0)
            value.x = axisValue;
        else if (axis == 1)
            value.y = axisValue;
        else
            value.z = axisValue;
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
            Undo.RegisterCreatedObjectUndo(target, "Create generated hair collider");
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
