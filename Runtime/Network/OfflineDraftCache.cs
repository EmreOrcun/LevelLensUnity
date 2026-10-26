using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;

namespace LevelLens.Runtime.Network
{
    public class OfflineDraftCache : MonoBehaviour
    {
        private string CacheDir => Path.Combine(Application.persistentDataPath, "LevelLens", "drafts");

        public void SaveDraft(SubmitReportDto draft)
        {
            if (draft == null) return;
            Directory.CreateDirectory(CacheDir);
            string path = Path.Combine(CacheDir, $"draft_{draft.reportId}.json");
            string json = JsonConvert.SerializeObject(draft, Formatting.Indented);
            File.WriteAllText(path, json);
        }

        public void DeleteDraft(string reportId)
        {
            string path = Path.Combine(CacheDir, $"draft_{reportId}.json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
