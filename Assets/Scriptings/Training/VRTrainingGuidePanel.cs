using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// HANDOFF NOTE (Phase 1 - Training Guide):
// New, self-contained script - does not modify VRTrainingMenuScreen or any other existing UI script.
// Builds its own small runtime World Space canvas, styled to roughly match VRTrainingMenuScreen's
// dark-panel look, and displays whatever TrainingGuideManager.GetCurrentStepInfo() currently returns.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Create an empty GameObject (e.g. "Training Guide Panel") and add this component.
// 2. Drag your TrainingGuideManager (the one from step 1 of TrainingGuideManager's own notes) into
//    the Training Guide Manager field.
// 3. Drag your existing HaircutManager into the Haircut Manager field (only used to show the haircut
//    name + Practice/Assessment mode at the top - purely display, not re-reading targets).
// 4. Optional: drag your player/XR camera (or leave empty to use Camera.main) into Player Camera,
//    so the panel can float in front of the player like VRTrainingMenuScreen does.
// 5. Press Play - the panel builds itself and shows the current step. Use the Next/Previous buttons,
//    or call ShowNextStep()/ShowPreviousStep() from a VR controller button binding.
[DisallowMultipleComponent]
public class VRTrainingGuidePanel : MonoBehaviour
{
    [Header("Required Scene Links")]
    [SerializeField] private TrainingGuideManager trainingGuideManager;
    [SerializeField] private HaircutManager haircutManager;

    [Header("Panel Placement")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform screenAnchor;
    [Tooltip("If true and no Screen Anchor is set, the panel places itself in front of the player once on Start.")]
    [SerializeField] private bool placeInFrontOfPlayerOnStart = true;
    [SerializeField] private float screenDistanceFromPlayer = 1.0f;
    [SerializeField] private float screenHeightOffset = 0.15f;
    [Tooltip("Suggested: keep this panel off to one side (e.g. left wrist / side monitor) so it does not block the mirror view.")]
    [SerializeField] private float worldScale = 0.0012f;
    [SerializeField] private Vector2 screenSize = new Vector2(760f, 620f);

    [Header("Behaviour")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool visibleOnStart = true;

    private readonly Color panelBlack = new Color(0.03f, 0.035f, 0.032f, 0.85f);
    private readonly Color buttonDark = new Color(0.08f, 0.085f, 0.08f, 0.9f);
    private readonly Color buttonGreen = new Color(0.04f, 0.29f, 0.18f, 0.95f);
    private readonly Color textWhite = new Color(0.96f, 0.96f, 0.94f, 1f);
    private readonly Color textMuted = new Color(0.65f, 0.66f, 0.63f, 1f);
    private readonly Color toolChipColor = new Color(0.15f, 0.32f, 0.5f, 0.95f);

    private GameObject panelRoot;
    private RectTransform contentRoot;

    private TMP_Text haircutNameLabel;
    private TMP_Text modeLabel;
    private TMP_Text stepCounterLabel;
    private TMP_Text zoneNameLabel;
    private TMP_Text toolLabel;
    private TMP_Text instructionLabel;
    private TMP_Text targetLengthLabel;
    private TMP_Text checkResultLabel;
    private TMP_Text cuttingMarkerButtonLabel;

    private void Start()
    {
        if (buildOnStart)
            Build();

        SetVisible(visibleOnStart);
        Refresh();
    }

    private void OnEnable()
    {
        if (panelRoot != null)
            Refresh();
    }

    [ContextMenu("Rebuild Panel")]
    public void Build()
    {
        if (panelRoot != null)
            return;

        EnsureEventSystem();

        panelRoot = new GameObject("VR Training Guide Panel");
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
        contentRoot = screenRect;

        BuildLayout();
        PositionPanel();
    }

    public void ShowNextStep()
    {
        trainingGuideManager?.NextStep();
        Refresh();
    }

    public void ShowPreviousStep()
    {
        trainingGuideManager?.PreviousStep();
        Refresh();
    }

    public void RunOptionalCheck()
    {
        if (trainingGuideManager == null)
            return;

        TrainingZoneCheckResult checkResult = trainingGuideManager.CheckCurrentZoneOptional();
        if (checkResultLabel != null)
            checkResultLabel.text = checkResult.Summary;
    }

    // 'Show Cutting Marker' cheat aid - draws a red line on every hair card at its target length so the
    // player can see exactly where to cut. Purely visual, does not affect cutting or scoring.
    public void ToggleCuttingMarkers()
    {
        if (trainingGuideManager == null)
            return;

        trainingGuideManager.SetCuttingMarkersVisible(!trainingGuideManager.CuttingMarkersVisible);
        RefreshCuttingMarkerLabel();
    }

    private void RefreshCuttingMarkerLabel()
    {
        if (cuttingMarkerButtonLabel == null || trainingGuideManager == null)
            return;

        cuttingMarkerButtonLabel.text = trainingGuideManager.CuttingMarkersVisible
            ? "Show Cutting Marker: On"
            : "Show Cutting Marker: Off";
    }

    // PRECAUTION (Reset Combing): one-shot button - snaps every comb-bent hair card back to its pre-comb
    // pose. Does NOT touch cut length. Safety net for if a card ever looks stuck instead of settling back
    // on its own after the comb moves away.
    public void ResetCombing()
    {
        if (trainingGuideManager == null)
            return;

        trainingGuideManager.ResetCombedHairToOriginalPose();

        if (checkResultLabel != null)
        {
            checkResultLabel.text = trainingGuideManager.CanResetCombing
                ? "Combing reset to original position."
                : "Reset Combing: no CuttingManager assigned.";
        }
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

    // Call this after RefreshStepsForActiveHaircut() on TrainingGuideManager (e.g. hook both into the
    // same 'On Start Training' UnityEvent on VRTrainingMenuScreen), or just call it any time you want
    // the panel text to catch up to the manager's current step.
    public void Refresh()
    {
        if (panelRoot == null || trainingGuideManager == null)
            return;

        if (haircutNameLabel != null && haircutManager != null)
            haircutNameLabel.text = ToDisplayName(haircutManager.ActiveHairstyle.ToString());

        if (modeLabel != null && haircutManager != null)
            modeLabel.text = haircutManager.TrainingMode == Stage1TrainingMode.Practice ? "Practice Mode" : "Assessment Mode";

        TrainingStepInfo step = trainingGuideManager.GetCurrentStepInfo();

        if (stepCounterLabel != null)
            stepCounterLabel.text = step.TotalSteps > 0 ? $"Step {step.StepNumber} / {step.TotalSteps}" : "No steps loaded";

        if (zoneNameLabel != null)
            zoneNameLabel.text = step.ZoneDisplayName;

        if (toolLabel != null)
            toolLabel.text = $"Tool: {step.Tool}";

        if (targetLengthLabel != null)
            targetLengthLabel.text = step.HasValidTarget ? $"Target: {step.TargetLengthDisplayText}" : "Target: -";

        if (instructionLabel != null)
            instructionLabel.text = step.Instruction;

        if (checkResultLabel != null)
            checkResultLabel.text = string.Empty;

        RefreshCuttingMarkerLabel();
    }

    private void BuildLayout()
    {
        GameObject panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(contentRoot, false);
        RectTransform panelRect = panelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = screenSize;
        panelRect.anchoredPosition = Vector2.zero;
        panelObject.AddComponent<Image>().color = panelBlack;

        float halfW = screenSize.x / 2f;
        float top = screenSize.y / 2f - 40f;

        haircutNameLabel = CreateLabel("Haircut Name", panelRect, new Vector2(0f, top), 30, textWhite, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 44f));
        modeLabel = CreateLabel("Mode", panelRect, new Vector2(0f, top - 36f), 20, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 30f));
        stepCounterLabel = CreateLabel("Step", panelRect, new Vector2(0f, top - 68f), 18, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 28f));

        zoneNameLabel = CreateLabel("Zone", panelRect, new Vector2(0f, top - 120f), 34, textWhite, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 48f));

        GameObject toolChip = new GameObject("Tool Chip");
        toolChip.transform.SetParent(panelRect, false);
        RectTransform toolChipRect = toolChip.AddComponent<RectTransform>();
        toolChipRect.anchorMin = new Vector2(0.5f, 0.5f);
        toolChipRect.anchorMax = new Vector2(0.5f, 0.5f);
        toolChipRect.pivot = new Vector2(0.5f, 0.5f);
        toolChipRect.sizeDelta = new Vector2(260f, 46f);
        toolChipRect.anchoredPosition = new Vector2(0f, top - 172f);
        toolChip.AddComponent<Image>().color = toolChipColor;
        toolLabel = CreateLabel("Tool", toolChipRect, Vector2.zero, 22, textWhite, TextAlignmentOptions.Center, new Vector2(240f, 40f));

        targetLengthLabel = CreateLabel("Target", panelRect, new Vector2(0f, top - 218f), 22, textWhite, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 30f));

        instructionLabel = CreateLabel("Instruction", panelRect, new Vector2(0f, top - 290f), 20, textWhite, TextAlignmentOptions.Center, new Vector2(screenSize.x - 80f, 130f));

        checkResultLabel = CreateLabel(string.Empty, panelRect, new Vector2(0f, top - 380f), 18, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 80f, 60f));

        float bottom = -(screenSize.y / 2f) + 55f;

        // Sits in its own row just above the Prev/Check/Next row so it doesn't crowd them.
        GameObject cuttingMarkerButtonObject = new GameObject("Show Cutting Marker Button");
        cuttingMarkerButtonObject.transform.SetParent(panelRect, false);
        RectTransform cuttingMarkerRect = cuttingMarkerButtonObject.AddComponent<RectTransform>();
        cuttingMarkerRect.anchorMin = new Vector2(0.5f, 0.5f);
        cuttingMarkerRect.anchorMax = new Vector2(0.5f, 0.5f);
        cuttingMarkerRect.pivot = new Vector2(0.5f, 0.5f);
        cuttingMarkerRect.sizeDelta = new Vector2(340f, 48f);
        cuttingMarkerRect.anchoredPosition = new Vector2(0f, bottom + 78f);
        cuttingMarkerButtonObject.AddComponent<Image>().color = buttonDark;
        Button cuttingMarkerButton = cuttingMarkerButtonObject.AddComponent<Button>();
        cuttingMarkerButton.onClick.AddListener(ToggleCuttingMarkers);
        cuttingMarkerButtonLabel = CreateLabel("Show Cutting Marker: Off", cuttingMarkerRect, Vector2.zero, 20, textWhite, TextAlignmentOptions.Center, new Vector2(320f, 38f));

        // PRECAUTION (Reset Combing): its own row above the cutting marker button, so all three rows
        // (Reset Combing / Show Cutting Marker / Prev-Check-Next) stack cleanly without crowding.
        CreateButton("Reset Combing", panelRect, new Vector2(340f, 48f), new Vector2(0f, bottom + 132f), buttonDark, ResetCombing, 20);

        CreateButton("< Prev", panelRect, new Vector2(160f, 64f), new Vector2(-190f, bottom), buttonDark, ShowPreviousStep, 24);
        CreateButton("Check This Zone", panelRect, new Vector2(300f, 64f), new Vector2(0f, bottom), buttonDark, RunOptionalCheck, 22);
        CreateButton("Next >", panelRect, new Vector2(160f, 64f), new Vector2(190f, bottom), buttonGreen, ShowNextStep, 24);
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
        textComponent.fontSizeMin = 12f;
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

        CreateLabel(label, rect, Vector2.zero, fontSize, textWhite, TextAlignmentOptions.Center, size - new Vector2(16f, 12f));
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
        {
            RecenterToPlayer();
            return;
        }

        panelRoot.transform.localPosition = Vector3.zero;
        panelRoot.transform.localRotation = Quaternion.identity;
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
        Vector3 position = cameraTransform.position + forward * Mathf.Max(0.25f, screenDistanceFromPlayer);
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
            EnsureUiInputModule(existingEventSystem.gameObject);
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        EnsureUiInputModule(eventSystemObject);
    }

    private void EnsureUiInputModule(GameObject eventSystemObject)
    {
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

    private string ToDisplayName(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(raw.Length + 8);
        builder.Append(raw[0]);

        for (int i = 1; i < raw.Length; i++)
        {
            if ((char.IsUpper(raw[i]) || raw[i] == '_') && raw[i - 1] != '_')
                builder.Append(raw[i] == '_' ? ' ' : ' ');

            if (raw[i] != '_')
                builder.Append(raw[i]);
        }

        return builder.ToString();
    }
}
