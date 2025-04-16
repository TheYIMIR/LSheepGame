using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

public class AdvancedAudioReactivePostProcessing : MonoBehaviour
{
    [Header("Audio Analysis Settings")]
    [Tooltip("The audio source to analyze")]
    public AudioSource targetAudioSource;

    [Tooltip("Size of the frequency sample window (must be power of 2)")]
    public int sampleSize = 1024;

    [SerializeField]
    private List<AudioReactiveEffect> reactiveEffects = new List<AudioReactiveEffect>();

    // Private variables
    private float[] spectrumData;
    private float sampleRate;

    [System.Serializable]
    public class AudioReactiveEffect
    {
        [Tooltip("Name for this effect mapping (for organization)")]
        public string mappingName = "New Effect Mapping";

        [Header("Frequency Settings")]
        [Tooltip("Minimum frequency to monitor (Hz)")]
        public float minFrequency = 60f;

        [Tooltip("Maximum frequency to monitor (Hz)")]
        public float maxFrequency = 200f;

        [Tooltip("Sensitivity multiplier for effect")]
        [Range(1f, 100f)]
        public float sensitivity = 10f;

        [Tooltip("How quickly the effect returns to normal")]
        [Range(0.1f, 10f)]
        public float falloffSpeed = 2f;

        [Header("Effect Target")]
        [Tooltip("The post-processing volume to modify")]
        public PostProcessVolume postProcessVolume;

        [Tooltip("Which effect to modify")]
        public EffectType effectType = EffectType.Bloom;

        [Tooltip("Which property of the effect to modify")]
        public PropertyType propertyType = PropertyType.Intensity;

        [Header("Effect Mapping")]
        [Tooltip("Minimum value for the effect")]
        public float minEffectValue = 0f;

        [Tooltip("Maximum value for the effect")]
        public float maxEffectValue = 1f;

        [Tooltip("Invert the effect response (high frequencies = low value)")]
        public bool invertResponse = false;

        [Tooltip("Apply smoothing to the effect changes")]
        public bool smoothEffect = true;

        [Tooltip("How much to smooth the effect (higher = smoother)")]
        [Range(1f, 30f)]
        public float smoothingFactor = 5f;

        [HideInInspector]
        public int minFreqIndex;

        [HideInInspector]
        public int maxFreqIndex;

        [HideInInspector]
        public float currentEffectValue;

        [HideInInspector]
        public float targetEffectValue;

        // Property types for each effect
        public enum PropertyType
        {
            // Bloom properties
            Intensity,
            Threshold,
            SoftKnee,
            Diffusion,
            Color,

            // Chromatic Aberration properties
            ChromaticIntensity,

            // Color Grading properties
            Temperature,
            Tint,
            PostExposure,
            Hue,
            Saturation,
            Contrast,

            // Depth of Field properties
            FocusDistance,
            Aperture,
            FocalLength,

            // Grain properties
            GrainIntensity,
            GrainSize,
            GrainColored,

            // Lens Distortion properties
            DistortionIntensity,
            DistortionScale,

            // Motion Blur properties
            ShutterAngle,
            SampleCount,

            // Vignette properties
            VignetteIntensity,
            VignetteSmoothness,
            VignetteRoundness,
            VignetteColor
        }
    }

    public enum EffectType
    {
        Bloom,
        ChromaticAberration,
        ColorGrading,
        DepthOfField,
        Grain,
        LensDistortion,
        MotionBlur,
        Vignette
    }

    void OnValidate()
    {
        // This gets called in the editor when values change
        // Ensure there's always at least one effect mapping
        if (reactiveEffects.Count == 0)
        {
            reactiveEffects.Add(new AudioReactiveEffect());
        }
    }

    void Start()
    {
        // Initialize the spectrum data array
        spectrumData = new float[sampleSize];

        // Get the sample rate from the audio source
        sampleRate = AudioSettings.outputSampleRate;

        // Initialize all effect mappings
        foreach (var effect in reactiveEffects)
        {
            // Calculate the frequency bin indices
            CalculateFrequencyIndices(effect);

            // Set initial effect values
            effect.currentEffectValue = effect.minEffectValue;
            effect.targetEffectValue = effect.minEffectValue;
        }
    }

    void CalculateFrequencyIndices(AudioReactiveEffect effect)
    {
        // Convert Hz frequencies to spectrum data indices
        // Each bin represents (sampleRate / 2) / sampleSize Hz
        float binSize = sampleRate / 2f / sampleSize;

        effect.minFreqIndex = Mathf.FloorToInt(effect.minFrequency / binSize);
        effect.maxFreqIndex = Mathf.FloorToInt(effect.maxFrequency / binSize);

        // Clamp indices to valid range
        effect.minFreqIndex = Mathf.Clamp(effect.minFreqIndex, 0, sampleSize - 1);
        effect.maxFreqIndex = Mathf.Clamp(effect.maxFreqIndex, effect.minFreqIndex, sampleSize - 1);

        Debug.Log($"Mapping '{effect.mappingName}': Monitoring frequency range {effect.minFrequency}Hz - {effect.maxFrequency}Hz (bins {effect.minFreqIndex} - {effect.maxFreqIndex})");
    }

    void Update()
    {
        if (targetAudioSource == null || !targetAudioSource.isPlaying)
            return;

        // Get the spectrum data from the audio source
        targetAudioSource.GetSpectrumData(spectrumData, 0, FFTWindow.Blackman);

        // Process each effect mapping
        foreach (var effect in reactiveEffects)
        {
            // Skip if volume not assigned
            if (effect.postProcessVolume == null)
                continue;

            // Calculate the average amplitude in this effect's frequency range
            float sum = 0f;
            for (int i = effect.minFreqIndex; i <= effect.maxFreqIndex; i++)
            {
                sum += spectrumData[i];
            }

            float average = sum / (effect.maxFreqIndex - effect.minFreqIndex + 1);

            // Apply sensitivity
            float amplitudeValue = average * effect.sensitivity;

            // Apply inversion if needed
            if (effect.invertResponse)
            {
                amplitudeValue = 1f - Mathf.Clamp01(amplitudeValue);
            }
            else
            {
                amplitudeValue = Mathf.Clamp01(amplitudeValue);
            }

            // Set the target effect value based on the amplitude
            effect.targetEffectValue = Mathf.Lerp(effect.minEffectValue, effect.maxEffectValue, amplitudeValue);

            // Apply falloff
            if (effect.targetEffectValue < effect.currentEffectValue)
            {
                effect.currentEffectValue = Mathf.Lerp(effect.currentEffectValue, effect.targetEffectValue, effect.falloffSpeed * Time.deltaTime);
            }
            else if (effect.smoothEffect)
            {
                effect.currentEffectValue = Mathf.Lerp(effect.currentEffectValue, effect.targetEffectValue, Time.deltaTime * effect.smoothingFactor);
            }
            else
            {
                effect.currentEffectValue = effect.targetEffectValue;
            }

            // Apply the effect value
            ApplyEffectValue(effect);
        }
    }

    void ApplyEffectValue(AudioReactiveEffect effect)
    {
        // Get the post-processing profile
        PostProcessProfile profile = effect.postProcessVolume.profile;
        if (profile == null)
            return;

        // Apply based on effect type and property
        switch (effect.effectType)
        {
            case EffectType.Bloom:
                if (profile.TryGetSettings(out Bloom bloomEffect))
                {
                    switch (effect.propertyType)
                    {
                        case AudioReactiveEffect.PropertyType.Intensity:
                            bloomEffect.intensity.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.Threshold:
                            bloomEffect.threshold.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.SoftKnee:
                            bloomEffect.softKnee.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.Diffusion:
                            bloomEffect.diffusion.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.Color:
                            // For color, we'll adjust the intensity while keeping hue and saturation
                            Color bloomColor = bloomEffect.color.value;
                            float h, s, v;
                            Color.RGBToHSV(bloomColor, out h, out s, out v);
                            v = effect.currentEffectValue;
                            bloomEffect.color.value = Color.HSVToRGB(h, s, v);
                            break;
                    }
                }
                break;

            case EffectType.ChromaticAberration:
                if (profile.TryGetSettings(out ChromaticAberration chromaticAberration))
                {
                    if (effect.propertyType == AudioReactiveEffect.PropertyType.ChromaticIntensity)
                    {
                        chromaticAberration.intensity.value = effect.currentEffectValue;
                    }
                }
                break;

            case EffectType.ColorGrading:
                if (profile.TryGetSettings(out ColorGrading colorGrading))
                {
                    switch (effect.propertyType)
                    {
                        case AudioReactiveEffect.PropertyType.Temperature:
                            colorGrading.temperature.value = Mathf.Lerp(-100f, 100f, effect.currentEffectValue);
                            break;
                        case AudioReactiveEffect.PropertyType.Tint:
                            colorGrading.tint.value = Mathf.Lerp(-100f, 100f, effect.currentEffectValue);
                            break;
                        case AudioReactiveEffect.PropertyType.PostExposure:
                            colorGrading.postExposure.value = Mathf.Lerp(-5f, 5f, effect.currentEffectValue);
                            break;
                        case AudioReactiveEffect.PropertyType.Hue:
                            colorGrading.hueShift.value = Mathf.Lerp(-180f, 180f, effect.currentEffectValue);
                            break;
                        case AudioReactiveEffect.PropertyType.Saturation:
                            colorGrading.saturation.value = Mathf.Lerp(-100f, 100f, effect.currentEffectValue);
                            break;
                        case AudioReactiveEffect.PropertyType.Contrast:
                            colorGrading.contrast.value = Mathf.Lerp(-100f, 100f, effect.currentEffectValue);
                            break;
                    }
                }
                break;

            case EffectType.DepthOfField:
                if (profile.TryGetSettings(out DepthOfField depthOfField))
                {
                    switch (effect.propertyType)
                    {
                        case AudioReactiveEffect.PropertyType.FocusDistance:
                            depthOfField.focusDistance.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.Aperture:
                            depthOfField.aperture.value = Mathf.Lerp(0.1f, 32f, effect.currentEffectValue);
                            break;
                        case AudioReactiveEffect.PropertyType.FocalLength:
                            depthOfField.focalLength.value = Mathf.Lerp(1f, 300f, effect.currentEffectValue);
                            break;
                    }
                }
                break;

            case EffectType.Grain:
                if (profile.TryGetSettings(out Grain grain))
                {
                    switch (effect.propertyType)
                    {
                        case AudioReactiveEffect.PropertyType.GrainIntensity:
                            grain.intensity.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.GrainSize:
                            grain.size.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.GrainColored:
                            // Boolean properties need special handling
                            grain.colored.value = effect.currentEffectValue > 0.5f;
                            break;
                    }
                }
                break;

            case EffectType.LensDistortion:
                if (profile.TryGetSettings(out LensDistortion lensDistortion))
                {
                    switch (effect.propertyType)
                    {
                        case AudioReactiveEffect.PropertyType.DistortionIntensity:
                            lensDistortion.intensity.value = Mathf.Lerp(-100f, 100f, effect.currentEffectValue);
                            break;
                        case AudioReactiveEffect.PropertyType.DistortionScale:
                            lensDistortion.scale.value = effect.currentEffectValue;
                            break;
                    }
                }
                break;

            case EffectType.MotionBlur:
                if (profile.TryGetSettings(out MotionBlur motionBlur))
                {
                    switch (effect.propertyType)
                    {
                        case AudioReactiveEffect.PropertyType.ShutterAngle:
                            motionBlur.shutterAngle.value = effect.currentEffectValue * 360f;
                            break;
                        case AudioReactiveEffect.PropertyType.SampleCount:
                            // For sample count, we need to convert to int and clamp to valid range
                            int sampleCount = Mathf.RoundToInt(Mathf.Lerp(4, 32, effect.currentEffectValue));
                            motionBlur.sampleCount.value = sampleCount;
                            break;
                    }
                }
                break;

            case EffectType.Vignette:
                if (profile.TryGetSettings(out Vignette vignette))
                {
                    switch (effect.propertyType)
                    {
                        case AudioReactiveEffect.PropertyType.VignetteIntensity:
                            vignette.intensity.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.VignetteSmoothness:
                            vignette.smoothness.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.VignetteRoundness:
                            vignette.roundness.value = effect.currentEffectValue;
                            break;
                        case AudioReactiveEffect.PropertyType.VignetteColor:
                            // For color, modify the intensity/brightness
                            Color vigColor = vignette.color.value;
                            float h, s, v;
                            Color.RGBToHSV(vigColor, out h, out s, out v);
                            v = effect.currentEffectValue;
                            vignette.color.value = Color.HSVToRGB(h, s, v);
                            break;
                    }
                }
                break;
        }
    }

    /*
    void OnGUI()
    {
        // Optional debug display - remove or comment out for final build
        GUILayout.BeginVertical("box");
        GUILayout.Label("Audio Reactive Effects Debug:");

        foreach (var effect in reactiveEffects)
        {
            GUILayout.Label($"{effect.mappingName}: {effect.currentEffectValue:F2}");
        }

        GUILayout.EndVertical();
    }
    */

    // Used for the editor to add a new effect mapping
    public void AddNewEffectMapping()
    {
        reactiveEffects.Add(new AudioReactiveEffect()
        {
            mappingName = "New Effect " + (reactiveEffects.Count + 1)
        });
    }
}

#if UNITY_EDITOR
// Custom editor script to add the "Add Effect" button
[UnityEditor.CustomEditor(typeof(AdvancedAudioReactivePostProcessing))]
public class AdvancedAudioReactivePostProcessingEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector
        DrawDefaultInspector();

        // Add a button to add a new effect mapping
        if (GUILayout.Button("Add New Effect Mapping", GUILayout.Height(30)))
        {
            AdvancedAudioReactivePostProcessing script = (AdvancedAudioReactivePostProcessing)target;
            script.AddNewEffectMapping();
        }
    }
}
#endif