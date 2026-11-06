using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using LevelLens.Runtime.Core;
using LevelLens.Runtime.Media;
using LevelLens.Runtime.Network;
using UnityEditor;
using UnityEngine;

namespace LevelLens.Editor
{
    [InitializeOnLoad]
    public static class LevelLensBatchSubmitter
    {
        private const string ACTIVE_KEY = "LevelLens.BatchSubmit.Active";
        private const string HOLDER_KEY = "LevelLens.BatchSubmit.HolderPath";
        private const string COMPLETION_KEY = "LevelLens.BatchSubmit.CompletionMessage";

        static LevelLensBatchSubmitter()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (SessionState.GetBool(ACTIVE_KEY, false))
                {
                    var runnerGo = new GameObject("[LevelLensBatchRunner]");
                    UnityEngine.Object.DontDestroyOnLoad(runnerGo);
                    runnerGo.AddComponent<LevelLensBatchRunner>();
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                string completionMsg = SessionState.GetString(COMPLETION_KEY, string.Empty);
                if (!string.IsNullOrEmpty(completionMsg))
                {
                    SessionState.EraseString(COMPLETION_KEY);
                    EditorUtility.DisplayDialog("LevelLens Level Submission", completionMsg, "OK");
                }
            }
        }
    }

    public class LevelLensBatchRunner : MonoBehaviour
    {
        private const string ACTIVE_KEY = "LevelLens.BatchSubmit.Active";
        private const string HOLDER_KEY = "LevelLens.BatchSubmit.HolderPath";
        private const string COMPLETION_KEY = "LevelLens.BatchSubmit.CompletionMessage";
        private const string PREF_AUTH_TOKEN = "LevelLens_AuthToken";
        private const string PREF_USER_EMAIL = "LevelLens_UserEmail";
        private const string DEFAULT_API_HOST = "https://levellens.creoapp.workers.dev";

        private string _status = "Initializing batch submitter...";
        private int _currentIndex = 0;
        private int _totalLevels = 0;
        private string _currentLevelName = "";
        private bool _isComplete = false;

        private void Awake()
        {
            if (LevelLensManager.Instance != null)
            {
                LevelLensManager.Instance.SuppressReviewUi = true;
            }
        }

        private void Start()
        {
            if (LevelLensManager.Instance != null)
            {
                LevelLensManager.Instance.SuppressReviewUi = true;
            }

            StartCoroutine(RunBatch());
        }

        private void Update()
        {
            if (LevelLensManager.Instance != null && !LevelLensManager.Instance.SuppressReviewUi)
            {
                LevelLensManager.Instance.SuppressReviewUi = true;
            }
        }

        private IEnumerator RunBatch()
        {
            string holderPath = SessionState.GetString(HOLDER_KEY, string.Empty);
            if (string.IsNullOrEmpty(holderPath))
            {
                AbortRun("Holder path was not found in SessionState.");
                yield break;
            }

            var holder = AssetDatabase.LoadAssetAtPath<ScriptableObject>(holderPath);
            if (holder == null)
            {
                AbortRun($"Could not load Level Holder from '{holderPath}'.");
                yield break;
            }

            var handler = LevelLensBatchHandlerRegistry.FindHandler(holder);
            if (handler == null)
            {
                AbortRun($"No ILevelLensBatchHandler registered to handle '{holder.name}'.");
                yield break;
            }

            var levelNames = handler.GetLevelNames(holder);
            if (levelNames == null || levelNames.Count == 0)
            {
                AbortRun($"Level Holder '{holder.name}' contains 0 levels.");
                yield break;
            }

            _totalLevels = levelNames.Count;

            string authToken = PlayerPrefs.GetString(PREF_AUTH_TOKEN, "");
            string userEmail = PlayerPrefs.GetString(PREF_USER_EMAIL, "QA Tester");
            string apiHost = LevelLensManager.Instance != null ? LevelLensManager.Instance.ApiHost : DEFAULT_API_HOST;

            for (int i = 0; i < _totalLevels; i++)
            {
                _currentIndex = i + 1;
                _currentLevelName = levelNames[i];

                _status = $"Loading level {_currentIndex}/{_totalLevels} ({_currentLevelName})...";

                yield return handler.PrepareLevelForScreenshot(holder, i);

                // Capture clean gameplay screenshot
                _status = $"Capturing screenshot for {_currentIndex}/{_totalLevels}...";
                ScreenshotCapturer.CalculateAspectDimensions(800, out int targetW, out int targetH);

                string mediaDir = Path.Combine(Application.persistentDataPath, "LevelLens", "media");
                Directory.CreateDirectory(mediaDir);
                string screenshotPath = Path.Combine(mediaDir, $"thumb_{DateTime.UtcNow.Ticks}.jpg");

                string capturedPath = null;
                yield return ScreenshotCapturer.CaptureCleanScreenshotCoroutine(
                    Camera.main,
                    targetW,
                    targetH,
                    screenshotPath,
                    path => capturedPath = path
                );

                string screenshotKey = "";
                if (!string.IsNullOrEmpty(capturedPath) && File.Exists(capturedPath))
                {
                    _status = $"Uploading screenshot for {_currentIndex}/{_totalLevels}...";
                    PresignResponseDto presign = null;
                    string presignError = null;

                    yield return R2DirectUploader.RequestPresignedUrlCoroutine(
                        apiHost,
                        authToken,
                        capturedPath,
                        "image",
                        "image/jpeg",
                        res => presign = res,
                        err => presignError = err
                    );

                    if (presign != null)
                    {
                        string uploadError = null;
                        yield return R2DirectUploader.StreamFileToR2Coroutine(
                            presign.uploadUrl,
                            capturedPath,
                            "image/jpeg",
                            null,
                            () => screenshotKey = presign.r2Key,
                            err => uploadError = err
                        );

                        if (!string.IsNullOrEmpty(uploadError))
                        {
                            Debug.LogWarning($"[LevelLens Batch] Upload warning on level {_currentLevelName}: {uploadError}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[LevelLens Batch] Presign warning on level {_currentLevelName}: {presignError}");
                    }
                }

                // Submit level metadata without report
                _status = $"Registering level {_currentIndex}/{_totalLevels} in Cloud...";
                var reportPayload = new SubmitReportDto
                {
                    reportId = Guid.NewGuid().ToString(),
                    gameId = Application.productName.ToLower().Replace(" ", "-").Replace("'", ""),
                    gameName = Application.productName,
                    versionName = Application.version,
                    holderName = holder.name,
                    levelIndex = _currentIndex,
                    levelName = _currentLevelName,
                    testerName = userEmail,
                    screenshotKey = screenshotKey,
                    withoutReport = true
                };

                bool submitDone = false;
                yield return R2DirectUploader.SubmitReportCoroutine(
                    apiHost,
                    authToken,
                    reportPayload,
                    () => submitDone = true,
                    err =>
                    {
                        Debug.LogWarning($"[LevelLens Batch] Level register error on level {_currentLevelName}: {err}");
                        submitDone = true;
                    }
                );

                while (!submitDone) yield return null;
            }

            _isComplete = true;
            _status = "All levels submitted successfully!";

            SessionState.EraseBool(ACTIVE_KEY);
            SessionState.EraseString(HOLDER_KEY);
            SessionState.SetString(COMPLETION_KEY, $"Successfully submitted {_totalLevels} levels from '{holder.name}' to LevelLens!");

            EditorApplication.isPlaying = false;
        }

        private void AbortRun(string errorMessage)
        {
            Debug.LogError($"[LevelLens Batch Submitter] Run aborted: {errorMessage}");
            SessionState.EraseBool(ACTIVE_KEY);
            SessionState.EraseString(HOLDER_KEY);
            SessionState.SetString(COMPLETION_KEY, $"Level submission was aborted:\n\n{errorMessage}");
            EditorApplication.isPlaying = false;
        }

        private void OnGUI()
        {
            GUI.depth = -1000;
            float panelW = 460;
            float panelH = 120;
            Rect r = new Rect((Screen.width - panelW) * 0.5f, 30, panelW, panelH);

            GUI.Box(r, GUIContent.none, (GUIStyle)"flow node 0");

            GUILayout.BeginArea(new Rect(r.x + 15, r.y + 12, r.width - 30, r.height - 24));
            GUILayout.Label("<b>LevelLens Automated Level Submitter</b>", new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14 });
            GUILayout.Space(4);

            if (_totalLevels > 0)
            {
                float progress = (float)_currentIndex / _totalLevels;
                Rect barRect = GUILayoutUtility.GetRect(r.width - 30, 16);
                EditorGUI.ProgressBar(barRect, progress, $"{_currentIndex} / {_totalLevels} ({Mathf.RoundToInt(progress * 100)}%)");
            }

            GUILayout.Space(4);
            GUILayout.Label($"<b>Status:</b> {_status}", new GUIStyle(GUI.skin.label) { richText = true });
            GUILayout.EndArea();
        }
    }
}

