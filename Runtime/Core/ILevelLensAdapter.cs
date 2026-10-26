using System;

namespace LevelLens.Runtime.Core
{
    /// <summary>
    /// Minimal decoupled adapter interface connecting any studio game to LevelLens.
    /// Games provide an implementation in game-specific code to expose state and lifecycle hooks.
    /// </summary>
    public interface ILevelLensAdapter
    {
        string CurrentLevelId { get; }
        string CurrentLevelHolderName { get; }
        int CurrentLevelIndex { get; }
        void RestartLevel();
        void SetPaused(bool paused);
        event Action OnLevelReady;
        event Action OnLevelWon;
        event Action OnLevelFailed;
        event Action OnReviveUsed;
    }
}
