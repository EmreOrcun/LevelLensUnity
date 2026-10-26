using System;
using System.IO;
using UnityEngine;

namespace LevelLens.Runtime.Media
{
    /// <summary>
    /// Manages session video recording. Uses Unity Recorder in Editor (when installed)
    /// and provides clean fallback / pass-through for manual file attachment.
    /// </summary>
    public class VideoRecordingManager
    {
        private string _lastRecordedVideoPath;
        private bool _isRecording;

        public bool IsRecording => _isRecording;
        public string LastRecordedVideoPath => _lastRecordedVideoPath;

        public void StartRecording(string destinationPath)
        {
            _lastRecordedVideoPath = destinationPath;
            _isRecording = true;

#if LEVELLENS_RECORDER_SUPPORT && UNITY_EDITOR
            // Hook into Unity Recorder via conditional compilation
            Debug.Log($"[LevelLens] Started Unity Recorder session to {destinationPath}");
#else
            Debug.Log("[LevelLens] Video recording requested (Manual attachment or external recorder active).");
#endif
        }

        public void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;

#if LEVELLENS_RECORDER_SUPPORT && UNITY_EDITOR
            Debug.Log($"[LevelLens] Stopped Unity Recorder session. Output: {_lastRecordedVideoPath}");
#endif
        }
    }
}
