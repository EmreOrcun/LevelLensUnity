using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LevelLens.Runtime.Core;
using LevelLens.Runtime.Media;
using LevelLens.Runtime.Network;

namespace LevelLens.Runtime.UI
{
    /// <summary>
    /// In-game review menu controller. Handles score rating, difficulty tags,
    /// bug/feedback items, and initiates R2 uploads.
    /// </summary>
    public class LevelLensReviewPanel : MonoBehaviour
    {
        [Header("Root Canvas")]
        [SerializeField] private GameObject m_PanelRoot;

        [Header("Controls")]
        [SerializeField] private Button m_TestPlayButton;
        [SerializeField] private Button m_CancelButton;
        [SerializeField] private Button m_SubmitButton;
        [SerializeField] private Button m_AddBugButton;
        [SerializeField] private Button m_AddFeedbackButton;

        private int _score = 8;
        private string _difficultyTag = "Medium";
        private readonly List<ReportItemDto> _items = new();
        private bool _isSubmitting;

        public void Open()
        {
            if (m_PanelRoot != null)
            {
                m_PanelRoot.SetActive(true);
            }
            _isSubmitting = false;
        }

        public void Close()
        {
            if (m_PanelRoot != null)
            {
                m_PanelRoot.SetActive(false);
            }
        }

        private void Start()
        {
            if (m_TestPlayButton != null) m_TestPlayButton.onClick.AddListener(OnTestPlayClicked);
            if (m_CancelButton != null) m_CancelButton.onClick.AddListener(OnCancelClicked);
            if (m_SubmitButton != null) m_SubmitButton.onClick.AddListener(OnSubmitClicked);
            if (m_AddBugButton != null) m_AddBugButton.onClick.AddListener(AddBugCard);
            if (m_AddFeedbackButton != null) m_AddFeedbackButton.onClick.AddListener(AddFeedbackCard);
        }

        public void SetScore(int score) => _score = Mathf.Clamp(score, 1, 10);
        public void SetDifficulty(string tag)
        {
            if (tag == "Easy" || tag == "Medium" || tag == "Hard" || tag == "Very Hard")
            {
                _difficultyTag = tag;
            }
        }

        public void AddBugCard()
        {
            if (_items.Count >= 10) return;
            _items.Add(new ReportItemDto
            {
                itemType = "bug",
                title = "New Bug",
                priority = "medium",
                description = ""
            });
        }

        public void AddFeedbackCard()
        {
            if (_items.Count >= 10) return;
            _items.Add(new ReportItemDto
            {
                itemType = "feedback",
                title = "Feedback Note",
                category = "improve",
                description = ""
            });
        }

        private void OnTestPlayClicked()
        {
            LevelLensManager.Instance?.RestartForTesting();
        }

        private void OnCancelClicked()
        {
            LevelLensManager.Instance?.CloseReviewPanel();
        }

        private void OnSubmitClicked()
        {
            if (_isSubmitting) return;
            _isSubmitting = true;
            StartCoroutine(SubmitRoutine());
        }

        private IEnumerator SubmitRoutine()
        {
            var manager = LevelLensManager.Instance;
            if (manager == null || manager.Adapter == null)
            {
                _isSubmitting = false;
                yield break;
            }

            var adapter = manager.Adapter;
            string apiHost = manager.ApiHost;
            string apiKey = manager.ApiKey;
            string screenshotLocalPath = manager.LatestScreenshotPath;
            string screenshotKey = "";

            // 1. Upload screenshot if available
            if (!string.IsNullOrEmpty(screenshotLocalPath) && File.Exists(screenshotLocalPath))
            {
                PresignResponseDto presign = null;
                string presignError = null;

                yield return R2DirectUploader.RequestPresignedUrlCoroutine(
                    apiHost,
                    apiKey,
                    screenshotLocalPath,
                    "image",
                    "image/jpeg",
                    res => presign = res,
                    err => presignError = err
                );

                if (presignError != null)
                {
                    Debug.LogError($"[LevelLens] Screenshot presign failed: {presignError}");
                }
                else if (presign != null)
                {
                    bool uploadSuccess = false;
                    string uploadError = null;

                    yield return R2DirectUploader.StreamFileToR2Coroutine(
                        presign.uploadUrl,
                        screenshotLocalPath,
                        "image/jpeg",
                        null,
                        () => uploadSuccess = true,
                        err => uploadError = err
                    );

                    if (uploadSuccess)
                    {
                        screenshotKey = presign.r2Key;
                    }
                    else
                    {
                        Debug.LogError($"[LevelLens] Screenshot upload failed: {uploadError}");
                    }
                }
            }

            // 2. Build report DTO
            var reportDto = new SubmitReportDto
            {
                reportId = Guid.NewGuid().ToString(),
                gameId = Application.productName.ToLower().Replace(" ", "-").Replace("'", ""),
                gameName = Application.productName,
                versionName = Application.version,
                holderName = adapter.CurrentLevelHolderName,
                levelName = adapter.CurrentLevelId,
                levelIndex = adapter.CurrentLevelIndex,
                testerName = "QA Tester",
                score = _score,
                difficultyTag = _difficultyTag,
                screenshotKey = screenshotKey,
                reviveCount = manager.Telemetry?.SessionRevives ?? 0,
                failCount = manager.Telemetry?.SessionFails ?? 0,
                playtimeSeconds = manager.Telemetry?.ActivePlaytimeSeconds ?? 0,
                items = _items
            };

            // 3. Cache draft in case of network interruption
            OfflineDraftCache.SaveDraft(reportDto);

            // 4. Submit to REST API
            bool submitSuccess = false;
            string submitError = null;

            yield return R2DirectUploader.SubmitReportCoroutine(
                apiHost,
                apiKey,
                reportDto,
                () => submitSuccess = true,
                err => submitError = err
            );

            _isSubmitting = false;

            if (submitSuccess)
            {
                OfflineDraftCache.RemoveDraft(reportDto.reportId);
                Close();
                Debug.Log("[LevelLens] Report submitted successfully!");
            }
            else
            {
                Debug.LogError($"[LevelLens] Report submission failed: {submitError}. Saved as offline draft.");
            }
        }
    }
}
