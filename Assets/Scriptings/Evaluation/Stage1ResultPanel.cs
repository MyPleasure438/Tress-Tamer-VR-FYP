using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// HANDOFF NOTE:
// Result UI - restyled to match VRTrainingGuidePanel/VRTrainingMenuScreen's visual language (same dark
// panel color palette, same CreateLabel/CreateButton/world-space-canvas pattern). Builds its own polished
// UI at runtime instead of relying on separately-authored scene TMP text objects. All existing public API
// (ShowResults/HideResults/ToggleResults) and the evaluationSystem/stage1HaircutManager/feedbackVisualizer
// field names are unchanged, so existing scene wiring/events pointing at this component keep working with
// no re-wiring needed. Legacy resultText/legacyResultText fields (if still assigned from before) are still
// updated too, purely as a harmless fallback - the new built panel is what actually displays now.
public class Stage1ResultPanel : MonoBehaviour
{
    [Header("Links")]
    [SerializeField] private EvaluationSystem evaluationSystem;
    [SerializeField] private HaircutManager stage1HaircutManager;
    [SerializeField] private Stage1HairFeedbackVisualizer feedbackVisualizer;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Text legacyResultText;

    [Header("Panel Placement")]
    [SerializeField] private float worldScale = 0.0016f;
    [SerializeField] private Vector2 screenSize = new Vector2(720f, 560f);
    [Tooltip("Optional. If assigned, the panel snaps to this Transform's position/rotation once at Start instead of auto-placing in front of the player.")]
    [SerializeField] private Transform screenAnchor;
    [SerializeField] private bool placeInFrontOfPlayerOnStart = true;
    [SerializeField] private float screenDistanceFromPlayer = 0.6f;
    [SerializeField] private float screenHeightOffset = -0.05f;
    [SerializeField] private Transform playerCamera;

    [Header("Testing")]
    [SerializeField] private bool hideOnStart = true;
    [SerializeField] private bool evaluateWhenPanelIsEnabled = true;
    [SerializeField] private bool showFeedbackColorsWithResults = true;
    [SerializeField] private bool evaluateWithKeyboardKey = true;
    [SerializeField] private KeyCode evaluateKey = KeyCode.Return;

    // Same color palette as VRTrainingGuidePanel/VRTrainingMenuScreen, for visual consistency.
    private readonly Color panelBlack = new Color(0.03f, 0.035f, 0.032f, 0.85f);
    private readonly Color buttonDark = new Color(0.08f, 0.085f, 0.08f, 0.9f);
    private readonly Color buttonGreen = new Color(0.04f, 0.29f, 0.18f, 0.95f);
    private readonly Color textWhite = new Color(0.96f, 0.96f, 0.94f, 1f);
    private readonly Color textMuted = new Color(0.65f, 0.66f, 0.63f, 1f);
    private readonly Color gradeGreen = new Color(0.16f, 0.55f, 0.29f, 0.95f);
    private readonly Color gradeBlue = new Color(0.15f, 0.32f, 0.5f, 0.95f);
    private readonly Color gradeAmber = new Color(0.55f, 0.4f, 0.08f, 0.95f);
    private readonly Color gradeRed = new Color(0.5f, 0.14f, 0.12f, 0.95f);

    private RectTransform contentRoot;
    private GameObject builtPanel;
    private TMP_Text titleLabel;
    private TMP_Text haircutNameLabel;
    private TMP_Text modeLabel;
    private TMP_Text gradeLabel;
    private Image gradeBadgeImage;
    private TMP_Text summaryLabel;

    private void Start()
    {
        if (panelRoot == null)
            Build();

        if (hideOnStart)
            HideResults();
    }

    private void OnEnable()
    {
        if (Application.isPlaying && evaluateWhenPanelIsEnabled && panelRoot != null)
            ShowResults();
    }

    private void Update()
    {
        if (evaluateWithKeyboardKey && WasEvaluateKeyPressed())
            ShowResults();
    }

    [ContextMenu("Rebuild Panel")]
    public void Build()
    {
        EnsureEventSystem();

        panelRoot = new GameObject("Stage1 Result Panel");
        panelRoot.transform.SetParent(transform, false);
        panelRoot.transform.localScale = Vector3.one * Mathf.Max(0.0001f, worldScale);

        Canvas canvas = panelRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = ResolveCamera();
        panelRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
        panelRoot.AddComponent<GraphicRaycaster>();

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
            SetSummaryText("Evaluation System is not assigned.");
            ShowPanel();
            return;
        }

        evaluationSystem.EvaluateActiveHaircut();

        string haircutName = stage1HaircutManager != null ? ToDisplayName(stage1HaircutManager.ActiveHairstyle.ToString()) : "Haircut";
        string modeName = stage1HaircutManager != null ? stage1HaircutManager.TrainingMode.ToString() : "Unknown";
        string grade = GetGrade(evaluationSystem.finalScore);
        string summary = evaluationSystem.GetStage1ResultSummary();

        if (haircutNameLabel != null) haircutNameLabel.text = haircutName;
        if (modeLabel != null) modeLabel.text = modeName == "Practice" ? "Practice Mode" : (modeName == "Assessment" ? "Assessment Mode" : modeName);
        if (gradeLabel != null) gradeLabel.text = $"Grade: {grade}  ({evaluationSystem.finalScore:0.#}%)";
        if (gradeBadgeImage != null) gradeBadgeImage.color = GetGradeColor(grade);
        SetSummaryText(summary);

        if (showFeedbackColorsWithResults && feedbackVisualizer != null)
            feedbackVisualizer.ShowFeedback();

        ShowPanel();
    }

    public void HideResults()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    public void ToggleResults()
    {
        if (panelRoot == null || !panelRoot.activeSelf)
            ShowResults();
        else
            HideResults();
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
        builtPanel = panelObject;

        float top = screenSize.y / 2f - 40f;

        titleLabel = CreateLabel("Haircut Complete", panelRect, new Vector2(0f, top), 26, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 34f));
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

        summaryLabel = CreateLabel("Summary", panelRect, new Vector2(0f, top - 230f), 18, textWhite, TextAlignmentOptions.TopLeft, new Vector2(screenSize.x - 80f, 260f));

        float bottom = -(screenSize.y / 2f) + 55f;
        CreateButton("Try Again", panelRect, new Vector2(220f, 64f), new Vector2(-130f, bottom), buttonDark, ShowResults, 22);
        CreateButton("Close", panelRect, new Vector2(220f, 64f), new Vector2(130f, bottom), buttonGreen, HideResults, 22);
    }

    private Color GetGradeColor(string grade)
    {
        switch (grade)
        {
            case "A": return gradeGreen;
            case "B": return gradeBlue;
            case "C": return gradeAmber;
            default: return gradeRed;
        }
    }

    private void SetSummaryText(string value)
    {
        if (summaryLabel != null) summaryLabel.text = value;
        if (resultText != null) resultText.text = value;
        if (legacyResultText != null) legacyResultText.text = value;
    }

    private void ShowPanel()
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    private string GetGrade(float score)
    {
        if (score >= 85f) return "A";
        if (score >= 70f) return "B";
        if (score >= 50f) return "C";
        return "F";
    }

    private string ToDisplayName(string rawEnumName)
    {
        if (string.IsNullOrEmpty(rawEnumName))
            return rawEnumName;

        string cleaned = rawEnumName.Contains("_") ? rawEnumName.Substring(rawEnumName.IndexOf('_') + 1) : rawEnumName;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (char c in cleaned)
        {
            if (char.IsUpper(c) && sb.Length > 0)
                sb.Append(' ');
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
            return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }

    private bool WasEvaluateKeyPressed()
    {
        if (Keyboard.current == null)
            return false;

        switch (evaluateKey)
        {
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                return Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame;
            case KeyCode.Space:
                return Keyboard.current.spaceKey.wasPressedThisFrame;
            case KeyCode.E:
                return Keyboard.current.eKey.wasPressedThisFrame;
            case KeyCode.R:
                return Keyboard.current.rKey.wasPressedThisFrame;
            default:
                return false;
        }
    }
}
