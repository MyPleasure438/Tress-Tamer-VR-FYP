using System.Collections.Generic;
using UnityEngine;

public enum Stage1HairFeedbackDisplayMode
{
    PropertyBlockTint,
    MaterialOverride
}

// HANDOFF NOTE:
// Visual training guide. It colors hair cards green/yellow/red based on distance from target length.
// MaterialOverride is the safer mode for double-sided/Shader Graph hair; PropertyBlockTint is lighter but less reliable.
public class Stage1HairFeedbackVisualizer : MonoBehaviour
{
    [Header("Links")]
    [SerializeField] private HaircutManager stage1HaircutManager;
    [SerializeField] private Transform hairCardsRoot;

    [Header("Feedback Colors")]
    [SerializeField] private Color tooLongColor = Color.yellow;
    [SerializeField] private Color correctColor = Color.green;
    [SerializeField] private Color tooShortColor = Color.red;
    [SerializeField] private float colorStrength = 0.85f;
    // Use MaterialOverride if Shader Graph hair does not react to property-block color changes.
    [SerializeField] private Stage1HairFeedbackDisplayMode feedbackDisplayMode = Stage1HairFeedbackDisplayMode.MaterialOverride;
    [SerializeField] private Material tooLongMaterial;
    [SerializeField] private Material correctMaterial;
    [SerializeField] private Material tooShortMaterial;

    [Header("Behavior")]
    [SerializeField] private bool showFeedbackOnStart = false;
    [SerializeField] private bool updateEveryFrame = false;
    [SerializeField] private bool includeInactiveHairCards = true;

    private MaterialPropertyBlock propertyBlock;
    private readonly List<Renderer> coloredRenderers = new List<Renderer>();
    private readonly Dictionary<Renderer, Material[]> originalMaterialsByRenderer = new Dictionary<Renderer, Material[]>();

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int CullId = Shader.PropertyToID("_Cull");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        CreateFallbackMaterialsIfNeeded();
    }

    private void Start()
    {
        if (showFeedbackOnStart)
            ShowFeedback();
    }

    private void LateUpdate()
    {
        if (updateEveryFrame)
            ShowFeedback();
    }

    [ContextMenu("Show Stage 1 Feedback Colors")]
    public void ShowFeedback()
    {
        Transform root = hairCardsRoot != null ? hairCardsRoot : transform;
        HairCardData[] hairCards = root.GetComponentsInChildren<HairCardData>(includeInactiveHairCards);

        coloredRenderers.Clear();

        for (int i = 0; i < hairCards.Length; i++)
        {
            HairCardData hairCard = hairCards[i];

            if (hairCard == null || !TryGetHairCardTarget(hairCard, out float targetLength, out float tolerance))
                continue;

            Stage1HairLengthState state = GetLengthState(hairCard, targetLength, tolerance);
            ApplyColor(hairCard, GetColorForState(state));
        }
    }

    [ContextMenu("Clear Stage 1 Feedback Colors")]
    public void ClearFeedback()
    {
        for (int i = 0; i < coloredRenderers.Count; i++)
        {
            Renderer targetRenderer = coloredRenderers[i];

            if (targetRenderer == null)
                continue;

            targetRenderer.SetPropertyBlock(null);

            if (originalMaterialsByRenderer.TryGetValue(targetRenderer, out Material[] originalMaterials))
                targetRenderer.sharedMaterials = originalMaterials;
        }

        coloredRenderers.Clear();
        originalMaterialsByRenderer.Clear();
    }

    public void SetFeedbackVisible(bool visible)
    {
        if (visible)
            ShowFeedback();
        else
            ClearFeedback();
    }

    private bool TryGetHairCardTarget(HairCardData hairCard, out float targetLength, out float tolerance)
    {
        targetLength = 0f;
        tolerance = 0f;

        if (hairCard == null)
            return false;

        if (stage1HaircutManager != null && stage1HaircutManager.TryGetTargetLength(hairCard.Section, out targetLength, out tolerance))
            return targetLength > 0f;

        if (!hairCard.HasUsableTargetLength())
            return false;

        targetLength = hairCard.TargetLength;
        tolerance = hairCard.TargetTolerance;
        return targetLength > 0f;
    }

    private Stage1HairLengthState GetLengthState(HairCardData hairCard, float targetLength, float tolerance)
    {
        if (IsCompletedByMinimumVisibleSegment(hairCard, targetLength, tolerance))
            return Stage1HairLengthState.Correct;

        float safeTolerance = Mathf.Max(0f, tolerance);

        if (hairCard.CurrentLength > targetLength + safeTolerance)
            return Stage1HairLengthState.TooLong;

        if (hairCard.CurrentLength < targetLength - safeTolerance)
            return Stage1HairLengthState.TooShort;

        return Stage1HairLengthState.Correct;
    }

    private bool IsCompletedByMinimumVisibleSegment(HairCardData hairCard, float targetLength, float tolerance)
    {
        SegmentedHairCard segmentedHairCard = hairCard.GetComponent<SegmentedHairCard>();

        if (segmentedHairCard == null)
            return false;

        return segmentedHairCard.IsAtMinimumSegmentLength && segmentedHairCard.IsTargetBelowMinimumSegmentLength(targetLength, tolerance);
    }

    private Color GetColorForState(Stage1HairLengthState state)
    {
        switch (state)
        {
            case Stage1HairLengthState.Correct:
                return correctColor;

            case Stage1HairLengthState.TooShort:
                return tooShortColor;

            case Stage1HairLengthState.TooLong:
            default:
                return tooLongColor;
        }
    }

    private void ApplyColor(HairCardData hairCard, Color feedbackColor)
    {
        Renderer[] renderers = hairCard.GetComponentsInChildren<Renderer>(includeInactiveHairCards);
        Color finalColor = Color.Lerp(Color.white, feedbackColor, Mathf.Clamp01(colorStrength));

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];

            if (targetRenderer == null || !targetRenderer.gameObject.activeInHierarchy)
                continue;

            if (feedbackDisplayMode == Stage1HairFeedbackDisplayMode.MaterialOverride)
                ApplyMaterialOverride(targetRenderer, feedbackColor);
            else
                ApplyPropertyBlockTint(targetRenderer, finalColor);

            if (!coloredRenderers.Contains(targetRenderer))
                coloredRenderers.Add(targetRenderer);
        }
    }

    private void ApplyPropertyBlockTint(Renderer targetRenderer, Color finalColor)
    {
        targetRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(BaseColorId, finalColor);
        propertyBlock.SetColor(ColorId, finalColor);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }

    private void ApplyMaterialOverride(Renderer targetRenderer, Color feedbackColor)
    {
        if (!originalMaterialsByRenderer.ContainsKey(targetRenderer))
            originalMaterialsByRenderer.Add(targetRenderer, targetRenderer.sharedMaterials);

        Material feedbackMaterial = GetMaterialForColor(feedbackColor);

        if (feedbackMaterial == null)
            return;

        Material[] replacementMaterials = targetRenderer.sharedMaterials;

        for (int i = 0; i < replacementMaterials.Length; i++)
            replacementMaterials[i] = feedbackMaterial;

        targetRenderer.sharedMaterials = replacementMaterials;
    }

    private Material GetMaterialForColor(Color feedbackColor)
    {
        if (feedbackColor == tooShortColor)
            return tooShortMaterial;

        if (feedbackColor == correctColor)
            return correctMaterial;

        return tooLongMaterial;
    }

    private void CreateFallbackMaterialsIfNeeded()
    {
        if (tooLongMaterial == null)
            tooLongMaterial = CreateFallbackMaterial("Stage 1 Too Long Feedback", tooLongColor);

        if (correctMaterial == null)
            correctMaterial = CreateFallbackMaterial("Stage 1 Correct Feedback", correctColor);

        if (tooShortMaterial == null)
            tooShortMaterial = CreateFallbackMaterial("Stage 1 Too Short Feedback", tooShortColor);
    }

    private Material CreateFallbackMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");

        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader == null)
            return null;

        Material material = new Material(shader);
        material.name = materialName;
        material.color = color;

        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);

        if (material.HasProperty(ColorId))
            material.SetColor(ColorId, color);

        ConfigureDoubleSidedMaterial(material);

        return material;
    }

    private void ConfigureDoubleSidedMaterial(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty(CullId))
            material.SetFloat(CullId, (float)UnityEngine.Rendering.CullMode.Off);

        if (material.HasProperty(SurfaceId))
            material.SetFloat(SurfaceId, 0f);

        material.doubleSidedGI = true;
    }
}
