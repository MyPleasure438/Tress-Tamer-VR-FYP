using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// HANDOFF NOTE (Haircut Trivia Panel):
// New, self-contained script - does not modify VRTrainingMenuScreen, TrainingGuideManager,
// ReferenceImageRowPanel, or any other existing script (only READS
// VRTrainingMenuScreen.OnStartTrainingEvent and HaircutManager.ActiveHairstyle). Same overall pattern as
// ReferenceImageRowPanel.cs, just a different content shape: an optional image ABOVE a block of trivia
// text, instead of a row of 5 images. Perfectly fine to run both panels side by side in the same scene.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Create an empty GameObject (e.g. "Haircut Trivia Panel") and add this component.
// 2. Drag your existing HaircutManager into the Haircut Manager field.
// 3. Drag your existing VRTrainingMenuScreen into the Menu Screen field - this makes the panel show itself
//    automatically the instant Begin Training is pressed. Leave empty if you'd rather call
//    RefreshForActiveHaircut()/Show() yourself from somewhere else.
// 4. Trivia Entries is pre-populated with one entry per HaircutStyle (15 total) the first time you add this
//    component - just expand each entry, write its Trivia Text, and optionally assign an Image. Leave Image
//    empty for that haircut and the text simply starts higher up instead of leaving a gap.
// 5. Tune Content Offset / Text Box Size / Text Font Size / Text Alignment under "Text Layout" to
//    reposition, resize, and align the trivia text. Tune Screen Anchor / Screen Distance From Player /
//    Screen Height Offset to place the whole panel in-world.
[DisallowMultipleComponent]
public class HaircutTriviaPanel : MonoBehaviour
{
    [Serializable]
    public class HaircutTriviaEntry
    {
        public HaircutStyle Haircut;
        [Tooltip("Optional image shown above the trivia text for this haircut. Leave empty for no image - the text starts higher up instead of leaving a gap where the image would have been.")]
        public Sprite Image;
        [Tooltip("The trivia/fact text shown for this haircut.")]
        [TextArea(3, 12)]
        public string TriviaText = string.Empty;
    }

    [Header("Required Scene Links")]
    [Tooltip("Your existing HaircutManager - used to read which of the 15 haircuts is currently active so the matching trivia entry is shown.")]
    [SerializeField] private HaircutManager haircutManager;
    [Tooltip("Optional. Your existing VRTrainingMenuScreen - if assigned, this panel shows itself automatically the moment Begin Training is pressed (hooks its Start Training event). Leave empty and call RefreshForActiveHaircut()/Show() yourself if you'd rather wire it another way.")]
    [SerializeField] private VRTrainingMenuScreen menuScreen;

    [Header("Panel Placement")]
    [SerializeField] private Transform playerCamera;
    [Tooltip("If assigned, the panel always sits exactly at this transform's position/rotation instead of re-centering on the player.")]
    [SerializeField] private Transform screenAnchor;
    [Tooltip("If true and no Screen Anchor is set, the panel re-centers itself in front of the player every time it's shown (e.g. every time a new stage starts).")]
    [SerializeField] private bool placeInFrontOfPlayerOnShow = true;
    [SerializeField] private float screenDistanceFromPlayer = 1.6f;
    [SerializeField] private float screenHeightOffset = 0.35f;
    [SerializeField] private float worldScale = 0.0015f;
    [Tooltip("Overall size of the panel itself, in UI pixels - resizes the whole trivia UI (background + everything on it).")]
    [SerializeField] private Vector2 screenSize = new Vector2(700f, 820f);

    [Header("Image (optional, sits above the trivia text)")]
    [Tooltip("Width/height of the optional image, in UI pixels.")]
    [SerializeField] private Vector2 imageSize = new Vector2(420f, 420f);
    [Tooltip("Vertical gap between the image and the text below it - only applied when this haircut's entry actually has an Image assigned.")]
    [SerializeField] private float gapBetweenImageAndText = 24f;

    [Header("Text Layout (reposition / align / resize / font size)")]
    [Tooltip("Moves the whole content stack (image + text together) within the panel - use this to reposition without moving the panel itself (see Panel Placement above for that).")]
    [SerializeField] private Vector2 contentOffset = Vector2.zero;
    [Tooltip("Width/height of the trivia text box, in UI pixels - resizes just the text area.")]
    [SerializeField] private Vector2 textBoxSize = new Vector2(620f, 340f);
    [SerializeField] private int textFontSize = 26;
    [SerializeField] private TextAlignmentOptions textAlignment = TextAlignmentOptions.Top;
    [SerializeField] private Color textColor = new Color(0.96f, 0.96f, 0.94f, 1f);

    [Header("Behaviour")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool visibleOnStart = false;
    [Tooltip("Shown as the panel's own header, above the image/text content.")]
    [SerializeField] private string headerText = "Did You Know?";

    [Header("Trivia Entries (one per haircut - 15 total)")]
    [Tooltip("One entry per HaircutStyle, auto-populated the first time this component is added. Don't remove/reorder entries unless you also fix up the Haircut field, or the wrong entry may show for a haircut.")]
    [SerializeField] private HaircutTriviaEntry[] triviaEntries = CreateDefaultEntries();

    private readonly Color panelBlack = new Color(0.03f, 0.035f, 0.032f, 0.85f);
    private readonly Color textWhite = new Color(0.96f, 0.96f, 0.94f, 1f);
    private readonly Color textMuted = new Color(0.65f, 0.66f, 0.63f, 1f);

    private GameObject panelRoot;
    private RectTransform contentRoot;
    private RectTransform contentContainer;
    private TMP_Text headerLabel;

    private static HaircutTriviaEntry[] CreateDefaultEntries()
    {
        Array values = Enum.GetValues(typeof(HaircutStyle));
        HaircutTriviaEntry[] entries = new HaircutTriviaEntry[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            entries[i] = new HaircutTriviaEntry
            {
                Haircut = (HaircutStyle)values.GetValue(i),
                Image = null,
                TriviaText = string.Empty
            };
        }

        return entries;
    }

    private void Start()
    {
        if (menuScreen != null)
            menuScreen.OnStartTrainingEvent.AddListener(OnTrainingStarted);

        if (buildOnStart)
            Build();

        SetVisible(visibleOnStart);
    }

    private void OnDestroy()
    {
        if (menuScreen != null)
            menuScreen.OnStartTrainingEvent.RemoveListener(OnTrainingStarted);
    }

    private void OnTrainingStarted()
    {
        RefreshForActiveHaircut();
        Show();
    }

    [ContextMenu("Rebuild Panel")]
    public void Build()
    {
        if (panelRoot != null)
            return;

        EnsureEventSystem();

        panelRoot = new GameObject("Haircut Trivia Panel");
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

    // Re-reads HaircutManager.ActiveHairstyle and rebuilds the image+text content to match. Safe to call any
    // time (e.g. hooked to VRTrainingMenuScreen.OnStartTrainingEvent, or your own haircut-change hook for
    // chain mode) - works whether or not the panel is currently visible.
    [ContextMenu("Refresh For Active Haircut")]
    public void RefreshForActiveHaircut()
    {
        Build();

        if (contentContainer == null)
            return;

        if (haircutManager == null)
        {
            SetContentMessage("No HaircutManager assigned.");
            return;
        }

        HaircutStyle activeHaircut = haircutManager.ActiveHairstyle;

        if (headerLabel != null)
            headerLabel.text = headerText;

        if (TryFindEntryForHaircut(activeHaircut, out HaircutTriviaEntry entry))
            RebuildContent(entry);
        else
            SetContentMessage("No trivia set for this haircut.");
    }

    public void Show()
    {
        Build();

        if (placeInFrontOfPlayerOnShow && screenAnchor == null)
            RecenterToPlayer();

        SetVisible(true);
    }

    public void Hide()
    {
        SetVisible(false);
    }

    public void Toggle()
    {
        if (panelRoot != null)
            panelRoot.SetActive(!panelRoot.activeSelf);
    }

    private bool TryFindEntryForHaircut(HaircutStyle haircut, out HaircutTriviaEntry entry)
    {
        entry = null;

        if (triviaEntries == null)
            return false;

        for (int i = 0; i < triviaEntries.Length; i++)
        {
            if (triviaEntries[i] != null && triviaEntries[i].Haircut == haircut)
            {
                entry = triviaEntries[i];
                return true;
            }
        }

        return false;
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
        headerLabel = CreateLabel(headerText, panelRect, new Vector2(0f, top), 28, textWhite, TextAlignmentOptions.Center, new Vector2(screenSize.x - 60f, 44f));

        GameObject contentObject = new GameObject("Content");
        contentObject.transform.SetParent(panelRect, false);
        contentContainer = contentObject.AddComponent<RectTransform>();
        contentContainer.anchorMin = new Vector2(0.5f, 0.5f);
        contentContainer.anchorMax = new Vector2(0.5f, 0.5f);
        contentContainer.pivot = new Vector2(0.5f, 1f); // top pivot - content stacks DOWN from its top edge
        contentContainer.sizeDelta = new Vector2(screenSize.x - 40f, screenSize.y - 100f);
        contentContainer.anchoredPosition = new Vector2(contentOffset.x, top - 60f + contentOffset.y);

        CreateButton("Hide", panelRect, new Vector2(90f, 44f), new Vector2(screenSize.x / 2f - 70f, top), textMuted, Hide, 20);
    }

    // Stacks the optional image then the trivia text, top-down, starting from contentContainer's top edge
    // (its pivot is (0.5, 1)). Skipping the image when none is assigned means the text simply starts at the
    // very top instead of leaving a gap - there's no reserved slot for a missing photo.
    private void RebuildContent(HaircutTriviaEntry entry)
    {
        ClearContent();

        if (contentContainer == null)
            return;

        float cursorY = 0f; // distance already consumed from the container's top edge

        if (entry.Image != null)
        {
            CreateImageTile(entry.Image, new Vector2(0f, -(cursorY + imageSize.y / 2f)));
            cursorY += imageSize.y + gapBetweenImageAndText;
        }

        string text = string.IsNullOrEmpty(entry.TriviaText) ? "(No trivia text set for this haircut.)" : entry.TriviaText;
        Color color = string.IsNullOrEmpty(entry.TriviaText) ? textMuted : textColor;
        CreateLabel(text, contentContainer, new Vector2(0f, -(cursorY + textBoxSize.y / 2f)), textFontSize, color, textAlignment, textBoxSize);
    }

    private void SetContentMessage(string message)
    {
        ClearContent();

        if (contentContainer == null)
            return;

        CreateLabel(message, contentContainer, new Vector2(0f, -(textBoxSize.y / 2f)), 22, textMuted, TextAlignmentOptions.Center, new Vector2(contentContainer.sizeDelta.x, textBoxSize.y));
    }

    private void ClearContent()
    {
        if (contentContainer == null)
            return;

        for (int i = contentContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = contentContainer.GetChild(i);

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private void CreateImageTile(Sprite sprite, Vector2 position)
    {
        GameObject tileObject = new GameObject(sprite != null ? sprite.name : "Trivia Image");
        tileObject.transform.SetParent(contentContainer, false);

        RectTransform rect = tileObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = imageSize;
        rect.anchoredPosition = position;

        Image image = tileObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = Color.white;
        image.preserveAspect = true;
    }

    private TMP_Text CreateLabel(string text, RectTransform parent, Vector2 position, int fontSize, Color color, TextAlignmentOptions alignment, Vector2 size)
    {
        GameObject labelObject = new GameObject("Text");
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        TextMeshProUGUI textComponent = labelObject.AddComponent<TextMeshProUGUI>();
        textComponent.text = text;
        textComponent.color = color;
        textComponent.alignment = alignment;
        textComponent.fontSize = fontSize;
        textComponent.enableAutoSizing = false;
        textComponent.enableWordWrapping = true;
        textComponent.raycastTarget = false;

        return textComponent;
    }

    private void CreateButton(string label, RectTransform parent, Vector2 size, Vector2 position, Color textColorForButton, UnityEngine.Events.UnityAction action, int fontSize)
    {
        GameObject buttonObject = new GameObject(label + " Button");
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        buttonObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        Button button = buttonObject.AddComponent<Button>();

        if (action != null)
            button.onClick.AddListener(action);

        CreateLabel(label, rect, Vector2.zero, fontSize, textColorForButton, TextAlignmentOptions.Center, size);
    }

    private void SetVisible(bool isVisible)
    {
        if (panelRoot != null)
            panelRoot.SetActive(isVisible);
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

        if (placeInFrontOfPlayerOnShow)
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
        Type type = Type.GetType(typeName);

        if (type == null || target.GetComponent(type) != null)
            return type != null;

        target.AddComponent(type);
        return true;
    }
}
