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
        public List<NoAdsRule> noAdsRules = new List<NoAdsRule>();
        public MediationReport mediation;
    }

    /// <summary>Snapshot cho iOS: dùng lại phần chung (preset/version/scene/ads/rule), field Android bỏ qua.</summary>
    public class IosValidationInput : ValidationInput
    {
        public bool iosModuleInstalled = true;
        public bool activeTargetIsIos = true;
        public bool needsPods;
        public bool podFound = true;
        public bool automaticSigning = true;
        public string teamId;
        public bool facebookInstalled;
        public bool podsToMainTarget = true;
        public List<string> iconErrors = new List<string>();
        public List<string> iconWarnings = new List<string>();
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

            ValidateApp(i, list, "App ID", BuildRules.IsValidAppId);

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

            ValidateAdsAndRules(i, list);

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

        static void ValidateApp(ValidationInput i, List<ValidationMessage> list, string idLabel, System.Func<string, bool> isValidId)
        {
            void Err(string s) => list.Add(new ValidationMessage(Severity.Error, s));
            var p = i.preset;
            if (!isValidId(p.appId))
                Err($"{idLabel} không hợp lệ: '{p.appId}' (dạng com.company.game).");
            if (string.IsNullOrWhiteSpace(i.version))
                Err("Version trống.");

            if (i.scenePaths == null || i.scenePaths.Length == 0)
                Err("Không có scene nào được bật trong danh sách build.");
            foreach (var s in i.missingScenes ?? new string[0])
                Err($"Scene không tồn tại: {s}");
        }

        static void ValidateAdsAndRules(ValidationInput i, List<ValidationMessage> list)
        {
            void Err(string s) => list.Add(new ValidationMessage(Severity.Error, s));
            void Warn(string s) => list.Add(new ValidationMessage(Severity.Warning, s));
            var p = i.preset;

            // Ads
            var rules = i.noAdsRules ?? new List<NoAdsRule>();
            if (!i.adPackInstalled && p.useTestAd)
                Warn("Chưa cài com.raccoon.adpack → bỏ qua Use Test Ad.");
            if (!i.adPackInstalled && p.noAds && rules.All(r => !r.enabled))
                Warn("Chưa cài com.raccoon.adpack và không có No Ads rule → No Ads không có tác dụng.");

            // No Ads rule (check tĩnh; resolve object/property chỉ khi build hoặc bấm "Kiểm tra rule")
            foreach (var r in rules.Where(r => r.enabled))
            {
                var name = NoAdsRuleApplier.Describe(r);
                if (string.IsNullOrWhiteSpace(r.objectPath) || string.IsNullOrWhiteSpace(r.property))
                    Err($"No Ads rule '{name}': thiếu Object path hoặc Property.");
                if (string.IsNullOrEmpty(r.noAdsValue) && string.IsNullOrEmpty(r.adsValue))
                    Warn($"No Ads rule '{name}': chưa nhập giá trị nào → không có tác dụng.");
                if (!string.IsNullOrEmpty(r.scenePath) && !(i.scenePaths ?? new string[0]).Contains(r.scenePath))
                    Warn($"No Ads rule '{name}': scene {r.scenePath} không nằm trong build list → bỏ qua.");
            }
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
                noAdsRules = config.noAdsRules,
                mediation = mediation
            };
            return Validate(input);
        }

        public static List<ValidationMessage> ValidateIos(IosValidationInput i)
        {
            var list = new List<ValidationMessage>();
            void Err(string s) => list.Add(new ValidationMessage(Severity.Error, s));
            void Warn(string s) => list.Add(new ValidationMessage(Severity.Warning, s));

            if (!(i.preset is IosBuildPreset p))
            {
                Err("Chưa chọn preset.");
                return list;
            }

            ValidateApp(i, list, "Bundle ID", BuildRules.IsValidBundleId);

            if (!i.iosModuleInstalled)
                Err("Chưa cài iOS Build Support cho Unity này (Unity Hub → Add modules).");
            if (!i.activeTargetIsIos)
                Warn("Platform hiện tại không phải iOS — tool sẽ Switch Platform trước khi build (có thể lâu).");

            if (i.needsPods && !i.podFound)
                Warn("Có EDM4U iOS Resolver nhưng không tìm thấy CocoaPods (pod) → Xcode project thiếu pod (không có .xcworkspace). Cài: brew install cocoapods");

            // Signing: chỉ ghi vào Xcode project, thiếu thì chọn lại trong Xcode → chỉ cảnh báo.
            if (i.automaticSigning)
            {
                if (string.IsNullOrEmpty(i.teamId))
                    Warn("Chưa nhập Team ID → phải chọn team trong Xcode (Signing & Capabilities) trước khi build.");
                else if (!BuildRules.IsValidTeamId(i.teamId))
                    Warn($"Team ID '{i.teamId}' không đúng dạng (10 ký tự A-Z0-9).");
            }
            else if (string.IsNullOrEmpty(p.provisioningProfile))
            {
                Warn("Manual Signing chưa nhập Provisioning Profile UUID → chọn trong Xcode.");
            }

            if (i.facebookInstalled && !i.podsToMainTarget)
                Warn("Có Facebook SDK nhưng đang tắt 'Thêm pod FB vào Unity-iPhone' → phải add framework FB tay trong Xcode.");

            foreach (var e in i.iconErrors ?? new List<string>()) Err(e);
            foreach (var w in i.iconWarnings ?? new List<string>()) Warn(w);

            ValidateAdsAndRules(i, list);
            return list;
        }

        public static List<ValidationMessage> ValidateIosCurrent(BuildConfig shared, IosBuildConfig config, IosBuildPreset preset)
        {
            var scenes = shared.EnabledScenePaths;
            var iconErrors = new List<string>();
            var iconWarnings = new List<string>();
            IosXcodePostProcess.CheckIcons(config, iconErrors, iconWarnings);
            var input = new IosValidationInput
            {
                preset = preset,
                version = shared.version,
                scenePaths = scenes,
                missingScenes = scenes.Where(s => !File.Exists(s)).ToArray(),
                adPackInstalled = AdsSettingApplier.IsAdPackInstalled,
                noAdsRules = shared.noAdsRules,
                iosModuleInstalled = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.iOS, BuildTarget.iOS),
                activeTargetIsIos = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS,
                needsPods = IosTools.HasIosResolver,
                podFound = IosTools.PodPath != null,
                automaticSigning = config.automaticSigning,
                teamId = config.teamId,
                facebookInstalled = IosTools.IsFacebookInstalled,
                podsToMainTarget = config.podsToMainTarget,
                iconErrors = iconErrors,
                iconWarnings = iconWarnings
            };
            return ValidateIos(input);
        }

        public static bool HasErrors(IEnumerable<ValidationMessage> list) => list.Any(m => m.severity == Severity.Error);
    }
}
