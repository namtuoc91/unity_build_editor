using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Raccoon.BuildEditor
{
    /// <summary>
    /// Đọc/ghi icon app vào PlayerSettings: set Default Icon và clear mọi override Android
    /// (Adaptive/Round/Legacy) để Android dùng Default Icon.
    /// </summary>
    public static class AppIconService
    {
        public const int RecommendedSize = 512;

        public static Texture2D GetDefaultIcon()
        {
            var icons = PlayerSettings.GetIcons(NamedBuildTarget.Unknown, IconKind.Any);
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }

        public static void SetIcon(Texture2D icon)
        {
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            ClearAndroidOverrides();

            AssetDatabase.SaveAssets();
        }

        /// <summary>Set null mọi layer của mọi icon Android (Adaptive/Round/Legacy, mọi size).</summary>
        public static void ClearAndroidOverrides()
        {
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                if (icons == null || icons.Length == 0) continue;
                foreach (var i in icons)
                {
                    for (var layer = 0; layer < i.maxLayerCount; layer++) i.SetTexture(null, layer);
                }

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
        }

        /// <summary>Còn override Android nào đang có texture (sẽ che Default Icon).</summary>
        public static bool HasAndroidOverrides()
        {
            return PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android)
                .Select(k => PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, k))
                .Where(icons => icons != null)
                .Any(icons => icons.Any(i => i.GetTextures().Any(t => t != null)));
        }

        /// <summary>Trả về cảnh báo cho texture icon, null nếu ổn.</summary>
        public static string CheckTexture(Texture2D tex)
        {
            if (tex == null) return null;
            if (tex.width != tex.height) return $"Icon không vuông ({tex.width}x{tex.height}).";
            if (tex.width < RecommendedSize) return $"Icon nhỏ ({tex.width}px), nên ≥ {RecommendedSize}px.";
            return null;
        }
    }
}
