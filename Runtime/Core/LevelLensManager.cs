using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
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
        private bool _showGuiReview;
        private bool _isSubmitting;
        private int _guiScore = 8;
        private string _guiDifficulty = "Medium";
        private string _guiNote = "";
        private string _guiSubmitStatus = "";

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
            else
            {
                _showGuiReview = true;
                _guiSubmitStatus = "";
            }
        }

        public void CloseReviewPanel()
        {
            if (m_ReviewPanel != null)
            {
                m_ReviewPanel.Close();
            }
            _showGuiReview = false;
            _adapter?.SetPaused(false);
        }

        private void OnGUI()
        {
            if (!_isLevelLensActive || _adapter == null) return;

            Matrix4x4 origMatrix = GUI.matrix;
            float refWidth = Screen.width < Screen.height ? 720f : 1280f;
            float scale = Screen.width / refWidth;
            float virtualWidth = refWidth;
            float virtualHeight = Screen.height / scale;

            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1.0f));

            // 1. Floating trigger button
            if (m_FloatingButton == null && !_showGuiReview)
            {
                float btnW = 135;
                float btnH = 50;
                Rect btnRect = new Rect(virtualWidth - btnW - 16, 24, btnW, btnH);
                GUI.backgroundColor = new Color(0.2f, 0.45f, 0.9f, 0.95f);
                if (GUI.Button(btnRect, "<b><size=18>LevelLens</size></b>"))
                {
                    OpenReviewPanel();
                }
                GUI.backgroundColor = Color.white;
            }

            // 2. Immediate IMGUI review dialog fallback
            if (_showGuiReview && m_ReviewPanel == null)
            {
                float panelW = Mathf.Min(640, virtualWidth - 32);
                float panelH = Mathf.Min(840, virtualHeight - 48);
                Rect panelRect = new Rect((virtualWidth - panelW) * 0.5f, (virtualHeight - panelH) * 0.5f, panelW, panelH);

                GUI.Box(panelRect, "");
                GUILayout.BeginArea(panelRect, GUI.skin.box);
                GUILayout.Space(10);

                GUILayout.Label($"<b><size=22>LevelLens — Review</size></b>", GUILayout.ExpandWidth(true));
                GUILayout.Space(4);
                GUILayout.Label($"<b><size=15>Level:</size></b> {_adapter.CurrentLevelId}   |   <b><size=15>Holder:</size></b> {_adapter.CurrentLevelHolderName}");
                if (_telemetryTracker != null)
                {
                    GUILayout.Label($"<b><size=15>Time:</size></b> {_telemetryTracker.ActivePlaytimeSeconds:F1}s   |   <b><size=15>Fails:</size></b> {_telemetryTracker.SessionFails}   |   <b><size=15>Revives:</size></b> {_telemetryTracker.SessionRevives}");
                }

                GUILayout.Space(12);
                GUILayout.Label($"<b><size=16>Score: {_guiScore} / 10</size></b>");
                GUILayout.BeginHorizontal();
                for (int i = 1; i <= 10; i++)
                {
                    if (GUILayout.Toggle(_guiScore == i, $"<b>{i}</b>", "Button", GUILayout.Height(46), GUILayout.ExpandWidth(true)))
                    {
                        _guiScore = i;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(12);
                GUILayout.Label($"<b><size=16>Difficulty: {_guiDifficulty}</size></b>");
                GUILayout.BeginHorizontal();
                string[] diffs = { "Easy", "Medium", "Hard", "Very Hard" };
                foreach (var d in diffs)
                {
                    if (GUILayout.Toggle(_guiDifficulty == d, $"<b>{d}</b>", "Button", GUILayout.Height(44), GUILayout.ExpandWidth(true)))
                    {
                        _guiDifficulty = d;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(12);
                GUILayout.Label("<b><size=16>Feedback & Bug Notes:</size></b>");
                _guiNote = GUILayout.TextArea(_guiNote, GUILayout.Height(110));

                if (!string.IsNullOrEmpty(_guiSubmitStatus))
                {
                    GUILayout.Space(6);
                    GUILayout.Label($"<b><size=15><color=cyan>{_guiSubmitStatus}</color></size></b>");
                }

                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (!_isSubmitting && GUILayout.Button("<b>Restart Test</b>", GUILayout.Height(52)))
                {
                    RestartForTesting();
                }

                if (!_isSubmitting && GUILayout.Button("<b>Resume</b>", GUILayout.Height(52)))
                {
                    CloseReviewPanel();
                }

                GUI.backgroundColor = new Color(0.2f, 0.78f, 0.38f);
                if (!_isSubmitting && GUILayout.Button("<b>Submit to Cloud</b>", GUILayout.Height(52)))
                {
                    _isSubmitting = true;
                    _guiSubmitStatus = "Uploading media and saving report...";
                    StartCoroutine(SubmitGuiReportRoutine());
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(10);
                GUILayout.EndArea();
            }

            GUI.matrix = origMatrix;
        }

        private IEnumerator SubmitGuiReportRoutine()
        {
            string screenshotKey = "";

            if (!string.IsNullOrEmpty(_latestScreenshotPath) && File.Exists(_latestScreenshotPath))
            {
                PresignResponseDto presign = null;
                yield return R2DirectUploader.RequestPresignedUrlCoroutine(
                    m_ApiHost,
                    m_ApiKey,
                    _latestScreenshotPath,
                    "image",
                    "image/jpeg",
                    res => presign = res,
                    err => Debug.LogWarning($"[LevelLens] Presign warning: {err}")
                );

                if (presign != null)
                {
                    yield return R2DirectUploader.StreamFileToR2Coroutine(
                        presign.uploadUrl,
                        _latestScreenshotPath,
                        "image/jpeg",
                        null,
                        () => screenshotKey = presign.r2Key,
                        err => Debug.LogWarning($"[LevelLens] Upload warning: {err}")
                    );
                }
            }

            var items = new List<ReportItemDto>();
            if (!string.IsNullOrWhiteSpace(_guiNote))
            {
                items.Add(new ReportItemDto
                {
                    itemType = "feedback",
                    title = "Tester Note",
                    category = "general",
                    description = _guiNote.Trim()
                });
            }

            var reportDto = new SubmitReportDto
            {
                reportId = Guid.NewGuid().ToString(),
                gameId = Application.productName.ToLower().Replace(" ", "-").Replace("'", ""),
                gameName = Application.productName,
                versionName = Application.version,
                holderName = _adapter.CurrentLevelHolderName,
                levelName = _adapter.CurrentLevelId,
                levelIndex = _adapter.CurrentLevelIndex,
                testerName = "QA Tester",
                score = _guiScore,
                difficultyTag = _guiDifficulty,
                screenshotKey = screenshotKey,
                reviveCount = _telemetryTracker?.SessionRevives ?? 0,
                failCount = _telemetryTracker?.SessionFails ?? 0,
                playtimeSeconds = _telemetryTracker?.ActivePlaytimeSeconds ?? 0,
                items = items
            };

            bool success = false;
            string error = null;

            yield return R2DirectUploader.SubmitReportCoroutine(
                m_ApiHost,
                m_ApiKey,
                reportDto,
                () => success = true,
                err => error = err
            );

            _isSubmitting = false;

            if (success)
            {
                _guiSubmitStatus = "Report submitted successfully!";
                _guiNote = "";
                yield return new WaitForSecondsRealtime(0.8f);
                CloseReviewPanel();
            }
            else
            {
                OfflineDraftCache.SaveDraft(reportDto);
                _guiSubmitStatus = $"Saved offline ({error})";
                yield return new WaitForSecondsRealtime(1.2f);
                CloseReviewPanel();
            }
        }
    }
}
