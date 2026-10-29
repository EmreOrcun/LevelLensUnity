using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LevelLens.Runtime.Media;
using LevelLens.Runtime.Network;
using LevelLens.Runtime.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace LevelLens.Runtime.Core
{
    public enum LevelLensStatus
    {
        auto,       // Active in Editor and Development Builds; Dormant in Release
        enabled,    // Force Active (for explicit QA testing in release builds)
        disabled    // Force Inactive
    }

    public class GuiCardModel
    {
        public string itemType = "bug"; // "bug" | "feedback"
        public string title = "";
        public string priority = "medium"; // low, medium, high, blocker
        public string category = "improve"; // improve, art, level_design, good
        public string description = "";

        // Universal attachments
        public string attachedVideoPath = null;
        public bool hasVideoClamping = false;
        public string clampStartStr = "0";
        public string clampEndStr = "";

        public string attachedImagePath = null;
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
        [SerializeField] private int m_MaxScreenshotDimension = 1080;

        private ILevelLensAdapter _adapter;
        private SessionTelemetryTracker _telemetryTracker;
        private string _latestScreenshotPath;
        private Coroutine _screenshotCoroutine;
        private bool _isLevelLensActive;
        private bool _isSessionActive = false;
        private bool _showGuiReview;
        private bool _isSubmitting;
        private string _testerName = "Tester";
        private int _guiScore = 8;
        private string _guiDifficulty = "Medium";
        private string _guiSubmitStatus = "";
        private Vector2 _cardsScrollPos;

        private string _authToken;
        private string _userEmail;
        private string _loginEmailInput = "";
        private string _loginPasswordInput = "";
        private string _loginError = "";
        private bool _isLoggingIn;
        private bool _showLoginDialog;

        private readonly List<GuiCardModel> _guiCards = new List<GuiCardModel>();

        public bool IsActive => _isLevelLensActive;
        public bool IsSessionActive => _isSessionActive;
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
            _testerName = PlayerPrefs.GetString("LevelLens_TesterName", "Tester");
            _authToken = PlayerPrefs.GetString("LevelLens_AuthToken", "");
            _userEmail = PlayerPrefs.GetString("LevelLens_UserEmail", "");
            if (!string.IsNullOrEmpty(_userEmail))
            {
                _loginEmailInput = _userEmail;
            }
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
            _telemetryTracker = new SessionTelemetryTracker(_adapter, () => _isSessionActive);

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
            if (_isSessionActive && _telemetryTracker != null)
            {
                _telemetryTracker.Update(Time.deltaTime);
            }
        }

        private void OnLevelReady()
        {
            if (!_isSessionActive) return;
            if (_screenshotCoroutine != null) StopCoroutine(_screenshotCoroutine);
            _screenshotCoroutine = StartCoroutine(CaptureDelayedScreenshot(1.0f));
        }

        private void OnLevelWon()
        {
            if (!_isSessionActive) return;
            OpenReviewPanel();
        }

        private IEnumerator CaptureDelayedScreenshot(float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);

            string fileName = $"thumb_{DateTime.UtcNow.Ticks}.jpg";
            string dir = Path.Combine(Application.persistentDataPath, "LevelLens", "media");
            _latestScreenshotPath = Path.Combine(dir, fileName);

            ScreenshotCapturer.CalculateAspectDimensions(m_MaxScreenshotDimension, out int targetW, out int targetH);

            yield return ScreenshotCapturer.CaptureCleanScreenshotCoroutine(
                Camera.main,
                targetW,
                targetH,
                _latestScreenshotPath,
                path =>
                {
                    Debug.Log($"[LevelLens] Captured clean gameplay screenshot ({targetW}x{targetH}): {path}");
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
            if (string.IsNullOrEmpty(_authToken))
            {
                _showLoginDialog = true;
                _loginError = "";
                return;
            }

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

        public void DeactivateSession()
        {
            _isSessionActive = false;
            if (_screenshotCoroutine != null)
            {
                StopCoroutine(_screenshotCoroutine);
                _screenshotCoroutine = null;
            }
            _telemetryTracker?.ResetSession();
            CloseReviewPanel();
        }

        public void ActivateSessionAndRestart()
        {
            _isSessionActive = true;
            _telemetryTracker?.ResetSession();
            _adapter?.RestartLevel();
        }

        private int GetAttachedVideosCount()
        {
            int count = 0;
            for (int i = 0; i < _guiCards.Count; i++)
            {
                if (!string.IsNullOrEmpty(_guiCards[i].attachedVideoPath)) count++;
            }
            return count;
        }

        private int GetAttachedImagesCount()
        {
            int count = 0;
            for (int i = 0; i < _guiCards.Count; i++)
            {
                if (!string.IsNullOrEmpty(_guiCards[i].attachedImagePath)) count++;
            }
            return count;
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

            // 1. Floating trigger button with stateful ON/OFF status
            if (m_FloatingButton == null && !_showGuiReview && !_showLoginDialog)
            {
                float btnW = 160;
                float btnH = 50;
                Rect btnRect = new Rect(virtualWidth - btnW - 16, 24, btnW, btnH);

                if (_isSessionActive)
                {
                    GUI.backgroundColor = new Color(0.13f, 0.77f, 0.37f, 0.95f); // Emerald green
                    if (GUI.Button(btnRect, "<b><size=16>LevelLens [ON]</size></b>"))
                    {
                        OpenReviewPanel();
                    }
                }
                else
                {
                    GUI.backgroundColor = new Color(0.28f, 0.33f, 0.41f, 0.9f); // Slate gray
                    if (GUI.Button(btnRect, "<b><size=16>LevelLens [OFF]</size></b>"))
                    {
                        if (string.IsNullOrEmpty(_authToken))
                        {
                            _showLoginDialog = true;
                            _loginError = "";
                        }
                        else
                        {
                            ActivateSessionAndRestart();
                        }
                    }
                }
                GUI.backgroundColor = Color.white;
            }

            // 2. Sign In Dialog
            if (_showLoginDialog)
            {
                float dialogW = Mathf.Min(480, virtualWidth - 32);
                float dialogH = 370;
                Rect dialogRect = new Rect((virtualWidth - dialogW) * 0.5f, (virtualHeight - dialogH) * 0.5f, dialogW, dialogH);

                Color prevColor = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(new Rect(0, 0, virtualWidth, virtualHeight), Texture2D.whiteTexture);
                GUI.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);
                GUI.DrawTexture(dialogRect, Texture2D.whiteTexture);
                GUI.color = prevColor;

                GUI.Box(dialogRect, "");
                GUILayout.BeginArea(dialogRect, GUI.skin.box);
                GUILayout.Space(12);

                GUILayout.Label("<b><size=22>LevelLens — Sign In</size></b>");
                GUILayout.Space(4);
                GUILayout.Label("<size=13>Enter your credentials to record and submit playtest sessions.</size>");
                GUILayout.Space(14);

                GUILayout.Label("<b><size=14>Email:</size></b>");
                _loginEmailInput = GUILayout.TextField(_loginEmailInput ?? "", 80, GUILayout.Height(32));

                GUILayout.Space(8);
                GUILayout.Label("<b><size=14>Password:</size></b>");
                _loginPasswordInput = GUILayout.PasswordField(_loginPasswordInput ?? "", '*', 80, GUILayout.Height(32));

                GUILayout.Space(6);
                string webAppUrl = !string.IsNullOrEmpty(m_ApiHost) ? m_ApiHost : "https://levellens.creoapp.workers.dev";
                if (GUILayout.Button($"<b><size=13><color=#60a5fa>Open Web App ({webAppUrl}) ↗</color></size></b>", GUI.skin.label))
                {
                    Application.OpenURL(webAppUrl);
                }

                if (!string.IsNullOrEmpty(_loginError))
                {
                    GUILayout.Space(6);
                    GUILayout.Label($"<b><size=13><color=#ff4d4f>{_loginError}</color></size></b>");
                }

                GUILayout.FlexibleSpace();

                GUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(0.4f, 0.4f, 0.4f, 0.9f);
                if (GUILayout.Button("<b>Cancel</b>", GUILayout.Height(40), GUILayout.Width(100)))
                {
                    _showLoginDialog = false;
                    _loginError = "";
                }

                bool canSubmit = !_isLoggingIn && !string.IsNullOrWhiteSpace(_loginEmailInput) && !string.IsNullOrEmpty(_loginPasswordInput);
                GUI.backgroundColor = canSubmit
                    ? new Color(0.13f, 0.77f, 0.37f, 0.95f)
                    : Color.gray;

                string btnText = _isLoggingIn ? "<b>Signing In...</b>" : "<b>Sign In</b>";

                if (GUILayout.Button(btnText, GUILayout.Height(40), GUILayout.ExpandWidth(true)) && canSubmit)
                {
                    StartCoroutine(ExecuteLoginRoutine());
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.EndArea();

                GUI.matrix = origMatrix;
                return;
            }

            // 2. Immediate IMGUI review dialog fallback
            if (_showGuiReview && m_ReviewPanel == null)
            {
                float panelW = Mathf.Min(680, virtualWidth - 32);
                float panelH = Mathf.Min(900, virtualHeight - 48);
                Rect panelRect = new Rect((virtualWidth - panelW) * 0.5f, (virtualHeight - panelH) * 0.5f, panelW, panelH);

                Color prevColor = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.75f);
                GUI.DrawTexture(new Rect(0, 0, virtualWidth, virtualHeight), Texture2D.whiteTexture);
                GUI.color = new Color(0.10f, 0.12f, 0.16f, 0.98f);
                GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
                GUI.color = prevColor;

                GUI.Box(panelRect, "");
                GUILayout.BeginArea(panelRect, GUI.skin.box);
                GUILayout.Space(8);

                // Header with Title and Close (X) button
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b><size=22>LevelLens — Review</size></b>", GUILayout.ExpandWidth(true));
                GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f, 0.95f);
                if (GUILayout.Button("<b><size=20>✕</size></b>", GUILayout.Height(34), GUILayout.Width(44)))
                {
                    CloseReviewPanel();
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.EndArea();
                    GUI.matrix = origMatrix;
                    return;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                // Authenticated user row with Log Out and Deactivate buttons
                GUILayout.BeginHorizontal();
                string displayUser = !string.IsNullOrEmpty(_userEmail) ? _userEmail : "Unauthenticated";
                GUILayout.Label($"<b><size=14>User:</size></b> {displayUser}", GUILayout.ExpandWidth(true));
                if (!string.IsNullOrEmpty(_authToken))
                {
                    GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f, 0.95f);
                    if (GUILayout.Button("<b>Log Out</b>", GUILayout.Height(30), GUILayout.Width(85)))
                    {
                        _authToken = "";
                        _userEmail = "";
                        PlayerPrefs.DeleteKey("LevelLens_AuthToken");
                        PlayerPrefs.DeleteKey("LevelLens_UserEmail");
                        PlayerPrefs.Save();
                        DeactivateSession();
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                        GUILayout.EndArea();
                        GUI.matrix = origMatrix;
                        return;
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUI.backgroundColor = new Color(0.85f, 0.32f, 0.32f, 0.95f);
                if (GUILayout.Button("<b>Deactivate [Turn OFF]</b>", GUILayout.Height(30), GUILayout.Width(170)))
                {
                    DeactivateSession();
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.EndArea();
                    GUI.matrix = origMatrix;
                    return;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label($"<b><size=15>Level:</size></b> {_adapter.CurrentLevelId}   |   <b><size=15>Holder:</size></b> {_adapter.CurrentLevelHolderName}");
                if (_telemetryTracker != null)
                {
                    GUILayout.Label($"<b><size=15>Time:</size></b> {_telemetryTracker.ActivePlaytimeSeconds:F1}s   |   <b><size=15>Restarts:</size></b> {_telemetryTracker.SessionRestarts}   |   <b><size=15>Fails:</size></b> {_telemetryTracker.SessionFails}   |   <b><size=15>Revives:</size></b> {_telemetryTracker.SessionRevives}");
                }

                GUILayout.Space(8);
                GUILayout.Label($"<b><size=15>Score: {_guiScore} / 10</size></b>");
                GUILayout.BeginHorizontal();
                for (int i = 1; i <= 10; i++)
                {
                    if (GUILayout.Toggle(_guiScore == i, $"<b>{i}</b>", "Button", GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                    {
                        _guiScore = i;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.Label($"<b><size=15>Difficulty: {_guiDifficulty}</size></b>");
                GUILayout.BeginHorizontal();
                string[] diffs = { "Easy", "Medium", "Hard", "Very Hard" };
                foreach (var d in diffs)
                {
                    if (GUILayout.Toggle(_guiDifficulty == d, $"<b>{d}</b>", "Button", GUILayout.Height(38), GUILayout.ExpandWidth(true)))
                    {
                        _guiDifficulty = d;
                    }
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(10);

                // Quota bar & Add buttons
                int videoCount = GetAttachedVideosCount();
                int imageCount = GetAttachedImagesCount();
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>Cards ({_guiCards.Count}/10) | Videos ({videoCount}/2) | Images ({imageCount}/3)</b>", GUILayout.ExpandWidth(true));

                bool canAddCard = _guiCards.Count < 10;
                GUI.backgroundColor = canAddCard ? new Color(0.95f, 0.45f, 0.45f) : Color.gray;
                if (GUILayout.Button("<b>+ Add Bug</b>", GUILayout.Height(36), GUILayout.Width(110)) && canAddCard)
                {
                    _guiCards.Add(new GuiCardModel { itemType = "bug", priority = "medium" });
                }

                GUI.backgroundColor = canAddCard ? new Color(0.4f, 0.65f, 0.95f) : Color.gray;
                if (GUILayout.Button("<b>+ Add Feedback</b>", GUILayout.Height(36), GUILayout.Width(130)) && canAddCard)
                {
                    _guiCards.Add(new GuiCardModel { itemType = "feedback", category = "improve" });
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                // Scrollable Cards Area
                float nonScrollableHeight = string.IsNullOrEmpty(_guiSubmitStatus) ? 440f : 470f;
                float scrollHeight = Mathf.Clamp(panelH - nonScrollableHeight, 200f, 460f);
                _cardsScrollPos = GUILayout.BeginScrollView(_cardsScrollPos, GUILayout.Height(scrollHeight));
                for (int i = 0; i < _guiCards.Count; i++)
                {
                    var card = _guiCards[i];
                    GUILayout.BeginVertical(GUI.skin.box);

                    // Card header
                    GUILayout.BeginHorizontal();
                    if (card.itemType == "bug")
                    {
                        GUILayout.Label($"<b><size=15><color=#f87171>[BUG #{i + 1}]</color></size></b>", GUILayout.ExpandWidth(true));
                    }
                    else
                    {
                        GUILayout.Label($"<b><size=15><color=#60a5fa>[FEEDBACK #{i + 1}]</color></size></b>", GUILayout.ExpandWidth(true));
                    }

                    GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
                    if (GUILayout.Button("Delete", GUILayout.Width(60), GUILayout.Height(24)))
                    {
                        _guiCards.RemoveAt(i);
                        i--;
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                        GUILayout.EndVertical();
                        continue;
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    // Title
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Title:", GUILayout.Width(45));
                    card.title = GUILayout.TextField(card.title, 120);
                    GUILayout.EndHorizontal();

                    // Priority (Bug) or Category (Feedback)
                    if (card.itemType == "bug")
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Priority:", GUILayout.Width(60));
                        string[] priorities = { "low", "medium", "high", "blocker" };
                        foreach (var p in priorities)
                        {
                            if (GUILayout.Toggle(card.priority == p, p.ToUpper(), "Button", GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                            {
                                card.priority = p;
                            }
                        }
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Category:", GUILayout.Width(70));
                        string[] categories = { "improve", "art", "level_design", "good" };
                        foreach (var c in categories)
                        {
                            string display = c == "level_design" ? "DESIGN" : c.ToUpper();
                            if (GUILayout.Toggle(card.category == c, display, "Button", GUILayout.Height(28), GUILayout.ExpandWidth(true)))
                            {
                                card.category = c;
                            }
                        }
                        GUILayout.EndHorizontal();
                    }

                    // Description
                    GUILayout.Label("Description:");
                    card.description = GUILayout.TextArea(card.description, GUILayout.Height(46));

                    // Universal Attachments
                    GUILayout.Space(4);
                    GUILayout.Label("<b>Attachments:</b>");

                    // Video slot
                    GUILayout.BeginHorizontal();
                    if (!string.IsNullOrEmpty(card.attachedVideoPath))
                    {
                        GUILayout.Label($"🎬 Video: {Path.GetFileName(card.attachedVideoPath)}", GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Remove", GUILayout.Width(65), GUILayout.Height(24)))
                        {
                            card.attachedVideoPath = null;
                            card.hasVideoClamping = false;
                        }
                    }
                    else if (videoCount < 2)
                    {
#if UNITY_EDITOR
                        if (GUILayout.Button("+ Attach Video (.mp4)", GUILayout.Height(28)))
                        {
                            string path = EditorUtility.OpenFilePanel("Attach Video", "", "mp4");
                            if (!string.IsNullOrEmpty(path))
                            {
                                card.attachedVideoPath = path;
                                card.clampStartStr = "0";
                                card.clampEndStr = "";
                            }
                        }
#else
                        GUILayout.Label("(Attach video available in Editor)", GUILayout.ExpandWidth(true));
#endif
                    }
                    else
                    {
                        GUILayout.Label("(Video limit reached 2/2)", GUILayout.ExpandWidth(true));
                    }
                    GUILayout.EndHorizontal();

                    // Clamping controls if video attached
                    if (!string.IsNullOrEmpty(card.attachedVideoPath))
                    {
                        GUILayout.BeginHorizontal();
                        card.hasVideoClamping = GUILayout.Toggle(card.hasVideoClamping, "Clamp Video Range", GUILayout.Width(150));
                        if (card.hasVideoClamping)
                        {
                            GUILayout.Label("Start(s):", GUILayout.Width(55));
                            card.clampStartStr = GUILayout.TextField(card.clampStartStr, 6, GUILayout.Width(50));
                            GUILayout.Label("End(s):", GUILayout.Width(50));
                            card.clampEndStr = GUILayout.TextField(card.clampEndStr, 6, GUILayout.Width(50));
                        }
                        GUILayout.EndHorizontal();
                    }

                    // Image slot
                    GUILayout.BeginHorizontal();
                    if (!string.IsNullOrEmpty(card.attachedImagePath))
                    {
                        GUILayout.Label($"🖼️ Image: {Path.GetFileName(card.attachedImagePath)}", GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Remove", GUILayout.Width(65), GUILayout.Height(24)))
                        {
                            card.attachedImagePath = null;
                        }
                    }
                    else if (imageCount < 3)
                    {
#if UNITY_EDITOR
                        if (GUILayout.Button("+ Attach Image (.png/.jpg)", GUILayout.Height(28)))
                        {
                            string path = EditorUtility.OpenFilePanel("Attach Image", "", "png,jpg,jpeg");
                            if (!string.IsNullOrEmpty(path))
                            {
                                card.attachedImagePath = path;
                            }
                        }
#else
                        GUILayout.Label("(Attach image available in Editor)", GUILayout.ExpandWidth(true));
#endif
                    }
                    else
                    {
                        GUILayout.Label("(Image limit reached 3/3)", GUILayout.ExpandWidth(true));
                    }
                    GUILayout.EndHorizontal();

                    GUILayout.EndVertical();
                    GUILayout.Space(6);
                }
                GUILayout.EndScrollView();

                if (!string.IsNullOrEmpty(_guiSubmitStatus))
                {
                    GUILayout.Space(4);
                    GUILayout.Label($"<b><size=15><color=cyan>{_guiSubmitStatus}</color></size></b>");
                }

                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (!_isSubmitting && GUILayout.Button("<b>Restart Test</b>", GUILayout.Height(48)))
                {
                    RestartForTesting();
                }

                GUI.backgroundColor = new Color(0.2f, 0.78f, 0.38f);
                if (!_isSubmitting && GUILayout.Button("<b>Submit to Cloud</b>", GUILayout.Height(48)))
                {
                    _isSubmitting = true;
                    _guiSubmitStatus = "Uploading media and saving report...";
                    StartCoroutine(SubmitGuiReportRoutine());
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.Space(8);
                GUILayout.EndArea();
            }

            GUI.matrix = origMatrix;
        }

        private IEnumerator SubmitGuiReportRoutine()
        {
            string effectiveKey = !string.IsNullOrEmpty(_authToken) ? _authToken : m_ApiKey;
            string screenshotKey = "";

            if (!string.IsNullOrEmpty(_latestScreenshotPath) && File.Exists(_latestScreenshotPath))
            {
                PresignResponseDto presign = null;
                yield return R2DirectUploader.RequestPresignedUrlCoroutine(
                    m_ApiHost,
                    effectiveKey,
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

            for (int i = 0; i < _guiCards.Count; i++)
            {
                var card = _guiCards[i];
                var attachments = new List<ReportAttachmentDto>();

                // 1. Upload attached video if present
                if (!string.IsNullOrEmpty(card.attachedVideoPath) && File.Exists(card.attachedVideoPath))
                {
                    PresignResponseDto vidPresign = null;
                    yield return R2DirectUploader.RequestPresignedUrlCoroutine(
                        m_ApiHost,
                        effectiveKey,
                        card.attachedVideoPath,
                        "video",
                        "video/mp4",
                        res => vidPresign = res,
                        err => Debug.LogWarning($"[LevelLens] Video presign warning: {err}")
                    );

                    if (vidPresign != null)
                    {
                        bool vidOk = false;
                        yield return R2DirectUploader.StreamFileToR2Coroutine(
                            vidPresign.uploadUrl,
                            card.attachedVideoPath,
                            "video/mp4",
                            null,
                            () => vidOk = true,
                            err => Debug.LogWarning($"[LevelLens] Video stream warning: {err}")
                        );

                        if (vidOk)
                        {
                            float start = 0f;
                            float.TryParse(card.clampStartStr, out start);
                            float? end = null;
                            if (card.hasVideoClamping && float.TryParse(card.clampEndStr, out float parsedEnd) && parsedEnd > start)
                            {
                                end = parsedEnd;
                            }

                            attachments.Add(new ReportAttachmentDto
                            {
                                r2Key = vidPresign.r2Key,
                                fileName = Path.GetFileName(card.attachedVideoPath),
                                mediaType = "video",
                                sizeBytes = (int)new FileInfo(card.attachedVideoPath).Length,
                                videoClampStart = start,
                                videoClampEnd = end
                            });
                        }
                    }
                }

                // 2. Upload attached image if present
                if (!string.IsNullOrEmpty(card.attachedImagePath) && File.Exists(card.attachedImagePath))
                {
                    string mime = card.attachedImagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                    PresignResponseDto imgPresign = null;
                    yield return R2DirectUploader.RequestPresignedUrlCoroutine(
                        m_ApiHost,
                        effectiveKey,
                        card.attachedImagePath,
                        "image",
                        mime,
                        res => imgPresign = res,
                        err => Debug.LogWarning($"[LevelLens] Image presign warning: {err}")
                    );

                    if (imgPresign != null)
                    {
                        bool imgOk = false;
                        yield return R2DirectUploader.StreamFileToR2Coroutine(
                            imgPresign.uploadUrl,
                            card.attachedImagePath,
                            mime,
                            null,
                            () => imgOk = true,
                            err => Debug.LogWarning($"[LevelLens] Image stream warning: {err}")
                        );

                        if (imgOk)
                        {
                            attachments.Add(new ReportAttachmentDto
                            {
                                r2Key = imgPresign.r2Key,
                                fileName = Path.GetFileName(card.attachedImagePath),
                                mediaType = "image",
                                sizeBytes = (int)new FileInfo(card.attachedImagePath).Length,
                                videoClampStart = 0f,
                                videoClampEnd = null
                            });
                        }
                    }
                }

                string title = string.IsNullOrWhiteSpace(card.title)
                    ? (card.itemType == "bug" ? $"Bug #{i + 1}" : $"Feedback #{i + 1}")
                    : card.title.Trim();

                if (card.itemType == "bug")
                {
                    items.Add(new ReportItemDto
                    {
                        itemType = "bug",
                        title = title,
                        priority = card.priority.ToLower(),
                        category = null, // Strictly null for bug
                        description = card.description ?? "",
                        attachments = attachments
                    });
                }
                else
                {
                    items.Add(new ReportItemDto
                    {
                        itemType = "feedback",
                        title = title,
                        priority = null, // Strictly null for feedback
                        category = card.category.ToLower().Replace(" ", "_"),
                        description = card.description ?? "",
                        attachments = attachments
                    });
                }
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
                testerName = !string.IsNullOrEmpty(_userEmail) ? _userEmail : (string.IsNullOrWhiteSpace(_testerName) ? "Tester" : _testerName.Trim()),
                score = _guiScore,
                difficultyTag = _guiDifficulty,
                screenshotKey = screenshotKey,
                reviveCount = _telemetryTracker?.SessionRevives ?? 0,
                failCount = _telemetryTracker?.SessionFails ?? 0,
                restartCount = _telemetryTracker?.SessionRestarts ?? 0,
                playtimeSeconds = _telemetryTracker?.ActivePlaytimeSeconds ?? 0,
                items = items
            };

            bool success = false;
            string error = null;

            yield return R2DirectUploader.SubmitReportCoroutine(
                m_ApiHost,
                effectiveKey,
                reportDto,
                () => success = true,
                err => error = err
            );

            _isSubmitting = false;

            if (success)
            {
                _guiSubmitStatus = "Report submitted successfully!";
                _guiCards.Clear();
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

        private IEnumerator ExecuteLoginRoutine()
        {
            _isLoggingIn = true;
            _loginError = "";

            string email = _loginEmailInput.Trim().ToLower();
            string pass = _loginPasswordInput;

            bool success = false;
            string tokenResult = null;
            string emailResult = null;
            string errorResult = null;

            yield return R2DirectUploader.LoginCoroutine(
                m_ApiHost,
                email,
                pass,
                (token, userEmail) =>
                {
                    success = true;
                    tokenResult = token;
                    emailResult = userEmail;
                },
                err => errorResult = err
            );

            _isLoggingIn = false;

            if (success && !string.IsNullOrEmpty(tokenResult))
            {
                _authToken = tokenResult;
                _userEmail = emailResult;
                PlayerPrefs.SetString("LevelLens_AuthToken", _authToken);
                PlayerPrefs.SetString("LevelLens_UserEmail", _userEmail);
                PlayerPrefs.Save();

                _showLoginDialog = false;
                _loginPasswordInput = "";
                _loginError = "";
                ActivateSessionAndRestart();
            }
            else
            {
                _loginError = errorResult ?? "Sign in failed.";
            }
        }
    }
}
