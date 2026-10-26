using System;
using System.IO;
using System.Collections;
using UnityEngine;
using LevelLens.Runtime.Media;
using LevelLens.Runtime.Network;
using LevelLens.Runtime.UI;

namespace LevelLens.Runtime.Core
{
    public enum LevelLensStatus
    {
        auto,       // Active in Editor and Development Builds; Dormant in Release
        enabled,    // Force Active (for explicit QA testing in release builds)
        disabled    // Force Inactive
    }

    /// <summary>
    /// Drop-in root manager for LevelLens in Unity. Coordinates lifecycle events,
    /// telemetry tracking, screenshot captures, and report UI presentation.
    /// </summary>
    public class LevelLensManager : MonoBehaviour
    {
        private static LevelLensManager _instance;
        public static LevelLensManager Instance => _instance;

        [Header("Tool Status & Credentials")]
        [SerializeField] private LevelLensStatus m_Status = LevelLensStatus.auto;
        [SerializeField] private string m_ApiHost = "https://levellens.creoapp.workers.dev";
        [SerializeField] private string m_ApiKey = "ll_live_token_7a8b9c";

        [Header("UI & Presentation")]
        [SerializeField] private LevelLensReviewPanel m_ReviewPanel;
        [SerializeField] private FloatingToggleButton m_FloatingButton;

        [Header("Capture Settings")]
        [SerializeField] private int m_ScreenshotWidth = 1920;
        [SerializeField] private int m_ScreenshotHeight = 1080;

        private ILevelLensAdapter _adapter;
        private SessionTelemetryTracker _telemetryTracker;
        private string _latestScreenshotPath;
        private bool _isLevelLensActive;

        public bool IsActive => _isLevelLensActive;
        public ILevelLensAdapter Adapter => _adapter;
        public SessionTelemetryTracker Telemetry => _telemetryTracker;
        public string ApiHost => m_ApiHost;
        public string ApiKey => m_ApiKey;
        public string LatestScreenshotPath => _latestScreenshotPath;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            if (!ShouldActivate())
            {
                gameObject.SetActive(false);
                return;
            }

            _isLevelLensActive = true;
        }

        private bool ShouldActivate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return m_Status == LevelLensStatus.auto || m_Status == LevelLensStatus.enabled;
#else
            return m_Status == LevelLensStatus.enabled;
#endif
        }

        public void BindAdapter(ILevelLensAdapter adapter)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _telemetryTracker = new SessionTelemetryTracker(_adapter);

            _adapter.OnLevelReady += OnLevelReady;
            _adapter.OnLevelWon += OnLevelWon;
        }

        private void OnDestroy()
        {
            if (_adapter != null)
            {
                _adapter.OnLevelReady -= OnLevelReady;
                _adapter.OnLevelWon -= OnLevelWon;
            }

            _telemetryTracker?.Dispose();
        }

        private void Update()
        {
            if (_telemetryTracker != null)
            {
                _telemetryTracker.Update(Time.deltaTime);
            }
        }

        private void OnLevelReady()
        {
            StartCoroutine(CaptureDelayedScreenshot(2.0f));
        }

        private void OnLevelWon()
        {
            OpenReviewPanel();
        }

        private IEnumerator CaptureDelayedScreenshot(float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);

            string fileName = $"thumb_{DateTime.UtcNow.Ticks}.jpg";
            string dir = Path.Combine(Application.persistentDataPath, "LevelLens", "media");
            _latestScreenshotPath = Path.Combine(dir, fileName);

            yield return ScreenshotCapturer.CaptureCleanScreenshotCoroutine(
                Camera.main,
                m_ScreenshotWidth,
                m_ScreenshotHeight,
                _latestScreenshotPath,
                path =>
                {
                    Debug.Log($"[LevelLens] Captured clean gameplay screenshot: {path}");
                });
        }

        public void RestartForTesting()
        {
            _telemetryTracker?.ResetSession();
            CloseReviewPanel();
            _adapter?.RestartLevel();
        }

        public void OpenReviewPanel()
        {
            _adapter?.SetPaused(true);
            if (m_ReviewPanel != null)
            {
                m_ReviewPanel.Open();
            }
        }

        public void CloseReviewPanel()
        {
            if (m_ReviewPanel != null)
            {
                m_ReviewPanel.Close();
            }
            _adapter?.SetPaused(false);
        }
    }
}
