using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class RainbowScissorHairCutter : MonoBehaviour
{
    [Header("Cut Point")]
    [Tooltip("Place this at the point where the scissor blades meet. If empty, this object's transform is used.")]
    [SerializeField] private Transform cutGizmo;

    [Tooltip("Hair cards whose cut line is this close to the gizmo can be cut.")]
    [SerializeField] private float cutRadius = 0.035f;

    [Tooltip("Small safety distance from the root so hair is not cut completely at the scalp.")]
    [SerializeField] private float minimumLengthFromRoot = 0.015f;

    [Header("Cut Timing")]
    [Tooltip("Useful for quick Play Mode testing. For VR, call TryCutAtGizmo from your scissor snip event instead.")]
    [SerializeField] private bool cutOnMouseClick = true;

    [Tooltip("If true, the script cuts automatically while hair is inside the cut radius.")]
    [SerializeField] private bool cutContinuously = false;

    [Tooltip("Minimum delay between cuts when using continuous cutting.")]
    [SerializeField] private float continuousCutCooldown = 0.12f;

    [Header("Scene References")]
    [SerializeField] private HairManager hairManager;
    [SerializeField] private CutHairPieceSpawner cutHairPieceSpawner;

    private float nextContinuousCutTime;

    private void Awake()
    {
        if (cutGizmo == null)
            cutGizmo = transform;

        if (hairManager == null)
            hairManager = HairManager.Instance;

        if (cutHairPieceSpawner == null)
            cutHairPieceSpawner = GetComponent<CutHairPieceSpawner>();
    }

    private void Update()
    {
        if (cutOnMouseClick && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            TryCutAtGizmo();

        if (!cutContinuously || Time.time < nextContinuousCutTime)
            return;

        int cutCount = TryCutAtGizmo();

        if (cutCount > 0)
            nextContinuousCutTime = Time.time + continuousCutCooldown;
    }

    [ContextMenu("Cut At Gizmo")]
    public int TryCutAtGizmo()
    {
        if (cutGizmo == null)
            return 0;

        return TryCut(cutGizmo.position);
    }

    public int TryCut(Vector3 cutWorldPosition)
    {
        HairManager activeHairManager = hairManager != null ? hairManager : HairManager.Instance;

        if (activeHairManager == null)
        {
            Debug.LogWarning("[RainbowScissorHairCutter] No HairManager found in scene.");
            return 0;
        }

        List<HairCardData> hairCards = activeHairManager.HairCards;
        int cutCount = 0;

        for (int i = 0; i < hairCards.Count; i++)
        {
            HairCardData hair = hairCards[i];

            if (hair == null || !hair.IsCuttable || hair.CurrentLength <= minimumLengthFromRoot)
                continue;

            Vector3 root = GetRootPosition(hair);
            Vector3 growthDirection = GetGrowthDirection(hair);
            float distanceFromRoot = Vector3.Dot(cutWorldPosition - root, growthDirection);

            if (distanceFromRoot <= minimumLengthFromRoot || distanceFromRoot >= hair.CurrentLength)
                continue;

            Vector3 closestPointOnHair = root + growthDirection * distanceFromRoot;
            float distanceToHair = Vector3.Distance(cutWorldPosition, closestPointOnHair);

            if (distanceToHair > cutRadius)
                continue;

            float oldLength = hair.CurrentLength;
            float newLength = Mathf.Clamp(distanceFromRoot, minimumLengthFromRoot, oldLength);
            float removedLength = oldLength - newLength;

            if (removedLength <= 0.001f)
                continue;

            hair.CurrentLength = newLength;
            activeHairManager.UpdateHairVisual(hair);

            if (cutHairPieceSpawner != null)
                cutHairPieceSpawner.SpawnCutPiece(hair, closestPointOnHair, removedLength);

            cutCount++;
        }

        return cutCount;
    }

    private static Vector3 GetRootPosition(HairCardData hair)
    {
        if (hair.RootPosition != Vector3.zero)
            return hair.RootPosition;

        return hair.transform.position;
    }

    private static Vector3 GetGrowthDirection(HairCardData hair)
    {
        if (hair.GrowthDirection.sqrMagnitude > 0.0001f)
            return hair.GrowthDirection.normalized;

        return -hair.transform.up;
    }

    private void OnDrawGizmosSelected()
    {
        Transform gizmo = cutGizmo != null ? cutGizmo : transform;

        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(gizmo.position, cutRadius);
    }
}
