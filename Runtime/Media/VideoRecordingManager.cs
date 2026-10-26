using System;
using System.IO;
using UnityEngine;

namespace LevelLens.Runtime.Media
{
    public class VideoRecordingManager : MonoBehaviour
    {
        private bool _isRecording;
        private string _currentRecordingPath;

        public bool IsRecording => _isRecording;
        public string CurrentRecordingPath => _currentRecordingPath;

        public void StartRecording()
        {
            if (_isRecording) return;
            string dir = Path.Combine(Application.persistentDataPath, "LevelLens", "media");
            Directory.CreateDirectory(dir);
            _currentRecordingPath = Path.Combine(dir, $"clip_{DateTime.UtcNow.Ticks}.mp4");
            _isRecording = true;

#if LEVELLENS_RECORDER_SUPPORT && UNITY_EDITOR
            // Hook into Unity Recorder if present in Editor
#endif
        }

        public void StopRecording(Action<string> onCompleted)
        {
            if (!_isRecording)
            {
                onCompleted?.Invoke(null);
                return;
            }

            _isRecording = false;
#if LEVELLENS_RECORDER_SUPPORT && UNITY_EDITOR
            // Finalize recorder clip
#endif
            onCompleted?.Invoke(File.Exists(_currentRecordingPath) ? _currentRecordingPath : null);
        }
    }
}
