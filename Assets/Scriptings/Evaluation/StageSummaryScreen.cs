using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// HANDOFF NOTE:
// Shows combined results across all haircuts completed in an 'All Haircuts' chain (see
// HaircutChainManager). Same proven World Space architecture as HaircutResultScreen (same color palette,
// same CreateLabel/CreateButton/positioning pattern) - manually position this GameObject's Transform in
// the scene the same way (e.g. copy Training Guide Panel's Transform) rather than relying on dynamic
// camera-following, matching the proven-working setup established this session.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Create an empty GameObject (e.g. 'Stage Summary Screen'), add this component.
// 2. Drag this component into HaircutChainManager's 'Stage Summary Screen' field, and drag
//    HaircutChainManager into this component's 'Chain Manager' field (they reference each other).
// 3. Position this GameObject's Transform manually in the scene (near your other panels).
[DisallowMultipleComponent]
public class StageSummaryScreen : MonoBehaviour
{
    [Header("Required Scene Links")]
    [SerializeField] private HaircutChainManager chainManager;

    [Header("Panel Placement (same pattern as HaircutResultScreen)")]
    [SerializeField] private Transform playerCamera;
    [SerializeField] private Transform screenAnchor;
    [SerializeField] private bool placeInFrontOfPlayerOnStart = false;
    [SerializeField] private float screenDistanceFromPlayer = 1.0f;
    [SerializeField] private float screenHeightOffset = 0.15f;
    [SerializeField] private float worldScale = 0.0012f;
    [SerializeField] private Vector2 screenSize = new Vector2(820f, 760f);

    [Header("Behaviour")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool visibleOnStart = false;

    private readonly Color panelBlack = new Color(0.03f, 0.035f, 0.032f, 0.85f);
    private readonly Color buttonDark = new Color(0.08f, 0.085f, 0.08f, 0.9f);
    private readonly Color buttonGreen = new Color(0.04f, 0.29f, 0.18f, 0.95f);
    private readonly Color buttonBlue = new Color(0.09f, 0.22f, 0.38f, 0.95f);
    private readonly Color textWhite = new Color(0.96f, 0.96f, 0.94f, 1f);
    private readonly Color textMuted = new Color(0.65f, 0.66f, 0.63f, 1f);
    private readonly Color gradeGreen = new Color(0.16f, 0.55f, 0.29f, 0.95f);
    private readonly Color gradeBlue = new Color(0.15f, 0.32f, 0.5f, 0.95f);
    private readonly Color gradeAmber = new Color(0.55f, 0.4f, 0.08f, 0.95f);
    private readonly Color gradeRed = new Color(0.5f, 0.14f, 0.12f, 0.95f);

    private GameObject panelRoot;
    private RectTransform contentRoot;
    private RectTransform panelRect;
    private TMP_Text stageNameLabel;
    private TMP_Text overallGradeLabel;
    private Image overallGradeBadgeImage;
    private TMP_Text resultsListLabel;

    private void Start()
    {
        if (buildOnStart)
            Build();

        SetVisible(visibleOnStart);
    }

    [ContextMenu("Rebuild Panel")]
    public void Build()
    {
        if (panelRoot != null)
            return;

        EnsureEventSystem();

        panelRoot = new GameObject("Stage Summary Screen Panel");
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

    public void ShowSummary(string stageName, List<ChainResultEntry> results)
    {
        if (panelRoot == null)
            Build();

        if (stageNameLabel != null)
            stageNameLabel.text = stageName;

        float total = 0f;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < results.Count; i++)
        {
            var entry = results[i];
            total += entry.Score;
            sb.AppendLine($"{ToDisplayName(entry.Style.ToString())}: {entry.Grade}  ({entry.Score:0.#}%)");
        }

        float average = results.Count > 0 ? total / results.Count : 0f;
        string overallGrade = average >= 85f ? "A" : average >= 70f ? "B" : average >= 50f ? "C" : "F";

        if (overallGradeLabel != null)
            overallGradeLabel.text = $"Overall: {overallGrade}  ({average:0.#}%)";

        if (overallGradeBadgeImage != null)
            overallGradeBadgeImage.color = GetGradeColor(overallGrade);

        if (resultsListLabel != null)
            resultsListLabel.text = sb.ToString();

        SetVisible(true);
    }

    public void HideSummary() => SetVisible(false);

    // Called when the next stage doesn't have any haircuts configured yet (placeholder) - keeps this
    // screen visible but swaps its content to a clear 'not ready yet' message instead of silently doing
    // nothing, which was confusing to test against.
    public void ShowComingSoon(string stageName)
    {
        if (panelRoot == null)
            Build();

        if (stageNameLabel != null)
            stageNameLabel.text = stageName;

        if (overallGradeLabel != null)
            overallGradeLabel.text = "Coming Soon";

        if (overallGradeBadgeImage != null)
            overallGradeBadgeImage.color = buttonDark;

        if (resultsListLabel != null)
            resultsListLabel.text = "This stage hasn't been set up yet - check back later!";

        SetVisible(true);
    }

    public void SetVisible(bool isVisible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(isVisible);
    }

    public void OnRetryStageClicked()
    {
        PlayClickSound();
        chainManager?.RetryStage();
    }

    public void OnNextStageClicked()
    {
        PlayClickSound();
        chainManager?.GoToNextStage();
    }

    public void OnMainMenuClicked()
    {
        PlayClickSound();
        chainManager?.ReturnToMainMenu();
    }

    private void PlayClickSound()
    {
        if (UIAudioManager.Instance != null)
            UIAudioManager.Instance.PlayClickSound(transform.position);
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

    private void BuildLayout()
    {
        GameObject panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(contentRoot, false);
        panelRect = panelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = screenSize;
        panelRect.anchoredPosition = Vector2.zero;
        panelObject.AddComponent<Image>().color = panelBlack;

        float top = screenSize.y / 2f - 40f;

        CreateLabel("Stage Complete", panelRect, new Vector2(0f, top), 26, textMuted, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 34f));
        stageNameLabel = CreateLabel("Stage Name", panelRect, new Vector2(0f, top - 40f), 30, textWhite, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 44f));

        GameObject gradeBadge = new GameObject("Overall Grade Badge");
        gradeBadge.transform.SetParent(panelRect, false);
        RectTransform gradeBadgeRect = gradeBadge.AddComponent<RectTransform>();
        gradeBadgeRect.anchorMin = new Vector2(0.5f, 0.5f);
        gradeBadgeRect.anchorMax = new Vector2(0.5f, 0.5f);
        gradeBadgeRect.pivot = new Vector2(0.5f, 0.5f);
        gradeBadgeRect.sizeDelta = new Vector2(320f, 56f);
        gradeBadgeRect.anchoredPosition = new Vector2(0f, top - 98f);
        overallGradeBadgeImage = gradeBadge.AddComponent<Image>();
        overallGradeBadgeImage.color = gradeGreen;
        overallGradeLabel = CreateLabel("Overall", gradeBadgeRect, Vector2.zero, 24, textWhite, TextAlignmentOptions.Center, new Vector2(300f, 48f));

        resultsListLabel = CreateLabel("Results", panelRect, new Vector2(0f, -40f), 20, textWhite, TextAlignmentOptions.TopLeft, new Vector2(screenSize.x - 80f, 380f));

        float bottom = -(screenSize.y / 2f) + 55f;
        CreateButton("Retry Stage", panelRect, new Vector2(220f, 64f), new Vector2(-270f, bottom), buttonDark, OnRetryStageClicked, 18);
        CreateButton("Next Stage", panelRect, new Vector2(220f, 64f), new Vector2(0f, bottom), buttonGreen, OnNextStageClicked, 18);
        CreateButton("Main Menu", panelRect, new Vector2(220f, 64f), new Vector2(270f, bottom), buttonBlue, OnMainMenuClicked, 18);
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
