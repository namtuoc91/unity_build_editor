using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using Debug = UnityEngine.Debug;

namespace Raccoon.BuildEditor
{
    /// <summary>
    /// Sửa Xcode project sau khi Unity export (chạy cho mọi build iOS, theo RaccoonIosBuildConfig.json):
    /// 1. Thêm pod FB (prefix cấu hình) vào target Unity-iPhone trong Podfile — giữa lúc EDM4U sinh Podfile (40) và pod install (50).
    /// 2. Icon A/B test: tạo app icon set phụ trong Images.xcassets + set ASSETCATALOG_COMPILER_* ở target Unity-iPhone.
    /// </summary>
    public static class IosXcodePostProcess
    {
        const string Log = "[RaccoonBuild] ";
        public const string MainTarget = "Unity-iPhone";
        public const string IconSetMarker = "raccoon_alternate_icon";
        public const string AssetCatalog = "Unity-iPhone/Images.xcassets";

        // EDM4U IOSResolver: BUILD_ORDER_GEN_PODFILE = 40, BUILD_ORDER_INSTALL_PODS = 50.
        [PostProcessBuild(45)]
        public static void OnPodfileGenerated(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var config = IosBuildConfig.Load();
            if (!config.podsToMainTarget) return;

            var podfile = Path.Combine(path, "Podfile");
            if (!File.Exists(podfile)) return;

            var text = File.ReadAllText(podfile);
            var patched = PatchPodfile(text, config.mainTargetPodPrefixes, out var added);
            if (added.Count == 0) return;

            File.WriteAllText(podfile, patched);
            Debug.Log($"{Log}Podfile: thêm vào target '{MainTarget}': {string.Join(", ", added)}");
            if (Directory.Exists(Path.Combine(path, IosTools.WorkspaceName)))
                Debug.LogWarning($"{Log}Pod đã install trước khi sửa Podfile → chạy lại `pod install` trong {path}.");
        }

        [PostProcessBuild(100)]
        public static void OnXcodeExported(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS) return;
            var config = IosBuildConfig.Load();
            try
            {
                ApplyAlternateIcons(config, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"{Log}Icon A/B test lỗi: {e.Message}");
            }
        }

        // ---------- Podfile ----------

        static readonly Regex PodLine = new Regex(@"^\s*pod\s+['""]([^'""]+)['""]");
        static readonly Regex TargetLine = new Regex(@"^\s*target\s+['""]([^'""]+)['""]\s+do\s*$");

        static string PodName(string line)
        {
            var m = PodLine.Match(line);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Copy các dòng `pod '...'` có tên (phần trước '/') bắt đầu bằng prefix sang block target 'Unity-iPhone'.
        /// Chưa có block thì tạo mới ở cuối các target. Pod đã có trong block thì bỏ qua.
        /// </summary>
        public static string PatchPodfile(string podfile, IEnumerable<string> prefixes, out List<string> added)
        {
            added = new List<string>();
            var pre = (prefixes ?? Enumerable.Empty<string>()).Select(p => p?.Trim()).Where(p => !string.IsNullOrEmpty(p)).ToList();
            if (string.IsNullOrEmpty(podfile) || pre.Count == 0) return podfile;

            var newline = podfile.Contains("\r\n") ? "\r\n" : "\n";
            var lines = podfile.Replace("\r\n", "\n").Split('\n').ToList();

            // Block target: (tên, dòng mở, dòng 'end').
            var blocks = new List<(string name, int start, int end)>();
            for (var i = 0; i < lines.Count; i++)
            {
                var m = TargetLine.Match(lines[i]);
                if (!m.Success) continue;
                var end = i + 1;
                while (end < lines.Count && lines[end].Trim() != "end") end++;
                blocks.Add((m.Groups[1].Value, i, end));
                i = end;
            }

            var main = blocks.FirstOrDefault(b => b.name == MainTarget);
            var hasMain = main.name != null;
            var inMain = new HashSet<string>();
            if (hasMain)
                for (var i = main.start + 1; i < main.end; i++)
                    if (PodName(lines[i]) is string n) inMain.Add(n);

            var toAdd = new List<string>();
            foreach (var b in blocks.Where(b => b.name != MainTarget))
            {
                for (var i = b.start + 1; i < b.end && i < lines.Count; i++)
                {
                    var name = PodName(lines[i]);
                    if (name == null || inMain.Contains(name)) continue;
                    var root = name.Split('/')[0];
                    if (!pre.Any(p => root.StartsWith(p, StringComparison.Ordinal))) continue;
                    inMain.Add(name);
                    added.Add(name);
                    toAdd.Add("  " + lines[i].Trim());
                }
            }

            if (toAdd.Count == 0) return podfile;

            if (hasMain)
            {
                lines.InsertRange(main.end, toAdd);
            }
            else
            {
                var at = blocks.Count > 0 ? Math.Min(blocks.Max(b => b.end) + 1, lines.Count) : lines.Count;
                var block = new List<string> { $"target '{MainTarget}' do" };
                block.AddRange(toAdd);
                block.Add("end");
                lines.InsertRange(at, block);
            }

            return string.Join(newline, lines);
        }

        // ---------- Icon A/B test ----------

        static readonly Regex IconNameRegex = new Regex(@"^[A-Za-z0-9][A-Za-z0-9_\-]*$");

        public static bool IsValidIconName(string name) =>
            !string.IsNullOrEmpty(name) && IconNameRegex.IsMatch(name) && name != "AppIcon";

        /// <summary>Contents.json app icon set 1 size (Xcode 14+ tự sinh các size còn lại).</summary>
        public static string IconSetContents(string fileName) =>
            "{\n  \"images\" : [\n    {\n      \"filename\" : \"" + fileName + "\",\n      \"idiom\" : \"universal\",\n" +
            "      \"platform\" : \"ios\",\n      \"size\" : \"1024x1024\"\n    }\n  ],\n" +
            "  \"info\" : {\n    \"author\" : \"xcode\",\n    \"version\" : 1\n  }\n}\n";

        /// <summary>Kiểm tra cấu hình icon (dùng ở validator). Lỗi = chặn build.</summary>
        public static void CheckIcons(IosBuildConfig config, List<string> errors, List<string> warnings)
        {
            if (!config.alternateIconsEnabled) return;
            var icons = config.alternateIcons.Where(i => i.enabled).ToList();
            if (icons.Count == 0)
            {
                warnings.Add("Icon A/B test: đang bật nhưng chưa có icon nào.");
                return;
            }

            foreach (var dup in icons.GroupBy(i => i.name).Where(g => g.Count() > 1))
                errors.Add($"Icon A/B test: trùng tên '{dup.Key}'.");

            foreach (var icon in icons)
            {
                var label = string.IsNullOrEmpty(icon.name) ? "(chưa đặt tên)" : icon.name;
                if (!IsValidIconName(icon.name))
                    errors.Add($"Icon A/B test '{label}': tên chỉ gồm chữ/số/-/_ và khác 'AppIcon'.");
                if (string.IsNullOrEmpty(icon.texturePath) || !File.Exists(icon.texturePath))
                {
                    errors.Add($"Icon A/B test '{label}': chưa chọn texture hoặc file không tồn tại.");
                    continue;
                }

                if (!icon.texturePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Icon A/B test '{label}': texture phải là file .png.");

                if (AssetImporter.GetAtPath(icon.texturePath) is TextureImporter imp)
                {
                    imp.GetSourceTextureWidthAndHeight(out var w, out var h);
                    if (w != 1024 || h != 1024) warnings.Add($"Icon A/B test '{label}': {w}x{h}, App Store yêu cầu 1024x1024.");
                    if (imp.DoesSourceTextureHaveAlpha())
                        warnings.Add($"Icon A/B test '{label}': PNG có kênh alpha → App Store có thể từ chối, nên xuất PNG không alpha.");
                }
            }
        }

        static void ApplyAlternateIcons(IosBuildConfig config, string path)
        {
            var catalog = Path.Combine(path, AssetCatalog);
            if (!Directory.Exists(catalog))
            {
                if (config.alternateIconsEnabled) Debug.LogError($"{Log}Không thấy {AssetCatalog} trong Xcode project.");
                return;
            }

            // Xóa icon set do tool tạo ở lần trước (Append) để không còn icon đã bỏ khỏi config.
            foreach (var dir in Directory.GetDirectories(catalog, "*.appiconset"))
                if (File.Exists(Path.Combine(dir, IconSetMarker))) Directory.Delete(dir, true);

            var icons = config.alternateIconsEnabled
                ? config.alternateIcons.Where(i => i.enabled && IsValidIconName(i.name) && File.Exists(i.texturePath))
                    .GroupBy(i => i.name).Select(g => g.First()).ToList()
                : new List<AlternateIcon>();

            foreach (var icon in icons)
            {
                var dir = Path.Combine(catalog, icon.name + ".appiconset");
                Directory.CreateDirectory(dir);
                File.Copy(icon.texturePath, Path.Combine(dir, "icon.png"), true);
                File.WriteAllText(Path.Combine(dir, "Contents.json"), IconSetContents("icon.png"));
                File.WriteAllText(Path.Combine(dir, IconSetMarker), "Tạo bởi Raccoon Build Editor, bị xóa/tạo lại mỗi lần build.");
            }

            var names = string.Join(" ", icons.Select(i => i.name));
            SetMainTargetBuildProperties(path, new Dictionary<string, string>
            {
                ["ASSETCATALOG_COMPILER_ALTERNATE_APPICON_NAMES"] = names,
                ["ASSETCATALOG_COMPILER_INCLUDE_ALL_APPICON_ASSETS"] = icons.Count > 0 ? "YES" : "NO"
            });

            if (icons.Count > 0) Debug.Log($"{Log}Icon A/B test: {names}");
        }

        /// <summary>PBXProject qua reflection (UnityEditor.iOS.Extensions.Xcode chỉ có khi cài iOS Build Support).</summary>
        static void SetMainTargetBuildProperties(string buildPath, Dictionary<string, string> props)
        {
            var type = Type.GetType("UnityEditor.iOS.Xcode.PBXProject, UnityEditor.iOS.Extensions.Xcode", false);
            if (type == null) throw new Exception("Không load được UnityEditor.iOS.Xcode.PBXProject (thiếu iOS Build Support?).");

            var projPath = (string)type.GetMethod("GetPBXProjectPath", new[] { typeof(string) }).Invoke(null, new object[] { buildPath });
            var proj = Activator.CreateInstance(type);
            type.GetMethod("ReadFromFile", new[] { typeof(string) }).Invoke(proj, new object[] { projPath });
            var mainGuid = (string)type.GetMethod("GetUnityMainTargetGuid", Type.EmptyTypes).Invoke(proj, null);
            var set = type.GetMethod("SetBuildProperty", new[] { typeof(string), typeof(string), typeof(string) });
            foreach (var kv in props) set.Invoke(proj, new object[] { mainGuid, kv.Key, kv.Value });
            type.GetMethod("WriteToFile", new[] { typeof(string) }).Invoke(proj, new object[] { projPath });
        }
    }
}
