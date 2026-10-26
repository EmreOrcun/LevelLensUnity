using System;
using System.IO;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace LevelLens.Runtime.Network
{
    /// <summary>
    /// Handles requesting presigned URLs from Cloudflare Workers and streaming
    /// local files directly to R2 using disk-based UploadHandlerFile.
    /// </summary>
    public static class R2DirectUploader
    {
        public static IEnumerator RequestPresignedUrlCoroutine(
            string apiHost,
            string apiKey,
            string localFilePath,
            string mediaType,
            string mimeType,
            Action<PresignResponseDto> onSuccess,
            Action<string> onError)
        {
            if (!File.Exists(localFilePath))
            {
                onError?.Invoke($"Local file not found: {localFilePath}");
                yield break;
            }

            var fileInfo = new FileInfo(localFilePath);
            var reqDto = new PresignRequestDto
            {
                fileName = Path.GetFileName(localFilePath),
                mediaType = mediaType,
                mimeType = mimeType,
                sizeBytes = (int)fileInfo.Length
            };

            string json = JsonConvert.SerializeObject(reqDto);
            string url = $"{apiHost.TrimEnd('/')}/api/upload/presign";

            using var req = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
            {
                req.SetRequestHeader("X-LevelLens-Key", apiKey);
            }

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Presign failed ({req.responseCode}): {req.downloadHandler.text}");
            }
            else
            {
                var resDto = JsonConvert.DeserializeObject<PresignResponseDto>(req.downloadHandler.text);
                onSuccess?.Invoke(resDto);
            }
        }

        public static IEnumerator StreamFileToR2Coroutine(
            string presignedUploadUrl,
            string localFilePath,
            string mimeType,
            Action<float> onProgress,
            Action onSuccess,
            Action<string> onError)
        {
            if (!File.Exists(localFilePath))
            {
                onError?.Invoke($"Cannot upload missing file: {localFilePath}");
                yield break;
            }

            using var req = new UnityWebRequest(presignedUploadUrl, "PUT");
            req.uploadHandler = new UploadHandlerFile(localFilePath);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", mimeType);

            var asyncOp = req.SendWebRequest();

            while (!asyncOp.isDone)
            {
                onProgress?.Invoke(req.uploadProgress);
                yield return null;
            }

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Direct R2 upload failed ({req.responseCode}): {req.error}");
            }
            else
            {
                onProgress?.Invoke(1f);
                onSuccess?.Invoke();
            }
        }

        public static IEnumerator SubmitReportCoroutine(
            string apiHost,
            string apiKey,
            SubmitReportDto report,
            Action onSuccess,
            Action<string> onError)
        {
            string json = JsonConvert.SerializeObject(report, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            string url = $"{apiHost.TrimEnd('/')}/api/reports";

            using var req = new UnityWebRequest(url, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
            {
                req.SetRequestHeader("X-LevelLens-Key", apiKey);
            }

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Report submission failed ({req.responseCode}): {req.downloadHandler.text}");
            }
            else
            {
                onSuccess?.Invoke();
            }
        }
    }
}
