using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Raccoon.BuildEditor
{
    /// <summary>
    /// Preset iOS: dùng lại field chung của BuildPreset (appId = Bundle ID, mode, dev build, No Ads, test ad…).
    /// Mode Release khóa dev build / No Ads / test ad giống Android. Output luôn là Xcode project.
    /// </summary>
    [Serializable]
    public class IosBuildPreset : BuildPreset
    {
        /// <summary>UUID provisioning profile, chỉ dùng khi tắt Automatic Signing (Dev = Development, Release = Distribution).</summary>
        public string provisioningProfile = "";

        public new IosBuildPreset Clone() => (IosBuildPreset)MemberwiseClone();

        public new static IosBuildPreset CreateDefaultDev(string bundleId) => new IosBuildPreset
        {
            name = "Dev",
            appId = bundleId,
            mode = BuildMode.Dev,
            autoIncrement = false
        };

        public new static IosBuildPreset CreateDefaultRelease(string bundleId) => new IosBuildPreset
        {
            name = "Release",
            appId = bundleId,
            mode = BuildMode.Release,
            autoIncrement = true
        };
    }

    /// <summary>Icon phụ cho A/B test (App Store Product Page Optimization / setAlternateIconName).</summary>
    [Serializable]
    public class AlternateIcon
    {
        public bool enabled = true;
        /// <summary>Tên app icon set trong Xcode, vd "AppIcon-B" (chữ, số, '-', '_').</summary>
        public string name = "";
        /// <summary>Texture PNG vuông 1024x1024 trong project.</summary>
        public string texturePath = "";
    }

    /// <summary>
    /// Config iOS, lưu ở ProjectSettings/RaccoonIosBuildConfig.json (commit được).
    /// Version, scene list và No Ads Rules dùng chung với Android (RaccoonBuildConfig.json).
    /// </summary>
    [Serializable]
    public class IosBuildConfig
    {
        public const string FilePath = "ProjectSettings/RaccoonIosBuildConfig.json";
        public static readonly string[] DefaultMainTargetPodPrefixes = { "FBSDK", "FBAEMKit" };

        public int buildNumber = 1;

        public bool automaticSigning = true;
        public string teamId = "";

        public string outputFolder = "Builds/iOS";

        /// <summary>Thêm các pod khớp prefix vào target Unity-iPhone trong Podfile (thay cho add framework FB tay trong Xcode).</summary>
        public bool podsToMainTarget = true;
        public List<string> mainTargetPodPrefixes = new List<string>(DefaultMainTargetPodPrefixes);

        /// <summary>Icon A/B test: copy vào Images.xcassets + set ASSETCATALOG_COMPILER_* ở target Unity-iPhone.</summary>
        public bool alternateIconsEnabled;
        public List<AlternateIcon> alternateIcons = new List<AlternateIcon>();

        public List<IosBuildPreset> presets = new List<IosBuildPreset>();
        public int activePresetIndex;

        public IosBuildPreset ActivePreset
        {
            get
            {
                if (presets.Count == 0) return null;
                activePresetIndex = Mathf.Clamp(activePresetIndex, 0, presets.Count - 1);
                return presets[activePresetIndex];
            }
        }

        public static int PlayerSettingsBuildNumber =>
            int.TryParse(PlayerSettings.iOS.buildNumber, out var n) ? n : 0;

        public static IosBuildConfig Load()
        {
            IosBuildConfig config = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    config = JsonUtility.FromJson<IosBuildConfig>(File.ReadAllText(FilePath));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RaccoonBuild] Không đọc được {FilePath}: {e.Message}");
                }
            }

            if (config == null)
            {
                config = new IosBuildConfig();
                config.InitDefaults();
                config.EnsureValid();
                config.Save();
                return config;
            }

            config.EnsureValid();
            return config;
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }

        void InitDefaults()
        {
            buildNumber = Mathf.Max(1, PlayerSettingsBuildNumber);
            teamId = PlayerSettings.iOS.appleDeveloperTeamID ?? "";
            automaticSigning = PlayerSettings.iOS.appleEnableAutomaticSigning;
        }

        void EnsureValid()
        {
            presets ??= new List<IosBuildPreset>();
            if (presets.Count == 0)
            {
                var bundleId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS);
                presets.Add(IosBuildPreset.CreateDefaultDev(bundleId));
                presets.Add(IosBuildPreset.CreateDefaultRelease(bundleId));
            }

            foreach (var p in presets)
            {
                if (string.IsNullOrEmpty(p.fileNameTemplate)) p.fileNameTemplate = BuildPreset.DefaultFileNameTemplate;
                p.provisioningProfile ??= "";
                BuildRules.EnforceModeLocks(p);
            }

            mainTargetPodPrefixes ??= new List<string>(DefaultMainTargetPodPrefixes);
            alternateIcons ??= new List<AlternateIcon>();
            if (buildNumber <= 0) buildNumber = 1;
            activePresetIndex = Mathf.Clamp(activePresetIndex, 0, presets.Count - 1);
        }
    }

    [Serializable]
    public class IosBuildHistoryEntry
    {
        public string timestamp;
        public string presetName;
        public BuildMode mode;
        public string bundleId;
        public string version;
        public int buildNumber;
        public bool noAds;
        public bool useTestAd;
        public bool developmentBuild;
        public bool appended;
        public double durationSeconds;
        /// <summary>Folder Xcode project.</summary>
        public string path;
    }

    /// <summary>
    /// History local ở Library/RaccoonIosBuildHistory.json, chỉ ghi build thành công, giữ MaxEntries bản gần nhất.
    /// Không xóa folder Xcode khi entry bị đẩy ra (folder dùng lại theo preset).
    /// </summary>
    [Serializable]
    public class IosBuildHistory
    {
        public const string FilePath = "Library/RaccoonIosBuildHistory.json";
        public const int MaxEntries = 20;

        public List<IosBuildHistoryEntry> entries = new List<IosBuildHistoryEntry>();

        public static IosBuildHistory Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var h = JsonUtility.FromJson<IosBuildHistory>(File.ReadAllText(FilePath));
                    if (h != null)
                    {
                        h.entries ??= new List<IosBuildHistoryEntry>();
                        return h;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RaccoonBuild] Không đọc được {FilePath}: {e.Message}");
                }
            }

            return new IosBuildHistory();
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }

        public void Add(IosBuildHistoryEntry entry)
        {
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }
    }
}
