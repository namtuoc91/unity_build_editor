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
        static readonly string[] Tabs = { "Devices", "History" };

        BuildConfig _config;
        BuildHistory _history;
        MediationReport _mediation;
        List<ValidationMessage> _validation = new List<ValidationMessage>();
        List<AdbDevice> _devices = new List<AdbDevice>();
        string _deviceError;
        bool _loadingDevices;
        string _selectedSerial;
        string _adbStatus;

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
                _tab = GUILayout.Toolbar(_tab, Tabs);
                if (_tab == 0) DrawDevices();
                else DrawHistory();

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
                var icon = (Texture2D)EditorGUILayout.ObjectField(current, typeof(Texture2D), false,
                    GUILayout.Width(64), GUILayout.Height(64));
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
            EditorGUILayout.LabelField(" ", release ? "AAB · ARMv7 + ARM64 · IL2CPP" : "APK · ARM64 · IL2CPP",
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
            using (new EditorGUI.DisabledScope(release || !adPack))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    p.noAds = EditorGUILayout.ToggleLeft("No Ads (creative mode)", p.noAds && !release, GUILayout.Width(200));
                    using (new EditorGUI.DisabledScope(p.noAds))
                        p.useTestAd = EditorGUILayout.ToggleLeft("Use Test Ad", p.useTestAd && !release);
                }
            }

            if (!adPack)
                EditorGUILayout.HelpBox("Chưa cài com.raccoon.adpack → No Ads / Use Test Ad bị khóa.", MessageType.None);
            else if (release)
                EditorGUILayout.HelpBox("Release: luôn ép _creativeMode = false và use_test_ad = false (sửa + save scene/asset).", MessageType.None);

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

        void DrawHistory()
        {
            if (_history.entries.Count == 0)
            {
                EditorGUILayout.LabelField("Chưa có build nào.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.LabelField($"Giữ tối đa {BuildHistory.MaxApk} APK / {BuildHistory.MaxAab} AAB (file cũ hơn bị xóa).",
                EditorStyles.miniLabel);

            foreach (var e in _history.entries.ToList())
            {
                var exists = File.Exists(e.path);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var flags = new List<string>();
                    if (e.developmentBuild) flags.Add("dev build");
                    if (e.noAds) flags.Add("no ads");
                    if (e.useTestAd) flags.Add("test ad");
                    EditorGUILayout.LabelField(
                        $"{e.timestamp} · {e.presetName} · {(e.isAab ? "AAB" : "APK")} · {e.version} ({e.versionCode})",
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
