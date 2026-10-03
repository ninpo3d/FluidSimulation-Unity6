using UnityEngine;

/// <summary>
/// Utility for generating 1D gradient LUT textures (256x1 RGBA32).
/// The resulting texture is sampled along UV.x by the fluid surface shader
/// using a combination of Fresnel falloff and surface wave height.
/// Supports both in-memory runtime texture creation and editor asset baking.
/// </summary>
public class GradientTextureGenerator : MonoBehaviour
{
    public const string NeonRampPath = "Assets/Textures/NeonSlimeRamp.png";
    public const string RampTexturePath = NeonRampPath;

    [ContextMenu("Bake Reference Neon Ramp Asset")]
    public void GenerateTexture()
    {
#if UNITY_EDITOR
        BakeRampAsset(CreateReferenceNeonGradient());
#endif
    }

    public static Gradient CreateReferenceNeonGradient()
    {
        Gradient g = new Gradient();
        GradientColorKey[] colorKeys = new GradientColorKey[6];
        colorKeys[0] = new GradientColorKey(new Color(0.04f, 0.02f, 0.30f), 0.00f); // Deep Indigo/Navy (cavity)
        colorKeys[1] = new GradientColorKey(new Color(0.04f, 0.32f, 0.95f), 0.20f); // Royal Blue (inner pool)
        colorKeys[2] = new GradientColorKey(new Color(0.00f, 0.88f, 1.00f), 0.40f); // Electric Cyan (slopes)
        colorKeys[3] = new GradientColorKey(new Color(0.96f, 0.02f, 0.78f), 0.65f); // Neon Magenta / Hot Pink (crests)
        colorKeys[4] = new GradientColorKey(new Color(1.00f, 0.38f, 0.02f), 0.84f); // Fiery Coral / Amber (surge)
        colorKeys[5] = new GradientColorKey(new Color(1.00f, 0.88f, 0.96f), 1.00f); // Bright Rim Highlight

        GradientAlphaKey[] alphaKeys = new GradientAlphaKey[2];
        alphaKeys[0] = new GradientAlphaKey(1.0f, 0.0f);
        alphaKeys[1] = new GradientAlphaKey(1.0f, 1.0f);

        g.SetKeys(colorKeys, alphaKeys);
        return g;
    }

    /// <summary>
    /// Generates an in-memory 256x1 Texture2D LUT from the specified gradient.
    /// Enables dynamic in-memory runtime palette switching without touching the filesystem.
    /// </summary>
    public static Texture2D CreateRampTexture(Gradient gradient)
    {
        const int width = 256;
        const int height = 1;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "FluidColorRamp_Runtime",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Gradient source = gradient ?? CreateReferenceNeonGradient();
        Color[] pixels = new Color[width];
        for (int x = 0; x < width; x++)
        {
            float t = (float)x / (width - 1);
            pixels[x] = source.Evaluate(t);
        }

        tex.SetPixels(pixels);
        tex.Apply(false, false);
        return tex;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Bakes the gradient into a PNG texture asset on disk and configures importer settings.
    /// </summary>
    public static void BakeRampAsset(Gradient gradient, string path = RampTexturePath)
    {
        Texture2D tex = CreateRampTexture(gradient);

        string directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
        DestroyImmediate(tex);

        UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceUpdate);
        UnityEditor.TextureImporter importer = UnityEditor.AssetImporter.GetAtPath(path) as UnityEditor.TextureImporter;
        if (importer != null)
        {
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureType = UnityEditor.TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }
        UnityEditor.AssetDatabase.Refresh();
        Debug.Log("<color=cyan>[GradientTextureGenerator]</color> Color ramp asset baked successfully at: " + path);
    }

    /// <summary>
    /// Returns the pre-baked Neon ramp texture asset, or bakes it on disk if missing.
    /// </summary>
    public static Texture2D GetOrCreateNeonRampAsset()
    {
        string path = NeonRampPath;
        Texture2D existing = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        Gradient g = CreateReferenceNeonGradient();
        BakeRampAsset(g, path);
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
#endif
}