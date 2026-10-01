using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Raccoon.BuildEditor
{
    /// <summary>
    /// Ghi setting ads của com.raccoon.adpack mà không reference assembly: tìm type theo tên,
    /// sửa qua SerializedObject rồi SAVE thật (scene + asset). Không restore sau build.
    /// </summary>
    public static class AdsSettingApplier
    {
        public const string ManagerTypeName = "RCKit.Ads.Main.RaccoonAdsManager";
        public const string DataTypeName = "RCKit.Ads.Main.AdsScriptableObj";
        public const string CreativeModeField = "_creativeMode";
        public const string AdsDataField = "adsData";
        public const string UseTestAdField = "use_test_ad";
        public const string DefaultAdsAssetPath = "Assets/RaccoonAds/Resources/RaccoonAdUnit.asset";

        static Type _managerType;
        static bool _searched;

        public static Type ManagerType
        {
            get
            {
                if (!_searched)
                {
                    _searched = true;
                    _managerType = FindType(ManagerTypeName);
                }

                return _managerType;
            }
        }

        public static bool IsAdPackInstalled => ManagerType != null;

        static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }

            return null;
        }

        /// <summary>
        /// Mở từng scene trong build list, set _creativeMode trên mọi RaccoonAdsManager → SaveScene;
        /// nếu target.useTestAd có giá trị thì set use_test_ad trên asset adsData → SetDirty + SaveAssets.
        /// Caller chịu trách nhiệm lưu/khôi phục scene setup. Trả về danh sách lỗi (rỗng = OK).
        /// </summary>
        public static List<string> Apply(AdsTarget target, IEnumerable<string> scenePaths, List<string> warnings)
        {
            var errors = new List<string>();
            var type = ManagerType;
            if (type == null) return errors;

            // Lưu path chứ không giữ reference: OpenScene(Single) unload asset không dùng → reference thành fake-null.
            var assetPaths = new HashSet<string>();
            var managerCount = 0;

            foreach (var path in scenePaths)
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var managers = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren(type, true))
                    .ToList();
                if (managers.Count == 0) continue;

                var sceneOk = true;
                var changed = false;
                foreach (var m in managers)
                {
                    managerCount++;
                    var so = new SerializedObject(m);
                    var creative = so.FindProperty(CreativeModeField);
                    if (creative == null || creative.propertyType != SerializedPropertyType.Boolean)
                    {
                        errors.Add($"{path}: không tìm thấy field bool '{CreativeModeField}' trên {ManagerTypeName} (adpack đổi tên?).");
                        sceneOk = false;
                        continue;
                    }

                    if (target.useTestAd.HasValue)
                    {
                        var data = so.FindProperty(AdsDataField);
                        if (data == null || data.propertyType != SerializedPropertyType.ObjectReference)
                        {
                            errors.Add($"{path}: không tìm thấy field '{AdsDataField}' trên {ManagerTypeName}.");
                            sceneOk = false;
                        }
                        else if (data.objectReferenceValue == null)
                        {
                            errors.Add($"{path}: RaccoonAdsManager '{m.gameObject.name}' có adsData = null.");
                            sceneOk = false;
                        }
                        else
                        {
                            var assetPath = AssetDatabase.GetAssetPath(data.objectReferenceValue);
                            if (string.IsNullOrEmpty(assetPath))
                            {
                                errors.Add($"{path}: adsData của '{m.gameObject.name}' không phải asset trong project.");
                                sceneOk = false;
                            }
                            else
                            {
                                assetPaths.Add(assetPath);
                            }
                        }
                    }

                    if (creative.boolValue != target.creativeMode)
                    {
                        creative.boolValue = target.creativeMode;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;
                    }

                    so.Update();
                    if (so.FindProperty(CreativeModeField).boolValue != target.creativeMode)
                    {
                        errors.Add($"{path}: set {CreativeModeField} = {target.creativeMode} nhưng đọc lại sai.");
                        sceneOk = false;
                    }
                }

                if (sceneOk && changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                        errors.Add($"{path}: SaveScene thất bại.");
                }
            }

            if (managerCount == 0)
                warnings?.Add("Không tìm thấy RaccoonAdsManager nào trong các scene build list.");

            if (!target.useTestAd.HasValue) return errors;

            if (File.Exists(DefaultAdsAssetPath))
            {
                var def = AssetDatabase.LoadMainAssetAtPath(DefaultAdsAssetPath);
                if (def != null && def.GetType().FullName == DataTypeName) assetPaths.Add(DefaultAdsAssetPath);
            }

            var assets = new List<Object>();
            foreach (var assetPath in assetPaths)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset == null) errors.Add($"{assetPath}: không load được asset adsData.");
                else assets.Add(asset);
            }

            var value = target.useTestAd.Value;
            foreach (var asset in assets)
            {
                var so = new SerializedObject(asset);
                var prop = so.FindProperty(UseTestAdField);
                if (prop == null || prop.propertyType != SerializedPropertyType.Boolean)
                {
                    errors.Add($"{AssetDatabase.GetAssetPath(asset)}: không tìm thấy field bool '{UseTestAdField}' (adpack đổi tên?).");
                    continue;
                }

                if (prop.boolValue != value)
                {
                    prop.boolValue = value;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(asset);
                }
            }

            AssetDatabase.SaveAssets();

            foreach (var asset in assets)
            {
                var prop = new SerializedObject(asset).FindProperty(UseTestAdField);
                if (prop != null && prop.boolValue != value)
                    errors.Add($"{AssetDatabase.GetAssetPath(asset)}: set {UseTestAdField} = {value} nhưng đọc lại sai.");
            }

            return errors;
        }
    }
}
