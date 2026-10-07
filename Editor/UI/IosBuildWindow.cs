using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using static Raccoon.BuildEditor.BuildWindowUtil;

namespace Raccoon.BuildEditor
{
    /// <summary>
    /// Build iOS → Xcode project. Version, scene list, No Ads Rules dùng chung với Android Build (RaccoonBuildConfig.json);
    /// phần riêng iOS lưu ở RaccoonIosBuildConfig.json.
    /// </summary>
    public class IosBuildWindow : EditorWindow
    {
        const float IconPreviewSize = 64f;

        BuildConfig _shared;
        IosBuildConfig _config;
        IosBuildHistory _history;
        List<ValidationMessage> _validation = new List<ValidationMessage>();

        Vector2 _scroll;
        bool _foldApp = true, _foldBuild = true, _foldSigning, _foldFacebook, _foldIcons, _foldScenes, _foldOutput, _foldDefines;

        [MenuItem("Raccoon/Build editor/iOS Build")]
        public static void Open()
        {
            var window = GetWindow<IosBuildWindow>();
            window.titleContent = new GUIContent("iOS Build");
            window.minSize = new Vector2(460, 520);
            window.Show();
        }

        void OnEnable()
        {
            Reload();
            IosBuilder.BuildFinished -= OnBuildFinished;
            IosBuilder.BuildFinished += OnBuildFinished;
        }

        void OnDisable()
        {
            IosBuilder.BuildFinished -= OnBuildFinished;
        }

        // Android Build ghi chung RaccoonBuildConfig.json → đọc lại khi quay về window.
        void OnFocus()
        {
            if (!IosBuilder.IsBuilding) Reload();
        }

        void Reload()
        {
            _shared = BuildConfig.Load();
            _config = IosBuildConfig.Load();
            _history = IosBuildHistory.Load();
            RefreshValidation();
        }

        void OnBuildFinished(BuildOutcome o)
        {
            Reload();
            Repaint();
        }

        void RefreshValidation()
        {
            if (_config == null || _shared == null) return;
            _validation = BuildValidator.ValidateIosCurrent(_shared, _config, _config.ActivePreset);
        }

        void SaveConfig()
        {
            _config.Save();
            _shared.Save();
            RefreshValidation();
        }

        // ================= GUI =================

        void OnGUI()
        {
            if (_config == null || _shared == null) Reload();

            using (new EditorGUI.DisabledScope(IosBuilder.IsBuilding || EditorApplication.isCompiling))
            {
                DrawPresetBar();
                var preset = _config.ActivePreset;

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                EditorGUI.BeginChangeCheck();

                _foldApp = Section(_foldApp, "App", () => DrawApp(preset));
                _foldBuild = Section(_foldBuild, "Build", () => DrawBuild(preset));
                _foldSigning = Section(_foldSigning, "Signing", () => DrawSigning(preset));
                _foldFacebook = Section(_foldFacebook, "Facebook SDK → Unity-iPhone", DrawFacebook);
                _foldIcons = Section(_foldIcons, $"Icon A/B test ({(_config.alternateIconsEnabled ? _config.alternateIcons.Count(i => i.enabled) + " icon" : "tắt")})", DrawIcons);
                _foldOutput = Section(_foldOutput, "Output", () => DrawOutput(preset));

                if (EditorGUI.EndChangeCheck())
                {
                    BuildRules.EnforceModeLocks(preset);
                    SaveConfig();
                }

                _foldScenes = Section(_foldScenes, "Scenes (dùng chung Android)", DrawScenes);
                _foldDefines = Section(_foldDefines, "Define Symbols iOS (read-only)", DrawDefines);

                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Kiểm tra", EditorStyles.boldLabel);
                    if (GUILayout.Button("Validate", GUILayout.Width(80))) Reload();
                }

                DrawValidationList(_validation);

                using (new EditorGUI.DisabledScope(BuildValidator.HasErrors(_validation)))
                {
                    if (GUILayout.Button("Build Xcode project", GUILayout.Height(32)))
                        IosBuilder.RequestBuild(_shared, _config);
                }

                DrawLastOutcome();

                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"History ({_history.entries.Count})", EditorStyles.boldLabel);
                DrawHistory();

                EditorGUILayout.EndScrollView();
            }
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
                    var bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
                    _config.presets.Add(new IosBuildPreset { name = "Preset " + (_config.presets.Count + 1), appId = bundleId });
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

        void DrawApp(IosBuildPreset p)
        {
            p.name = EditorGUILayout.TextField(new GUIContent("Tên preset", "Cũng là tên folder Xcode trong Output"), p.name);

            var name = EditorGUILayout.DelayedTextField(new GUIContent("App Name", "PlayerSettings.productName (dùng chung Android)"),
                PlayerSettings.productName);
            if (name != PlayerSettings.productName && !string.IsNullOrWhiteSpace(name))
            {
                PlayerSettings.productName = name.Trim();
                AssetDatabase.SaveAssets();
            }

            p.appId = EditorGUILayout.TextField("Bundle ID", p.appId);
            _shared.version = EditorGUILayout.TextField(new GUIContent("Version", "CFBundleShortVersionString — dùng chung với Android"), _shared.version);

            using (new EditorGUILayout.HorizontalScope())
            {
                var shown = BuildRules.CurrentVersionCode(_config.buildNumber, IosBuildConfig.PlayerSettingsBuildNumber);
                var code = EditorGUILayout.IntField(new GUIContent("Build Number", "CFBundleVersion"), shown);
                if (GUILayout.Button("−", GUILayout.Width(24))) code--;
                if (GUILayout.Button("+", GUILayout.Width(24))) code++;
                if (code != shown && code > 0)
                {
                    _config.buildNumber = code;
                    PlayerSettings.iOS.buildNumber = code.ToString();
                    GUI.changed = true;
                }
            }

            p.autoIncrement = EditorGUILayout.Toggle(new GUIContent("Auto-increment", "Tăng build number +1 trước mỗi build"), p.autoIncrement);

            var minOs = EditorGUILayout.DelayedTextField(new GUIContent("Target iOS (min)", "PlayerSettings.iOS.targetOSVersionString"),
                PlayerSettings.iOS.targetOSVersionString);
            if (minOs != PlayerSettings.iOS.targetOSVersionString && !string.IsNullOrWhiteSpace(minOs))
            {
                PlayerSettings.iOS.targetOSVersionString = minOs.Trim();
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.LabelField(" ", "Icon chính: Default Icon (set ở Android Build).", EditorStyles.miniLabel);
        }

        void DrawBuild(IosBuildPreset p)
        {
            p.mode = (BuildMode)EditorGUILayout.EnumPopup("Mode", p.mode);
            var release = p.mode == BuildMode.Release;
            EditorGUILayout.LabelField(" ", $"Xcode project · ARM64 · IL2CPP · LZ4HC · Xcode config {(release || !p.developmentBuild ? "Release" : "Debug")}",
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

                var adPack = AdsSettingApplier.IsAdPackInstalled;
                using (new EditorGUILayout.HorizontalScope())
                {
                    p.noAds = EditorGUILayout.ToggleLeft("No Ads (creative mode)", p.noAds && !release, GUILayout.Width(200));
                    using (new EditorGUI.DisabledScope(p.noAds || !adPack))
                        p.useTestAd = EditorGUILayout.ToggleLeft("Use Test Ad", p.useTestAd && !release);
                }
            }

            var rules = _shared.noAdsRules.Count(r => r.enabled);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(" ",
                    $"No Ads Rules dùng chung Android: {rules} rule{(AdsSettingApplier.IsAdPackInstalled ? " + built-in" : "")} → áp {(p.noAds && !release ? "Value No Ads" : "Value Has Ads")}",
                    EditorStyles.miniLabel);
                if (GUILayout.Button("Sửa rule", GUILayout.Width(70))) AndroidBuildWindow.Open();
            }

            p.cleanCache = EditorGUILayout.Toggle(new GUIContent("Clean Build (Replace)",
                "Tắt: Append vào folder Xcode cũ (nhanh, giữ chỉnh sửa tay). Bật: xóa build cache + Replace toàn bộ"), p.cleanCache);
        }

        void DrawSigning(IosBuildPreset p)
        {
            _config.automaticSigning = EditorGUILayout.Toggle("Automatic Signing", _config.automaticSigning);
            _config.teamId = EditorGUILayout.TextField(new GUIContent("Team ID", "Apple Developer → Membership → Team ID (10 ký tự)"),
                _config.teamId).Trim();

            if (!_config.automaticSigning)
            {
                var type = p.mode == BuildMode.Release ? "Distribution" : "Development";
                p.provisioningProfile = EditorGUILayout.TextField(new GUIContent($"Profile UUID ({type})",
                    "UUID provisioning profile cho preset này; Dev = Development, Release = Distribution"), p.provisioningProfile).Trim();
            }

            EditorGUILayout.HelpBox("Ghi vào Xcode project; ký app / archive vẫn làm trong Xcode.", MessageType.None);
        }

        void DrawFacebook()
        {
            var fb = IosTools.IsFacebookInstalled;
            EditorGUILayout.LabelField("Facebook SDK", fb ? "Có trong project" : "Không phát hiện (Facebook.Unity.FB)", EditorStyles.miniLabel);

            _config.podsToMainTarget = EditorGUILayout.ToggleLeft(
                new GUIContent("Thêm pod FB vào target Unity-iPhone", "Sửa Podfile trước khi EDM4U chạy pod install"),
                _config.podsToMainTarget);
            using (new EditorGUI.DisabledScope(!_config.podsToMainTarget))
            {
                var text = string.Join(", ", _config.mainTargetPodPrefixes);
                var edited = EditorGUILayout.DelayedTextField(new GUIContent("Prefix pod", "Phân cách bằng dấu phẩy"), text);
                if (edited != text)
                {
                    _config.mainTargetPodPrefixes = edited.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    GUI.changed = true;
                }

                if (GUILayout.Button("Mặc định (FBSDK, FBAEMKit)", GUILayout.Width(200)))
                {
                    _config.mainTargetPodPrefixes = IosBuildConfig.DefaultMainTargetPodPrefixes.ToList();
                    GUI.changed = true;
                }
            }

            EditorGUILayout.HelpBox(
                "Thay cho thao tác tay: Xcode → target Unity-iPhone → Frameworks → add FBSDKCoreKit, FBAEMKit…\n" +
                "Tool copy các dòng pod khớp prefix (đang nằm ở target UnityFramework) sang block target 'Unity-iPhone' của Podfile, " +
                "rồi EDM4U pod install như bình thường. Mở .xcworkspace để build.", MessageType.None);
        }

        void DrawIcons()
        {
            _config.alternateIconsEnabled = EditorGUILayout.ToggleLeft("Bật icon A/B test", _config.alternateIconsEnabled);
            EditorGUILayout.HelpBox(
                "Mỗi icon → app icon set <Tên> trong Unity-iPhone/Images.xcassets (PNG 1024x1024, không alpha) + set " +
                "ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES và INCLUDE_ALL_APPICON_ASSETS = YES ở target Unity-iPhone.\n" +
                "Dùng cho App Store Connect → Product Page Optimization (chọn icon test theo Tên), hoặc đổi icon runtime bằng setAlternateIconName(\"<Tên>\").",
                MessageType.None);

            using (new EditorGUI.DisabledScope(!_config.alternateIconsEnabled))
            {
                var icons = _config.alternateIcons;
                for (var i = 0; i < icons.Count; i++)
                {
                    var icon = icons[i];
                    using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                    {
                        var current = string.IsNullOrEmpty(icon.texturePath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(icon.texturePath);
                        var rect = GUILayoutUtility.GetRect(IconPreviewSize, IconPreviewSize,
                            GUILayout.Width(IconPreviewSize), GUILayout.Height(IconPreviewSize));
                        var indent = EditorGUI.indentLevel;
                        EditorGUI.indentLevel = 0;
                        var tex = (Texture2D)EditorGUI.ObjectField(rect, current, typeof(Texture2D), false);
                        EditorGUI.indentLevel = indent;
                        if (tex != current)
                        {
                            icon.texturePath = tex != null ? AssetDatabase.GetAssetPath(tex) : "";
                            if (tex != null && string.IsNullOrEmpty(icon.name)) icon.name = "AppIcon-" + (char)('B' + i);
                            GUI.changed = true;
                        }

                        using (new EditorGUILayout.VerticalScope())
                        {
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                icon.enabled = EditorGUILayout.ToggleLeft("Bật", icon.enabled, GUILayout.Width(50));
                                GUILayout.FlexibleSpace();
                                if (GUILayout.Button("✕", GUILayout.Width(22)))
                                {
                                    icons.RemoveAt(i);
                                    GUI.changed = true;
                                    break;
                                }
                            }

                            icon.name = EditorGUILayout.TextField("Tên", icon.name).Trim();
                            if (!string.IsNullOrEmpty(icon.name) && !IosXcodePostProcess.IsValidIconName(icon.name))
                                EditorGUILayout.LabelField(" ", "Chỉ chữ/số/-/_, khác 'AppIcon'", EditorStyles.miniLabel);
                            EditorGUILayout.LabelField(" ", string.IsNullOrEmpty(icon.texturePath) ? "(kéo PNG 1024 vào ô bên trái)" : icon.texturePath,
                                EditorStyles.miniLabel);
                        }
                    }
                }

                if (GUILayout.Button("+ Thêm icon"))
                {
                    icons.Add(new AlternateIcon { name = "AppIcon-" + (char)('B' + icons.Count) });
                    GUI.changed = true;
                }
            }
        }

        void DrawScenes()
        {
            var scenes = _shared.scenes;
            if (scenes.Count == 0) EditorGUILayout.LabelField("(chưa có scene)", EditorStyles.miniLabel);
            foreach (var s in scenes)
            {
                var missing = !File.Exists(s.path);
                EditorGUILayout.LabelField((s.enabled ? "✓ " : "   ") + s.path + (missing ? "  (không tồn tại)" : ""),
                    s.enabled ? EditorStyles.label : EditorStyles.miniLabel);
            }

            if (GUILayout.Button("Sửa ở Android Build", GUILayout.Width(150))) AndroidBuildWindow.Open();
        }

        void DrawOutput(IosBuildPreset p)
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

            var folder = IosBuilder.XcodeFolder(_config, p);
            EditorGUILayout.LabelField("Xcode project", ToProjectRelative(folder), EditorStyles.miniLabel);
            var exists = IosTools.XcodeEntry(folder) != null;
            if (exists)
            {
                var append = !p.cleanCache && BuildPipeline.BuildCanBeAppended(BuildTarget.iOS, folder) == CanAppendBuild.Yes;
                EditorGUILayout.LabelField(" ", append ? "Build tiếp sẽ Append vào project này" : "Build tiếp sẽ Replace project này",
                    EditorStyles.miniLabel);
            }
        }

        static void DrawDefines()
        {
            var raw = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.iOS);
            var symbols = (raw ?? "").Split(';').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (symbols.Length == 0)
            {
                EditorGUILayout.LabelField("(không có symbol nào)", EditorStyles.miniLabel);
                return;
            }

            foreach (var s in symbols) EditorGUILayout.SelectableLabel(s, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        void DrawLastOutcome()
        {
            var o = IosBuilder.LastOutcome;
            if (o == null) return;

            var type = o.success ? MessageType.Info : o.cancelled ? MessageType.Warning : MessageType.Error;
            var text = $"{o.finishedAt:HH:mm:ss} — {o.status}";
            if (o.success) text += $"\n{ToProjectRelative(o.path)} · {o.seconds:0}s · build {o.versionCode}";
            foreach (var e in o.errors.Take(5)) text += "\n• " + e;
            if (o.errors.Count > 5) text += $"\n… (+{o.errors.Count - 5} lỗi, xem Console)";
            foreach (var w in o.warnings.Take(5)) text += "\n⚠ " + w;
            EditorGUILayout.HelpBox(text, type);

            if (!o.success) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Mở Xcode")) IosTools.OpenInXcode(o.path);
                if (GUILayout.Button("Mở folder")) EditorUtility.RevealInFinder(o.path);
            }
        }

        void DrawHistory()
        {
            if (_history.entries.Count == 0)
            {
                EditorGUILayout.LabelField("Chưa có build iOS nào.", EditorStyles.miniLabel);
                return;
            }

            foreach (var e in _history.entries.ToList())
            {
                var exists = Directory.Exists(e.path);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    var flags = new List<string>();
                    if (e.developmentBuild) flags.Add("dev build");
                    if (e.noAds) flags.Add("no ads");
                    if (e.useTestAd) flags.Add("test ad");
                    if (e.appended) flags.Add("append");
                    EditorGUILayout.LabelField($"{e.timestamp} · {e.presetName} · {e.version} ({e.buildNumber})", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"{e.bundleId} · {e.durationSeconds:0}s" +
                                               (flags.Count > 0 ? " · " + string.Join(", ", flags) : "") +
                                               (exists ? "" : " · (folder đã mất)"), EditorStyles.miniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUI.DisabledScope(!exists))
                        {
                            if (GUILayout.Button("Mở Xcode", GUILayout.Width(80))) IosTools.OpenInXcode(e.path);
                            if (GUILayout.Button("Mở folder", GUILayout.Width(80))) EditorUtility.RevealInFinder(e.path);
                        }

                        if (GUILayout.Button("Xóa entry", GUILayout.Width(80)))
                        {
                            _history.entries.Remove(e);
                            _history.Save();
                        }
                    }
                }
            }
        }
    }
}
