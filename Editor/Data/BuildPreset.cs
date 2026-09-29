using System;

namespace Raccoon.BuildEditor
{
    public enum BuildMode
    {
        Dev = 0,
        Release = 1
    }

    /// <summary>
    /// Một cấu hình build. Mode quyết định format (APK/AAB) + architecture; backend luôn IL2CPP.
    /// </summary>
    [Serializable]
    public class BuildPreset
    {
        public const string DefaultFileNameTemplate = "{product}_{version}_{code}_{date}_{time}_{mode}{noads}";

        public string name = "New Preset";
        public string appId = "";
        public BuildMode mode = BuildMode.Dev;

        public bool developmentBuild;
        public bool scriptDebugging;
        public bool autoconnectProfiler;

        public bool noAds;
        public bool useTestAd;

        public bool cleanCache;
        public bool autoIncrement;
        public string fileNameTemplate = DefaultFileNameTemplate;

        public BuildPreset Clone()
        {
            return (BuildPreset)MemberwiseClone();
        }

        public static BuildPreset CreateDefaultDev(string appId)
        {
            return new BuildPreset
            {
                name = "Dev",
                appId = appId,
                mode = BuildMode.Dev,
                autoIncrement = false
            };
        }

        public static BuildPreset CreateDefaultRelease(string appId)
        {
            return new BuildPreset
            {
                name = "Release",
                appId = appId,
                mode = BuildMode.Release,
                autoIncrement = true
            };
        }
    }
}
