using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Raccoon.BuildEditor
{
    /// <summary>Giá trị ads cần ghi: creativeMode luôn ghi; useTestAd = null nghĩa là không đụng asset.</summary>
    public readonly struct AdsTarget
    {
        public readonly bool creativeMode;
        public readonly bool? useTestAd;

        public AdsTarget(bool creativeMode, bool? useTestAd)
        {
            this.creativeMode = creativeMode;
            this.useTestAd = useTestAd;
        }
    }

    /// <summary>Setting thực tế sau khi áp luật Mode lên preset.</summary>
    public readonly struct EffectiveSettings
    {
        public readonly bool isAab;
        public readonly AndroidArchitecture architectures;
        public readonly bool development;
        public readonly bool scriptDebugging;
        public readonly bool autoconnectProfiler;
        public readonly bool noAds;
        public readonly AdsTarget ads;

        public EffectiveSettings(bool isAab, AndroidArchitecture architectures, bool development,
            bool scriptDebugging, bool autoconnectProfiler, bool noAds, AdsTarget ads)
        {
            this.isAab = isAab;
            this.architectures = architectures;
            this.development = development;
            this.scriptDebugging = scriptDebugging;
            this.autoconnectProfiler = autoconnectProfiler;
            this.noAds = noAds;
            this.ads = ads;
        }
    }

    /// <summary>Luật build thuần (không đụng Unity state) — dùng chung cho builder, UI và test.</summary>
    public static class BuildRules
    {
        static readonly Regex AppIdRegex = new Regex(@"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z][a-zA-Z0-9_]*)+$");

        public static bool IsAab(BuildMode mode) => mode == BuildMode.Release;

        public static string Extension(BuildMode mode) => IsAab(mode) ? "aab" : "apk";

        public static AndroidArchitecture Architectures(BuildMode mode) =>
            mode == BuildMode.Release
                ? AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64
                : AndroidArchitecture.ARM64;

        public static bool IsValidAppId(string appId) => !string.IsNullOrEmpty(appId) && AppIdRegex.IsMatch(appId);

        /// <summary>Tắt các option bị khóa theo Mode (Release không có dev build / no ads / test ad).</summary>
        public static void EnforceModeLocks(BuildPreset p)
        {
            if (p.mode == BuildMode.Release)
            {
                p.developmentBuild = false;
                p.noAds = false;
                p.useTestAd = false;
            }

            if (!p.developmentBuild)
            {
                p.scriptDebugging = false;
                p.autoconnectProfiler = false;
            }
        }

        public static AdsTarget ResolveAds(BuildMode mode, bool noAds, bool useTestAd)
        {
            if (mode == BuildMode.Release) return new AdsTarget(false, false);
            if (noAds) return new AdsTarget(true, null);
            return new AdsTarget(false, useTestAd);
        }

        public static EffectiveSettings Resolve(BuildPreset p)
        {
            var release = p.mode == BuildMode.Release;
            var dev = !release && p.developmentBuild;
            var noAds = !release && p.noAds;
            return new EffectiveSettings(
                IsAab(p.mode),
                Architectures(p.mode),
                dev,
                dev && p.scriptDebugging,
                dev && p.autoconnectProfiler,
                noAds,
                ResolveAds(p.mode, noAds, !release && p.useTestAd));
        }

        // ---- Version code ----

        /// <summary>Lấy max để sửa tay ngoài tool (PlayerSettings) không bị lùi số.</summary>
        public static int CurrentVersionCode(int configCode, int playerSettingsCode) =>
            Math.Max(1, Math.Max(configCode, playerSettingsCode));

        public static int NextVersionCode(int current, bool autoIncrement) => autoIncrement ? current + 1 : current;

        // ---- Tên file ----

        public static string FormatFileName(string template, string product, string version, int code,
            DateTime time, BuildMode mode, bool noAds)
        {
            if (string.IsNullOrWhiteSpace(template)) template = BuildPreset.DefaultFileNameTemplate;
            var s = template
                .Replace("{product}", product ?? "")
                .Replace("{version}", version ?? "")
                .Replace("{code}", code.ToString())
                .Replace("{date}", time.ToString("yyyyMMdd"))
                .Replace("{time}", time.ToString("HHmmss"))
                .Replace("{mode}", mode == BuildMode.Release ? "release" : "dev")
                .Replace("{noads}", noAds ? "_noads" : "");
            return SanitizeFileName(s);
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (char.IsWhiteSpace(c) || Array.IndexOf(invalid, c) >= 0 || c == '/' || c == '\\' || c == ':')
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            var result = sb.ToString().Trim('_', '.');
            return string.IsNullOrEmpty(result) ? "build" : result;
        }
    }
}
