using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// HANDOFF NOTE (Phase 2 - Clipper Guard Training):
// New, self-contained script - a separate small HUD from VRTrainingGuidePanel (Phase 1), kept apart
// on purpose so each panel stays simple and Phase 1 did not need risky edits. Does not modify
// Clipper.cs, CuttingManager.cs, TrainingGuideManager.cs, or VRTrainingGuidePanel.cs.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Create an empty GameObject (e.g. "Clipper Training HUD") - works well parented to the clipper
//    tool itself, or to the player's off-hand, so it is always in view while clipping.
// 2. Add this component. Drag your ClipperGuardController, ClipperGuardScorer, and (optional)
//    ClipperMotionGuide into the matching fields.
// 3. Press Play. Use the < / > buttons to cycle guards, 'Check Guard' to score against the current
//    training step, and the bottom line updates automatically with upward-motion feedback if
//    ClipperMotionGuide is linked.
[DisallowMultipleComponent]
public class ClipperTrainingHud : MonoBehaviour
{
    [Header("Required / Optional Links")]
    [SerializeField] private ClipperGuardController guardController;
    [SerializeField] private ClipperGuardScorer guardScorer;
    [Tooltip("Optional. Leave empty if you are not using upward-motion guidance yet.")]
    [SerializeField] private ClipperMotionGuide motionGuide;

    [Header("Panel Placement")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform screenAnchor;
    [SerializeField] private bool placeInFrontOfPlayerOnStart = true;
    [SerializeField] private float screenDistanceFromPlayer = 0.55f;
    [SerializeField] private float screenHeightOffset = -0.15f;
    [SerializeField] private float worldScale = 0.0008f;
    [SerializeField] private Vector2 screenSize = new Vector2(520f, 300f);

    [Header("Behaviour")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool visibleOnStart = true;
    [Tooltip("Keep the panel facing the camera every frame, regardless of this object's parent rotation (e.g. when parented to the Clipper, which tilts in-hand). Position still follows the parent normally - only rotation is overridden.")]
    [SerializeField] private bool billboardToCamera = true;

    private readonly Color panelBlack = new Color(0.03f, 0.035f, 0.032f, 0.85f);
    private readonly Color buttonDark = new Color(0.08f, 0.085f, 0.08f, 0.9f);
    private readonly Color buttonGreen = new Color(0.04f, 0.29f, 0.18f, 0.95f);
    private readonly Color textWhite = new Color(0.96f, 0.96f, 0.94f, 1f);
    private readonly Color textMuted = new Color(0.65f, 0.66f, 0.63f, 1f);

    private GameObject panelRoot;
    private TMP_Text guardLabel;
    private TMP_Text guardCheckLabel;
    private TMP_Text motionLabel;

    private void Start()
    {
        if (buildOnStart)
            Build();

        SetVisible(visibleOnStart);
        RefreshGuardLabel();

        // Keep the guard label in sync no matter what changes the guard - VR stick input
        // (VRToolControlBindings) never called RefreshGuardLabel() directly, which is why the
        // label was staying stuck even though the guard itself was changing correctly.
        if (guardController != null)
            guardController.OnGuardChanged.AddListener(OnGuardControllerChanged);

        if (motionGuide != null)
            InvokeRepeating(nameof(PollMotionFeedback), 0.2f, 0.2f);
    }

    private void OnDestroy()
    {
        if (guardController != null)
            guardController.OnGuardChanged.RemoveListener(OnGuardControllerChanged);
    }

    private void OnGuardControllerChanged(float _)
    {
        RefreshGuardLabel();
    }

    private void LateUpdate()
    {
        if (!billboardToCamera || panelRoot == null)
            return;

        Transform cameraTransform = ResolveCameraTransform();

        if (cameraTransform == null)
            return;

        Vector3 directionToCamera = panelRoot.transform.position - cameraTransform.position;

        if (directionToCamera.sqrMagnitude <= 0.0001f)
            return;

        panelRoot.transform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
    }

    [ContextMenu("Rebuild HUD")]
    public void Build()
    {
        if (panelRoot != null)
            return;

        EnsureEventSystem();

        panelRoot = new GameObject("Clipper Training HUD");
        panelRoot.transform.SetParent(transform, false);
        panelRoot.transform.localScale = Vector3.one * Mathf.Max(0.0001f, worldScale);

        Canvas canvas = panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = ResolveCamera();
        panelRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        panelRoot.AddComponent<GraphicRaycaster>();
        AddOptionalComponent(panelRoot, "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit");

        RectTransform screenRect = canvas.GetComponent<RectTransform>();
        screenRect.sizeDelta = screenSize;

        GameObject panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(screenRect, false);
        RectTransform panelRectT = panelObject.AddComponent<RectTransform>();
        panelRectT.anchorMin = new Vector2(0.5f, 0.5f);
        panelRectT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRectT.pivot = new Vector2(0.5f, 0.5f);
        panelRectT.sizeDelta = screenSize;
        panelRectT.anchoredPosition = Vector2.zero;
        panelObject.AddComponent<Image>().color = panelBlack;

        float top = screenSize.y / 2f - 30f;

        CreateLabel("CLIPPER GUARD", panelRectT, new Vector2(0f, top), 16, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 40f, 26f));

        guardLabel = CreateLabel("1.5 cm", panelRectT, new Vector2(0f, top - 40f), 34, textWhite, TextAlignmentOptions.Center, new Vector2(220f, 46f));

        CreateButton("<", panelRectT, new Vector2(56f, 56f), new Vector2(-140f, top - 40f), buttonDark, () => { guardController?.CyclePreviousGuard(); RefreshGuardLabel(); }, 28);
        CreateButton(">", panelRectT, new Vector2(56f, 56f), new Vector2(140f, top - 40f), buttonDark, () => { guardController?.CycleNextGuard(); RefreshGuardLabel(); }, 28);

        // "Check Guard" button removed per request. guardCheckLabel/RunGuardCheck() are left in
        // place (unused) rather than deleted, in case guard-correctness scoring gets a new UI
        // trigger later - guardScorer and ClipperGuardScorer.cs are untouched either way.

        motionLabel = CreateLabel("", panelRectT, new Vector2(0f, -(screenSize.y / 2f) + 30f), 16, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 40f, 40f));

        PositionPanel();
    }

    public void RunGuardCheck()
    {
        if (guardScorer == null)
        {
            if (guardCheckLabel != null)
                guardCheckLabel.text = "No guard scorer linked.";
            return;
        }

        guardScorer.CheckGuardCorrectness(out string summary);

        if (guardCheckLabel != null)
            guardCheckLabel.text = summary;
    }

    public void RefreshGuardLabel()
    {
        if (guardLabel != null && guardController != null)
            guardLabel.text = $"{guardController.CurrentGuardCm:0.#} cm";
    }

    public void SetVisible(bool isVisible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(isVisible);
    }

    public void Toggle()
    {
        if (panelRoot != null)
            panelRoot.SetActive(!panelRoot.activeSelf);
    }

    // Public read-only surface for other scripts (e.g. VRToolControlBindings) that need to know
    // whether the HUD is currently showing, without needing their own reference to panelRoot.
    public bool IsVisible => panelRoot != null && panelRoot.activeSelf;

    private void PollMotionFeedback()
    {
        if (motionGuide == null || motionLabel == null)
            return;

        if (!string.IsNullOrEmpty(motionGuide.LatestFeedback))
            motionLabel.text = motionGuide.LatestFeedback;
    }

    private TMP_Text CreateLabel(string text, RectTransform parent, Vector2 position, int fontSize, Color color, TextAlignmentOptions alignment, Vector2 size)
    {
        GameObject labelObject = new GameObject("Text");
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        TextMeshProUGUI textComponent = labelObject.AddComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.color = color;
        textComponent.alignment = alignment;
        textComponent.fontSize = fontSize;
        textComponent.enableAutoSizing = true;
        textComponent.fontSizeMax = fontSize;
        textComponent.fontSizeMin = 10f;
        textComponent.enableWordWrapping = true;
        textComponent.raycastTarget = false;

        return textComponent;
    }

    private void CreateButton(string label, RectTransform parent, Vector2 size, Vector2 position, Color color, UnityEngine.Events.UnityAction action, int fontSize)
    {
        GameObject buttonObject = new GameObject(label + " Button");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        buttonObject.AddComponent<Image>().color = color;
        Button button = buttonObject.AddComponent<Button>();

        if (action != null)
            button.onClick.AddListener(action);

        CreateLabel(label, rect, Vector2.zero, fontSize, textWhite, TextAlignmentOptions.Center, size - new Vector2(12f, 10f));
    }

    private void PositionPanel()
    {
        if (panelRoot == null)
            return;

        if (screenAnchor != null)
        {
            panelRoot.transform.SetPositionAndRotation(screenAnchor.position, screenAnchor.rotation);
            return;
        }

        if (placeInFrontOfPlayerOnStart)
            RecenterToPlayer();
    }

    public void RecenterToPlayer()
    {
        Transform cameraTransform = ResolveCameraTransform();

        if (cameraTransform == null || panelRoot == null)
            return;

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up);

        if (forward.sqrMagnitude <= 0.0001f)
            forward = cameraTransform.forward;

        forward.Normalize();
        Vector3 position = cameraTransform.position + forward * Mathf.Max(0.2f, screenDistanceFromPlayer);
        position.y += screenHeightOffset;
        panelRoot.transform.position = position;
        panelRoot.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    private Camera ResolveCamera()
    {
        Transform cameraTransform = ResolveCameraTransform();
        return cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
    }

    private Transform ResolveCameraTransform()
    {
        if (playerCamera != null)
            return playerCamera;

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private void EnsureEventSystem()
    {
        EventSystem existingEventSystem = FindAnyObjectByType<EventSystem>();

        if (existingEventSystem != null)
        {
            AddOptionalComponent(existingEventSystem.gameObject, "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        AddOptionalComponent(eventSystemObject, "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
    }

    private bool AddOptionalComponent(GameObject target, string typeName)
    {
        System.Type type = System.Type.GetType(typeName);

        if (type == null || target.GetComponent(type) != null)
            return type != null;

        target.AddComponent(type);
        return true;
    }
}
