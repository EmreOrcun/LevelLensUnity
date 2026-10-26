using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace LevelLens.Runtime.Network
{
    /// <summary>
    /// Persists unsent report drafts to Application.persistentDataPath/LevelLens/drafts/
    /// so crash recoveries or connection drops never lose tester reports.
    /// </summary>
    public static class OfflineDraftCache
    {
        private static string DraftsDirectory => Path.Combine(Application.persistentDataPath, "LevelLens", "drafts");

        public static void SaveDraft(SubmitReportDto report)
        {
            if (report == null || string.IsNullOrEmpty(report.reportId)) return;

            try
            {
                if (!Directory.Exists(DraftsDirectory))
                {
                    Directory.CreateDirectory(DraftsDirectory);
                }

                string filePath = Path.Combine(DraftsDirectory, $"{report.reportId}.json");
                string json = JsonConvert.SerializeObject(report, Formatting.Indented);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LevelLens] Failed to save offline draft: {ex.Message}");
            }
        }

        public static void RemoveDraft(string reportId)
        {
            if (string.IsNullOrEmpty(reportId)) return;

            try
            {
                string filePath = Path.Combine(DraftsDirectory, $"{reportId}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LevelLens] Failed to remove draft file: {ex.Message}");
            }
        }

        public static SubmitReportDto[] GetPendingDrafts()
        {
            if (!Directory.Exists(DraftsDirectory)) return Array.Empty<SubmitReportDto>();

            var files = Directory.GetFiles(DraftsDirectory, "*.json");
            var drafts = new System.Collections.Generic.List<SubmitReportDto>();

            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var dto = JsonConvert.DeserializeObject<SubmitReportDto>(json);
                    if (dto != null)
                    {
                        drafts.Add(dto);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LevelLens] Could not parse draft file {file}: {ex.Message}");
                }
            }

            return drafts.ToArray();
        }
    }
}
