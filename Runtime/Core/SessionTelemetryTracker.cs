using System;
using UnityEngine;

namespace LevelLens.Runtime.Core
{
    /// <summary>
    /// Tracks active gameplay playtime, restart counts, fail counts, and revive counts for the current test run.
    /// Excludes loading screens, win/fail sequences, and pause screens.
    /// </summary>
    public class SessionTelemetryTracker
    {
        private readonly ILevelLensAdapter _adapter;
        private readonly Func<bool> _isSessionActive;
        private bool _isTrackingPlaytime;
        private float _activePlaytimeSeconds;
        private int _failCount;
        private int _reviveCount;
        private int _restartCount;

        public int SessionFails => _failCount;
        public int SessionRevives => _reviveCount;
        public int SessionRestarts => _restartCount;
        public int ActivePlaytimeSeconds => Mathf.RoundToInt(_activePlaytimeSeconds);
        public float RawPlaytimeSeconds => _activePlaytimeSeconds;

        private bool IsActive => _isSessionActive == null || _isSessionActive();

        public SessionTelemetryTracker(ILevelLensAdapter adapter, Func<bool> isSessionActive = null)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _isSessionActive = isSessionActive;

            _adapter.OnLevelReady += HandleLevelReady;
            _adapter.OnReviveUsed += HandleReviveUsed;
            _adapter.OnLevelFailed += HandleLevelFailed;
            _adapter.OnLevelRestarted += HandleLevelRestarted;
            _adapter.OnLevelWon += HandleLevelWon;
        }

        public void Dispose()
        {
            _adapter.OnLevelReady -= HandleLevelReady;
            _adapter.OnReviveUsed -= HandleReviveUsed;
            _adapter.OnLevelFailed -= HandleLevelFailed;
            _adapter.OnLevelRestarted -= HandleLevelRestarted;
            _adapter.OnLevelWon -= HandleLevelWon;
        }

        public void ResetSession()
        {
            _activePlaytimeSeconds = 0f;
            _failCount = 0;
            _reviveCount = 0;
            _restartCount = 0;
            _isTrackingPlaytime = false;
        }

        public void Update(float deltaTime)
        {
            if (IsActive && _isTrackingPlaytime && Time.timeScale > 0f)
            {
                _activePlaytimeSeconds += deltaTime;
            }
        }

        private void HandleLevelReady()
        {
            if (!IsActive) return;
            _isTrackingPlaytime = true;
        }

        private void HandleReviveUsed()
        {
            if (!IsActive) return;
            _reviveCount++;
            _isTrackingPlaytime = true;
        }

        private void HandleLevelFailed()
        {
            if (!IsActive) return;
            _failCount++;
            _isTrackingPlaytime = false;
        }

        private void HandleLevelRestarted()
        {
            if (!IsActive) return;
            _restartCount++;
            _isTrackingPlaytime = false;
        }

        private void HandleLevelWon()
        {
            _isTrackingPlaytime = false;
        }
    }
}