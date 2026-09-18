using UnityEngine;

[ExecuteAlways]
// HANDOFF NOTE:
// Optional scalp/buzz visual support. It does not replace hair cards; shader-only induction stubble is still imperfect.
// HaircutManager can call SetBuzzLook/Use*Preset when switching hairstyles.
public class HairCapController : MonoBehaviour
{
    [Header("Links")]
    [SerializeField] private Renderer hairCapRenderer;
    [SerializeField] private Material hairCapMaterial;

    [Header("Hair Cap Look")]
    [SerializeField] private Color scalpColor = new Color(0.72f, 0.48f, 0.36f, 1f);
    [SerializeField] private Color stubbleColor = new Color(0.09f, 0.045f, 0.025f, 1f);
    // Optional grayscale mask. Assigning the wrong texture here can hide or distort the cap.
    [SerializeField] private Texture2D densityMask;
    // Visual texture only. It helps buzz/stubble appearance but does not create real 3D hair.
    [SerializeField] private Texture2D stubbleTexture;
    [SerializeField] private Texture2D flowMap;
    [SerializeField] private Texture2D normalMap;
    [SerializeField, Range(0f, 1f)] private float density = 0.42f;
    [SerializeField, Range(0f, 3f)] private float darkness = 0.8f;
    [SerializeField, Range(20f, 650f)] private float stubbleScale = 360f;
    [SerializeField, Range(0.02f, 1f)] private float strandLength = 0.16f;
    [SerializeField, Range(0.002f, 0.25f)] private float strandThinness = 0.018f;
    [SerializeField, Range(0f, 1f)] private float fineNoiseStrength = 0.38f;
    [SerializeField, Range(0f, 360f)] private float directionAngle = 90f;
    [SerializeField, Range(0f, 1f)] private float opacity = 1f;
    [SerializeField, Range(0f, 1f)] private float smoothness = 0.2f;
    [SerializeField, Range(0f, 1f)] private float softness = 0.65f;
    [SerializeField, Range(0f, 1f)] private float stubbleTextureStrength = 1f;
    [SerializeField, Range(0f, 1f)] private float flowMapStrength = 0f;
    [SerializeField, Range(0f, 2f)] private float normalStrength = 0f;
    [SerializeField, Range(0.1f, 8f)] private float textureContrast = 3f;
    [SerializeField, Range(0f, 1f)] private float baseBuzzShadow = 0.18f;
    [SerializeField, Range(0f, 1f)] private float textureOnlyDebug = 0f;
    [SerializeField, Range(0f, 1f)] private float useWorldProjection = 1f;
    [SerializeField, Range(1f, 80f)] private float worldTextureScale = 18f;

    private static readonly int ScalpColorId = Shader.PropertyToID("_ScalpColor");
    private static readonly int StubbleColorId = Shader.PropertyToID("_StubbleColor");
    private static readonly int DensityMaskId = Shader.PropertyToID("_DensityMask");
    private static readonly int StubbleTexId = Shader.PropertyToID("_StubbleTex");
    private static readonly int FlowMapId = Shader.PropertyToID("_FlowMap");
    private static readonly int NormalMapId = Shader.PropertyToID("_NormalMap");
    private static readonly int DensityId = Shader.PropertyToID("_Density");
    private static readonly int DarknessId = Shader.PropertyToID("_Darkness");
    private static readonly int StubbleScaleId = Shader.PropertyToID("_StubbleScale");
    private static readonly int StrandLengthId = Shader.PropertyToID("_StrandLength");
    private static readonly int StrandThinnessId = Shader.PropertyToID("_StrandThinness");
    private static readonly int NoiseStrengthId = Shader.PropertyToID("_NoiseStrength");
    private static readonly int DirectionAngleId = Shader.PropertyToID("_DirectionAngle");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
    private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
    private static readonly int StubbleTextureStrengthId = Shader.PropertyToID("_StubbleTextureStrength");
    private static readonly int FlowMapStrengthId = Shader.PropertyToID("_FlowMapStrength");
    private static readonly int NormalStrengthId = Shader.PropertyToID("_NormalStrength");
    private static readonly int TextureContrastId = Shader.PropertyToID("_TextureContrast");
    private static readonly int BaseBuzzShadowId = Shader.PropertyToID("_BaseBuzzShadow");
    private static readonly int TextureOnlyDebugId = Shader.PropertyToID("_TextureOnlyDebug");
    private static readonly int UseWorldProjectionId = Shader.PropertyToID("_UseWorldProjection");
    private static readonly int WorldTextureScaleId = Shader.PropertyToID("_WorldTextureScale");

    private void Reset()
    {
        hairCapRenderer = GetComponent<Renderer>();
    }

    private void OnEnable()
    {
        ApplySettings();
    }

    private void OnValidate()
    {
        ApplySettings();
    }

    [ContextMenu("Apply Hair Cap Settings")]
    public void ApplySettings()
    {
        if (hairCapRenderer == null)
            hairCapRenderer = GetComponent<Renderer>();

        if (hairCapRenderer == null)
            return;

        hairCapRenderer.enabled = opacity > 0.01f;

        Material material = GetOrCreateMaterial();

        if (material == null)
            return;

        material.SetColor(ScalpColorId, scalpColor);
        material.SetColor(StubbleColorId, stubbleColor);

        if (densityMask != null)
            material.SetTexture(DensityMaskId, densityMask);

        if (stubbleTexture != null)
            material.SetTexture(StubbleTexId, stubbleTexture);

        if (flowMap != null)
            material.SetTexture(FlowMapId, flowMap);

        if (normalMap != null)
            material.SetTexture(NormalMapId, normalMap);

        material.SetFloat(DensityId, density);
        material.SetFloat(DarknessId, darkness);
        material.SetFloat(StubbleScaleId, stubbleScale);
        material.SetFloat(StrandLengthId, strandLength);
        material.SetFloat(StrandThinnessId, strandThinness);
        material.SetFloat(NoiseStrengthId, fineNoiseStrength);
        material.SetFloat(DirectionAngleId, directionAngle);
        material.SetFloat(OpacityId, opacity);
        material.SetFloat(SmoothnessId, smoothness);
        material.SetFloat(SoftnessId, softness);
        material.SetFloat(StubbleTextureStrengthId, stubbleTextureStrength);
        material.SetFloat(FlowMapStrengthId, flowMapStrength);
        material.SetFloat(NormalStrengthId, normalStrength);
        material.SetFloat(TextureContrastId, textureContrast);
        material.SetFloat(BaseBuzzShadowId, baseBuzzShadow);
        material.SetFloat(TextureOnlyDebugId, textureOnlyDebug);
        material.SetFloat(UseWorldProjectionId, useWorldProjection);
        material.SetFloat(WorldTextureScaleId, worldTextureScale);
    }

    public void SetBuzzLook(float targetDensity, float targetDarkness, float targetOpacity)
    {
        density = Mathf.Clamp01(targetDensity);
        darkness = Mathf.Clamp(targetDarkness, 0f, 3f);
        opacity = Mathf.Clamp01(targetOpacity);
        ApplySettings();
    }

    [ContextMenu("Use Soft Induction Preset")]
    public void UseSoftInductionPreset()
    {
        // Tuning preset for a subtle scalp texture. It is a demo helper, not a final replacement for authored short hair.
        stubbleColor = new Color(0.16f, 0.075f, 0.035f, 1f);
        density = 0.9f;
        darkness = 0.82f;
        stubbleScale = 115f;
        strandLength = 0.028f;
        strandThinness = 0.006f;
        fineNoiseStrength = 0.32f;
        opacity = 1f;
        smoothness = 0.18f;
        softness = 0.82f;
        stubbleTextureStrength = 0.12f;
        flowMapStrength = 0f;
        normalStrength = 0.08f;
        textureContrast = 1.7f;
        baseBuzzShadow = 0.42f;
        textureOnlyDebug = 0f;
        useWorldProjection = 1f;
        worldTextureScale = 2f;
        ApplySettings();
    }

    [ContextMenu("Use Buzz Cut Preset")]
    public void UseBuzzCutPreset()
    {
        density = 0.85f;
        darkness = 0.75f;
        stubbleScale = 120f;
        strandLength = 0.07f;
        strandThinness = 0.008f;
        fineNoiseStrength = 0.3f;
        opacity = 1f;
        softness = 0.72f;
        stubbleTextureStrength = 1f;
        textureContrast = 3.4f;
        baseBuzzShadow = 0.16f;
        textureOnlyDebug = 0f;
        useWorldProjection = 1f;
        worldTextureScale = 2.5f;
        ApplySettings();
    }

    [ContextMenu("Debug Show Stubble Texture Strongly")]
    public void DebugShowStubbleTextureStrongly()
    {
        density = 1f;
        darkness = 1.2f;
        stubbleScale = 120f;
        stubbleTextureStrength = 1f;
        textureContrast = 8f;
        baseBuzzShadow = 0f;
        textureOnlyDebug = 1f;
        useWorldProjection = 1f;
        worldTextureScale = 2f;
        ApplySettings();
    }

    [ContextMenu("Stop Texture Debug")]
    public void StopTextureDebug()
    {
        textureOnlyDebug = 0f;
        ApplySettings();
    }

    private Material GetOrCreateMaterial()
    {
        if (hairCapMaterial == null)
        {
            Shader shader = Shader.Find("Custom/Hair/Hair Cap Stubble");

            if (shader == null)
                return null;

            hairCapMaterial = new Material(shader);
            hairCapMaterial.name = "Generated Hair Cap Stubble";
        }

        if (Application.isPlaying)
            hairCapRenderer.material = hairCapMaterial;
        else
            hairCapRenderer.sharedMaterial = hairCapMaterial;

        return hairCapMaterial;
    }
}
