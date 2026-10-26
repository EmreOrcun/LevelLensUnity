using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace LevelLens.Runtime.Network
{
    public class R2DirectUploader : MonoBehaviour
    {
        private string _apiHost;
        private string _apiKey;

        public void Initialize(string apiHost, string apiKey)
        {
            _apiHost = apiHost?.TrimEnd('/');
            _apiKey = apiKey;
        }

        public IEnumerator RequestPresign(string filePath, string mediaType, string mimeType, Action<PresignResponseDto, string> callback)
        {
            if (!File.Exists(filePath))
            {
                callback?.Invoke(null, $"File does not exist: {filePath}");
                yield break;
            }

            var fileInfo = new FileInfo(filePath);
            var reqDto = new PresignRequestDto
            {
                fileName = fileInfo.Name,
                mediaType = mediaType,
                mimeType = mimeType,
                sizeBytes = (int)fileInfo.Length
            };

            string json = JsonConvert.SerializeObject(reqDto);
            string url = $"{_apiHost}/api/upload/presign";

            using (var uwr = new UnityWebRequest(url, "POST"))
            {
                uwr.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(_apiKey)) uwr.SetRequestHeader("X-LevelLens-Key", _apiKey);

                yield return uwr.SendWebRequest();

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(null, $"Presign request failed: {uwr.error} - {uwr.downloadHandler?.text}");
                    yield break;
                }

                try
                {
                    var response = JsonConvert.DeserializeObject<PresignResponseDto>(uwr.downloadHandler.text);
                    callback?.Invoke(response, null);
                }
                catch (Exception ex)
                {
                    callback?.Invoke(null, $"Failed to parse presign response: {ex.Message}");
                }
            }
        }

        public IEnumerator UploadFileDirect(string filePath, string uploadUrl, string mimeType, Action<bool, string> callback)
        {
            if (!File.Exists(filePath))
            {
                callback?.Invoke(false, "File not found on disk");
                yield break;
            }

            using (var uwr = new UnityWebRequest(uploadUrl, "PUT"))
            {
                uwr.uploadHandler = new UploadHandlerFile(filePath);
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", mimeType);

                yield return uwr.SendWebRequest();

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(false, $"Upload failed: {uwr.error}");
                }
                else
                {
                    callback?.Invoke(true, null);
                }
            }
        }

        public IEnumerator SubmitReport(SubmitReportDto report, Action<bool, string> callback)
        {
            string json = JsonConvert.SerializeObject(report, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            string url = $"{_apiHost}/api/reports";
            using (var uwr = new UnityWebRequest(url, "POST"))
            {
                uwr.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                uwr.downloadHandler = new DownloadHandlerBuffer();
                uwr.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(_apiKey)) uwr.SetRequestHeader("X-LevelLens-Key", _apiKey);

                yield return uwr.SendWebRequest();

                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    callback?.Invoke(false, $"Report submit failed: {uwr.error} - {uwr.downloadHandler?.text}");
                }
                else
                {
                    callback?.Invoke(true, null);
                }
            }
        }
    }
}
