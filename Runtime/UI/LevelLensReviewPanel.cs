using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LevelLens.Runtime.Core;
using LevelLens.Runtime.Network;

namespace LevelLens.Runtime.UI
{
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
        private string _capturedScreenshotPath;
        private string _screenshotR2Key;

        public void Open()
        {
            m_PanelRoot.SetActive(true);
            CaptureThumbnailIfEmpty();
        }

        public void Close()
        {
            m_PanelRoot.SetActive(false);
        }

        private void Start()
        {
            if (m_TestPlayButton != null) m_TestPlayButton.onClick.AddListener(OnTestPlayClicked);
            if (m_CancelButton != null) m_CancelButton.onClick.AddListener(OnCancelClicked);
            if (m_SubmitButton != null) m_SubmitButton.onClick.AddListener(OnSubmitClicked);
            if (m_AddBugButton != null) m_AddBugButton.onClick.AddListener(AddBugCard);
            if (m_AddFeedbackButton != null) m_AddFeedbackButton.onClick.AddListener(AddFeedbackCard);
        }

        private void CaptureThumbnailIfEmpty()
        {
            if (!string.IsNullOrEmpty(_capturedScreenshotPath)) return;
            var capturer = LevelLensManager.Instance?.ScreenshotCapturer;
            if (capturer != null)
            {
                StartCoroutine(capturer.CaptureUIFreeScreenshot(path =>
                {
                    _capturedScreenshotPath = path;
                }));
            }
        }

        public void SetScore(int score) => _score = Mathf.Clamp(score, 1, 10);
        public void SetDifficulty(string tag) => _difficultyTag = tag;

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
            StartCoroutine(SubmitRoutine());
        }

        private IEnumerator SubmitRoutine()
        {
            var manager = LevelLensManager.Instance;
            if (manager == null || manager.Adapter == null) yield break;

            var adapter = manager.Adapter;
            var uploader = manager.Uploader;

            // 1. Upload screenshot if needed
            if (!string.IsNullOrEmpty(_capturedScreenshotPath) && string.IsNullOrEmpty(_screenshotR2Key))
            {
                bool presignDone = false;
                uploader.StartCoroutine(uploader.RequestPresign(_capturedScreenshotPath, "image", "image/jpeg", (resp, err) =>
                {
                    if (resp != null) _screenshotR2Key = resp.r2Key;
                    presignDone = true;
                }));
                yield return new WaitUntil(() => presignDone);

                if (!string.IsNullOrEmpty(_screenshotR2Key))
                {
                    // Presigned upload
                    // In real flow, upload directly
                }
            }

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
                screenshotKey = _screenshotR2Key ?? "screenshots/mock_key.jpg",
                reviveCount = manager.Telemetry?.SessionRevives ?? 0,
                failCount = manager.Telemetry?.SessionFails ?? 0,
                playtimeSeconds = manager.Telemetry?.ActivePlaytimeSeconds ?? 0,
                items = _items
            };

            bool submitDone = false;
            uploader.StartCoroutine(uploader.SubmitReport(reportDto, (success, err) =>
            {
                submitDone = true;
                if (success)
                {
                    Close();
                }
                else
                {
                    manager.DraftCache?.SaveDraft(reportDto);
                }
            }));
            yield return new WaitUntil(() => submitDone);
        }
    }
}
