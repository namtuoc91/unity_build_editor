using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Raccoon.BuildEditor
{
    [Serializable]
    public class SceneEntry
    {
        public string path;
        public bool enabled = true;
    }

    /// <summary>
    /// Rule No Ads riêng của project: set 1 field trên component (hoặc GameObject) trong scene theo cờ No Ads.
    /// Value rỗng = không đụng ở trạng thái đó.
    /// </summary>
    [Serializable]
    public class NoAdsRule
    {
        public const string GameObjectType = "GameObject";

        public bool enabled = true;
        /// <summary>Rỗng = mọi scene trong build list.</summary>
        public string scenePath = "";
        /// <summary>Đường dẫn hierarchy từ root, vd "Canvas/Shop/BtnRemoveAds".</summary>
        public string objectPath = "";
        /// <summary>Tên type (ngắn hoặc full). Rỗng / "GameObject" = chính GameObject.</summary>
        public string componentType = "";
        /// <summary>SerializedProperty path, vd "_hideRemoveAds" hoặc "m_IsActive".</summary>
        public string property = "";
        public string noAdsValue = "";
        public string adsValue = "";
        /// <summary>Kiểu property (tên SerializedPropertyType) lưu khi chọn qua ▼ để UI hiện ô nhập đúng kiểu; rỗng = ô text.</summary>
        public string valueType = "";
        /// <summary>Tên các giá trị enum (khi valueType = Enum).</summary>
        public List<string> enumNames = new List<string>();
    }

    /// <summary>
    /// Config chung, lưu ở ProjectSettings/RaccoonBuildConfig.json (commit được).
    /// Password keystore KHÔNG lưu ở đây — chỉ nằm trong SessionState.
    /// </summary>
    [Serializable]
    public class BuildConfig
    {
        public const string FilePath = "ProjectSettings/RaccoonBuildConfig.json";
        public const int DefaultAppBundleSizeWarningMB = 210;

        const string KeystorePassKey = "Raccoon.BuildEditor.KeystorePass";
        const string KeyAliasPassKey = "Raccoon.BuildEditor.KeyAliasPass";

        public string version = "";
        public int versionCode = 1;

        public string keystorePath = "";
        public string keystoreAlias = "";

        public string outputFolder = "Builds/Android";
        public int appBundleSizeWarningMB = DefaultAppBundleSizeWarningMB;

        public List<SceneEntry> scenes = new List<SceneEntry>();

        public List<NoAdsRule> noAdsRules = new List<NoAdsRule>();

        public List<BuildPreset> presets = new List<BuildPreset>();
        public int activePresetIndex;

        public static string KeystorePass
        {
            get => SessionState.GetString(KeystorePassKey, "");
            set => SessionState.SetString(KeystorePassKey, value ?? "");
        }

        public static string KeyAliasPass
        {
            get => SessionState.GetString(KeyAliasPassKey, "");
            set => SessionState.SetString(KeyAliasPassKey, value ?? "");
        }

        public BuildPreset ActivePreset
        {
            get
            {
                if (presets.Count == 0) return null;
                activePresetIndex = Mathf.Clamp(activePresetIndex, 0, presets.Count - 1);
                return presets[activePresetIndex];
            }
        }

        public string[] EnabledScenePaths =>
            scenes.Where(s => s.enabled && !string.IsNullOrEmpty(s.path)).Select(s => s.path).ToArray();

        public static BuildConfig Load()
        {
            BuildConfig config = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    config = JsonUtility.FromJson<BuildConfig>(File.ReadAllText(FilePath));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RaccoonBuild] Không đọc được {FilePath}: {e.Message}");
                }
            }

            if (config == null)
            {
                config = new BuildConfig();
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
            version = PlayerSettings.bundleVersion;
            versionCode = Mathf.Max(1, PlayerSettings.Android.bundleVersionCode);
            keystorePath = PlayerSettings.Android.keystoreName ?? "";
            keystoreAlias = PlayerSettings.Android.keyaliasName ?? "";
            ImportScenesFromBuildSettings();
        }

        void EnsureValid()
        {
            scenes ??= new List<SceneEntry>();
            noAdsRules ??= new List<NoAdsRule>();
            presets ??= new List<BuildPreset>();
            if (presets.Count == 0)
            {
                var appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
                presets.Add(BuildPreset.CreateDefaultDev(appId));
                presets.Add(BuildPreset.CreateDefaultRelease(appId));
            }

            foreach (var p in presets)
            {
                if (string.IsNullOrEmpty(p.fileNameTemplate)) p.fileNameTemplate = BuildPreset.DefaultFileNameTemplate;
                BuildRules.EnforceModeLocks(p);
            }

            if (versionCode <= 0) versionCode = 1;
            if (appBundleSizeWarningMB < 0) appBundleSizeWarningMB = 0;
            activePresetIndex = Mathf.Clamp(activePresetIndex, 0, presets.Count - 1);
        }

        public void ImportScenesFromBuildSettings()
        {
            scenes = EditorBuildSettings.scenes
                .Select(s => new SceneEntry { path = s.path, enabled = s.enabled })
                .ToList();
        }

        public void ApplyScenesToBuildSettings()
        {
            EditorBuildSettings.scenes = scenes
                .Where(s => !string.IsNullOrEmpty(s.path))
                .Select(s => new EditorBuildSettingsScene(s.path, s.enabled))
                .ToArray();
        }
    }
}
