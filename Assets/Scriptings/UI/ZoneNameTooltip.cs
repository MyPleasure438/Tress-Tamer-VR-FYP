using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Shows a single small tooltip with the current zone's name near whichever tool tip is closest
/// to the head, instead of showing all 21 zone labels at once. Uses the same nearest-zone-centroid
/// approach we used throughout this project (computed from the long hair card objects' positions).
/// Active whenever a tracked tool tip is close enough to the head - not tied to the H key toggle.
/// </summary>
public class ZoneNameTooltip : MonoBehaviour
{
    [Header("Tool Tips To Track")]
    [Tooltip("Assign the tip transform of each tool (Scissor blade tip, Clipper tip, Comb tip, Brush tip, etc). Whichever is closest to the head at any moment drives the tooltip.")]
    public List<Transform> toolTipReferences = new List<Transform>();

    [Header("Detection")]
    [Tooltip("If the closest tool tip is farther than this from the head (in meters), the tooltip hides.")]
    public float maxDetectionDistance = 0.2f;

    [Header("Appearance")]
    public float tooltipWorldScale = 0.008f;
    public Color tooltipColor = Color.white;
    public Vector3 tooltipOffset = new Vector3(0f, 0.02f, 0f);

    private static readonly string[] ZoneNamePrefixes = {
        "Back", "BackMiddle", "BackNape", "BackUpper", "CrownBack", "CrownLeft", "CrownRight",
        "FringeLeft", "FringeMiddle", "FringeRight", "LeftSideSideburn", "LeftSideUpper",
        "RightSideSideburn", "RightSideUpper", "TopLeft", "TopMiddle", "TopRight",
        "TransitionBackLeftAtMiddleSection", "TransitionBackLeftAtUpperSection",
        "TransitionBackRightAtMiddleSection", "TransitionBackRightAtUpperSection"
    };

    private readonly List<(Vector3 position, string zoneName)> zoneCardPositions = new List<(Vector3, string)>();
    private GameObject tooltipObject;
    private TextMeshPro tooltipText;
    private Camera mainCameraCache;
    private string currentZoneShown = null;

    void Start()
    {
        ComputeZoneCentroids();
        CreateTooltipObject();
    }

    void Update()
    {
        Transform closestTip = GetClosestToolTip(out float closestDistance);

        if (closestTip == null || closestDistance > maxDetectionDistance)
        {
            SetTooltipVisible(false);
            return;
        }

        string zoneName = FindNearestZone(closestTip.position);

        if (zoneName == null)
        {
            SetTooltipVisible(false);
            return;
        }

        SetTooltipVisible(true);
        UpdateTooltipPosition(closestTip.position);

        if (zoneName != currentZoneShown)
        {
            tooltipText.text = InsertSpacesBeforeCapitals(zoneName);
            currentZoneShown = zoneName;
        }

        if (mainCameraCache == null)
            mainCameraCache = Camera.main;

        if (mainCameraCache != null)
        {
            tooltipObject.transform.rotation = Quaternion.LookRotation(
                tooltipObject.transform.position - mainCameraCache.transform.position);
        }
    }

    private Transform GetClosestToolTip(out float closestDistance)
    {
        Transform closest = null;
        closestDistance = float.MaxValue;

        for (int i = 0; i < toolTipReferences.Count; i++)
        {
            Transform tip = toolTipReferences[i];
            if (tip == null)
                continue;

            float nearestZoneDistance = float.MaxValue;
            for (int j = 0; j < zoneCardPositions.Count; j++)
            {
                float d = Vector3.Distance(tip.position, zoneCardPositions[j].position);
                if (d < nearestZoneDistance)
                    nearestZoneDistance = d;
            }

            if (nearestZoneDistance < closestDistance)
            {
                closestDistance = nearestZoneDistance;
                closest = tip;
            }
        }

        return closest;
    }

    private string FindNearestZone(Vector3 worldPosition)
    {
        string best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < zoneCardPositions.Count; i++)
        {
            float d = Vector3.Distance(worldPosition, zoneCardPositions[i].position);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = zoneCardPositions[i].zoneName;
            }
        }

        return best;
    }

    private void ComputeZoneCentroids()
    {
        zoneCardPositions.Clear();
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        foreach (var obj in allObjects)
        {
            if (!obj.scene.IsValid())
                continue;

            string baseName = GetZoneBaseName(obj.name);
            if (baseName == null)
                continue;

            zoneCardPositions.Add((obj.transform.position, baseName));
        }

        Debug.Log("ZoneNameTooltip: collected " + zoneCardPositions.Count + " individual card positions across all zones.");
    }

    private string GetZoneBaseName(string objectName)
    {
        int dotIndex = objectName.IndexOf('.');
        string baseName = dotIndex >= 0 ? objectName.Substring(0, dotIndex) : objectName;

        for (int i = 0; i < ZoneNamePrefixes.Length; i++)
        {
            if (baseName == ZoneNamePrefixes[i])
                return baseName;
        }

        return null;
    }

    private void CreateTooltipObject()
    {
        tooltipObject = new GameObject("ZoneNameTooltip_Text");
        tooltipObject.transform.localScale = Vector3.one * tooltipWorldScale;

        tooltipText = tooltipObject.AddComponent<TextMeshPro>();
        tooltipText.color = tooltipColor;
        tooltipText.alignment = TextAlignmentOptions.Center;
        tooltipText.enableWordWrapping = false;
        tooltipText.fontSize = 12f;

        // Black outline so the text stays readable against dark hair, skin, or any zone color.
        if (tooltipText.fontMaterial != null)
        {
            tooltipText.fontMaterial.EnableKeyword("OUTLINE_ON");
            tooltipText.fontMaterial.SetColor("_OutlineColor", Color.black);
            tooltipText.fontMaterial.SetFloat("_OutlineWidth", 0.2f);
        }

        tooltipObject.SetActive(false);
    }

    private void SetTooltipVisible(bool visible)
    {
        if (tooltipObject != null && tooltipObject.activeSelf != visible)
            tooltipObject.SetActive(visible);

        if (!visible)
            currentZoneShown = null;
    }

    private void UpdateTooltipPosition(Vector3 nearToolTipPosition)
    {
        tooltipObject.transform.position = nearToolTipPosition + tooltipOffset;
    }

    private string InsertSpacesBeforeCapitals(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}
