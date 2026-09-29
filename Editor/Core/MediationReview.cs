using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Raccoon.BuildEditor
{
    public enum MediationStatus
    {
        Ok,
        VersionMismatch,
        MissingInGradle,
        ExtraInGradle,
        NoAdapterInfo
    }

    public class MediationRow
    {
        public string network;
        public string packageVersion;
        public string artifact;
        public string xmlVersion;
        public string gradleVersion;
        public string source;
        public MediationStatus status;
    }

    public class CoreLib
    {
        public string name;
        public string version;
    }

    public class GradleDep
    {
        public string group;
        public string artifact;
        public string version;
        public string comment;
    }

    public class XmlDep
    {
        public string network;
        public string artifact;
        public string version;
        public string source;
        public string packageVersion;
        public string xmlPath;
    }

    public class MediationReport
    {
        public bool adMobInstalled;
        public string adMobVersion;
        /// <summary>"mainTemplate.gradle", ".aar (Assets/Plugins/Android)" hoặc null.</summary>
        public string gradleSource;
        public readonly List<string> warnings = new List<string>();
        public readonly List<CoreLib> core = new List<CoreLib>();
        public readonly List<MediationRow> rows = new List<MediationRow>();
    }

    /// <summary>
    /// Review read-only các network AdMob mediation: package UPM + *Dependencies.xml, đối chiếu block
    /// "Android Resolver Dependencies" trong mainTemplate.gradle (fallback: .aar trong Assets/Plugins/Android).
    /// </summary>
    public static class MediationReview
    {
        public const string MainTemplatePath = "Assets/Plugins/Android/mainTemplate.gradle";
        const string AndroidPluginsDir = "Assets/Plugins/Android";
        const string CorePackage = "com.google.ads.mobile";
        const string MediationPackagePrefix = "com.google.ads.mobile.mediation.";
        const string MediationGroup = "com.google.ads.mediation";
        const string BlockStart = "// Android Resolver Dependencies Start";
        const string BlockEnd = "// Android Resolver Dependencies End";

        static readonly (string group, string artifact)[] CoreArtifacts =
        {
            ("com.google.android.gms", "play-services-ads"),
            ("com.google.android.ump", "user-messaging-platform")
        };

        static readonly Regex XmlSpecRegex =
            new Regex(@"androidPackage\s+spec\s*=\s*""com\.google\.ads\.mediation:([^:""]+):([^""]+)""");

        static readonly Regex GradleLineRegex =
            new Regex(@"^\s*(?:implementation|api)\s*\(?\s*['""]([^:'""]+):([^:'""]+):([^'""]+)['""]\s*\)?\s*(?://\s*(.*))?$");

        static readonly Regex NameVersionRegex = new Regex(@"^(.+?)-(\d.*)$");

        public static string StatusLabel(MediationStatus s)
        {
            switch (s)
            {
                case MediationStatus.Ok: return "✅ OK";
                case MediationStatus.VersionMismatch: return "⚠️ Lệch version";
                case MediationStatus.MissingInGradle: return "❌ Thiếu trong gradle";
                case MediationStatus.ExtraInGradle: return "❓ Lib thừa";
                default: return "❓ Không rõ adapter";
            }
        }

        // ---------- Parser thuần (test được) ----------

        public static List<(string artifact, string version)> ParseDependenciesXml(string xml)
        {
            return XmlSpecRegex.Matches(xml ?? "").Cast<Match>()
                .Select(m => (m.Groups[1].Value.Trim(), NormalizeVersion(m.Groups[2].Value)))
                .ToList();
        }

        /// <summary>Trả null nếu không có block resolver.</summary>
        public static List<GradleDep> ParseResolverBlock(string gradle)
        {
            if (string.IsNullOrEmpty(gradle)) return null;
            var start = gradle.IndexOf(BlockStart, StringComparison.Ordinal);
            if (start < 0) return null;
            var end = gradle.IndexOf(BlockEnd, start, StringComparison.Ordinal);
            if (end < 0) end = gradle.Length;

            var block = gradle.Substring(start + BlockStart.Length, end - start - BlockStart.Length);
            var list = new List<GradleDep>();
            foreach (var line in block.Split('\n'))
            {
                var m = GradleLineRegex.Match(line.TrimEnd('\r'));
                if (!m.Success) continue;
                list.Add(new GradleDep
                {
                    group = m.Groups[1].Value,
                    artifact = m.Groups[2].Value,
                    version = NormalizeVersion(m.Groups[3].Value),
                    comment = m.Groups[4].Success ? m.Groups[4].Value.Trim() : null
                });
            }

            return list;
        }

        /// <summary>Parse tên file .aar dạng "{group}.{artifact}-{version}.aar" theo các group quan tâm.</summary>
        public static List<GradleDep> ParseAarFileNames(IEnumerable<string> fileNames)
        {
            var groups = new[] { MediationGroup }.Concat(CoreArtifacts.Select(c => c.group)).Distinct().ToArray();
            var list = new List<GradleDep>();
            foreach (var raw in fileNames)
            {
                var name = Path.GetFileName(raw);
                if (!name.EndsWith(".aar", StringComparison.OrdinalIgnoreCase)) continue;
                name = name.Substring(0, name.Length - 4);
                foreach (var g in groups)
                {
                    if (!name.StartsWith(g + ".", StringComparison.Ordinal)) continue;
                    var m = NameVersionRegex.Match(name.Substring(g.Length + 1));
                    if (!m.Success) continue;
                    list.Add(new GradleDep { group = g, artifact = m.Groups[1].Value, version = m.Groups[2].Value });
                    break;
                }
            }

            return list;
        }

        /// <summary>Ghép xml ↔ gradle theo tên artifact (tên network trong gradle có thể khác tên package).</summary>
        public static List<MediationRow> BuildRows(IList<XmlDep> xmlDeps, IList<GradleDep> gradleDeps,
            IEnumerable<(string network, string packageVersion)> upmWithoutXml)
        {
            var rows = new List<MediationRow>();
            var mediationGradle = (gradleDeps ?? new List<GradleDep>())
                .Where(d => d.group == MediationGroup).ToList();
            var matched = new HashSet<GradleDep>();

            foreach (var x in xmlDeps)
            {
                var g = mediationGradle.FirstOrDefault(d => d.artifact == x.artifact);
                if (g != null) matched.Add(g);
                rows.Add(new MediationRow
                {
                    network = x.network,
                    packageVersion = x.packageVersion,
                    artifact = x.artifact,
                    xmlVersion = x.version,
                    gradleVersion = g?.version,
                    source = x.source,
                    status = g == null
                        ? MediationStatus.MissingInGradle
                        : g.version == x.version ? MediationStatus.Ok : MediationStatus.VersionMismatch
                });
            }

            foreach (var (network, packageVersion) in upmWithoutXml ?? Enumerable.Empty<(string, string)>())
            {
                rows.Add(new MediationRow
                {
                    network = network, packageVersion = packageVersion, source = "UPM",
                    status = MediationStatus.NoAdapterInfo
                });
            }

            foreach (var g in mediationGradle.Where(d => !matched.Contains(d)))
            {
                rows.Add(new MediationRow
                {
                    network = g.artifact, artifact = g.artifact, gradleVersion = g.version, source = "gradle",
                    status = MediationStatus.ExtraInGradle
                });
            }

            return rows;
        }

        static string NormalizeVersion(string v) => (v ?? "").Trim().Trim('[', ']', '+').Trim();

        // ---------- Scan project thật ----------

        public static MediationReport Scan()
        {
            var report = new MediationReport();
            PackageInfo[] packages;
            try
            {
                packages = PackageInfo.GetAllRegisteredPackages();
            }
            catch (Exception e)
            {
                report.warnings.Add("Không đọc được danh sách package: " + e.Message);
                packages = new PackageInfo[0];
            }

            var core = packages.FirstOrDefault(p => p.name == CorePackage);
            var mediationPkgs = packages.Where(p => p.name.StartsWith(MediationPackagePrefix)).ToList();
            var googlePkgs = packages.Where(p => p.name == CorePackage || p.name.StartsWith(MediationPackagePrefix)).ToList();

            // Dependencies.xml: Assets (bản import .unitypackage) + thư mục các package Google.
            var xmlDeps = new List<XmlDep>();
            var roots = new List<(string root, PackageInfo pkg)> { ("Assets", null) };
            roots.AddRange(googlePkgs.Where(p => Directory.Exists(p.resolvedPath)).Select(p => (p.resolvedPath, p)));
            foreach (var (root, pkg) in roots)
            {
                foreach (var file in SafeEnumerate(root, "*Dependencies.xml"))
                {
                    var norm = file.Replace('\\', '/');
                    var network = NetworkFromPath(norm);
                    if (network == null) continue;
                    string text;
                    try { text = File.ReadAllText(file); }
                    catch { continue; }

                    foreach (var (artifact, version) in ParseDependenciesXml(text))
                    {
                        xmlDeps.Add(new XmlDep
                        {
                            network = network,
                            artifact = artifact,
                            version = version,
                            source = pkg != null ? "UPM" : "Assets",
                            packageVersion = pkg?.version,
                            xmlPath = norm
                        });
                    }
                }
            }

            var upmWithoutXml = mediationPkgs
                .Where(p => !xmlDeps.Any(x => x.source == "UPM" && x.xmlPath.StartsWith(p.resolvedPath.Replace('\\', '/'))))
                .Select(p => (p.name.Substring(MediationPackagePrefix.Length), p.version));

            report.adMobInstalled = core != null || xmlDeps.Count > 0 || Directory.Exists("Assets/GoogleMobileAds");
            report.adMobVersion = core?.version;
            if (!report.adMobInstalled) return report;

            // Gradle
            List<GradleDep> gradleDeps = null;
            if (File.Exists(MainTemplatePath))
            {
                gradleDeps = ParseResolverBlock(File.ReadAllText(MainTemplatePath));
                if (gradleDeps == null)
                    report.warnings.Add("mainTemplate.gradle không có block 'Android Resolver Dependencies' → chạy Force Resolve.");
                else
                    report.gradleSource = "mainTemplate.gradle";
            }
            else
            {
                report.warnings.Add("Chưa bật Custom Main Gradle Template (không có Assets/Plugins/Android/mainTemplate.gradle).");
            }

            if (gradleDeps == null)
            {
                var aars = ParseAarFileNames(SafeEnumerate(AndroidPluginsDir, "*.aar"));
                if (aars.Count > 0)
                {
                    gradleDeps = aars;
                    report.gradleSource = ".aar (Assets/Plugins/Android)";
                }
            }

            foreach (var (group, artifact) in CoreArtifacts)
            {
                var d = gradleDeps?.FirstOrDefault(x => x.group == group && x.artifact == artifact);
                report.core.Add(new CoreLib { name = artifact, version = d?.version });
            }

            report.rows.AddRange(BuildRows(xmlDeps, gradleDeps, upmWithoutXml));
            return report;
        }

        /// <summary>Lấy tên network từ ".../GoogleMobileAds/Mediation/{Network}/Editor/xxxDependencies.xml".</summary>
        static string NetworkFromPath(string path)
        {
            var parts = path.Split('/');
            for (var i = 0; i < parts.Length - 2; i++)
            {
                if (parts[i] == "Mediation" && i > 0 && parts[i - 1] == "GoogleMobileAds" &&
                    parts.Skip(i + 2).Contains("Editor"))
                    return parts[i + 1];
            }

            return null;
        }

        static IEnumerable<string> SafeEnumerate(string root, string pattern)
        {
            if (!Directory.Exists(root)) return Enumerable.Empty<string>();
            try
            {
                return Directory.GetFiles(root, pattern, SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RaccoonBuild] Không quét được {root}: {e.Message}");
                return Enumerable.Empty<string>();
            }
        }

        // ---------- Force Resolve (EDM4U qua reflection) ----------

        public static bool IsResolverAvailable => FindResolverType() != null;

        static Type FindResolverType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("GooglePlayServices.PlayServicesResolver", false);
                if (t != null) return t;
            }

            return null;
        }

        /// <summary>Gọi Force Resolve; onDone chạy trên main thread khi resolve xong.</summary>
        public static bool ForceResolve(Action onDone)
        {
            var type = FindResolverType();
            if (type != null)
            {
                // Resolve(Action resolutionComplete, bool forceResolution, Action<bool> resolutionCompleteWithResult)
                var resolve = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Resolve" && m.GetParameters().Length == 3 &&
                                         m.GetParameters()[1].ParameterType == typeof(bool));
                if (resolve != null)
                {
                    try
                    {
                        Action<bool> cb = _ => EditorApplication.delayCall += () => onDone?.Invoke();
                        resolve.Invoke(null, new object[] { null, true, cb });
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[RaccoonBuild] Gọi PlayServicesResolver.Resolve lỗi: " + e.Message);
                    }
                }

                var resolveSync = type.GetMethod("ResolveSync", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(bool) }, null);
                if (resolveSync != null)
                {
                    try
                    {
                        resolveSync.Invoke(null, new object[] { true });
                        onDone?.Invoke();
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[RaccoonBuild] Gọi PlayServicesResolver.ResolveSync lỗi: " + e.Message);
                    }
                }
            }

            if (EditorApplication.ExecuteMenuItem("Assets/External Dependency Manager/Android Resolver/Force Resolve"))
            {
                EditorApplication.delayCall += () => onDone?.Invoke();
                return true;
            }

            return false;
        }
    }
}
