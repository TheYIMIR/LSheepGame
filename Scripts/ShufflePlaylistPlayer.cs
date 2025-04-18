using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages a playlist of audio clips that plays in shuffle mode
/// </summary>
public class ShufflePlaylistPlayer : MonoBehaviour
{
    [Header("Audio Settings")]
    [Tooltip("The AudioSource that will play the songs")]
    public AudioSource audioSource;

    [Tooltip("List of audio clips to include in the playlist")]
    public List<AudioClip> playlist = new List<AudioClip>();

    [Header("Player Settings")]
    [Tooltip("Start playing automatically when the scene loads")]
    public bool playOnAwake = true;

    [Tooltip("Volume for playback")]
    [Range(0f, 1f)]
    public float volume = 1.0f;

    [Tooltip("If enabled, will automatically play the next song when one finishes")]
    public bool autoAdvance = true;

    [Header("Debug Info")]
    [SerializeField]
    [Tooltip("Current song index in the shuffled sequence")]
    private int currentShuffleIndex = -1;

    [SerializeField]
    [Tooltip("Name of the currently playing song")]
    private string currentSongName = "None";

    // Private variables
    private List<int> shuffleSequence = new List<int>();
    private List<int> playHistory = new List<int>();
    private int historyIndex = -1;
    private bool isInitialized = false;

    void Awake()
    {
        // Create audio source if not assigned
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Set audio source properties
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.volume = volume;

        // Initialize the playlist
        if (playlist.Count > 0)
        {
            Initialize();

            if (playOnAwake)
            {
                Play();
            }
        }
        else
        {
            Debug.LogWarning("Playlist is empty. Add audio clips in the inspector.");
        }
    }

    void Update()
    {
        // Check if the current song has finished and we need to advance
        if (autoAdvance && audioSource.clip != null && !audioSource.isPlaying && isInitialized)
        {
            NextSong();
        }

        // Update volume in case it was changed in the inspector
        if (audioSource.volume != volume)
        {
            audioSource.volume = volume;
        }
    }

    /// <summary>
    /// Initializes or reinitializes the shuffle playlist
    /// </summary>
    public void Initialize()
    {
        if (playlist.Count == 0) return;

        // Generate the shuffle sequence
        GenerateShuffleSequence();

        // Reset history
        playHistory.Clear();
        historyIndex = -1;
        currentShuffleIndex = -1;

        isInitialized = true;

        Debug.Log("Shuffle playlist initialized with " + playlist.Count + " songs");
    }

    /// <summary>
    /// Generates a random sequence for playing the playlist
    /// </summary>
    private void GenerateShuffleSequence()
    {
        // Clear existing sequence
        shuffleSequence.Clear();

        // Create list of indices
        List<int> indices = new List<int>();
        for (int i = 0; i < playlist.Count; i++)
        {
            indices.Add(i);
        }

        // Shuffle the indices
        while (indices.Count > 0)
        {
            int randomIndex = Random.Range(0, indices.Count);
            shuffleSequence.Add(indices[randomIndex]);
            indices.RemoveAt(randomIndex);
        }

        Debug.Log("Shuffle sequence generated");
    }

    /// <summary>
    /// Starts or resumes playback
    /// </summary>
    public void Play()
    {
        if (!isInitialized)
        {
            Initialize();
        }

        if (audioSource.clip == null)
        {
            // If nothing is loaded, start with the first song
            NextSong();
        }
        else
        {
            // Resume playing the current song
            audioSource.Play();
        }
    }

    /// <summary>
    /// Pauses the current playback
    /// </summary>
    public void Pause()
    {
        audioSource.Pause();
    }

    /// <summary>
    /// Stops playback and unloads the current song
    /// </summary>
    public void Stop()
    {
        audioSource.Stop();
        audioSource.clip = null;
        currentSongName = "None";
    }

    /// <summary>
    /// Plays the next song in the shuffle sequence
    /// </summary>
    public void NextSong()
    {
        if (playlist.Count == 0) return;

        if (!isInitialized)
        {
            Initialize();
        }

        // If we're navigating through history, clear forward history
        if (historyIndex >= 0 && historyIndex < playHistory.Count - 1)
        {
            playHistory.RemoveRange(historyIndex + 1, playHistory.Count - historyIndex - 1);
        }

        // Move to next song in sequence
        currentShuffleIndex++;

        // If we reached the end, generate a new shuffle sequence
        if (currentShuffleIndex >= shuffleSequence.Count)
        {
            // Save the last song index to avoid immediate repeat
            int lastSongIndex = -1;
            if (shuffleSequence.Count > 0)
            {
                lastSongIndex = shuffleSequence[shuffleSequence.Count - 1];
            }

            GenerateShuffleSequence();
            currentShuffleIndex = 0;

            // Make sure the first song in the new sequence isn't the same as the last one
            if (playlist.Count > 1 && shuffleSequence[0] == lastSongIndex)
            {
                // Swap first element with another random element
                int swapIndex = Random.Range(1, shuffleSequence.Count);
                int temp = shuffleSequence[0];
                shuffleSequence[0] = shuffleSequence[swapIndex];
                shuffleSequence[swapIndex] = temp;
            }
        }

        // Get the actual playlist index
        int playlistIndex = shuffleSequence[currentShuffleIndex];

        // Add to history
        playHistory.Add(playlistIndex);
        historyIndex = playHistory.Count - 1;

        // Load and play the song
        PlaySongAtIndex(playlistIndex);
    }

    /// <summary>
    /// Plays the previous song from the play history
    /// </summary>
    public void PreviousSong()
    {
        if (playHistory.Count < 2 || historyIndex <= 0) return;

        // Move back in history
        historyIndex--;
        int playlistIndex = playHistory[historyIndex];

        // Play the song without modifying history
        PlaySongAtIndex(playlistIndex, false);
    }

    /// <summary>
    /// Loads and plays a song at the specified playlist index
    /// </summary>
    private void PlaySongAtIndex(int playlistIndex, bool updateCurrentShuffleIndex = true)
    {
        if (playlistIndex < 0 || playlistIndex >= playlist.Count) return;

        // Update the current shuffle index if needed
        if (updateCurrentShuffleIndex)
        {
            currentShuffleIndex = shuffleSequence.IndexOf(playlistIndex);
        }

        // Stop current playback
        audioSource.Stop();

        // Load and play the new song
        AudioClip clip = playlist[playlistIndex];
        audioSource.clip = clip;
        currentSongName = clip != null ? clip.name : "Unknown";

        // Start playing
        audioSource.Play();

        Debug.Log("Now playing: " + currentSongName);
    }

    /// <summary>
    /// Gets the index of the currently playing song in the original playlist
    /// </summary>
    public int GetCurrentPlaylistIndex()
    {
        if (currentShuffleIndex >= 0 && currentShuffleIndex < shuffleSequence.Count)
        {
            return shuffleSequence[currentShuffleIndex];
        }
        return -1;
    }

    /// <summary>
    /// Gets the name of the currently playing song
    /// </summary>
    public string GetCurrentSongName()
    {
        return currentSongName;
    }

    /// <summary>
    /// Add a song to the playlist
    /// </summary>
    public void AddToPlaylist(AudioClip clip)
    {
        if (clip != null)
        {
            playlist.Add(clip);

            // If this is the first song, initialize
            if (playlist.Count == 1 && !isInitialized)
            {
                Initialize();
                if (playOnAwake)
                {
                    Play();
                }
            }
            else
            {
                // Add to shuffle sequence
                shuffleSequence.Add(playlist.Count - 1);

                // Re-shuffle to integrate the new song
                GenerateShuffleSequence();
            }
        }
    }

    /// <summary>
    /// Remove a song from the playlist
    /// </summary>
    public void RemoveFromPlaylist(AudioClip clip)
    {
        int index = playlist.IndexOf(clip);
        if (index >= 0)
        {
            playlist.RemoveAt(index);

            // Regenerate shuffle sequence
            GenerateShuffleSequence();

            // Reset current index if the current song was removed
            if (audioSource.clip == clip)
            {
                audioSource.Stop();
                audioSource.clip = null;
                currentSongName = "None";

                // Start the next song if available
                if (playlist.Count > 0 && autoAdvance)
                {
                    NextSong();
                }
            }
        }
    }

    /// <summary>
    /// Clears the playlist
    /// </summary>
    public void ClearPlaylist()
    {
        Stop();
        playlist.Clear();
        shuffleSequence.Clear();
        playHistory.Clear();
        historyIndex = -1;
        currentShuffleIndex = -1;
        isInitialized = false;
    }
}