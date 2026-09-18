using UnityEngine;

[ExecuteAlways]
public class StubbleOverlay : MonoBehaviour
{
    [Header("Links")]
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Material stubbleMaterial;

    [Header("Stubble Look")]
    [SerializeField] private Color stubbleColor = new Color(0.11f, 0.065f, 0.035f, 1f);
    [SerializeField, Range(0f, 1f)] private float opacity = 0.45f;
    [SerializeField, Range(64, 2048)] private int textureSize = 512;
    [SerializeField, Range(100, 20000)] private int stubbleCount = 7000;
    [SerializeField, Range(1, 8)] private int stubbleLengthPixels = 2;
    [SerializeField, Range(1, 4)] private int stubbleThicknessPixels = 1;
    [SerializeField] private int randomSeed = 12345;

    [Header("Preview")]
    [SerializeField] private bool generateOnStart = true;
    [SerializeField] private bool showInEditor = true;

    private Texture2D generatedTexture;
    private static readonly int StubbleTexId = Shader.PropertyToID("_StubbleTex");
    private static readonly int StubbleColorId = Shader.PropertyToID("_StubbleColor");
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    private void Reset()
    {
        targetRenderer = GetComponent<Renderer>();
    }

    private void Awake()
    {
        if (generateOnStart)
            ApplyStubble();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying && showInEditor)
            ApplyStubble();
    }

    private void OnValidate()
    {
        textureSize = Mathf.ClosestPowerOfTwo(Mathf.Clamp(textureSize, 64, 2048));

        if (!Application.isPlaying && showInEditor)
            ApplyStubble();
    }

    [ContextMenu("Apply Stubble")]
    public void ApplyStubble()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<Renderer>();

        if (targetRenderer == null)
            return;

        Material material = GetOrCreateMaterial();

        if (material == null)
            return;

        generatedTexture = GenerateStubbleTexture();
        material.SetTexture(StubbleTexId, generatedTexture);
        material.SetColor(StubbleColorId, stubbleColor);
        material.SetFloat(OpacityId, opacity);
    }

    [ContextMenu("Randomize Stubble")]
    public void RandomizeStubble()
    {
        randomSeed = Random.Range(1, int.MaxValue);
        ApplyStubble();
    }

    private Material GetOrCreateMaterial()
    {
        if (stubbleMaterial == null)
        {
            Shader shader = Shader.Find("Custom/Hair/Stubble Overlay");

            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
                return null;

            stubbleMaterial = new Material(shader);
            stubbleMaterial.name = "Generated Stubble Overlay";
        }

        targetRenderer.sharedMaterial = stubbleMaterial;
        return stubbleMaterial;
    }

    private Texture2D GenerateStubbleTexture()
    {
        Texture2D texture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        texture.name = "Generated Stubble Texture";
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Bilinear;

        Color clear = new Color(0f, 0f, 0f, 0f);
        Color[] pixels = new Color[textureSize * textureSize];

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        System.Random random = new System.Random(randomSeed);

        for (int i = 0; i < stubbleCount; i++)
        {
            int x = random.Next(0, textureSize);
            int y = random.Next(0, textureSize);
            float angle = (float)(random.NextDouble() * Mathf.PI * 2f);
            float alpha = Mathf.Lerp(0.35f, 1f, (float)random.NextDouble());

            DrawStubbleStroke(pixels, x, y, angle, alpha);
        }

        texture.SetPixels(pixels);
        texture.Apply(false, false);
        return texture;
    }

    private void DrawStubbleStroke(Color[] pixels, int startX, int startY, float angle, float alpha)
    {
        float directionX = Mathf.Cos(angle);
        float directionY = Mathf.Sin(angle);

        for (int step = 0; step < stubbleLengthPixels; step++)
        {
            int x = Mathf.RoundToInt(startX + directionX * step);
            int y = Mathf.RoundToInt(startY + directionY * step);

            for (int thicknessX = -stubbleThicknessPixels + 1; thicknessX < stubbleThicknessPixels; thicknessX++)
            {
                for (int thicknessY = -stubbleThicknessPixels + 1; thicknessY < stubbleThicknessPixels; thicknessY++)
                    SetPixelAlpha(pixels, x + thicknessX, y + thicknessY, alpha);
            }
        }
    }

    private void SetPixelAlpha(Color[] pixels, int x, int y, float alpha)
    {
        x = (x % textureSize + textureSize) % textureSize;
        y = (y % textureSize + textureSize) % textureSize;

        int index = y * textureSize + x;
        float finalAlpha = Mathf.Max(pixels[index].a, alpha);
        pixels[index] = new Color(1f, 1f, 1f, finalAlpha);
    }
}
