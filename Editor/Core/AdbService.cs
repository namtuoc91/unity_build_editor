using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Raccoon.BuildEditor
{
    /// <summary>Đường dẫn SDK/NDK Android của Unity (qua reflection để không phụ thuộc module Android khi compile).</summary>
    public static class AndroidTools
    {
        static Type SettingsType =>
            Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions", false);

        static string GetStatic(string property)
        {
            try
            {
                var p = SettingsType?.GetProperty(property, BindingFlags.Public | BindingFlags.Static);
                return p?.GetValue(null) as string;
            }
            catch
            {
                return null;
            }
        }

        public static string SdkRoot
        {
            get
            {
                var sdk = GetStatic("sdkRootPath");
                if (string.IsNullOrEmpty(sdk)) sdk = EditorPrefs.GetString("AndroidSdkRoot", "");
                if (string.IsNullOrEmpty(sdk)) sdk = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
                if (string.IsNullOrEmpty(sdk)) sdk = Environment.GetEnvironmentVariable("ANDROID_HOME");
                return sdk ?? "";
            }
        }

        public static string NdkRoot => GetStatic("ndkRootPath") ?? "";

        public static bool NdkFound
        {
            get
            {
                var ndk = NdkRoot;
                return !string.IsNullOrEmpty(ndk) && Directory.Exists(ndk);
            }
        }

        public static string AdbPath
        {
            get
            {
                var sdk = SdkRoot;
                if (string.IsNullOrEmpty(sdk)) return null;
                var exe = Application_IsWindows ? "adb.exe" : "adb";
                var path = Path.Combine(sdk, "platform-tools", exe);
                return File.Exists(path) ? path : null;
            }
        }

        static bool Application_IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;
    }

    public class AdbDevice
    {
        public string serial;
        public string state;
        public string model;

        public string Label => string.IsNullOrEmpty(model) ? $"{serial} ({state})" : $"{model} — {serial} ({state})";
        public bool IsReady => state == "device";
    }

    /// <summary>Chạy adb async; callback luôn được gọi lại trên main thread.</summary>
    [InitializeOnLoad]
    public static class AdbService
    {
        static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();

        static AdbService()
        {
            EditorApplication.update += Pump;
        }

        static void Pump()
        {
            while (MainThread.TryDequeue(out var a))
            {
                try
                {
                    a();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public static bool IsAvailable => AndroidTools.AdbPath != null;

        public static void Run(string args, Action<int, string, string> onDone)
        {
            var adb = AndroidTools.AdbPath;
            if (adb == null)
            {
                onDone?.Invoke(-1, "", "Không tìm thấy adb trong Android SDK của Unity.");
                return;
            }

            Task.Run(() =>
            {
                int code;
                string stdout, stderr;
                try
                {
                    using var p = new Process
                    {
                        StartInfo = new ProcessStartInfo(adb, args)
                        {
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    p.Start();
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    var errTask = p.StandardError.ReadToEndAsync();
                    p.WaitForExit();
                    stdout = outTask.Result;
                    stderr = errTask.Result;
                    code = p.ExitCode;
                }
                catch (Exception e)
                {
                    code = -1;
                    stdout = "";
                    stderr = e.Message;
                }

                MainThread.Enqueue(() => onDone?.Invoke(code, stdout, stderr));
            });
        }

        public static List<AdbDevice> ParseDevices(string output)
        {
            var list = new List<AdbDevice>();
            foreach (var raw in (output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("List of devices") || line.StartsWith("*")) continue;
                var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var model = parts.FirstOrDefault(x => x.StartsWith("model:"))?.Substring("model:".Length);
                list.Add(new AdbDevice { serial = parts[0], state = parts[1], model = model?.Replace('_', ' ') });
            }

            return list;
        }

        public static void ListDevices(Action<List<AdbDevice>, string> onDone)
        {
            Run("devices -l", (code, output, err) =>
                onDone?.Invoke(code == 0 ? ParseDevices(output) : new List<AdbDevice>(), code == 0 ? null : err));
        }

        public static void Install(string serial, string apkPath, Action<bool, string> onDone)
        {
            // -d: cho phép version code thấp hơn bản đang cài (chỉ được khi bản cũ debuggable).
            Run($"-s {serial} install -r -d \"{apkPath}\"", (code, output, err) =>
            {
                var ok = code == 0 && output.Contains("Success");
                onDone?.Invoke(ok, ok ? output.Trim() : (err + "\n" + output).Trim());
            });
        }

        public static void Launch(string serial, string appId, Action<bool, string> onDone)
        {
            Run($"-s {serial} shell monkey -p {appId} -c android.intent.category.LAUNCHER 1", (code, output, err) =>
            {
                var ok = code == 0 && !output.Contains("No activities found");
                onDone?.Invoke(ok, ok ? "Đã launch " + appId : (err + "\n" + output).Trim());
            });
        }

        public static void Uninstall(string serial, string appId, Action<bool, string> onDone)
        {
            Run($"-s {serial} uninstall {appId}", (code, output, err) =>
            {
                var ok = code == 0 && output.Contains("Success");
                onDone?.Invoke(ok, ok ? "Đã uninstall " + appId : (err + "\n" + output).Trim());
            });
        }

        /// <summary>Lỗi chỉ sửa được bằng cách gỡ bản cũ: version code thấp hơn hoặc khác keystore.</summary>
        public static bool NeedsUninstall(string installError) =>
            installError != null &&
            (installError.Contains("INSTALL_FAILED_VERSION_DOWNGRADE") ||
             installError.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE"));

        public static void InstallAndLaunch(string serial, string apkPath, string appId, Action<bool, string> onDone)
        {
            Debug.Log($"[RaccoonBuild] adb install → {serial}: {apkPath}");
            Install(serial, apkPath, (ok, msg) =>
            {
                if (ok)
                {
                    LaunchAfterInstall(serial, appId, onDone);
                    return;
                }

                if (NeedsUninstall(msg) && !string.IsNullOrEmpty(appId))
                {
                    var reason = msg.Contains("VERSION_DOWNGRADE")
                        ? "Máy đang có bản version code cao hơn."
                        : "Máy đang có bản ký bằng keystore khác.";
                    if (EditorUtility.DisplayDialog("Không cài đè được",
                            $"{reason}\n\nUninstall {appId} (mất data app) rồi cài lại?", "Uninstall + cài lại", "Hủy"))
                    {
                        Uninstall(serial, appId, (okU, msgU) =>
                        {
                            if (!okU)
                            {
                                Debug.LogError("[RaccoonBuild] Uninstall lỗi: " + msgU);
                                onDone?.Invoke(false, msgU);
                                return;
                            }

                            Debug.Log("[RaccoonBuild] " + msgU);
                            Install(serial, apkPath, (ok2, msg2) =>
                            {
                                if (ok2) LaunchAfterInstall(serial, appId, onDone);
                                else Fail(msg2, onDone);
                            });
                        });
                        return;
                    }
                }

                Fail(msg, onDone);
            });
        }

        static void Fail(string msg, Action<bool, string> onDone)
        {
            Debug.LogError($"[RaccoonBuild] Install lỗi: {msg}");
            onDone?.Invoke(false, msg);
        }

        static void LaunchAfterInstall(string serial, string appId, Action<bool, string> onDone)
        {
            Launch(serial, appId, (ok, msg) =>
            {
                if (ok) Debug.Log("[RaccoonBuild] " + msg);
                else Debug.LogWarning("[RaccoonBuild] Launch lỗi: " + msg);
                onDone?.Invoke(ok, ok ? "Đã cài + launch." : msg);
            });
        }
    }
}
