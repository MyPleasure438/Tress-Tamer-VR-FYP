using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// HANDOFF NOTE (Reference Image Row):
// New, self-contained script - does not modify VRTrainingMenuScreen, TrainingGuideManager, or any other
// existing script (only READS VRTrainingMenuScreen.OnStartTrainingEvent and HaircutManager.ActiveHairstyle).
// Builds its own small runtime World Space canvas (same pattern as VRTrainingGuidePanel) showing 5 reference
// photos side by side in a row for whichever of the 15 HaircutStyles is currently active.
//
// HOW TO WIRE THIS UP IN UNITY:
// 1. Create an empty GameObject (e.g. "Reference Image Row Panel") and add this component.
// 2. Drag your existing HaircutManager into the Haircut Manager field.
// 3. Drag your existing VRTrainingMenuScreen into the Menu Screen field - this makes the panel show itself
//    automatically the instant Begin Training is pressed. Leave empty if you'd rather call
//    RefreshForActiveHaircut()/Show() yourself from somewhere else.
// 4. Reference Image Sets is pre-populated with one entry per HaircutStyle (15 total) the first time you
//    add this component - just expand each entry and drag in its 5 photos, in the left-to-right order you
//    want them shown. Leave a haircut's Images empty and that tile just shows blank.
// 5. Tune Row Offset / Image Size / Gap Between Images to reposition, resize, and space the row. Tune
//    Screen Anchor / Screen Distance From Player / Screen Height Offset to place the whole panel in-world.
[DisallowMultipleComponent]
public class ReferenceImageRowPanel : MonoBehaviour
{
    [Serializable]
    public class HaircutReferenceImageSet
    {
        public HaircutStyle Haircut;
        [Tooltip("Exactly 5 reference photos shown left-to-right for this haircut. A left-empty slot just shows a blank tile.")]
        public Sprite[] Images = new Sprite[5];
    }

    [Header("Required Scene Links")]
    [Tooltip("Your existing HaircutManager - used to read which of the 15 haircuts is currently active so the matching image set is shown.")]
    [SerializeField] private HaircutManager haircutManager;
    [Tooltip("Optional. Your existing VRTrainingMenuScreen - if assigned, this panel shows itself automatically the moment Begin Training is pressed (hooks its Start Training event). Leave empty and call RefreshForActiveHaircut()/Show() yourself if you'd rather wire it another way (e.g. a controller button).")]
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
    [SerializeField] private Vector2 screenSize = new Vector2(1400f, 360f);

    [Header("Row Layout (reposition / resize / gap)")]
    [Tooltip("Moves the whole row of 5 images within the panel - use this to reposition the row without moving the panel itself (see Panel Placement above for that).")]
    [SerializeField] private Vector2 rowOffset = new Vector2(0f, -20f);
    [Tooltip("Width/height of EACH of the 5 image tiles, in UI pixels - resizes every tile at once.")]
    [SerializeField] private Vector2 imageSize = new Vector2(220f, 220f);
    [Tooltip("Horizontal gap between adjacent image tiles, in UI pixels (edge to edge, not center to center).")]
    [SerializeField] private float gapBetweenImages = 24f;
    [Tooltip("Background tile color, visible behind transparent photos and for any empty/unassigned slot.")]
    [SerializeField] private Color tileBackgroundColor = new Color(0.08f, 0.085f, 0.08f, 0.9f);

    [Header("Behaviour")]
    [SerializeField] private bool buildOnStart = true;
    [SerializeField] private bool visibleOnStart = false;
    [Tooltip("Shown as the panel's own header, above the image row.")]
    [SerializeField] private string headerText = "Reference Photos";

    [Header("Reference Image Sets (one per haircut - 15 total)")]
    [Tooltip("One entry per HaircutStyle, auto-populated the first time this component is added. Each needs exactly 5 images, left-to-right. Don't remove/reorder entries unless you also fix up the Haircut field, or the wrong set may show for a haircut.")]
    [SerializeField] private HaircutReferenceImageSet[] referenceImageSets = CreateDefaultSets();

    private readonly Color panelBlack = new Color(0.03f, 0.035f, 0.032f, 0.85f);
    private readonly Color textWhite = new Color(0.96f, 0.96f, 0.94f, 1f);
    private readonly Color textMuted = new Color(0.65f, 0.66f, 0.63f, 1f);

    private GameObject panelRoot;
    private RectTransform contentRoot;
    private RectTransform rowContainer;
    private TMP_Text headerLabel;

    private static HaircutReferenceImageSet[] CreateDefaultSets()
    {
        Array values = Enum.GetValues(typeof(HaircutStyle));
        HaircutReferenceImageSet[] sets = new HaircutReferenceImageSet[values.Length];

        for (int i = 0; i < values.Length; i++)
        {
            sets[i] = new HaircutReferenceImageSet
            {
                Haircut = (HaircutStyle)values.GetValue(i),
                Images = new Sprite[5]
            };
        }

        return sets;
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

        panelRoot = new GameObject("Reference Image Row Panel");
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

    // Re-reads HaircutManager.ActiveHairstyle and rebuilds the 5-image row to match. Safe to call any time
    // (e.g. hooked to VRTrainingMenuScreen.OnStartTrainingEvent, or your own haircut-change hook for chain
    // mode) - works whether or not the panel is currently visible.
    [ContextMenu("Refresh For Active Haircut")]
    public void RefreshForActiveHaircut()
    {
        Build();

        if (contentRoot == null)
            return;

        if (haircutManager == null)
        {
            SetRowMessage("No HaircutManager assigned.");
            return;
        }

        HaircutStyle activeHaircut = haircutManager.ActiveHairstyle;

        if (headerLabel != null)
            headerLabel.text = $"{headerText} - {ToDisplayName(activeHaircut.ToString())}";

        if (TryFindSetForHaircut(activeHaircut, out HaircutReferenceImageSet set))
            RebuildRow(set);
        else
            SetRowMessage("No reference images set for this haircut.");
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

    private bool TryFindSetForHaircut(HaircutStyle haircut, out HaircutReferenceImageSet set)
    {
        set = null;

        if (referenceImageSets == null)
            return false;

        for (int i = 0; i < referenceImageSets.Length; i++)
        {
            if (referenceImageSets[i] != null && referenceImageSets[i].Haircut == haircut)
            {
                set = referenceImageSets[i];
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

        GameObject rowObject = new GameObject("Image Row");
        rowObject.transform.SetParent(panelRect, false);
        rowContainer = rowObject.AddComponent<RectTransform>();
        rowContainer.anchorMin = new Vector2(0.5f, 0.5f);
        rowContainer.anchorMax = new Vector2(0.5f, 0.5f);
        rowContainer.pivot = new Vector2(0.5f, 0.5f);
        rowContainer.sizeDelta = new Vector2(screenSize.x - 40f, screenSize.y - 100f);
        rowContainer.anchoredPosition = rowOffset;

        CreateButton("Hide", panelRect, new Vector2(90f, 44f), new Vector2(screenSize.x / 2f - 70f, top), textMuted, Hide, 20);
    }

    private void RebuildRow(HaircutReferenceImageSet set)
    {
        ClearRow();

        if (rowContainer == null)
            return;

        Sprite[] images = set.Images ?? Array.Empty<Sprite>();
        float step = imageSize.x + gapBetweenImages;

        for (int i = 0; i < 5; i++)
        {
            // Centers the 5 tiles as a group around x = 0 regardless of image size/gap tuning.
            float xOffset = (i - 2) * step;
            Sprite sprite = i < images.Length ? images[i] : null;
            CreateImageTile(sprite, new Vector2(xOffset, 0f));
        }
    }

    private void SetRowMessage(string message)
    {
        ClearRow();

        if (rowContainer == null)
            return;

        CreateLabel(message, rowContainer, Vector2.zero, 22, textMuted, TextAlignmentOptions.Center, new Vector2(rowContainer.sizeDelta.x - 40f, 60f));
    }

    private void ClearRow()
    {
        if (rowContainer == null)
            return;

        for (int i = rowContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = rowContainer.GetChild(i);

            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
    }

    private void CreateImageTile(Sprite sprite, Vector2 position)
    {
        GameObject tileObject = new GameObject(sprite != null ? sprite.name : "Empty Tile");
        tileObject.transform.SetParent(rowContainer, false);

        RectTransform rect = tileObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = imageSize;
        rect.anchoredPosition = position;

        Image image = tileObject.AddComponent<Image>();

        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
        }
        else
        {
            image.color = tileBackgroundColor;
        }
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

    private void CreateButton(string label, RectTransform parent, Vector2 size, Vector2 position, Color textColor, UnityEngine.Events.UnityAction action, int fontSize)
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

        CreateLabel(label, rect, Vector2.zero, fontSize, textColor, TextAlignmentOptions.Center, size);
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

    private string ToDisplayName(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        System.Text.StringBuilder builder = new System.Text.StringBuilder(raw.Length + 8);
        builder.Append(raw[0]);

        for (int i = 1; i < raw.Length; i++)
        {
            if ((char.IsUpper(raw[i]) || raw[i] == '_') && raw[i - 1] != '_')
                builder.Append(' ');

            if (raw[i] != '_')
                builder.Append(raw[i]);
        }

        return builder.ToString();
    }
}
