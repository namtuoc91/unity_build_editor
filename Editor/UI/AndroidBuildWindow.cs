using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Raccoon.BuildEditor
{
    public class AndroidBuildWindow : EditorWindow
    {
        const float IconFieldSize = 96f;
        const string FoldNoAdsRulesKey = "Raccoon.BuildEditor.FoldNoAdsRules";

        BuildConfig _config;
        BuildHistory _history;
        MediationReport _mediation;
        List<ValidationMessage> _validation = new List<ValidationMessage>();
        List<AdbDevice> _devices = new List<AdbDevice>();
        string _deviceError;
        bool _loadingDevices;
        string _selectedSerial;
        string _adbStatus;
        List<(MessageType type, string text)> _ruleCheck;

        Vector2 _scroll;
        int _tab;
        bool _foldApp = true, _foldBuild = true, _foldSigning, _foldScenes, _foldOutput, _foldDefines, _foldMediation;

        [MenuItem("Tools/Raccoon/Android Build")]
        public static void Open()
        {
            var window = GetWindow<AndroidBuildWindow>();
            window.titleContent = new GUIContent("Android Build");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        void OnEnable()
        {
            _config = BuildConfig.Load();
            _history = BuildHistory.Load();
            _mediation = MediationReview.Scan();
            AndroidBuilder.BuildFinished -= OnBuildFinished;
            AndroidBuilder.BuildFinished += OnBuildFinished;
            RefreshValidation();
            RefreshDevices();
        }

        void OnDisable()
        {
            AndroidBuilder.BuildFinished -= OnBuildFinished;
        }

        void OnBuildFinished(BuildOutcome o)
        {
            _config = BuildConfig.Load();
            _history = BuildHistory.Load();
            RefreshValidation();
            Repaint();
        }

        void RefreshValidation()
        {
            if (_config == null) return;
            _validation = BuildValidator.ValidateCurrent(_config, _config.ActivePreset, _mediation);
        }

        void RefreshDevices()
        {
            if (!AdbService.IsAvailable)
            {
                _deviceError = "Không tìm thấy adb (Android SDK của Unity).";
                return;
            }

            _loadingDevices = true;
            AdbService.ListDevices((list, err) =>
            {
                _loadingDevices = false;
                _devices = list;
                _deviceError = err;
                if (_devices.All(d => d.serial != _selectedSerial))
                    _selectedSerial = _devices.FirstOrDefault(d => d.IsReady)?.serial;
                Repaint();
            });
        }

        void SaveConfig()
        {
            _config.Save();
            RefreshValidation();
        }

        // ================= GUI =================

        void OnGUI()
        {
            if (_config == null) OnEnable();

            using (new EditorGUI.DisabledScope(AndroidBuilder.IsBuilding || EditorApplication.isCompiling))
            {
                DrawPresetBar();
                var preset = _config.ActivePreset;

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUI.BeginChangeCheck();

                _foldApp = Section(_foldApp, "App", () => DrawApp(preset));
                _foldBuild = Section(_foldBuild, "Build", () => DrawBuild(preset));
                _foldSigning = Section(_foldSigning, "Signing", () => DrawSigning(preset));
                _foldScenes = Section(_foldScenes, "Scenes", DrawScenes);
                _foldOutput = Section(_foldOutput, "Output", () => DrawOutput(preset));

                if (EditorGUI.EndChangeCheck())
                {
                    BuildRules.EnforceModeLocks(preset);
                    SaveConfig();
                }

                _foldDefines = Section(_foldDefines, "Define Symbols (read-only)", DrawDefines);
                _foldMediation = Section(_foldMediation, "AdMob Mediation (read-only)", DrawMediation);

                EditorGUILayout.Space();
                DrawValidation();
                DrawBuildButtons(preset);
                DrawLastOutcome();

                EditorGUILayout.Space();
                var apkCount = _history.entries.Count(e => !e.isAab);
                var aabCount = _history.entries.Count - apkCount;
                _tab = GUILayout.Toolbar(_tab, new[] { "Devices", $"History APK ({apkCount})", $"History AAB ({aabCount})" });
                if (_tab == 0) DrawDevices();
                else DrawHistory(_tab == 2);

                EditorGUILayout.EndScrollView();
            }
        }

        static bool Section(bool open, string title, System.Action draw)
        {
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            if (open)
            {
                EditorGUI.indentLevel++;
                draw();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            return open;
        }

        void DrawPresetBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var names = _config.presets.Select((p, i) => $"{i + 1}. {p.name} ({p.mode})").ToArray();
                var idx = EditorGUILayout.Popup(_config.activePresetIndex, names, EditorStyles.toolbarPopup,
                    GUILayout.MinWidth(160));
                if (idx != _config.activePresetIndex)
                {
                    _config.activePresetIndex = idx;
                    SaveConfig();
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Mới", EditorStyles.toolbarButton))
                {
                    var appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                    _config.presets.Add(new BuildPreset { name = "Preset " + (_config.presets.Count + 1), appId = appId });
                    _config.activePresetIndex = _config.presets.Count - 1;
                    SaveConfig();
                }

                if (GUILayout.Button("Duplicate", EditorStyles.toolbarButton))
                {
                    var copy = _config.ActivePreset.Clone();
                    copy.name += " Copy";
                    _config.presets.Insert(_config.activePresetIndex + 1, copy);
                    _config.activePresetIndex++;
                    SaveConfig();
                }

                using (new EditorGUI.DisabledScope(_config.presets.Count <= 1))
                {
                    if (GUILayout.Button("Xóa", EditorStyles.toolbarButton) &&
                        EditorUtility.DisplayDialog("Xóa preset", $"Xóa preset '{_config.ActivePreset.name}'?", "Xóa", "Hủy"))
                    {
                        _config.presets.RemoveAt(_config.activePresetIndex);
                        _config.activePresetIndex = Mathf.Clamp(_config.activePresetIndex - 1, 0, _config.presets.Count - 1);
                        SaveConfig();
                    }
                }
            }
        }

        void DrawApp(BuildPreset p)
        {
            p.name = EditorGUILayout.TextField("Tên preset", p.name);
            DrawAppIdentity();
            p.appId = EditorGUILayout.TextField("App ID", p.appId);
            _config.version = EditorGUILayout.TextField("Version", _config.version);

            using (new EditorGUILayout.HorizontalScope())
            {
                var shown = BuildRules.CurrentVersionCode(_config.versionCode, PlayerSettings.Android.bundleVersionCode);
                var code = EditorGUILayout.IntField("Version Code", shown);
                if (GUILayout.Button("−", GUILayout.Width(24))) code--;
                if (GUILayout.Button("+", GUILayout.Width(24))) code++;
                if (code != shown && code > 0)
                {
                    // Set tay: ghi cả config và PlayerSettings để max() không kéo ngược lại.
                    _config.versionCode = code;
                    PlayerSettings.Android.bundleVersionCode = code;
                    GUI.changed = true;
                }
            }

            p.autoIncrement = EditorGUILayout.Toggle(new GUIContent("Auto-increment", "Tăng version code +1 trước mỗi build"), p.autoIncrement);
        }

        /// <summary>Tên app + icon: ghi thẳng vào PlayerSettings (dùng chung mọi preset).</summary>
        void DrawAppIdentity()
        {
            var name = EditorGUILayout.DelayedTextField(new GUIContent("App Name", "PlayerSettings.productName"),
                PlayerSettings.productName);
            if (name != PlayerSettings.productName && !string.IsNullOrWhiteSpace(name))
            {
                PlayerSettings.productName = name.Trim();
                AssetDatabase.SaveAssets();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var current = AppIconService.GetDefaultIcon();
                EditorGUILayout.PrefixLabel(new GUIContent("Icon", "Kéo thả texture vào ô. Set Default Icon + clear icon Android Adaptive/Round/Legacy."));
                // Rect cố định + tắt indent để ô luôn vuông (indent của foldout làm hẹp chiều ngang).
                var rect = GUILayoutUtility.GetRect(IconFieldSize, IconFieldSize,
                    GUILayout.Width(IconFieldSize), GUILayout.Height(IconFieldSize));
                var indent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                var icon = (Texture2D)EditorGUI.ObjectField(rect, current, typeof(Texture2D), false);
                EditorGUI.indentLevel = indent;
                if (icon != current && icon != null)
                {
                    AppIconService.SetIcon(icon);
                    Debug.Log($"[RaccoonBuild] Đã set icon app: {AssetDatabase.GetAssetPath(icon)}");
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    var warn = AppIconService.CheckTexture(icon ?? current);
                    if (warn != null) EditorGUILayout.HelpBox(warn, MessageType.Warning);
                    if (AppIconService.HasAndroidOverrides())
                    {
                        EditorGUILayout.HelpBox("Còn icon Android Adaptive/Round/Legacy đang che Default Icon.", MessageType.Info);
                        if (GUILayout.Button("Clear icon Android", GUILayout.Width(140)))
                        {
                            AppIconService.ClearAndroidOverrides();
                            AssetDatabase.SaveAssets();
                        }
                    }
                }
            }
        }

        void DrawBuild(BuildPreset p)
        {
            p.mode = (BuildMode)EditorGUILayout.EnumPopup("Mode", p.mode);
            var release = p.mode == BuildMode.Release;
            EditorGUILayout.LabelField(" ", release ? "AAB · ARMv7 + ARM64 · IL2CPP · LZ4HC" : "APK · ARM64 · IL2CPP · LZ4HC",
                EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(release))
            {
                p.developmentBuild = EditorGUILayout.Toggle("Development Build", p.developmentBuild && !release);
                using (new EditorGUI.DisabledScope(!p.developmentBuild))
                {
                    EditorGUI.indentLevel++;
                    p.scriptDebugging = EditorGUILayout.Toggle("Script Debugging", p.scriptDebugging && p.developmentBuild);
                    p.autoconnectProfiler = EditorGUILayout.Toggle("Autoconnect Profiler", p.autoconnectProfiler && p.developmentBuild);
                    EditorGUI.indentLevel--;
                }
            }

            var adPack = AdsSettingApplier.IsAdPackInstalled;
            using (new EditorGUI.DisabledScope(release))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    p.noAds = EditorGUILayout.ToggleLeft("No Ads (creative mode)", p.noAds && !release, GUILayout.Width(200));
                    using (new EditorGUI.DisabledScope(p.noAds || !adPack))
                        p.useTestAd = EditorGUILayout.ToggleLeft("Use Test Ad", p.useTestAd && !release);
                }
            }

            if (!adPack)
                EditorGUILayout.HelpBox("Chưa cài com.raccoon.adpack → No Ads chỉ áp No Ads Rules; Use Test Ad bị khóa.", MessageType.None);
            else if (release)
                EditorGUILayout.HelpBox("Release: luôn ép _creativeMode = false và use_test_ad = false (sửa + save scene/asset).", MessageType.None);

            // Rule luôn hiện (collapse được); lưu ở config chung nên các lần sau / preset khác dùng lại.
            var enabledRules = _config.noAdsRules.Count(r => r.enabled);
            var foldRules = EditorPrefs.GetBool(FoldNoAdsRulesKey, true);
            var title = $"No Ads Rules ({enabledRules})" +
                        (enabledRules > 0 ? (p.noAds && !release ? " → áp Value No Ads" : " → áp Value Has Ads") : "");
            var newFold = EditorGUILayout.Foldout(foldRules, title, true, EditorStyles.foldoutHeader);
            if (newFold != foldRules) EditorPrefs.SetBool(FoldNoAdsRulesKey, newFold);
            if (newFold)
            {
                EditorGUI.indentLevel++;
                DrawNoAdsRules();
                EditorGUI.indentLevel--;
            }

            p.cleanCache = EditorGUILayout.Toggle("Clean Build Cache", p.cleanCache);
            _config.appBundleSizeWarningMB = Mathf.Max(0, EditorGUILayout.IntField(
                new GUIContent("App Bundle size warn (MB)", "Popup cảnh báo size của Unity. 0 = tắt."),
                _config.appBundleSizeWarningMB));
        }

        void DrawSigning(BuildPreset p)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _config.keystorePath = EditorGUILayout.TextField("Keystore", _config.keystorePath);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var f = EditorUtility.OpenFilePanel("Chọn keystore", "", "keystore,jks");
                    if (!string.IsNullOrEmpty(f))
                    {
                        _config.keystorePath = ToProjectRelative(f);
                        GUI.changed = true;
                    }
                }
            }

            _config.keystoreAlias = EditorGUILayout.TextField("Alias", _config.keystoreAlias);

            var ksPass = EditorGUILayout.PasswordField("Keystore Password", BuildConfig.KeystorePass);
            var aliasPass = EditorGUILayout.PasswordField("Alias Password", BuildConfig.KeyAliasPass);
            if (ksPass != BuildConfig.KeystorePass) BuildConfig.KeystorePass = ksPass;
            if (aliasPass != BuildConfig.KeyAliasPass) BuildConfig.KeyAliasPass = aliasPass;

            EditorGUILayout.HelpBox("Password chỉ lưu trong session Editor (SessionState), không ghi ra file.", MessageType.None);

            var exists = !string.IsNullOrEmpty(_config.keystorePath) && File.Exists(_config.keystorePath);
            if (p.mode == BuildMode.Dev &&
                !BuildValidator.HasUsableKeystore(exists, _config.keystoreAlias, ksPass, aliasPass))
                EditorGUILayout.HelpBox("Dev: thiếu keystore/password → build bằng debug keystore của Unity.", MessageType.Info);
        }

        void DrawScenes()
        {
            var changed = false;
            for (var i = 0; i < _config.scenes.Count; i++)
            {
                var s = _config.scenes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var en = EditorGUILayout.Toggle(s.enabled, GUILayout.Width(40));
                    if (en != s.enabled)
                    {
                        s.enabled = en;
                        changed = true;
                    }

                    var missing = !File.Exists(s.path);
                    EditorGUILayout.LabelField(missing ? s.path + "  (không tồn tại)" : s.path);
                    if (GUILayout.Button("▲", GUILayout.Width(22)) && i > 0)
                    {
                        (_config.scenes[i - 1], _config.scenes[i]) = (_config.scenes[i], _config.scenes[i - 1]);
                        changed = true;
                    }

                    if (GUILayout.Button("✕", GUILayout.Width(22)))
                    {
                        _config.scenes.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Thêm scene đang mở"))
                {
                    for (var i = 0; i < EditorSceneManager.sceneCount; i++)
                    {
                        var path = EditorSceneManager.GetSceneAt(i).path;
                        if (!string.IsNullOrEmpty(path) && _config.scenes.All(x => x.path != path))
                            _config.scenes.Add(new SceneEntry { path = path });
                    }

                    changed = true;
                }

                if (GUILayout.Button("Import từ Build Settings"))
                {
                    _config.ImportScenesFromBuildSettings();
                    changed = true;
                }
            }

            if (changed)
            {
                _config.ApplyScenesToBuildSettings();
                GUI.changed = true;
            }
        }

        // ---- No Ads Rules ----

        void DrawNoAdsRules()
        {
            EditorGUILayout.HelpBox(
                "Set field của object trong scene theo cờ No Ads (mọi preset, cả Release = 'Value Has Ads'). Value rỗng = không đụng.\n" +
                "Kéo GameObject (Hierarchy) hoặc Component (header Inspector) vào ô bên cạnh 'Rule n' để tự điền. " +
                "Nút ▼ chọn scene / object / component / property (scene chưa mở thì tự mở tạm để đọc).", MessageType.None);

            var rules = _config.noAdsRules;
            for (var i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        r.enabled = EditorGUILayout.ToggleLeft($"Rule {i + 1}", r.enabled, GUILayout.Width(80));
                        var picked = EditorGUILayout.ObjectField(GUIContent.none, null, typeof(Object), true);
                        if (picked != null && FillRuleFromPick(r, picked)) GUI.changed = true;
                        if (GUILayout.Button("✕", GUILayout.Width(22)))
                        {
                            rules.RemoveAt(i);
                            GUI.changed = true;
                            break;
                        }
                    }

                    using (new EditorGUI.DisabledScope(!r.enabled))
                    {
                        DrawRuleScene(r);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            r.objectPath = EditorGUILayout.TextField(new GUIContent("Object path", "Đường dẫn hierarchy từ root, vd Canvas/Shop/BtnRemoveAds"), r.objectPath);
                            if (GUILayout.Button("▼", GUILayout.Width(22))) ShowObjectMenu(r);
                        }
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            r.componentType = EditorGUILayout.TextField(new GUIContent("Component", "Tên type (ngắn hoặc full). Rỗng / GameObject = chính GameObject"), r.componentType);
                            if (GUILayout.Button("▼", GUILayout.Width(22))) ShowComponentMenu(r);
                        }

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            var prop = EditorGUILayout.TextField(new GUIContent("Property",
                                "SerializedProperty path, vd _hideRemoveAds, m_IsActive" +
                                (string.IsNullOrEmpty(r.valueType) ? "" : $"\nKiểu: {r.valueType}")), r.property);
                            if (prop != r.property)
                            {
                                // Gõ tay → không biết kiểu nữa, quay về ô text.
                                r.property = prop;
                                r.valueType = "";
                                r.enumNames.Clear();
                            }
                            if (GUILayout.Button("▼", GUILayout.Width(22))) ShowPropertyMenu(r);
                        }

                        r.noAdsValue = DrawRuleValue(new GUIContent("Value No Ads", "Giá trị ghi khi build Dev bật No Ads"), r, r.noAdsValue);
                        r.adsValue = DrawRuleValue(new GUIContent("Value Has Ads", "Giá trị ghi khi build Dev không No Ads + Release"), r, r.adsValue);
                        if (string.IsNullOrEmpty(r.valueType) && !string.IsNullOrEmpty(r.property))
                            EditorGUILayout.LabelField(" ", "Chọn Property bằng ▼ để có ô nhập đúng kiểu (checkbox / dropdown / số).", EditorStyles.miniLabel);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Thêm rule"))
                {
                    rules.Add(new NoAdsRule());
                    GUI.changed = true;
                }

                using (new EditorGUI.DisabledScope(rules.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Kiểm tra (No Ads)", "Chạy thử, không sửa scene")))
                        EditorApplication.delayCall += () => RunNoAdsRules(true, false);
                    if (GUILayout.Button(new GUIContent("Kiểm tra (Has Ads)", "Chạy thử, không sửa scene")))
                        EditorApplication.delayCall += () => RunNoAdsRules(false, false);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(rules.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("Áp dụng + Save (No Ads)", "Set Value No Ads + save scene, không build")))
                        EditorApplication.delayCall += () => RunNoAdsRules(true, true);
                    if (GUILayout.Button(new GUIContent("Áp dụng + Save (Has Ads)", "Set Value Has Ads + save scene, không build")))
                        EditorApplication.delayCall += () => RunNoAdsRules(false, true);
                }
            }

            if (_ruleCheck != null)
                foreach (var (type, text) in _ruleCheck)
                    EditorGUILayout.HelpBox(text, type);
        }

        /// <summary>Chọn scene trong project (kéo thả / picker / ▼); trống = mọi scene build list.</summary>
        void DrawRuleScene(NoAdsRule r)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var current = string.IsNullOrEmpty(r.scenePath) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(r.scenePath);
                var label = new GUIContent("Scene", "Trống = mọi scene trong build list");
                var picked = (SceneAsset)EditorGUILayout.ObjectField(label, current, typeof(SceneAsset), false);
                if (picked != current)
                {
                    r.scenePath = picked != null ? AssetDatabase.GetAssetPath(picked) : "";
                    GUI.changed = true;
                }

                if (GUILayout.Button("▼", GUILayout.Width(22))) ShowSceneMenu(r);
            }

            if (string.IsNullOrEmpty(r.scenePath))
                EditorGUILayout.LabelField(" ", "(mọi scene trong build list)", EditorStyles.miniLabel);
            else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(r.scenePath) == null)
                EditorGUILayout.HelpBox($"Scene không tồn tại: {r.scenePath}", MessageType.Warning);
            else if (!_config.EnabledScenePaths.Contains(r.scenePath))
                EditorGUILayout.HelpBox("Scene không nằm trong build list → rule bị bỏ qua khi build.", MessageType.Warning);
        }

        void ShowSceneMenu(NoAdsRule r)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Mọi scene build list"), string.IsNullOrEmpty(r.scenePath),
                () => SetRule(() => r.scenePath = ""));
            menu.AddSeparator("");
            var build = _config.EnabledScenePaths;
            var all = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(x => build.Contains(x) ? 0 : 1).ThenBy(x => x);
            foreach (var path in all)
            {
                var group = build.Contains(path) ? "Build list" : "Khác";
                // GenericMenu tách submenu theo '/', thay bằng ký tự giống để giữ nguyên đường dẫn.
                menu.AddItem(new GUIContent($"{group}/{path.Replace('/', '\u2215')}"), r.scenePath == path,
                    () => SetRule(() => r.scenePath = path));
            }

            menu.ShowAsContext();
        }

        /// <summary>
        /// Chạy action trên scene của rule (trống = các scene đang mở). Scene chưa mở thì mở Additive để đọc rồi đóng lại.
        /// Trả false nếu scene không tồn tại.
        /// </summary>
        bool WithRuleScenes(NoAdsRule r, System.Action<List<UnityEngine.SceneManagement.Scene>> action)
        {
            var scenes = new List<UnityEngine.SceneManagement.Scene>();
            UnityEngine.SceneManagement.Scene? opened = null;
            if (string.IsNullOrEmpty(r.scenePath))
            {
                for (var i = 0; i < EditorSceneManager.sceneCount; i++)
                    if (EditorSceneManager.GetSceneAt(i).isLoaded) scenes.Add(EditorSceneManager.GetSceneAt(i));
            }
            else
            {
                var scene = EditorSceneManager.GetSceneByPath(r.scenePath);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    if (AssetDatabase.LoadAssetAtPath<SceneAsset>(r.scenePath) == null)
                    {
                        ShowNotification(new GUIContent("Scene không tồn tại"));
                        return false;
                    }

                    scene = EditorSceneManager.OpenScene(r.scenePath, OpenSceneMode.Additive);
                    opened = scene;
                }

                scenes.Add(scene);
            }

            try
            {
                action(scenes);
            }
            finally
            {
                if (opened.HasValue) EditorSceneManager.CloseScene(opened.Value, true);
            }

            return true;
        }

        void ShowObjectMenu(NoAdsRule r)
        {
            var menu = new GenericMenu();
            if (!WithRuleScenes(r, scenes =>
                {
                    foreach (var scene in scenes)
                    {
                        // Nhiều scene → thêm cấp tên scene; 1 scene thì hiện thẳng hierarchy.
                        var prefix = scenes.Count > 1 ? scene.name + "/" : "";
                        var scenePath = scene.path;
                        var multi = scenes.Count > 1;
                        foreach (var t in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)))
                        {
                            var path = NoAdsRuleApplier.HierarchyPath(t);
                            // Node có con là submenu → thêm mục "● tên" ở đầu submenu để chọn chính nó.
                            var text = t.childCount > 0 ? $"{prefix}{path}/● {t.name}" : prefix + path;
                            menu.AddItem(new GUIContent(text), r.objectPath == path && NoAdsRuleApplier.AppliesToScene(r, scenePath),
                                () => SetRule(() =>
                                {
                                    r.objectPath = path;
                                    if (string.IsNullOrEmpty(r.scenePath) && multi) r.scenePath = scenePath;
                                }));
                        }
                    }
                })) return;

            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("Không có object (mở scene hoặc chọn Scene trước)"));
            menu.ShowAsContext();
        }

        bool FillRuleFromPick(NoAdsRule r, Object picked)
        {
            var comp = picked as Component;
            var go = comp != null ? comp.gameObject : picked as GameObject;
            if (go == null || !go.scene.IsValid() || string.IsNullOrEmpty(go.scene.path))
            {
                ShowNotification(new GUIContent("Chỉ nhận GameObject/Component trong scene đã save"));
                return false;
            }

            r.scenePath = go.scene.path;
            r.objectPath = NoAdsRuleApplier.HierarchyPath(go.transform);
            r.componentType = comp != null ? comp.GetType().FullName : NoAdsRule.GameObjectType;
            r.property = "";
            r.valueType = "";
            r.enumNames.Clear();
            return true;
        }

        /// <summary>Chạy action trên GameObject khớp rule (mở tạm scene nếu cần).</summary>
        void WithRuleObject(NoAdsRule r, System.Action<GameObject> action)
        {
            var probe = new NoAdsRule { objectPath = r.objectPath };
            var found = false;
            WithRuleScenes(r, scenes =>
            {
                var go = scenes.Select(sc => NoAdsRuleApplier.FindTargets(sc, probe, out _).FirstOrDefault() as GameObject)
                    .FirstOrDefault(g => g != null);
                if (go == null) return;
                found = true;
                action(go);
            });
            if (!found) ShowNotification(new GUIContent("Không tìm thấy object (chọn Scene + Object path trước)"));
        }

        void ShowComponentMenu(NoAdsRule r)
        {
            var menu = new GenericMenu();
            WithRuleObject(r, go =>
            {
                menu.AddItem(new GUIContent(NoAdsRule.GameObjectType), NoAdsRuleApplier.TargetsGameObject(r),
                    () => SetRule(() => r.componentType = NoAdsRule.GameObjectType));
                foreach (var c in go.GetComponents<Component>().Where(c => c != null))
                {
                    var name = c.GetType().FullName;
                    menu.AddItem(new GUIContent(name), r.componentType == name, () => SetRule(() => r.componentType = name));
                }
            });
            if (menu.GetItemCount() > 0) menu.ShowAsContext();
        }

        void ShowPropertyMenu(NoAdsRule r)
        {
            var menu = new GenericMenu();
            var hasObject = false;
            WithRuleObject(r, go =>
            {
                hasObject = true;
                Object target = go;
                if (!NoAdsRuleApplier.TargetsGameObject(r))
                {
                    target = go.GetComponents<Component>().FirstOrDefault(c =>
                        c != null && (c.GetType().FullName == r.componentType || c.GetType().Name == r.componentType));
                    if (target == null)
                    {
                        menu.AddDisabledItem(new GUIContent($"Object không có component {r.componentType}"));
                        return;
                    }
                }

                var props = NoAdsRuleApplier.ListProperties(target);
                if (props.Count == 0) menu.AddDisabledItem(new GUIContent("Không có field bool/int/float/string/enum"));
                foreach (var pi in props)
                    menu.AddItem(new GUIContent($"{pi.path}  —  {pi.label}"), r.property == pi.path, () => SetRule(() =>
                    {
                        var typeChanged = r.valueType != pi.type.ToString();
                        r.property = pi.path;
                        r.valueType = pi.type.ToString();
                        r.enumNames = pi.enumNames.ToList();
                        if (typeChanged) r.noAdsValue = r.adsValue = ""; // giá trị cũ có thể sai kiểu
                    }));
            });
            if (hasObject) menu.ShowAsContext();
        }

        static readonly string[] BoolOptions = { "(không đụng)", "true", "false" };
        const string NoTouch = "(không đụng)";

        /// <summary>Ô nhập theo kiểu property; "" = không đụng.</summary>
        static string DrawRuleValue(GUIContent label, NoAdsRule r, string value)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (r.valueType)
            {
                case nameof(SerializedPropertyType.Boolean):
                {
                    var idx = string.IsNullOrEmpty(value) || !NoAdsRuleApplier.TryParseBool(value, out var b) ? 0 : b ? 1 : 2;
                    idx = EditorGUILayout.Popup(label, idx, BoolOptions);
                    return idx == 0 ? "" : BoolOptions[idx];
                }
                case nameof(SerializedPropertyType.Enum):
                {
                    var names = r.enumNames.ToArray();
                    var options = new[] { NoTouch }.Concat(names).ToArray();
                    var idx = string.IsNullOrEmpty(value) ? 0 : NoAdsRuleApplier.EnumIndex(names, null, value) + 1;
                    idx = EditorGUILayout.Popup(label, idx, options);
                    return idx <= 0 ? "" : names[idx - 1];
                }
                case nameof(SerializedPropertyType.Integer):
                case nameof(SerializedPropertyType.Float):
                {
                    var isInt = r.valueType == nameof(SerializedPropertyType.Integer);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var set = !string.IsNullOrEmpty(value);
                        EditorGUILayout.PrefixLabel(label);
                        var indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        set = EditorGUILayout.ToggleLeft("Đổi", set, GUILayout.Width(50));
                        string result = "";
                        using (new EditorGUI.DisabledScope(!set))
                        {
                            if (isInt)
                            {
                                long.TryParse(value, System.Globalization.NumberStyles.Integer, inv, out var l);
                                l = EditorGUILayout.LongField(l);
                                if (set) result = l.ToString(inv);
                            }
                            else
                            {
                                double.TryParse(value, System.Globalization.NumberStyles.Float, inv, out var d);
                                d = EditorGUILayout.DoubleField(d);
                                if (set) result = d.ToString(inv);
                            }
                        }

                        EditorGUI.indentLevel = indent;
                        return result;
                    }
                }
                default:
                    return EditorGUILayout.TextField(new GUIContent(label.text,
                        label.tooltip + ". bool: true/false · số (dấu chấm) · chuỗi · tên enum. Rỗng = không đụng"), value);
            }
        }

        void SetRule(System.Action change)
        {
            change();
            SaveConfig();
            Repaint();
        }

        /// <summary>apply = false: dry run; true: set + save scene thật (giống bước build) nhưng không build.</summary>
        void RunNoAdsRules(bool noAds, bool apply)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var list = new List<(MessageType, string)>();
            try
            {
                var res = NoAdsRuleApplier.Apply(_config.noAdsRules, noAds, _config.EnabledScenePaths, !apply);
                list.AddRange(res.errors.Select(e => (MessageType.Error, e)));
                list.AddRange(res.warnings.Select(w => (MessageType.Warning, w)));
                list.AddRange(res.infos.Select(i => (MessageType.Info, i)));
                var state = noAds ? "No Ads" : "Has Ads";
                if (!NoAdsRuleApplier.HasActiveRules(_config.noAdsRules, noAds))
                    list.Add((MessageType.Info, $"Không có rule nào có Value {state}."));
                else if (res.errors.Count == 0)
                    list.Insert(0, (MessageType.Info, !apply
                        ? $"OK ({state}) — không sửa gì, chỉ kiểm tra."
                        : res.infos.Count == 0
                            ? $"Mọi field đã đúng Value {state}, không cần sửa."
                            : $"Đã áp Value {state} + save scene (chưa build). Mở scene để kiểm tra."));
            }
            catch (System.Exception e)
            {
                list.Add((MessageType.Error, e.Message));
            }
            finally
            {
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            _ruleCheck = list;
            Repaint();
        }

        void DrawOutput(BuildPreset p)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _config.outputFolder = EditorGUILayout.TextField("Output Folder", _config.outputFolder);
                if (GUILayout.Button("…", GUILayout.Width(28)))
                {
                    var f = EditorUtility.OpenFolderPanel("Output folder", "", "");
                    if (!string.IsNullOrEmpty(f))
                    {
                        _config.outputFolder = ToProjectRelative(f);
                        GUI.changed = true;
                    }
                }

                if (GUILayout.Button("Mở", GUILayout.Width(40))) RevealFolder(_config.outputFolder);
            }

            p.fileNameTemplate = EditorGUILayout.TextField("Tên file", p.fileNameTemplate);
            EditorGUILayout.LabelField(" ", "Token: {product} {version} {code} {date} {time} {mode} {noads}",
                EditorStyles.miniLabel);

            var fx = BuildRules.Resolve(p);
            var code = BuildRules.NextVersionCode(
                BuildRules.CurrentVersionCode(_config.versionCode, PlayerSettings.Android.bundleVersionCode), p.autoIncrement);
            var preview = BuildRules.FormatFileName(p.fileNameTemplate, PlayerSettings.productName, _config.version,
                code, System.DateTime.Now, p.mode, fx.noAds) + "." + BuildRules.Extension(p.mode);
            EditorGUILayout.LabelField("Preview", preview, EditorStyles.miniLabel);
        }

        void DrawDefines()
        {
            var raw = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
            var symbols = (raw ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (symbols.Length == 0)
            {
                EditorGUILayout.LabelField("(không có symbol nào)", EditorStyles.miniLabel);
                return;
            }

            foreach (var s in symbols) EditorGUILayout.SelectableLabel(s, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        void DrawMediation()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                {
                    _mediation = MediationReview.Scan();
                    RefreshValidation();
                }

                using (new EditorGUI.DisabledScope(!MediationReview.IsResolverAvailable))
                {
                    if (GUILayout.Button("Force Resolve", GUILayout.Width(110)))
                    {
                        if (!MediationReview.ForceResolve(() =>
                            {
                                AssetDatabase.Refresh();
                                _mediation = MediationReview.Scan();
                                RefreshValidation();
                                Repaint();
                            }))
                            Debug.LogWarning("[RaccoonBuild] Không gọi được Force Resolve của EDM4U.");
                    }
                }

                GUILayout.FlexibleSpace();
                if (_mediation?.gradleSource != null)
                    GUILayout.Label("Nguồn: " + _mediation.gradleSource, EditorStyles.miniLabel);
            }

            if (_mediation == null || !_mediation.adMobInstalled)
            {
                EditorGUILayout.LabelField("Không phát hiện Google Mobile Ads.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField("GoogleMobileAds", _mediation.adMobVersion ?? "(Assets)");
            foreach (var c in _mediation.core)
                EditorGUILayout.LabelField(c.name, c.version ?? "— (không có trong gradle)");

            foreach (var w in _mediation.warnings) EditorGUILayout.HelpBox(w, MessageType.Warning);

            if (_mediation.rows.Count == 0)
            {
                EditorGUILayout.LabelField("Chưa có network mediation nào.", EditorStyles.miniLabel);
                return;
            }

            var bold = EditorStyles.boldLabel;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Network", bold, GUILayout.Width(110));
                GUILayout.Label("Package", bold, GUILayout.Width(70));
                GUILayout.Label("Adapter (xml)", bold, GUILayout.Width(150));
                GUILayout.Label("Gradle", bold, GUILayout.Width(80));
                GUILayout.Label("Nguồn", bold, GUILayout.Width(55));
                GUILayout.Label("Trạng thái", bold);
            }

            foreach (var r in _mediation.rows)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(r.network, GUILayout.Width(110));
                    GUILayout.Label(r.packageVersion ?? "-", GUILayout.Width(70));
                    GUILayout.Label(r.artifact != null ? $"{r.artifact}:{r.xmlVersion ?? "-"}" : "-", GUILayout.Width(150));
                    GUILayout.Label(r.gradleVersion ?? "-", GUILayout.Width(80));
                    GUILayout.Label(r.source, GUILayout.Width(55));
                    GUILayout.Label(MediationReview.StatusLabel(r.status));
                }
            }
        }

        void DrawValidation()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Kiểm tra", EditorStyles.boldLabel);
                if (GUILayout.Button("Validate", GUILayout.Width(80)))
                {
                    _mediation = MediationReview.Scan();
                    RefreshValidation();
                }
            }

            if (_validation.Count == 0)
            {
                EditorGUILayout.HelpBox("OK — sẵn sàng build.", MessageType.Info);
                return;
            }

            foreach (var m in _validation)
            {
                var type = m.severity == Severity.Error ? MessageType.Error
                    : m.severity == Severity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(m.text, type);
            }
        }

        void DrawBuildButtons(BuildPreset p)
        {
            var hasErrors = BuildValidator.HasErrors(_validation);
            var dev = p.mode == BuildMode.Dev;
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(hasErrors))
                {
                    if (GUILayout.Button($"Build {(dev ? "APK" : "AAB")}", GUILayout.Height(32)))
                        AndroidBuilder.RequestBuild(_config, false, null);

                    using (new EditorGUI.DisabledScope(!dev || string.IsNullOrEmpty(_selectedSerial)))
                    {
                        if (GUILayout.Button("Build & Install", GUILayout.Height(32)))
                            AndroidBuilder.RequestBuild(_config, true, _selectedSerial);
                    }
                }
            }

            if (dev && string.IsNullOrEmpty(_selectedSerial))
                EditorGUILayout.LabelField("Build & Install cần chọn device ở tab Devices.", EditorStyles.miniLabel);
        }

        void DrawLastOutcome()
        {
            var o = AndroidBuilder.LastOutcome;
            if (o == null) return;

            var type = o.success ? MessageType.Info : o.cancelled ? MessageType.Warning : MessageType.Error;
            var text = $"{o.finishedAt:HH:mm:ss} — {o.status}";
            if (o.success)
                text += $"\n{Path.GetFileName(o.path)} · {o.sizeBytes / 1048576f:0.0} MB · {o.seconds:0}s · code {o.versionCode}";
            foreach (var e in o.errors.Take(5)) text += "\n• " + e;
            if (o.errors.Count > 5) text += $"\n… (+{o.errors.Count - 5} lỗi, xem Console)";
            foreach (var w in o.warnings.Take(5)) text += "\n⚠ " + w;
            EditorGUILayout.HelpBox(text, type);

            if (o.success && GUILayout.Button("Mở folder build")) EditorUtility.RevealInFinder(o.path);
        }

        void DrawDevices()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_loadingDevices))
                    if (GUILayout.Button(_loadingDevices ? "Đang tải…" : "Refresh", GUILayout.Width(90)))
                        RefreshDevices();
                GUILayout.FlexibleSpace();
            }

            if (!string.IsNullOrEmpty(_deviceError)) EditorGUILayout.HelpBox(_deviceError, MessageType.Warning);
            if (_devices.Count == 0)
            {
                EditorGUILayout.LabelField("Không có device nào.", EditorStyles.miniLabel);
                return;
            }

            foreach (var d in _devices)
            {
                using (new EditorGUI.DisabledScope(!d.IsReady))
                {
                    var selected = EditorGUILayout.ToggleLeft(d.Label, d.serial == _selectedSerial);
                    if (selected && d.serial != _selectedSerial) _selectedSerial = d.serial;
                }
            }

            if (!string.IsNullOrEmpty(_adbStatus)) EditorGUILayout.LabelField(_adbStatus, EditorStyles.miniLabel);
        }

        void DrawHistory(bool aab)
        {
            var kind = aab ? "AAB" : "APK";
            var entries = _history.entries.Where(e => e.isAab == aab).ToList();
            if (entries.Count == 0)
            {
                EditorGUILayout.LabelField($"Chưa có build {kind} nào.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField($"Giữ tối đa {(aab ? BuildHistory.MaxAab : BuildHistory.MaxApk)} {kind} (file cũ hơn bị xóa).",
                EditorStyles.miniLabel);

            foreach (var e in entries)
            {
                var exists = File.Exists(e.path);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var flags = new List<string>();
                    if (e.developmentBuild) flags.Add("dev build");
                    if (e.noAds) flags.Add("no ads");
                    if (e.useTestAd) flags.Add("test ad");
                    EditorGUILayout.LabelField(
                        $"{e.timestamp} · {e.presetName} · {e.version} ({e.versionCode})",
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        $"{e.sizeBytes / 1048576f:0.0} MB · {e.durationSeconds:0}s" +
                        (flags.Count > 0 ? " · " + string.Join(", ", flags) : "") +
                        (exists ? "" : " · (file đã mất)"), EditorStyles.miniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!exists))
                            if (GUILayout.Button("Mở folder", GUILayout.Width(80)))
                                EditorUtility.RevealInFinder(e.path);

                        using (new EditorGUI.DisabledScope(e.isAab || !exists || string.IsNullOrEmpty(_selectedSerial)))
                        {
                            if (GUILayout.Button("Install lại", GUILayout.Width(80)))
                            {
                                _adbStatus = "Đang install " + Path.GetFileName(e.path) + "…";
                                AdbService.InstallAndLaunch(_selectedSerial, e.path, e.appId, (ok, msg) =>
                                {
                                    _adbStatus = ok ? "Đã cài + launch." : "Lỗi: " + msg;
                                    Repaint();
                                });
                            }
                        }

                        if (GUILayout.Button("Xóa", GUILayout.Width(50)) &&
                            EditorUtility.DisplayDialog("Xóa build", $"Xóa entry và file?\n{e.path}", "Xóa", "Hủy"))
                        {
                            _history.Remove(e);
                            _history.Save();
                        }
                    }
                }
            }
        }

        static string ToProjectRelative(string absolute)
        {
            var root = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            var abs = absolute.Replace('\\', '/');
            return abs.StartsWith(root) ? abs.Substring(root.Length) : abs;
        }

        static void RevealFolder(string folder)
        {
            var full = Path.GetFullPath(string.IsNullOrEmpty(folder) ? "." : folder);
            Directory.CreateDirectory(full);
            EditorUtility.RevealInFinder(full);
        }
    }
}
