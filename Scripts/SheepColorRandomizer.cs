using UnityEngine;

public class SheepColorRandomizer : MonoBehaviour
{
    [Header("Color Settings")]
    [Range(0f, 1f)]
    public float minBrightness = 0.5f; // Minimum brightness (0 = black, 1 = white)
    [Range(0f, 1f)]
    public float maxBrightness = 1.0f; // Maximum brightness

    [Header("Pattern Settings")]
    public bool randomizeTextureOffset = true;
    public float minOffsetX = 0f;
    public float maxOffsetX = 1f;
    public float minOffsetY = 0f;
    public float maxOffsetY = 1f;

    [Header("References")]
    public Renderer[] renderersToAffect; // Array of renderers (body parts) to color

    [Header("Advanced Settings")]
    public string colorPropertyName = "_Color"; // Standard shader color property
    public string mainTexPropertyName = "_MainTex"; // Standard shader texture property

    private void Awake()
    {
        ApplyRandomColorAndPattern();
    }

    // Call this to randomize the sheep's appearance
    public void ApplyRandomColorAndPattern()
    {
        // Auto-find renderers if not assigned
        if (renderersToAffect == null || renderersToAffect.Length == 0)
        {
            renderersToAffect = GetComponentsInChildren<Renderer>();
        }

        // Generate a random brightness value between min and max
        float brightness = Random.Range(minBrightness, maxBrightness);

        // Create a color with the random brightness (same value for R, G, B = grayscale)
        Color sheepColor = new Color(brightness, brightness, brightness, 1f);

        // Generate random texture offsets if enabled
        Vector2 textureOffset = Vector2.zero;
        if (randomizeTextureOffset)
        {
            textureOffset = new Vector2(
                Random.Range(minOffsetX, maxOffsetX),
                Random.Range(minOffsetY, maxOffsetY)
            );
        }

        // Apply to all targeted renderers
        foreach (Renderer renderer in renderersToAffect)
        {
            if (renderer != null)
            {
                // Loop through all materials on this renderer
                foreach (Material material in renderer.materials)
                {
                    // Apply color
                    material.SetColor(colorPropertyName, sheepColor);

                    // Apply texture offset if enabled
                    if (randomizeTextureOffset)
                    {
                        material.SetTextureOffset(mainTexPropertyName, textureOffset);
                    }
                }
            }
        }
    }

    // Optional: Method to set a specific brightness (can be called from other scripts)
    public void SetBrightness(float brightness)
    {
        brightness = Mathf.Clamp01(brightness); // Ensure value is between 0-1
        Color sheepColor = new Color(brightness, brightness, brightness, 1f);

        foreach (Renderer renderer in renderersToAffect)
        {
            if (renderer != null)
            {
                foreach (Material material in renderer.materials)
                {
                    material.SetColor(colorPropertyName, sheepColor);
                }
            }
        }
    }

    // Optional: Method to set a specific texture offset (can be called from other scripts)
    public void SetTextureOffset(float offsetX, float offsetY)
    {
        Vector2 textureOffset = new Vector2(offsetX, offsetY);

        foreach (Renderer renderer in renderersToAffect)
        {
            if (renderer != null)
            {
                foreach (Material material in renderer.materials)
                {
                    material.SetTextureOffset(mainTexPropertyName, textureOffset);
                }
            }
        }
    }
}