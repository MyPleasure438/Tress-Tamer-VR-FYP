using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;
using System.Collections.Generic;
using System.Text.RegularExpressions;

/// <summary>
/// Toggles HeadZones_Player (the colored zone-reference mesh) on/off via a keyboard key or a public
/// method wireable to a VR button, and shows a floating text label at each zone's position while active.
/// Labels are created once at Start from every "Label_*" object in the scene (these are the same
/// bone-parented empties built in Blender, so they already follow head movement correctly).
/// </summary>
public class HeadZonePlayerToggle : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The HeadZones_Player GameObject. If left empty, it will be found by name at Start.")]
    public GameObject headZonesPlayerObject;

    [Header("Input")]
    [Tooltip("Desktop testing key. VR should call ToggleHeadZones() from a controller button event instead.")]
    public Key toggleKey = Key.H;

    [Header("Label Appearance")]
    public float labelWorldScale = 0.006f;
    public Color labelColor = Color.black;
    public bool billboardLabelsTowardCamera = true;
    [Tooltip("How far to push each label outward from the head surface, in meters, so it isn't clipped by the head/hair geometry.")]
    public float labelOutwardOffset = 0.015f;

    private bool isShowing = false;
    private readonly List<GameObject> spawnedLabels = new List<GameObject>();
    private readonly Dictionary<Renderer, bool> hiddenRendererPreviousStates = new Dictionary<Renderer, bool>();

    // Zone name prefixes used to find the long hair card objects and stubble meshes to hide while zones are shown.
    private static readonly string[] ZoneNamePrefixes = {
        "Back", "BackMiddle", "BackNape", "BackUpper", "CrownBack", "CrownLeft", "CrownRight",
        "FringeLeft", "FringeMiddle", "FringeRight", "LeftSideSideburn", "LeftSideUpper",
        "RightSideSideburn", "RightSideUpper", "TopLeft", "TopMiddle", "TopRight",
        "TransitionBackLeftAtMiddleSection", "TransitionBackLeftAtUpperSection",
        "TransitionBackRightAtMiddleSection", "TransitionBackRightAtUpperSection"
    };
    private static readonly string[] StubbleObjectNames = { "HairCards_InductionCut", "HairCards_ButchCut", "HairCards_CrewCut" };
    private Camera mainCameraCache;

    void Start()
    {
        if (headZonesPlayerObject == null)
        {
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var o in allObjects)
            {
                if (o.name == "HeadZones_Player" && o.scene.IsValid())
                {
                    headZonesPlayerObject = o;
                    break;
                }
            }
        }

        CreateZoneLabels();
        SetVisible(false);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
            ToggleHeadZones();

        if (isShowing && billboardLabelsTowardCamera)
            BillboardLabels();
    }

    /// <summary>Call this from a VR controller button event (e.g. XR Grab Interactable / Input Action callback).</summary>
    public void ToggleHeadZones()
    {
        SetVisible(!isShowing);
    }

    public void SetVisible(bool visible)
    {
        isShowing = visible;

        // Hair renderers are hidden below (visual only, by design - see HideHairForZoneView's own
        // comment), but colliders are intentionally left untouched. Without this flag, scissors and
        // the clipper could still cut hair that's invisible, causing already-cut hair to suddenly
        // appear once the overview is turned back off.
        CuttingManager.IsCuttingBlocked = visible;

        if (headZonesPlayerObject != null)
            headZonesPlayerObject.SetActive(visible);

        for (int i = 0; i < spawnedLabels.Count; i++)
        {
            if (spawnedLabels[i] != null)
                spawnedLabels[i].SetActive(visible);
        }

        if (visible)
            HideHairForZoneView();
        else
            RestoreHairAfterZoneView();
    }

    // Hides hair renderers (not GameObjects) so the segmented-cutting system's own active/inactive
    // state on individual segments is never touched - only visual rendering is toggled off here.
    private void HideHairForZoneView()
    {
        hiddenRendererPreviousStates.Clear();
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();

        foreach (var obj in allObjects)
        {
            if (!obj.scene.IsValid())
                continue;

            bool isLongHairCard = IsZoneNamedObject(obj.name);
            bool isStubbleMesh = System.Array.IndexOf(StubbleObjectNames, obj.name) >= 0;

            if (!isLongHairCard && !isStubbleMesh)
                continue;

            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (r == null || hiddenRendererPreviousStates.ContainsKey(r))
                    continue;

                hiddenRendererPreviousStates[r] = r.enabled;
                r.enabled = false;
            }
        }
    }

    private void RestoreHairAfterZoneView()
    {
        foreach (var pair in hiddenRendererPreviousStates)
        {
            if (pair.Key == null)
                continue;

            // If this renderer's card belongs to a zone that's still clipped (a clip was attached while
            // zones were being shown), leave it hidden instead of blindly restoring - otherwise unhiding
            // hair here would undo the clip's own hiding, showing hair that should still be held back.
            HairCardData ownerCard = pair.Key.GetComponentInParent<HairCardData>();
            if (ownerCard != null && ClipZoneGatherEffect.CurrentlyClippedZones.Contains(ownerCard.Section))
                continue;

            pair.Key.enabled = pair.Value;
        }
        hiddenRendererPreviousStates.Clear();
    }

    private bool IsZoneNamedObject(string objectName)
    {
        // Matches exact zone names ("Back") and Unity's duplicate-suffix pattern ("Back.001").
        int dotIndex = objectName.IndexOf('.');
        string baseName = dotIndex >= 0 ? objectName.Substring(0, dotIndex) : objectName;

        for (int i = 0; i < ZoneNamePrefixes.Length; i++)
        {
            if (baseName == ZoneNamePrefixes[i])
                return true;
        }

        return false;
    }

    private void CreateZoneLabels()
    {
        GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
        List<Transform> labelTransforms = new List<Transform>();

        foreach (var obj in allObjects)
        {
            if (obj.scene.IsValid() && obj.name.StartsWith("Label_"))
                labelTransforms.Add(obj.transform);
        }

        // Approximate head center as the average world position of all labels, used to compute
        // each label's own outward-facing direction so it isn't buried in the head/hair geometry.
        Vector3 headCenterApprox = Vector3.zero;
        for (int i = 0; i < labelTransforms.Count; i++)
            headCenterApprox += labelTransforms[i].position;
        if (labelTransforms.Count > 0)
            headCenterApprox /= labelTransforms.Count;

        foreach (var labelTransform in labelTransforms)
        {
            GameObject obj = labelTransform.gameObject;
            string zoneName = obj.name.Substring("Label_".Length);
            string displayName = InsertSpacesBeforeCapitals(zoneName);

            GameObject textObj = new GameObject("ZoneText_" + zoneName);
            textObj.transform.SetParent(obj.transform, false);

            Vector3 outwardWorldDirection = (obj.transform.position - headCenterApprox);
            outwardWorldDirection = outwardWorldDirection.sqrMagnitude > 0.0001f ? outwardWorldDirection.normalized : Vector3.up;
            Vector3 outwardLocalDirection = obj.transform.InverseTransformDirection(outwardWorldDirection);
            textObj.transform.localPosition = outwardLocalDirection * labelOutwardOffset;

            textObj.transform.localScale = Vector3.one * labelWorldScale;

            TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
            tmp.text = displayName;
            tmp.color = labelColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = true;
            tmp.overflowMode = TextOverflowModes.Overflow;
            // Constrain to a small box so long names wrap onto multiple lines and shrink to fit,
            // instead of stretching wide across neighboring zones.
            tmp.rectTransform.sizeDelta = new Vector2(18f, 10f);
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 8f;

            textObj.SetActive(false);
            spawnedLabels.Add(textObj);
        }

        Debug.Log("HeadZonePlayerToggle: created " + spawnedLabels.Count + " zone labels.");
    }

    private void BillboardLabels()
    {
        if (mainCameraCache == null)
            mainCameraCache = Camera.main;

        if (mainCameraCache == null)
            return;

        for (int i = 0; i < spawnedLabels.Count; i++)
        {
            if (spawnedLabels[i] == null)
                continue;

            spawnedLabels[i].transform.rotation = Quaternion.LookRotation(
                spawnedLabels[i].transform.position - mainCameraCache.transform.position);
        }
    }

    private string InsertSpacesBeforeCapitals(string input)
    {
        return Regex.Replace(input, "(?<!^)([A-Z])", " $1");
    }
}
