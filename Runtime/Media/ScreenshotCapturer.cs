using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LevelLens.Runtime.Media
{
    /// <summary>
    /// Captures UI-free gameplay screenshots by temporarily hiding root canvases
    /// and rendering the active Camera into a temporary RenderTexture.
    /// </summary>
    public static class ScreenshotCapturer
    {
        public static IEnumerator CaptureCleanScreenshotCoroutine(
            Camera targetCamera,
            int targetWidth,
            int targetHeight,
            string outputPath,
            Action<string> onComplete)
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (targetCamera == null)
            {
                throw new InvalidOperationException("No camera available to capture clean screenshot.");
            }

            var hiddenCanvases = new List<Canvas>();
            var allCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>(false);
            foreach (var canvas in allCanvases)
            {
                if (canvas.isRootCanvas && canvas.enabled)
                {
                    canvas.enabled = false;
                    hiddenCanvases.Add(canvas);
                }
            }

            yield return new WaitForEndOfFrame();

            RenderTexture rt = null;
            Texture2D screenShot = null;

            try
            {
                rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32);
                targetCamera.targetTexture = rt;
                targetCamera.Render();

                RenderTexture.active = rt;
                screenShot = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
                screenShot.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                screenShot.Apply();

                targetCamera.targetTexture = null;
                RenderTexture.active = null;

                byte[] bytes = screenShot.EncodeToJPG(85);
                var dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(outputPath, bytes);
                onComplete?.Invoke(outputPath);
            }
            finally
            {
                if (targetCamera != null)
                {
                    targetCamera.targetTexture = null;
                }
                RenderTexture.active = null;

                if (rt != null)
                {
                    RenderTexture.ReleaseTemporary(rt);
                }

                if (screenShot != null)
                {
                    UnityEngine.Object.Destroy(screenShot);
                }

                foreach (var canvas in hiddenCanvases)
                {
                    if (canvas != null)
                    {
                        canvas.enabled = true;
                    }
                }
            }
        }
    }
}
