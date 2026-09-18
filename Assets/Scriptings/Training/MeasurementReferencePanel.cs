using TMPro;
using UnityEngine;

// HANDOFF NOTE (Phase 1 - Training Guide):
// New, self-contained script. Builds a simple 'what does 1 cm actually look like' reference next to
// the player - a small row of true-to-scale bars (0.5/1/2/3 cm by default) with a label over each one.
// This is intentionally simple (per the brief: 'do not overbuild physical ruler mechanics yet') -
// no physics, no grabbing, just static true-scale visual reference the player can glance at or hold
// their tool up next to. Nothing here touches Clipper/Comb/Scissor/HaircutManager.
//
// HOW TO USE IN UNITY:
// 1. Create an empty GameObject (e.g. "Measurement Reference Panel") positioned somewhere convenient -
//    e.g. on a small side table/tray near the player, or parented to the player's off-hand controller
//    so it's always available at arm's reach.
// 2. Add this component. Default reference lengths (0.5/1/2/3 cm) are already filled in - edit the
//    Reference Lengths Cm array in the Inspector if you want different values.
// 3. Press Play - the bars + labels build themselves as children of this GameObject.
// 4. Optional: call Show()/Hide()/Toggle() from a VR menu button if you don't want it visible at all times.
[DisallowMultipleComponent]
public class MeasurementReferencePanel : MonoBehaviour
{
    [Header("Reference Lengths")]
    [Tooltip("Each value is one reference bar, in centimeters. True-to-scale in world space - a 1 cm entry is really 1 cm long in VR.")]
    [SerializeField] private float[] referenceLengthsCm = { 0.5f, 1f, 2f, 3f };

    [Header("Layout")]
    [Tooltip("Horizontal gap between bars, in meters.")]
    [SerializeField] private float spacingMeters = 0.06f;
    [SerializeField] private float barThicknessMeters = 0.004f;
    [SerializeField] private Color barColor = new Color(0.9f, 0.75f, 0.2f, 1f);
    [Tooltip("World-space text size for the cm labels above each bar. Small because this sits close to the player - tune in-headset.")]
    [SerializeField] private float labelWorldFontSize = 0.02f;

    [Header("Behaviour")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool visibleOnStart = true;

    private GameObject builtRoot;

    private void Start()
    {
        if (buildOnStart)
            Build();

        SetVisible(visibleOnStart);
    }

    [ContextMenu("Rebuild Reference Bars")]
    public void Build()
    {
        if (builtRoot != null)
        {
            if (Application.isPlaying)
                Destroy(builtRoot);
            else
                DestroyImmediate(builtRoot);
        }

        builtRoot = new GameObject("Reference Bars");
        builtRoot.transform.SetParent(transform, false);

        if (referenceLengthsCm == null || referenceLengthsCm.Length == 0)
            return;

        float totalWidth = (referenceLengthsCm.Length - 1) * spacingMeters;
        float startX = -totalWidth / 2f;

        for (int i = 0; i < referenceLengthsCm.Length; i++)
        {
            float lengthMeters = Mathf.Max(0.0001f, referenceLengthsCm[i] / 100f);
            float xPosition = startX + i * spacingMeters;
            CreateBar(lengthMeters, referenceLengthsCm[i], new Vector3(xPosition, 0f, 0f));
        }
    }

    public void SetVisible(bool isVisible)
    {
        if (builtRoot != null)
            builtRoot.SetActive(isVisible);
    }

    public void Toggle()
    {
        if (builtRoot != null)
            builtRoot.SetActive(!builtRoot.activeSelf);
    }

    private void CreateBar(float lengthMeters, float displayCm, Vector3 localPosition)
    {
        // A thin cube stretched to the exact real-world length - simplest possible true-scale reference.
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = $"Reference Bar {displayCm:0.#}cm";
        bar.transform.SetParent(builtRoot.transform, false);
        bar.transform.localPosition = localPosition + Vector3.up * (lengthMeters / 2f);
        bar.transform.localScale = new Vector3(barThicknessMeters, lengthMeters, barThicknessMeters);

        Collider barCollider = bar.GetComponent<Collider>();
        if (barCollider != null)
            Object.DestroyImmediate(barCollider);

        Renderer barRenderer = bar.GetComponent<Renderer>();
        if (barRenderer != null)
            barRenderer.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = barColor };

        GameObject labelObject = new GameObject($"Label {displayCm:0.#}cm");
        labelObject.transform.SetParent(builtRoot.transform, false);
        labelObject.transform.localPosition = localPosition + Vector3.up * (lengthMeters + 0.02f);
        labelObject.transform.localScale = Vector3.one * labelWorldFontSize;

        TextMeshPro label = labelObject.AddComponent<TextMeshPro>();
        label.text = $"{displayCm:0.#} cm";
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
    }
}
