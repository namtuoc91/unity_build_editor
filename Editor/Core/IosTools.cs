using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Raccoon.BuildEditor
{
    /// <summary>Thông tin môi trường build iOS (CocoaPods, EDM4U) + tiện ích Xcode project.</summary>
    public static class IosTools
    {
        public const string WorkspaceName = "Unity-iPhone.xcworkspace";
        public const string ProjectName = "Unity-iPhone.xcodeproj";

        /// <summary>Có EDM4U iOS Resolver (Google Mobile Ads…) → Xcode project cần pod install (EDM4U tự chạy sau build).</summary>
        public static bool HasIosResolver =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("Google.IOSResolver", false) != null);

        /// <summary>Facebook Unity SDK (Facebook.Unity.FB) có trong project.</summary>
        public static bool IsFacebookInstalled =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType("Facebook.Unity.FB", false) != null);

        /// <summary>Unity mở từ Hub không có PATH của shell → dò các chỗ cài pod hay gặp.</summary>
        public static string PodPath
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
                var candidates = new List<string> { "/opt/homebrew/bin/pod", "/usr/local/bin/pod", "/usr/bin/pod" };
                candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Where(d => d.Length > 0).Select(d => Path.Combine(d, "pod")));
                foreach (var dir in new[] { ".gem/bin", ".rbenv/shims", ".rvm/bin" })
                    candidates.Add(Path.Combine(home, dir, "pod"));
                return candidates.FirstOrDefault(File.Exists);
            }
        }

        /// <summary>File mở bằng Xcode: .xcworkspace (có pod) ưu tiên hơn .xcodeproj; null nếu chưa có.</summary>
        public static string XcodeEntry(string xcodeDir)
        {
            if (string.IsNullOrEmpty(xcodeDir)) return null;
            var ws = Path.Combine(xcodeDir, WorkspaceName);
            if (Directory.Exists(ws)) return ws;
            var proj = Path.Combine(xcodeDir, ProjectName);
            return Directory.Exists(proj) ? proj : null;
        }

        /// <summary>Có Podfile nhưng không có .xcworkspace → pod install chưa chạy / lỗi.</summary>
        public static bool PodInstallMissing(string xcodeDir) =>
            !string.IsNullOrEmpty(xcodeDir) && File.Exists(Path.Combine(xcodeDir, "Podfile")) &&
            !Directory.Exists(Path.Combine(xcodeDir, WorkspaceName));

        public static void OpenInXcode(string xcodeDir)
        {
            var entry = XcodeEntry(xcodeDir);
            if (entry != null) EditorUtility.OpenWithDefaultApp(entry);
            else EditorUtility.RevealInFinder(xcodeDir);
        }
    }
}
