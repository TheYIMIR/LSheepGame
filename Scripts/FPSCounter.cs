using UnityEngine;
using UnityEngine.UI;

public class FPSCounter : MonoBehaviour
{
    [Header("UI Reference")]
    public Text fpsText;

    [Header("FPS Settings")]
    public float updateInterval = 0.5f; // How often to update the FPS display
    public int targetFrameRate = 60; // Target frame rate to compare against
    public bool showFrameTime = true; // Whether to show frame time in ms

    [Header("Display Settings")]
    public string fpsLabel = "FPS: ";
    public Color goodFPSColor = new Color(0.0f, 1.0f, 0.0f); // Green
    public Color okayFPSColor = new Color(1.0f, 1.0f, 0.0f); // Yellow
    public Color badFPSColor = new Color(1.0f, 0.0f, 0.0f); // Red

    [Header("Thresholds")]
    public float goodFPSThreshold = 50f; // Above this is considered good
    public float okayFPSThreshold = 30f; // Above this is considered okay

    private float accum = 0; // FPS accumulated over the interval
    private int frames = 0; // Frames drawn over the interval
    private float timeLeft; // Left time for current interval
    private float currentFPS = 0;

    void Start()
    {
        // Initialize the FPS counter
        if (fpsText == null)
        {
            Debug.LogError("FPSCounter: FPS Text UI Reference not set!");
            enabled = false;
            return;
        }

        timeLeft = updateInterval;

        // Set the target frame rate (optional)
        Application.targetFrameRate = targetFrameRate;
    }

    void Update()
    {
        // Skip rendering if the text element isn't assigned
        if (fpsText == null) return;

        // Accumulate time and frames
        timeLeft -= Time.deltaTime;
        accum += Time.timeScale / Time.deltaTime;
        frames++;

        // Interval ended - update GUI text and start new interval
        if (timeLeft <= 0.0)
        {
            // Calculate FPS
            currentFPS = accum / frames;

            string fpsOutput = fpsLabel + Mathf.RoundToInt(currentFPS);

            // Optionally add frame time in milliseconds
            if (showFrameTime)
            {
                float frameTimeMs = 1000.0f / currentFPS;
                fpsOutput += " (" + frameTimeMs.ToString("F1") + "ms)";
            }

            // Set the text and color
            fpsText.text = fpsOutput;

            // Change color according to FPS
            if (currentFPS >= goodFPSThreshold)
                fpsText.color = goodFPSColor;
            else if (currentFPS >= okayFPSThreshold)
                fpsText.color = okayFPSColor;
            else
                fpsText.color = badFPSColor;

            // Reset for the next interval
            timeLeft = updateInterval;
            accum = 0.0F;
            frames = 0;
        }
    }
}