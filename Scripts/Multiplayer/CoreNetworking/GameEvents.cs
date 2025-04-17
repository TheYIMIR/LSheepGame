using UnityEngine;
using System;

/// <summary>
/// Global event system for the game to help with communication between components
/// </summary>
public static class GameEvents
{
    // Sheep death event - triggered when any sheep dies
    public static event Action<GameObject> onSheepDied;

    // Method to trigger the sheep death event
    public static void TriggerSheepDeath(GameObject sheep)
    {
        Debug.Log($"GameEvents: Triggering sheep death event for {sheep.name}");
        onSheepDied?.Invoke(sheep);
    }

    // Game start event
    public static event Action onGameStarted;

    // Method to trigger the game start event
    public static void TriggerGameStarted()
    {
        Debug.Log("GameEvents: Game started event triggered");
        onGameStarted?.Invoke();
    }

    // Game over event
    public static event Action<bool> onGameOver;

    // Method to trigger the game over event (bool indicates player victory)
    public static void TriggerGameOver(bool playerWon)
    {
        Debug.Log($"GameEvents: Game over event triggered. Player won: {playerWon}");
        onGameOver?.Invoke(playerWon);
    }
}