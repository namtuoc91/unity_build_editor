using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Raccoon.BuildEditor
{
    /// <summary>Helper UI dùng chung cho Android Build và iOS Build.</summary>
    public static class BuildWindowUtil
    {
        public static bool Section(bool open, string title, System.Action draw)
        {
            open = EditorGUILayout.BeginFoldoutHeaderGroup(open, title);
            if (open)
            {
                EditorGUI.indentLevel++;
                draw();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
            return open;
        }

        public static void DrawValidationList(List<ValidationMessage> validation)
        {
            if (validation.Count == 0)
            {
                EditorGUILayout.HelpBox("OK — sẵn sàng build.", MessageType.Info);
                return;
            }

            foreach (var m in validation)
            {
                var type = m.severity == Severity.Error ? MessageType.Error
                    : m.severity == Severity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(m.text, type);
            }
        }

        public static string ToProjectRelative(string absolute)
        {
            var root = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            var abs = absolute.Replace('\\', '/');
            return abs.StartsWith(root) ? abs.Substring(root.Length) : abs;
        }

        public static void RevealFolder(string folder)
        {
            var full = Path.GetFullPath(string.IsNullOrEmpty(folder) ? "." : folder);
            Directory.CreateDirectory(full);
            EditorUtility.RevealInFinder(full);
        }
    }
}
