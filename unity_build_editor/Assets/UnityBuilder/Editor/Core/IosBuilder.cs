using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Raccoon.BuildEditor
{
    /// <summary>Build iOS → Xcode project (không archive/IPA). Folder Xcode cố định theo preset để build sau Append được.</summary>
    public static class IosBuilder
    {
        const string Log = "[RaccoonBuild] ";

        public static bool IsBuilding { get; private set; }
        public static BuildOutcome LastOutcome { get; private set; }
        public static event Action<BuildOutcome> BuildFinished;

        public static string XcodeFolder(IosBuildConfig config, BuildPreset preset)
        {
            var root = string.IsNullOrEmpty(config.outputFolder) ? "Builds/iOS" : config.outputFolder;
            return Path.GetFullPath(Path.Combine(root, BuildRules.SanitizeFileName(preset.name)));
        }

        /// <summary>Build qua delayCall để thoát khỏi OnGUI trước khi BuildPlayer chạy.</summary>
        public static void RequestBuild(BuildConfig shared, IosBuildConfig config)
        {
            if (IsBuilding) return;
            EditorApplication.delayCall += () =>
            {
                var outcome = Build(shared, config);
                LastOutcome = outcome;
                BuildFinished?.Invoke(outcome);
            };
        }

        public static BuildOutcome Build(BuildConfig shared, IosBuildConfig config)
        {
            var outcome = new BuildOutcome();
            var preset = config.ActivePreset;
            if (preset == null) return BuildSteps.Fail(outcome, "Chưa có preset");

            BuildRules.EnforceModeLocks(preset);
            var fx = BuildRules.Resolve(preset);

            // 1. Validate
            var messages = BuildValidator.ValidateIosCurrent(shared, config, preset);
            foreach (var m in messages.Where(m => m.severity == Severity.Warning)) outcome.warnings.Add(m.text);
            if (BuildValidator.HasErrors(messages))
            {
                outcome.errors.AddRange(messages.Where(m => m.severity == Severity.Error).Select(m => m.text));
                foreach (var e in outcome.errors) Debug.LogError(Log + e);
                outcome.status = "Validate lỗi";
                return outcome;
            }

            var scenes = shared.EnabledScenePaths;
            shared.ApplyScenesToBuildSettings();

            IsBuilding = true;
            SceneSetup[] sceneSetup = null;
            var buildNumberTouched = false;
            var current = 0;
            try
            {
                // 2. Switch platform
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS &&
                    !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
                    return BuildSteps.Fail(outcome, "Switch Platform sang iOS thất bại.");

                // 3. Ads + No Ads rule (dùng chung với Android)
                if (!BuildSteps.ApplyAds(shared.noAdsRules, fx, scenes, outcome, ref sceneSetup)) return outcome;

                // 4. PlayerSettings
                ApplyPlayerSettings(shared, config, preset, fx);

                // 5. Build number
                current = BuildRules.CurrentVersionCode(config.buildNumber, IosBuildConfig.PlayerSettingsBuildNumber);
                var next = BuildRules.NextVersionCode(current, preset.autoIncrement);
                PlayerSettings.iOS.buildNumber = next.ToString();
                buildNumberTouched = true;
                outcome.versionCode = next;

                // 6. Folder Xcode: Append nếu được (giữ chỉnh sửa tay trong Xcode, nhanh hơn), Clean cache thì Replace.
                var path = XcodeFolder(config, preset);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                var append = !preset.cleanCache && Directory.Exists(path) &&
                             BuildPipeline.BuildCanBeAppended(BuildTarget.iOS, path) == CanAppendBuild.Yes;

                var options = BuildOptions.CompressWithLz4HC;
                if (fx.development) options |= BuildOptions.Development;
                if (fx.scriptDebugging) options |= BuildOptions.AllowDebugging;
                if (fx.autoconnectProfiler) options |= BuildOptions.ConnectWithProfiler;
                if (preset.cleanCache) options |= BuildOptions.CleanBuildCache;
                if (append) options |= BuildOptions.AcceptExternalModificationsToPlayer;

                // 7. Build
                Debug.Log($"{Log}Build iOS {preset.name} ({preset.mode}, build {next}, {(append ? "Append" : "Replace")}) → {path}");
                var sw = Stopwatch.StartNew();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = path,
                    target = BuildTarget.iOS,
                    targetGroup = BuildTargetGroup.iOS,
                    options = options
                });
                sw.Stop();

                outcome.seconds = sw.Elapsed.TotalSeconds;
                outcome.path = path;
                outcome.errors.AddRange(report.steps.SelectMany(s => s.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content));

                var result = report.summary.result;
                if (result != BuildResult.Succeeded || !Directory.Exists(Path.Combine(path, IosTools.ProjectName)))
                {
                    outcome.cancelled = result == BuildResult.Cancelled;
                    outcome.status = outcome.cancelled ? "Đã hủy" : "Build lỗi (" + result + ")";
                    return outcome;
                }

                if (IosTools.PodInstallMissing(path))
                    outcome.warnings.Add("Có Podfile nhưng chưa có Unity-iPhone.xcworkspace → pod install lỗi/chưa chạy. Chạy `pod install` trong folder Xcode.");

                // 8. Success → lưu build number + history
                outcome.success = true;
                outcome.status = "Thành công" + (append ? " (Append)" : "");
                buildNumberTouched = false;
                config.buildNumber = next;
                config.Save();

                var history = IosBuildHistory.Load();
                history.Add(new IosBuildHistoryEntry
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    presetName = preset.name,
                    mode = preset.mode,
                    bundleId = preset.appId,
                    version = shared.version,
                    buildNumber = next,
                    noAds = fx.noAds,
                    useTestAd = fx.ads.useTestAd ?? false,
                    developmentBuild = fx.development,
                    appended = append,
                    durationSeconds = outcome.seconds,
                    path = path
                });
                history.Save();

                Debug.Log($"{Log}Build iOS thành công: {path} ({outcome.seconds:0}s)");
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
                if (buildNumberTouched) PlayerSettings.iOS.buildNumber = current.ToString();
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

        static void ApplyPlayerSettings(BuildConfig shared, IosBuildConfig config, IosBuildPreset preset, EffectiveSettings fx)
        {
            var target = NamedBuildTarget.iOS;
            PlayerSettings.SetApplicationIdentifier(target, preset.appId);
            PlayerSettings.bundleVersion = shared.version;
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.DeviceSDK;

            PlayerSettings.iOS.appleEnableAutomaticSigning = config.automaticSigning;
            PlayerSettings.iOS.appleDeveloperTeamID = config.teamId ?? "";
            if (!config.automaticSigning)
            {
                PlayerSettings.iOS.iOSManualProvisioningProfileID = preset.provisioningProfile ?? "";
                PlayerSettings.iOS.iOSManualProvisioningProfileType = preset.mode == BuildMode.Release
                    ? ProvisioningProfileType.Distribution
                    : ProvisioningProfileType.Development;
            }

            EditorUserBuildSettings.iOSXcodeBuildConfig = fx.development ? XcodeBuildConfig.Debug : XcodeBuildConfig.Release;
            EditorUserBuildSettings.development = fx.development;
            EditorUserBuildSettings.allowDebugging = fx.scriptDebugging;
            EditorUserBuildSettings.connectProfiler = fx.autoconnectProfiler;
        }
    }
}
