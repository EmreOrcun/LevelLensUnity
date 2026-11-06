using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace LevelLens.Editor
{
    public class SubmitLevelsWindow : EditorWindow
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

        // Submit UI state
        private ScriptableObject _selectedHolder;
        private Vector2 _scrollPos;

        [MenuItem("Level Lens/Submit Levels", priority = 100)]
        public static void Open()
        {
            var window = GetWindow<SubmitLevelsWindow>("Submit Levels");
            window.minSize = new Vector2(420, 360);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshAuthState();
            TryFindDefaultHolder();
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

        private void TryFindDefaultHolder()
        {
            if (_selectedHolder != null) return;

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Blueprint") || path.Contains("LevelDataHolder") || path.Contains("v0.2"))
                {
                    var candidate = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (candidate != null && GetLevelCount(candidate) > 0)
                    {
                        _selectedHolder = candidate;
                        break;
                    }
                }
            }
        }

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("LevelLens Level Submitter", EditorStyles.boldLabel);
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
            EditorGUILayout.HelpBox("Sign in to your LevelLens account to submit levels directly to the cloud.", MessageType.Info);
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

            // Level Holder selector
            EditorGUILayout.LabelField("Level Holder Selection", EditorStyles.boldLabel);
            _selectedHolder = (ScriptableObject)EditorGUILayout.ObjectField("Level Holder", _selectedHolder, typeof(ScriptableObject), false);

            int levelCount = GetLevelCount(_selectedHolder);
            if (_selectedHolder != null)
            {
                EditorGUILayout.LabelField($"Total levels in holder: <b>{levelCount}</b>", new GUIStyle(EditorStyles.label) { richText = true });
            }
            else
            {
                EditorGUILayout.HelpBox("Select a valid Level Holder ScriptableObject containing levels to submit.", MessageType.Warning);
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.HelpBox(
                "Submitting levels will enter Play Mode, step through each level in the chosen holder, capture clean layout screenshots, and submit them directly to LevelLens Cloud without test reports.\n\n" +
                "Please do not interact with the Game view during submission.",
                MessageType.Info
            );

            EditorGUILayout.Space(16);

            bool canStart = _selectedHolder != null && levelCount > 0 && !EditorApplication.isPlaying;

            GUI.enabled = canStart;
            GUI.backgroundColor = new Color(0.2f, 0.78f, 0.38f);

            if (GUILayout.Button("<b>Start Submitting Levels</b>", new GUIStyle(GUI.skin.button) { richText = true }, GUILayout.Height(42)))
            {
                PromptAndStartSubmission();
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
        }

        public static int GetLevelCount(ScriptableObject holder)
        {
            if (holder == null) return 0;

            var handler = LevelLensBatchHandlerRegistry.FindHandler(holder);
            if (handler != null)
            {
                var names = handler.GetLevelNames(holder);
                if (names != null) return names.Count;
            }

            var so = new SerializedObject(holder);
            var prop = so.FindProperty("<Levels>k__BackingField") ?? so.FindProperty("Levels") ?? so.FindProperty("_levels");
            return prop != null && prop.isArray ? prop.arraySize : 0;
        }

        public static List<string> GetLevelNames(ScriptableObject holder)
        {
            var names = new List<string>();
            if (holder == null) return names;

            var handler = LevelLensBatchHandlerRegistry.FindHandler(holder);
            if (handler != null)
            {
                var list = handler.GetLevelNames(holder);
                if (list != null && list.Count > 0)
                {
                    names.AddRange(list);
                    return names;
                }
            }

            var so = new SerializedObject(holder);
            var prop = so.FindProperty("<Levels>k__BackingField") ?? so.FindProperty("Levels") ?? so.FindProperty("_levels");
            if (prop != null && prop.isArray)
            {
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var elem = prop.GetArrayElementAtIndex(i);
                    var idProp = elem.FindPropertyRelative("LevelId") ?? elem.FindPropertyRelative("id") ?? elem.FindPropertyRelative("name");
                    names.Add(idProp != null && !string.IsNullOrEmpty(idProp.stringValue) ? idProp.stringValue : $"Level_{i + 1:D3}");
                }
            }

            return names;
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

        private void PromptAndStartSubmission()
        {
            int count = GetLevelCount(_selectedHolder);
            if (_selectedHolder == null || count <= 0) return;

            bool confirmed = EditorUtility.DisplayDialog(
                "Submit Levels to LevelLens",
                $"Are you sure you want to submit all {count} levels from '{_selectedHolder.name}' to LevelLens?\n\n" +
                "Unity will enter Play Mode and automatically step through each level, capturing and uploading screenshots to LevelLens Cloud without test reports.",
                "Yes",
                "Cancel"
            );

            if (!confirmed) return;

            string holderPath = AssetDatabase.GetAssetPath(_selectedHolder);
            if (string.IsNullOrEmpty(holderPath))
            {
                EditorUtility.DisplayDialog("Error", "Selected Level Holder asset path could not be resolved.", "OK");
                return;
            }

            // Arm SessionState for runtime batch runner
            SessionState.SetBool("LevelLens.BatchSubmit.Active", true);
            SessionState.SetString("LevelLens.BatchSubmit.HolderPath", holderPath);

            string entryScenePath = ResolveMainScenePath();
            if (SceneManager.GetActiveScene().path != entryScenePath)
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    EditorSceneManager.OpenScene(entryScenePath);
                }
                else
                {
                    SessionState.EraseBool("LevelLens.BatchSubmit.Active");
                    SessionState.EraseString("LevelLens.BatchSubmit.HolderPath");
                    return;
                }
            }

            EditorApplication.isPlaying = true;
            Close();
        }

        private static string ResolveMainScenePath()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.name.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return activeScene.path;
            }

            foreach (var s in EditorBuildSettings.scenes)
            {
                if (s.enabled && s.path.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return s.path;
                }
            }

            if (File.Exists("Assets/Game/Scenes/MainScene.unity"))
            {
                return "Assets/Game/Scenes/MainScene.unity";
            }

            return activeScene.path;
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
    }
}

