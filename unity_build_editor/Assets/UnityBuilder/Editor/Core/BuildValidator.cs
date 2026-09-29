using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Raccoon.BuildEditor
{
    public enum Severity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct ValidationMessage
    {
        public readonly Severity severity;
        public readonly string text;

        public ValidationMessage(Severity severity, string text)
        {
            this.severity = severity;
            this.text = text;
        }

        public override string ToString() => $"[{severity}] {text}";
    }

    /// <summary>Snapshot trạng thái môi trường cần để validate — tách ra để test không phụ thuộc Editor.</summary>
    public class ValidationInput
    {
        public BuildPreset preset;
        public string version;
        public string[] scenePaths = new string[0];
        public string[] missingScenes = new string[0];
        public bool androidModuleInstalled = true;
        public bool ndkFound = true;
        public bool activeTargetIsAndroid = true;
        public string keystorePath;
        public bool keystoreExists;
        public string keystoreAlias;
        public string keystorePass;
        public string keyAliasPass;
        public bool adPackInstalled;
        public MediationReport mediation;
    }

    public static class BuildValidator
    {
        /// <summary>Dev: có keystore thật + đủ password thì dùng, không thì debug keystore của Unity.</summary>
        public static bool HasUsableKeystore(bool keystoreExists, string alias, string keystorePass, string aliasPass) =>
            keystoreExists && !string.IsNullOrEmpty(alias) && !string.IsNullOrEmpty(keystorePass) &&
            !string.IsNullOrEmpty(aliasPass);

        public static List<ValidationMessage> Validate(ValidationInput i)
        {
            var list = new List<ValidationMessage>();
            void Err(string s) => list.Add(new ValidationMessage(Severity.Error, s));
            void Warn(string s) => list.Add(new ValidationMessage(Severity.Warning, s));
            void Info(string s) => list.Add(new ValidationMessage(Severity.Info, s));

            var p = i.preset;
            if (p == null)
            {
                Err("Chưa chọn preset.");
                return list;
            }

            var release = p.mode == BuildMode.Release;

            if (!BuildRules.IsValidAppId(p.appId))
                Err($"App ID không hợp lệ: '{p.appId}' (dạng com.company.game).");
            if (string.IsNullOrWhiteSpace(i.version))
                Err("Version trống.");

            if (i.scenePaths == null || i.scenePaths.Length == 0)
                Err("Không có scene nào được bật trong danh sách build.");
            foreach (var s in i.missingScenes ?? new string[0])
                Err($"Scene không tồn tại: {s}");

            if (!i.androidModuleInstalled)
                Err("Chưa cài Android Build Support (module Android/IL2CPP) cho Unity này.");
            else if (!i.ndkFound)
                Err("Không tìm thấy Android NDK (cần cho IL2CPP). Kiểm tra Preferences > External Tools.");

            if (!i.activeTargetIsAndroid)
                Warn("Platform hiện tại không phải Android — tool sẽ Switch Platform trước khi build (có thể lâu).");

            // Keystore
            var usable = HasUsableKeystore(i.keystoreExists, i.keystoreAlias, i.keystorePass, i.keyAliasPass);
            if (release)
            {
                if (string.IsNullOrEmpty(i.keystorePath)) Err("Release cần keystore: chưa chọn file keystore.");
                else if (!i.keystoreExists) Err($"Không tìm thấy keystore: {i.keystorePath}");
                if (string.IsNullOrEmpty(i.keystoreAlias)) Err("Release cần keystore: chưa nhập alias.");
                if (string.IsNullOrEmpty(i.keystorePass) || string.IsNullOrEmpty(i.keyAliasPass))
                    Err("Release cần keystore: chưa nhập password (keystore + alias).");
            }
            else if (!usable)
            {
                Info("Dev: không có keystore đầy đủ → dùng debug keystore của Unity.");
            }

            // Ads
            if (!i.adPackInstalled && (p.noAds || p.useTestAd))
                Warn("Chưa cài com.raccoon.adpack → bỏ qua No Ads / Use Test Ad.");

            // Mediation
            if (i.mediation != null)
            {
                foreach (var w in i.mediation.warnings) Warn("Mediation: " + w);
                foreach (var r in i.mediation.rows)
                {
                    switch (r.status)
                    {
                        case MediationStatus.VersionMismatch:
                        case MediationStatus.MissingInGradle:
                            var msg = $"Mediation {r.network}: {MediationReview.StatusLabel(r.status)} " +
                                      $"(xml {r.xmlVersion ?? "-"}, gradle {r.gradleVersion ?? "-"}).";
                            if (release) Err(msg + " Chạy Force Resolve.");
                            else Warn(msg);
                            break;
                        case MediationStatus.ExtraInGradle:
                            Warn($"Mediation {r.network}: lib thừa trong gradle ({r.gradleVersion}), không còn package/xml.");
                            break;
                        case MediationStatus.NoAdapterInfo:
                            Warn($"Mediation {r.network}: không tìm thấy Dependencies.xml của package.");
                            break;
                    }
                }
            }

            return list;
        }

        /// <summary>Thu thập trạng thái Editor thật rồi validate.</summary>
        public static List<ValidationMessage> ValidateCurrent(BuildConfig config, BuildPreset preset, MediationReport mediation)
        {
            var scenes = config.EnabledScenePaths;
            var input = new ValidationInput
            {
                preset = preset,
                version = config.version,
                scenePaths = scenes,
                missingScenes = scenes.Where(s => !File.Exists(s)).ToArray(),
                androidModuleInstalled = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                ndkFound = AndroidTools.NdkFound,
                activeTargetIsAndroid = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
                keystorePath = config.keystorePath,
                keystoreExists = !string.IsNullOrEmpty(config.keystorePath) && File.Exists(config.keystorePath),
                keystoreAlias = config.keystoreAlias,
                keystorePass = BuildConfig.KeystorePass,
                keyAliasPass = BuildConfig.KeyAliasPass,
                adPackInstalled = AdsSettingApplier.IsAdPackInstalled,
                mediation = mediation
            };
            return Validate(input);
        }

        public static bool HasErrors(IEnumerable<ValidationMessage> list) => list.Any(m => m.severity == Severity.Error);
    }
}
