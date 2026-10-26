using System;
using UnityEngine;

namespace LevelLens.Runtime.Core
{
    public class SessionTelemetryTracker : MonoBehaviour
    {
        private ILevelLensAdapter _adapter;
        private int _sessionFails;
        private int _sessionRevives;
        private float _activePlaytimeSeconds;
        private bool _isTrackingPlaytime;

        public int SessionFails => _sessionFails;
        public int SessionRevives => _sessionRevives;
        public int ActivePlaytimeSeconds => Mathf.RoundToInt(_activePlaytimeSeconds);

        public void BindAdapter(ILevelLensAdapter adapter)
        {
            UnbindAdapter();
            _adapter = adapter;
            if (_adapter != null)
            {
                _adapter.OnLevelReady += HandleLevelReady;
                _adapter.OnReviveUsed += HandleReviveUsed;
                _adapter.OnLevelFailed += HandleLevelFailed;
                _adapter.OnLevelWon += HandleLevelWon;
            }
        }

        public void UnbindAdapter()
        {
            if (_adapter != null)
            {
                _adapter.OnLevelReady -= HandleLevelReady;
                _adapter.OnReviveUsed -= HandleReviveUsed;
                _adapter.OnLevelFailed -= HandleLevelFailed;
                _adapter.OnLevelWon -= HandleLevelWon;
                _adapter = null;
            }
        }

        private void OnDestroy()
        {
            UnbindAdapter();
        }

        public void ResetSessionCounters()
        {
            _sessionFails = 0;
            _sessionRevives = 0;
            _activePlaytimeSeconds = 0f;
            _isTrackingPlaytime = false;
        }

        private void HandleLevelReady()
        {
            _isTrackingPlaytime = true;
        }

        private void HandleReviveUsed()
        {
            _sessionRevives++;
            _isTrackingPlaytime = true;
        }

        private void HandleLevelFailed()
        {
            _sessionFails++;
            _isTrackingPlaytime = false;
        }

        private void HandleLevelWon()
        {
            _isTrackingPlaytime = false;
        }

        private void Update()
        {
            if (_isTrackingPlaytime && Time.timeScale > 0)
            {
                _activePlaytimeSeconds += Time.unscaledDeltaTime;
            }
        }
    }
}
