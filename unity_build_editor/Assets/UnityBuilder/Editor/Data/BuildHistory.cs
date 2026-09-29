using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Raccoon.BuildEditor
{
    [Serializable]
    public class BuildHistoryEntry
    {
        public string timestamp;
        public string presetName;
        public BuildMode mode;
        public bool isAab;
        public string appId;
        public string version;
        public int versionCode;
        public bool noAds;
        public bool useTestAd;
        public bool developmentBuild;
        public long sizeBytes;
        public double durationSeconds;
        public string path;
    }

    /// <summary>
    /// History local ở Library/RaccoonBuildHistory.json. Chỉ ghi build thành công.
    /// Giới hạn: AAB 10 bản, APK 20 bản; vượt thì xóa entry cũ nhất cùng loại + xóa file trên disk.
    /// </summary>
    [Serializable]
    public class BuildHistory
    {
        public const string FilePath = "Library/RaccoonBuildHistory.json";
        public const int MaxApk = 20;
        public const int MaxAab = 10;

        // Mới nhất ở đầu list.
        public List<BuildHistoryEntry> entries = new List<BuildHistoryEntry>();

        public static BuildHistory Load()
        {
            if (File.Exists(FilePath))
            {
                try
                {
                    var h = JsonUtility.FromJson<BuildHistory>(File.ReadAllText(FilePath));
                    if (h != null)
                    {
                        h.entries ??= new List<BuildHistoryEntry>();
                        return h;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RaccoonBuild] Không đọc được {FilePath}: {e.Message}");
                }
            }

            return new BuildHistory();
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }

        /// <summary>Thêm entry, trả về các entry bị loại do vượt giới hạn (file của chúng bị xóa nếu deleteFiles).</summary>
        public List<BuildHistoryEntry> Add(BuildHistoryEntry entry, bool deleteFiles = true)
        {
            entries.Insert(0, entry);
            var max = entry.isAab ? MaxAab : MaxApk;
            var removed = entries.Where(e => e.isAab == entry.isAab).Skip(max).ToList();
            foreach (var r in removed)
            {
                entries.Remove(r);
                // Không xóa file nếu entry mới trỏ cùng path (build đè tên file).
                if (deleteFiles && r.path != entry.path) DeleteFile(r.path);
            }

            return removed;
        }

        public void Remove(BuildHistoryEntry entry, bool deleteFile = true)
        {
            entries.Remove(entry);
            if (deleteFile) DeleteFile(entry.path);
        }

        static void DeleteFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RaccoonBuild] Không xóa được {path}: {e.Message}");
            }
        }
    }
}
