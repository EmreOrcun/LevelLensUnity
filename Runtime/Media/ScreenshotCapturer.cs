using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace LevelLens.Runtime.Media
{
    public class ScreenshotCapturer : MonoBehaviour
    {
        [SerializeField] private Canvas[] m_CanvasesToHide;

        public IEnumerator CaptureUIFreeScreenshot(Action<string> onCaptured)
        {
            // 1. Hide UI canvases
            if (m_CanvasesToHide != null)
            {
                foreach (var canvas in m_CanvasesToHide)
                {
                    if (canvas != null) canvas.enabled = false;
                }
            }

            yield return new WaitForEndOfFrame();

            // 2. Capture screenshot texture
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();

            // 3. Restore UI canvases immediately
            if (m_CanvasesToHide != null)
            {
                foreach (var canvas in m_CanvasesToHide)
                {
                    if (canvas != null) canvas.enabled = true;
                }
            }

            if (tex == null)
            {
                onCaptured?.Invoke(null);
                yield break;
            }

            // 4. Encode to JPG
            byte[] bytes = tex.EncodeToJPG(85);
            Destroy(tex);

            // 5. Save to persistent data path
            string dir = Path.Combine(Application.persistentDataPath, "LevelLens", "media");
            Directory.CreateDirectory(dir);
            string filePath = Path.Combine(dir, $"screenshot_{DateTime.UtcNow.Ticks}.jpg");
            File.WriteAllBytes(filePath, bytes);

            onCaptured?.Invoke(filePath);
        }
    }
}
