using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace LevelLens.Editor
{
    public class SubmitGameWindow : EditorWindow
    {
        private const string DEFAULT_API_HOST = "https://levellens.creoapp.workers.dev";
        private const string PREF_AUTH_TOKEN = "LevelLens_AuthToken";
        private const string PREF_USER_EMAIL = "LevelLens_UserEmail";

        private string _authToken;
        private string _userEmail;

        // Login UI state
        private string _loginEmail = "";
        private string _loginPassword = "";
        private string _loginError = "";
        private bool _isLoggingIn = false;

        // Game state
        private string _gameId = "";
        private string _gameName = "";
        private UnityEngine.Object _iconAsset;
        private string _statusMessage = "";
        private bool _isError = false;
        private bool _isSubmitting = false;

        private Vector2 _scrollPos;

        [MenuItem("Level Lens/Submit Game", priority = 90)]
        public static void Open()
        {
            var window = GetWindow<SubmitGameWindow>("Submit Game");
            window.minSize = new Vector2(400, 420);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshAuthState();
            InitializeGameDefaults();
        }

        private void RefreshAuthState()
        {
            _authToken = PlayerPrefs.GetString(PREF_AUTH_TOKEN, "");
            _userEmail = PlayerPrefs.GetString(PREF_USER_EMAIL, "");
            if (string.IsNullOrEmpty(_loginEmail) && !string.IsNullOrEmpty(_userEmail))
            {
                _loginEmail = _userEmail;
            }
        }

        private void InitializeGameDefaults()
        {
            if (string.IsNullOrEmpty(_gameName))
            {
                _gameName = Application.productName;
            }

            if (string.IsNullOrEmpty(_gameId))
            {
                _gameId = Application.productName.ToLower().Replace(" ", "-").Replace("'", "");
            }

            if (_iconAsset == null)
            {
                var icons = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Unknown);
                if (icons != null && icons.Length > 0 && icons[0] != null)
                {
                    _iconAsset = icons[0];
                }
            }
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("LevelLens Game Submitter", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            if (string.IsNullOrEmpty(_authToken))
            {
                DrawLoginView();
            }
            else
            {
                DrawSubmitView();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawLoginView()
        {
            EditorGUILayout.HelpBox("Sign in to your LevelLens account to submit or update game information.", MessageType.Info);
            EditorGUILayout.Space(8);

            GUI.enabled = !_isLoggingIn;

            _loginEmail = EditorGUILayout.TextField("Email", _loginEmail);
            _loginPassword = EditorGUILayout.PasswordField("Password", _loginPassword);

            if (!string.IsNullOrEmpty(_loginError))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_loginError, MessageType.Error);
            }

            EditorGUILayout.Space(12);

            if (GUILayout.Button(_isLoggingIn ? "Signing in..." : "Sign In", GUILayout.Height(32)))
            {
                ExecuteLogin();
            }

            GUI.enabled = true;
        }

        private void DrawSubmitView()
        {
            // Signed-in header
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Signed in as: <b>{_userEmail}</b>", new GUIStyle(EditorStyles.label) { richText = true });
            if (GUILayout.Button("Sign Out", GUILayout.Width(75)))
            {
                PlayerPrefs.DeleteKey(PREF_AUTH_TOKEN);
                PlayerPrefs.DeleteKey(PREF_USER_EMAIL);
                PlayerPrefs.Save();
                RefreshAuthState();
                return;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            EditorGUILayout.HelpBox("Configure and submit or update game fields (ID, Name, and Icon) in LevelLens Cloud.", MessageType.Info);
            EditorGUILayout.Space(8);

            GUI.enabled = !_isSubmitting;

            _gameId = EditorGUILayout.TextField("Game ID", _gameId);
            _gameName = EditorGUILayout.TextField("Game Name", _gameName);
            _iconAsset = EditorGUILayout.ObjectField("Game Icon", _iconAsset, typeof(UnityEngine.Object), false);

            Texture2D previewTex = ResolveTexture(_iconAsset);

            if (previewTex != null)
            {
                EditorGUILayout.Space(6);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUIUtility.labelWidth);
                Rect previewRect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                EditorGUI.DrawPreviewTexture(previewRect, previewTex, null, ScaleMode.ScaleToFit);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(4);
            }
            else
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox("No icon assigned. A game icon (Texture2D or Sprite) is optional but recommended.", MessageType.None);
            }

            EditorGUILayout.Space(10);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _isError ? MessageType.Error : MessageType.Info);
                EditorGUILayout.Space(6);
            }

            bool canSubmit = !string.IsNullOrWhiteSpace(_gameId) && !string.IsNullOrWhiteSpace(_gameName) && !_isSubmitting;
            GUI.enabled = canSubmit;
            GUI.backgroundColor = new Color(0.2f, 0.78f, 0.38f);

            if (GUILayout.Button(_isSubmitting ? "<b>Submitting...</b>" : "<b>Submit / Update Game</b>", new GUIStyle(GUI.skin.button) { richText = true }, GUILayout.Height(38)))
            {
                ExecuteGameSubmission(previewTex);
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }

        private static Texture2D ResolveTexture(UnityEngine.Object asset)
        {
            if (asset is Texture2D tex) return tex;
            if (asset is Sprite sprite) return sprite.texture;
            return null;
        }

        private static byte[] TextureToPngBytes(Texture2D source)
        {
            int maxDim = 512;
            int width = source.width;
            int height = source.height;
            if (width > maxDim || height > maxDim)
            {
                if (width > height)
                {
                    height = Mathf.RoundToInt((float)height * maxDim / width);
                    width = maxDim;
                }
                else
                {
                    width = Mathf.RoundToInt((float)width * maxDim / height);
                    height = maxDim;
                }
            }

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            byte[] pngBytes = readable.EncodeToPNG();
            DestroyImmediate(readable);
            return pngBytes;
        }

        private void ExecuteLogin()
        {
            if (string.IsNullOrWhiteSpace(_loginEmail) || string.IsNullOrWhiteSpace(_loginPassword))
            {
                _loginError = "Please enter both email and password.";
                return;
            }

            _isLoggingIn = true;
            _loginError = "";

            string email = _loginEmail.Trim().ToLower();
            string password = _loginPassword;

            var payload = new LoginRequestDto { email = email, password = password };
            string json = JsonUtility.ToJson(payload);

            var req = new UnityWebRequest(DEFAULT_API_HOST + "/api/auth/login", "POST");
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                _isLoggingIn = false;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var res = JsonUtility.FromJson<LoginResponseDto>(req.downloadHandler.text);
                        if (!string.IsNullOrEmpty(res.token))
                        {
                            _authToken = res.token;
                            _userEmail = res.user?.email ?? email;
                            PlayerPrefs.SetString(PREF_AUTH_TOKEN, _authToken);
                            PlayerPrefs.SetString(PREF_USER_EMAIL, _userEmail);
                            PlayerPrefs.Save();
                            _loginPassword = "";
                            _loginError = "";
                        }
                        else
                        {
                            _loginError = "Sign in succeeded but no session token was received.";
                        }
                    }
                    catch (Exception ex)
                    {
                        _loginError = $"Failed to parse response: {ex.Message}";
                    }
                }
                else
                {
                    string errText = req.downloadHandler != null ? req.downloadHandler.text : "";
                    _loginError = !string.IsNullOrEmpty(errText) ? errText : $"Sign in failed: {req.error}";
                }

                req.Dispose();
                Repaint();
            };
        }

        private void ExecuteGameSubmission(Texture2D iconTexture)
        {
            _isSubmitting = true;
            _isError = false;
            _statusMessage = "Starting game submission...";
            Repaint();

            if (iconTexture != null)
            {
                byte[] pngBytes;
                try
                {
                    pngBytes = TextureToPngBytes(iconTexture);
                }
                catch (Exception ex)
                {
                    FailSubmission($"Failed to process icon texture: {ex.Message}");
                    return;
                }

                _statusMessage = "Requesting icon upload URL...";
                Repaint();

                RequestIconPresign(pngBytes, (uploadUrl, r2Key) =>
                {
                    _statusMessage = "Uploading icon to storage...";
                    Repaint();

                    UploadIconBytes(uploadUrl, pngBytes, () =>
                    {
                        _statusMessage = "Saving game metadata in LevelLens Cloud...";
                        Repaint();

                        SubmitGameMetadata(r2Key);
                    });
                });
            }
            else
            {
                _statusMessage = "Saving game metadata in LevelLens Cloud...";
                Repaint();

                SubmitGameMetadata("");
            }
        }

        private void RequestIconPresign(byte[] pngBytes, Action<string, string> onPresigned)
        {
            var presignDto = new PresignRequestDto
            {
                fileName = "game_icon.png",
                mediaType = "icon",
                mimeType = "image/png",
                sizeBytes = pngBytes.Length
            };

            string json = JsonUtility.ToJson(presignDto);
            var req = new UnityWebRequest(DEFAULT_API_HOST + "/api/upload/presign", "POST");
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _authToken);

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var res = JsonUtility.FromJson<PresignResponseDto>(req.downloadHandler.text);
                        if (!string.IsNullOrEmpty(res.uploadUrl) && !string.IsNullOrEmpty(res.r2Key))
                        {
                            req.Dispose();
                            onPresigned(res.uploadUrl, res.r2Key);
                            return;
                        }
                        FailSubmission("Presign response missing uploadUrl or r2Key.");
                    }
                    catch (Exception ex)
                    {
                        FailSubmission($"Failed to parse presign response: {ex.Message}");
                    }
                }
                else
                {
                    FailSubmission($"Presign failed ({req.responseCode}): {req.downloadHandler.text}");
                }

                req.Dispose();
            };
        }

        private void UploadIconBytes(string uploadUrl, byte[] pngBytes, Action onUploaded)
        {
            var req = new UnityWebRequest(uploadUrl, "PUT");
            req.uploadHandler = new UploadHandlerRaw(pngBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "image/png");

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    req.Dispose();
                    onUploaded();
                }
                else
                {
                    FailSubmission($"Icon upload to R2 failed ({req.responseCode}): {req.error}");
                    req.Dispose();
                }
            };
        }

        private void SubmitGameMetadata(string iconKey)
        {
            var payload = new UpsertGameRequestDto
            {
                id = _gameId.Trim().ToLower(),
                name = _gameName.Trim(),
                icon_url = iconKey
            };

            string json = JsonUtility.ToJson(payload);
            var req = new UnityWebRequest(DEFAULT_API_HOST + "/api/games", "POST");
            byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + _authToken);

            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                _isSubmitting = false;
                if (req.result == UnityWebRequest.Result.Success)
                {
                    _isError = false;
                    _statusMessage = $"Game '{_gameName}' ({_gameId}) saved successfully!";
                    EditorUtility.DisplayDialog("Success", $"Game '{_gameName}' ({_gameId}) submitted / updated successfully!", "OK");
                }
                else
                {
                    _isError = true;
                    _statusMessage = $"Game update failed ({req.responseCode}): {req.downloadHandler.text}";
                }

                req.Dispose();
                Repaint();
            };
        }

        private void FailSubmission(string error)
        {
            _isSubmitting = false;
            _isError = true;
            _statusMessage = error;
            Repaint();
        }

        [Serializable]
        private class LoginRequestDto
        {
            public string email;
            public string password;
        }

        [Serializable]
        private class LoginResponseDto
        {
            public string token;
            public UserDto user;
        }

        [Serializable]
        private class UserDto
        {
            public string id;
            public string email;
            public string role;
        }

        [Serializable]
        private class PresignRequestDto
        {
            public string fileName;
            public string mediaType;
            public string mimeType;
            public int sizeBytes;
        }

        [Serializable]
        private class PresignResponseDto
        {
            public string r2Key;
            public string uploadUrl;
        }

        [Serializable]
        private class UpsertGameRequestDto
        {
            public string id;
            public string name;
            public string icon_url;
        }
    }
}

