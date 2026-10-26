using System;
using UnityEngine;
using LevelLens.Runtime.Network;
using LevelLens.Runtime.Media;
using LevelLens.Runtime.UI;

namespace LevelLens.Runtime.Core
{
    public enum LevelLensStatus
    {
        auto,       // Active in Editor and Development Builds; Inactive in Release
        enabled,    // Force Active in all builds (for explicit QA testing in release builds)
        disabled    // Force Inactive
    }

    [DefaultExecutionOrder(-1000)]
    public class LevelLensManager : MonoBehaviour
    {
        public static LevelLensManager Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private LevelLensStatus m_Status = LevelLensStatus.auto;
        [SerializeField] private string m_ApiHost = "https://levellens-api.yourstudio.com";
        [SerializeField] private string m_ApiKey = "ll_live_dev_token";

        [Header("Runtime Subsystems")]
        [SerializeField] private SessionTelemetryTracker m_Telemetry;
        [SerializeField] private ScreenshotCapturer m_ScreenshotCapturer;
        [SerializeField] private VideoRecordingManager m_VideoRecorder;
        [SerializeField] private R2DirectUploader m_Uploader;
        [SerializeField] private OfflineDraftCache m_DraftCache;
        [SerializeField] private LevelLensReviewPanel m_ReviewPanel;
        [SerializeField] private GameObject m_FloatingButton;

        private ILevelLensAdapter _adapter;

        public ILevelLensAdapter Adapter => _adapter;
        public SessionTelemetryTracker Telemetry => m_Telemetry;
        public ScreenshotCapturer ScreenshotCapturer => m_ScreenshotCapturer;
        public VideoRecordingManager VideoRecorder => m_VideoRecorder;
        public R2DirectUploader Uploader => m_Uploader;
        public OfflineDraftCache DraftCache => m_DraftCache;
        public string ApiHost => m_ApiHost;
        public string ApiKey => m_ApiKey;

        public bool IsActive => ShouldActivate();

        private bool ShouldActivate()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return m_Status == LevelLensStatus.auto || m_Status == LevelLensStatus.enabled;
#else
            return m_Status == LevelLensStatus.enabled;
#endif
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (!ShouldActivate())
            {
                gameObject.SetActive(false);
                return;
            }

            _adapter = GetComponentInChildren<ILevelLensAdapter>() ?? GetComponentInParent<ILevelLensAdapter>();
            if (_adapter == null)
            {
                // Fallback search across scene
                var mono = FindObjectOfType<MonoBehaviour>();
                if (mono is ILevelLensAdapter found) _adapter = found;
            }

            if (m_Uploader != null)
            {
                m_Uploader.Initialize(m_ApiHost, m_ApiKey);
            }
        }

        public void RegisterAdapter(ILevelLensAdapter adapter)
        {
            _adapter = adapter;
            if (m_Telemetry != null && _adapter != null)
            {
                m_Telemetry.BindAdapter(_adapter);
            }
        }

        public void OpenReviewPanel()
        {
            if (!IsActive) return;
            if (_adapter != null)
            {
                _adapter.SetPaused(true);
            }
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
            if (_adapter != null)
            {
                _adapter.SetPaused(false);
            }
        }

        public void RestartForTesting()
        {
            if (_adapter == null) return;
            m_Telemetry?.ResetSessionCounters();
            CloseReviewPanel();
            _adapter.RestartLevel();
        }
    }
}
