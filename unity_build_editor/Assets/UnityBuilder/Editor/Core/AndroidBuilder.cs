using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Raccoon.BuildEditor
{
    public class BuildOutcome
    {
        public bool success;
        public bool cancelled;
        public string status;
        public string path;
        public long sizeBytes;
        public double seconds;
        public int versionCode;
        public bool isAab;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public DateTime finishedAt = DateTime.Now;
    }

    public static class AndroidBuilder
    {
        const string Log = "[RaccoonBuild] ";

        public static bool IsBuilding { get; private set; }
        public static BuildOutcome LastOutcome { get; private set; }
        public static event Action<BuildOutcome> BuildFinished;

        /// <summary>Build qua delayCall để thoát khỏi OnGUI trước khi BuildPlayer chạy.</summary>
        public static void RequestBuild(BuildConfig config, bool installAfter, string deviceSerial)
        {
            if (IsBuilding) return;
            EditorApplication.delayCall += () =>
            {
                var outcome = Build(config, installAfter, deviceSerial);
                LastOutcome = outcome;
                BuildFinished?.Invoke(outcome);
            };
        }

        public static BuildOutcome Build(BuildConfig config, bool installAfter, string deviceSerial)
        {
            var outcome = new BuildOutcome();
            var preset = config.ActivePreset;
            if (preset == null)
            {
                outcome.status = "Chưa có preset";
                outcome.errors.Add("Chưa có preset.");
                return outcome;
            }

            BuildRules.EnforceModeLocks(preset);
            var fx = BuildRules.Resolve(preset);
            outcome.isAab = fx.isAab;

            // 1. Validate
            var messages = BuildValidator.ValidateCurrent(config, preset, MediationReview.Scan());
            foreach (var m in messages.Where(m => m.severity == Severity.Warning)) outcome.warnings.Add(m.text);
            if (BuildValidator.HasErrors(messages))
            {
                outcome.status = "Validate lỗi";
                outcome.errors.AddRange(messages.Where(m => m.severity == Severity.Error).Select(m => m.text));
                foreach (var e in outcome.errors) Debug.LogError(Log + e);
                return outcome;
            }

            var scenes = config.EnabledScenePaths;
            config.ApplyScenesToBuildSettings();

            IsBuilding = true;
            SceneSetup[] sceneSetup = null;
            var versionCodeTouched = false;
            var current = 0;
            try
            {
                // 2. Switch platform
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    return BuildSteps.Fail(outcome, "Switch Platform sang Android thất bại.");
                }

                // 3. Ads (adpack) + No Ads rule của project: sửa thật + SAVE scene/asset, không restore.
                if (!BuildSteps.ApplyAds(config.noAdsRules, fx, scenes, outcome, ref sceneSetup)) return outcome;

                // 4. PlayerSettings (ghi thẳng, không restore)
                ApplyPlayerSettings(config, preset, fx);

                // 5. Version code
                current = BuildRules.CurrentVersionCode(config.versionCode, PlayerSettings.Android.bundleVersionCode);
                var next = BuildRules.NextVersionCode(current, preset.autoIncrement);
                PlayerSettings.Android.bundleVersionCode = next;
                versionCodeTouched = true;
                outcome.versionCode = next;

                // 6. Output path
                var fileName = BuildRules.FormatFileName(preset.fileNameTemplate, PlayerSettings.productName,
                    config.version, next, DateTime.Now, preset.mode, fx.noAds);
                var folder = Path.GetFullPath(string.IsNullOrEmpty(config.outputFolder) ? "Builds/Android" : config.outputFolder);
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, fileName + "." + BuildRules.Extension(preset.mode));

                // 7. Build
                // Luôn nén LZ4HC (cả Dev lẫn Release), không có option trong preset.
                var options = BuildOptions.CompressWithLz4HC;
                if (fx.development) options |= BuildOptions.Development;
                if (fx.scriptDebugging) options |= BuildOptions.AllowDebugging;
                if (fx.autoconnectProfiler) options |= BuildOptions.ConnectWithProfiler;
                if (preset.cleanCache) options |= BuildOptions.CleanBuildCache;

                Debug.Log($"{Log}Build {preset.name} ({preset.mode}, {(fx.isAab ? "AAB" : "APK")}, code {next}) → {path}");
                var sw = Stopwatch.StartNew();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = options
                });
                sw.Stop();

                outcome.seconds = sw.Elapsed.TotalSeconds;
                outcome.path = path;
                var result = report.summary.result;
                outcome.errors.AddRange(report.steps.SelectMany(s => s.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content));

                if (result != BuildResult.Succeeded || !File.Exists(path))
                {
                    outcome.cancelled = result == BuildResult.Cancelled;
                    outcome.status = outcome.cancelled ? "Đã hủy" : "Build lỗi (" + result + ")";
                    return outcome;
                }

                // 8. Success → lưu version code + history
                outcome.success = true;
                outcome.status = "Thành công";
                outcome.sizeBytes = new FileInfo(path).Length;
                versionCodeTouched = false;
                config.versionCode = next;
                config.Save();

                var history = BuildHistory.Load();
                history.Add(new BuildHistoryEntry
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    presetName = preset.name,
                    mode = preset.mode,
                    isAab = fx.isAab,
                    appId = preset.appId,
                    version = config.version,
                    versionCode = next,
                    noAds = fx.noAds,
                    useTestAd = fx.ads.useTestAd ?? false,
                    developmentBuild = fx.development,
                    sizeBytes = outcome.sizeBytes,
                    durationSeconds = outcome.seconds,
                    path = path
                });
                history.Save();

                Debug.Log($"{Log}Build thành công: {path} ({outcome.sizeBytes / 1048576f:0.0} MB, {outcome.seconds:0}s)");

                if (installAfter && !fx.isAab)
                {
                    if (string.IsNullOrEmpty(deviceSerial))
                        outcome.warnings.Add("Build & Install: chưa chọn device → bỏ qua install.");
                    else
                        AdbService.InstallAndLaunch(deviceSerial, path, preset.appId, null);
                }

                return outcome;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                outcome.errors.Add(e.Message);
                return BuildSteps.Fail(outcome, "Exception");
            }
            finally
            {
                // Fail/Cancel → trả version code về current, config giữ nguyên.
                if (versionCodeTouched) PlayerSettings.Android.bundleVersionCode = current;
                if (sceneSetup != null && sceneSetup.Length > 0)
                {
                    try
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(sceneSetup);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(Log + "Không mở lại được scene setup cũ: " + e.Message);
                    }
                }

                IsBuilding = false;
                if (!outcome.success && !outcome.cancelled)
                    foreach (var e in outcome.errors.Take(20)) Debug.LogError(Log + e);
            }
        }

        /// <summary>
        /// PlayerSettings.Android.validateAppBundleSize / appBundleSizeToValidate là internal trong Unity 6
        /// → set qua reflection, fallback SerializedObject của PlayerSettings.
        /// </summary>
        static void SetAppBundleSizeValidation(int sizeMB)
        {
            var enabled = sizeMB > 0;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var type = typeof(PlayerSettings.Android);
            var validate = type.GetProperty("validateAppBundleSize", flags);
            var size = type.GetProperty("appBundleSizeToValidate", flags);
            try
            {
                if (validate != null && size != null)
                {
                    validate.SetValue(null, enabled);
                    if (enabled) size.SetValue(null, sizeMB);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(Log + "Reflection appBundleSize lỗi, fallback SerializedObject: " + e.Message);
            }

            var assets = Resources.FindObjectsOfTypeAll<PlayerSettings>();
            if (assets.Length == 0)
            {
                Debug.LogWarning(Log + "Không set được App Bundle size warning.");
                return;
            }

            var so = new SerializedObject(assets[0]);
            var pValidate = so.FindProperty("AndroidValidateAppBundleSize");
            var pSize = so.FindProperty("AndroidAppBundleSizeToValidate");
            if (pValidate == null || pSize == null)
            {
                Debug.LogWarning(Log + "Không tìm thấy field App Bundle size trong PlayerSettings.");
                return;
            }

            pValidate.boolValue = enabled;
            if (enabled) pSize.intValue = sizeMB;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ApplyPlayerSettings(BuildConfig config, BuildPreset preset, EffectiveSettings fx)
        {
            var target = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(target, preset.appId);
            PlayerSettings.bundleVersion = config.version;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = fx.architectures;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;

            EditorUserBuildSettings.buildAppBundle = fx.isAab;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = fx.development;
            EditorUserBuildSettings.allowDebugging = fx.scriptDebugging;
            EditorUserBuildSettings.connectProfiler = fx.autoconnectProfiler;

            // Popup "Warn about App Bundle size" của Unity: 0 = tắt.
            SetAppBundleSizeValidation(config.appBundleSizeWarningMB);

            // Keystore: Release bắt buộc (đã validate); Dev dùng nếu đủ, không thì debug keystore.
            var ksExists = !string.IsNullOrEmpty(config.keystorePath) && File.Exists(config.keystorePath);
            var usable = BuildValidator.HasUsableKeystore(ksExists, config.keystoreAlias,
                BuildConfig.KeystorePass, BuildConfig.KeyAliasPass);
            if (usable)
            {
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = Path.GetFullPath(config.keystorePath);
                PlayerSettings.Android.keystorePass = BuildConfig.KeystorePass;
                PlayerSettings.Android.keyaliasName = config.keystoreAlias;
                PlayerSettings.Android.keyaliasPass = BuildConfig.KeyAliasPass;
            }
            else
            {
                PlayerSettings.Android.useCustomKeystore = false;
            }
        }
    }
}
