using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// HANDOFF NOTE:
// Rebuilt to match VRTrainingGuidePanel's exact proven-working architecture (World Space canvas, same
// field structure/positioning logic) instead of the earlier Screen Space Overlay attempt - that attempt
// was based on an unconfirmed theory about Camera.main, which VRTrainingGuidePanel's own working config
// disproved (it works via a manually-positioned Transform, not dynamic camera-following).
//
// HOW TO WIRE THIS UP IN UNITY (mirrors VRTrainingGuidePanel's own setup):
// 1. Create an empty GameObject (e.g. 'Haircut Result Screen') and add this component.
// 2. Drag EvaluationSystem + HaircutManager into their fields.
// 3. IMPORTANT: Place In Front Of Player On Start defaults to FALSE here (matching your proven-working
//    Training Guide Panel config) - manually position this GameObject's Transform somewhere sensible in
//    the scene (e.g. near/next to your Training Guide Panel's own Transform) rather than relying on
//    dynamic camera-following, since that path hasn't been confirmed working in this project yet.
[DisallowMultipleComponent]
public class HaircutResultScreen : MonoBehaviour
{
    [Header("Required Scene Links")]
    [SerializeField] private EvaluationSystem evaluationSystem;
    [SerializeField] private HaircutManager haircutManager;
    [Tooltip("Optional. If assigned and a chain is active, End Haircut Session advances to the next haircut instead of just hiding.")]
    [SerializeField] private HaircutChainManager chainManager;

    [Header("Panel Placement")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform screenAnchor;
    [Tooltip("Defaults OFF here, matching your proven-working Training Guide Panel setup - manually position this GameObject's Transform instead.")]
    [SerializeField] private bool placeInFrontOfPlayerOnStart = false;
    [SerializeField] private float screenDistanceFromPlayer = 1.0f;
    [SerializeField] private float screenHeightOffset = 0.15f;
    [SerializeField] private float worldScale = 0.0012f;
    [SerializeField] private Vector2 screenSize = new Vector2(820f, 900f);
    [Tooltip("Font size for the 20-zone breakdown text. Lower this if the list looks too large/cramped for how much content there is.")]
    [SerializeField] private int summaryFontSize = 16;
    [Tooltip("Minimum font size the summary text is allowed to auto-shrink to if it doesn't fit the box.")]
    [SerializeField] private int summaryFontSizeMin = 10;

    [Header("Behaviour")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool visibleOnStart = true;
    [SerializeField] private bool evaluateWithKeyboardKey = true;
    [Tooltip("If true, automatically refreshes the grade/zone breakdown while this panel is visible, so the player can watch their progress update live while cutting instead of only seeing a snapshot from when training started (or after pressing Reset Haircut).")]
    [SerializeField] private bool liveUpdateWhileVisible = true;
    [Tooltip("How many frames to wait between automatic live-update refreshes (only while Live Update While Visible is on and the panel is actually visible). Lower = more responsive but more expensive - each refresh scans every hair card in the scene. Higher = cheaper but choppier. 30 is roughly twice a second at 60fps, a reasonable VR-friendly default.")]
    [SerializeField, Min(1)] private int framesPerLiveUpdate = 30;

    private readonly Color panelBlack = new Color(0.03f, 0.035f, 0.032f, 0.85f);
    private readonly Color buttonDark = new Color(0.08f, 0.085f, 0.08f, 0.9f);
    private readonly Color buttonGreen = new Color(0.04f, 0.29f, 0.18f, 0.95f);
    private readonly Color textWhite = new Color(0.96f, 0.96f, 0.94f, 1f);
    private readonly Color textMuted = new Color(0.65f, 0.66f, 0.63f, 1f);
    private readonly Color gradeGreen = new Color(0.16f, 0.55f, 0.29f, 0.95f);
    private readonly Color gradeBlue = new Color(0.15f, 0.32f, 0.5f, 0.95f);
    private readonly Color gradeAmber = new Color(0.55f, 0.4f, 0.08f, 0.95f);
    private readonly Color gradeRed = new Color(0.5f, 0.14f, 0.12f, 0.95f);

    private GameObject panelRoot;
    private RectTransform contentRoot;
    private TMP_Text haircutNameLabel;
    private TMP_Text modeLabel;
    private TMP_Text gradeLabel;
    private Image gradeBadgeImage;
    private TMP_Text summaryLabel;
    private int liveUpdateFrameCounter;

    private void Start()
    {
        if (buildOnStart)
            Build();

        SetVisible(visibleOnStart);
    }

    private void Update()
    {
        if (evaluateWithKeyboardKey && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            ShowResults();

        if (liveUpdateWhileVisible && panelRoot != null && panelRoot.activeInHierarchy)
        {
            liveUpdateFrameCounter++;

            if (liveUpdateFrameCounter >= Mathf.Max(1, framesPerLiveUpdate))
            {
                liveUpdateFrameCounter = 0;
                ShowResults();
            }
        }
    }

    [ContextMenu("Rebuild Panel")]
    public void Build()
    {
        if (panelRoot != null)
            return;

        EnsureEventSystem();

        panelRoot = new GameObject("Haircut Result Screen Panel");
        panelRoot.transform.SetParent(transform, false);
        panelRoot.transform.localScale = Vector3.one * Mathf.Max(0.0001f, worldScale);

        Canvas canvas = panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = ResolveCamera();
        panelRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        panelRoot.AddComponent<GraphicRaycaster>();
        AddOptionalComponent(panelRoot, "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit"); // needed for VR controller ray interaction, GraphicRaycaster alone only supports mouse

        RectTransform screenRect = canvas.GetComponent<RectTransform>();
        screenRect.sizeDelta = screenSize;
        contentRoot = screenRect;

        BuildLayout();
        PositionPanel();
    }

    public void ShowResults()
    {
        if (panelRoot == null)
            Build();

        if (evaluationSystem == null)
        {
            if (summaryLabel != null) summaryLabel.text = "Evaluation System is not assigned.";
            SetVisible(true);
            return;
        }

        evaluationSystem.EvaluateActiveHaircut();

        string haircutName = haircutManager != null ? ToDisplayName(haircutManager.ActiveHairstyle.ToString()) : "Haircut";
        string modeName = haircutManager != null ? haircutManager.TrainingMode.ToString() : "Unknown";
        string grade = GetGrade(evaluationSystem.finalScore);

        if (haircutNameLabel != null) haircutNameLabel.text = haircutName;
        if (modeLabel != null) modeLabel.text = modeName == "Practice" ? "Practice Mode" : (modeName == "Assessment" ? "Assessment Mode" : modeName);
        if (gradeLabel != null) gradeLabel.text = $"Grade: {grade}  ({evaluationSystem.finalScore:0.#}%)";
        if (gradeBadgeImage != null) gradeBadgeImage.color = GetGradeColor(grade);
        if (summaryLabel != null) summaryLabel.text = BuildDetailedZoneSummary();

        SetVisible(true);
    }

    public void HideResults() => SetVisible(false);

    // Builds a genuine per-zone breakdown across all 20 real zones (not just the 7 broad categories
    // EvaluationSystem.GetStage1ResultSummary() groups into).
    //
    // FIX ("per-zone breakdown shows nonzero Correct counts for a zone with a Required Stubble Style set,
    // even though the overall Grade is correctly F/0% and no matching stubble exists in the scene"): this
    // method used to keep its OWN copy of the correct/too-long/too-short comparison (plain CurrentLength
    // vs targetLength +/- tolerance, recomputed straight from HairCardData here), completely bypassing
    // EvaluationSystem.TryGetStubbleRequirementResult. That meant every stubble-requirement fix made to
    // EvaluationSystem.GetLengthState this session (checking StubbleAreaSwapController's actually-achieved
    // Crew/Butch/Induction finish before falling back to numeric comparison) never applied here - an
    // un-swapped (Crew) card's CurrentLength sits at true physical 0, which coincidentally matches a
    // 0-length Induction target, so this duplicate logic kept reading those cards as "Correct" by
    // coincidence even after EvaluationSystem itself was fixed. Rather than keep two copies of this
    // comparison in sync, this now reads the per-section tallies EvaluationSystem already computes (via
    // the exact same GetLengthState call, once per card) during EvaluateActiveHaircut() - called just
    // above in ShowResults() before this method runs - so there is exactly one place this logic lives.
    private string BuildDetailedZoneSummary()
    {
        if (evaluationSystem == null)
            return "Evaluation System not assigned.";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        foreach (HairSectionType section in evaluationSystem.GetEvaluatedStage1Sections())
        {
            if (!evaluationSystem.TryGetStage1SectionTally(section, out int correct, out int tooLong, out int tooShort))
                continue;

            int total = correct + tooLong + tooShort;
            float pct = total > 0 ? (correct / (float)total) * 100f : 0f;
            sb.AppendLine($"{section}: {pct:0.#}% ({correct}/{total}) Long: {tooLong}, Short: {tooShort}");
        }

        return sb.ToString();
    }

    public void ResetHaircut()
    {
        PlayClickSound();

        if (haircutManager != null)
            haircutManager.RestoreOriginalHairLength();

        ShowResults();
    }

    public void EndHaircutSession()
    {
        PlayClickSound();

        if (chainManager != null && chainManager.IsChainActive)
        {
            chainManager.AdvanceChain();
            return;
        }

        if (chainManager != null)
            chainManager.ReturnToMainMenu();
        else
            HideResults();
    }

    private void PlayClickSound()
    {
        if (UIAudioManager.Instance != null)
            UIAudioManager.Instance.PlayClickSound(transform.position);
    }

    public void SetVisible(bool isVisible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(isVisible);
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

        float top = screenSize.y / 2f - 40f;

        CreateLabel("Haircut Result Progress", panelRect, new Vector2(0f, top), 26, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 34f));
        haircutNameLabel = CreateLabel("Haircut Name", panelRect, new Vector2(0f, top - 40f), 32, textWhite, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 46f));
        modeLabel = CreateLabel("Mode", panelRect, new Vector2(0f, top - 78f), 20, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 30f));

        GameObject gradeBadge = new GameObject("Grade Badge");
        gradeBadge.transform.SetParent(panelRect, false);
        RectTransform gradeBadgeRect = gradeBadge.AddComponent<RectTransform>();
        gradeBadgeRect.anchorMin = new Vector2(0.5f, 0.5f);
        gradeBadgeRect.anchorMax = new Vector2(0.5f, 0.5f);
        gradeBadgeRect.pivot = new Vector2(0.5f, 0.5f);
        gradeBadgeRect.sizeDelta = new Vector2(300f, 56f);
        gradeBadgeRect.anchoredPosition = new Vector2(0f, top - 138f);
        gradeBadgeImage = gradeBadge.AddComponent<Image>();
        gradeBadgeImage.color = gradeGreen;
        gradeLabel = CreateLabel("Grade", gradeBadgeRect, Vector2.zero, 26, textWhite, TextAlignmentOptions.Center, new Vector2(280f, 48f));

        // Box CENTER sits at anchoredPosition even with TopLeft text alignment - recalculated so the
        // box's actual top edge sits safely below the grade badge, and its bottom edge sits safely above
        // the buttons, instead of guessing an offset that overlapped the header above it.
        summaryLabel = CreateLabel("Summary", panelRect, new Vector2(0f, -56f), summaryFontSize, textWhite, TextAlignmentOptions.TopLeft, new Vector2(screenSize.x - 80f, 560f));
        summaryLabel.fontSizeMin = summaryFontSizeMin;

        float bottom = -388f;
        CreateButton("Reset Haircut", panelRect, new Vector2(240f, 64f), new Vector2(-140f, bottom), buttonDark, ResetHaircut, 20);
        CreateButton("End Haircut Session", panelRect, new Vector2(300f, 64f), new Vector2(180f, bottom), buttonGreen, EndHaircutSession, 20);
    }

    // 6-step gradient from the F color (gradeRed) to the A color (gradeGreen) - each improving
    // grade sits proportionally further along the same red->dark-green transition, instead of
    // jumping between unrelated fixed swatches. gradeBlue/gradeAmber are no longer used here but
    // left declared rather than removed.
    private Color GetGradeColor(string grade)
    {
        float t;

        switch (grade)
        {
            case "A": t = 1.0f; break;
            case "B": t = 0.8f; break;
            case "C": t = 0.6f; break;
            case "D": t = 0.4f; break;
            case "E": t = 0.2f; break;
            case "F":
            default: t = 0f; break;
        }

        return Color.Lerp(gradeRed, gradeGreen, t);
    }

    // F < 20%, E 20-40%, D 40-60%, C 60-70%, B 70-80%, A 80-100%.
    private string GetGrade(float score)
    {
        if (score >= 80f) return "A";
        if (score >= 70f) return "B";
        if (score >= 60f) return "C";
        if (score >= 40f) return "D";
        if (score >= 20f) return "E";
        return "F";
    }

    private string ToDisplayName(string rawEnumName)
    {
        if (string.IsNullOrEmpty(rawEnumName)) return rawEnumName;
        string cleaned = rawEnumName.Contains("_") ? rawEnumName.Substring(rawEnumName.IndexOf('_') + 1) : rawEnumName;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (char c in cleaned)
        {
            if (char.IsUpper(c) && sb.Length > 0) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
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
        if (action != null) button.onClick.AddListener(action);
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
            return;

        // Use the new Input System's UI module, not the legacy StandaloneInputModule - this project has
        // Active Input Handling set to Input System Package only, so the legacy module throws every frame.
        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
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
