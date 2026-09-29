using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Raccoon.BuildEditor
{
    public class NoAdsRuleResult
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> infos = new List<string>();
    }

    /// <summary>
    /// Áp No Ads rule của project: mở từng scene build list, set field qua SerializedObject rồi SAVE scene
    /// (giống AdsSettingApplier, không restore). Caller chịu trách nhiệm lưu/khôi phục scene setup.
    /// </summary>
    public static class NoAdsRuleApplier
    {
        // ---- Luật thuần (test được) ----

        /// <summary>Giá trị cần ghi theo cờ No Ads; null = không đụng.</summary>
        public static string ValueFor(NoAdsRule r, bool noAds)
        {
            var v = noAds ? r.noAdsValue : r.adsValue;
            return string.IsNullOrEmpty(v) ? null : v;
        }

        public static bool IsActive(NoAdsRule r, bool noAds) => r != null && r.enabled && ValueFor(r, noAds) != null;

        public static bool HasActiveRules(IEnumerable<NoAdsRule> rules, bool noAds) =>
            rules != null && rules.Any(r => IsActive(r, noAds));

        public static bool AppliesToScene(NoAdsRule r, string scenePath) =>
            string.IsNullOrEmpty(r.scenePath) || r.scenePath == scenePath;

        public static bool TargetsGameObject(NoAdsRule r) =>
            string.IsNullOrEmpty(r.componentType) || r.componentType == NoAdsRule.GameObjectType;

        public static string NormalizePath(string path) =>
            string.Join("/", (path ?? "").Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()));

        public static string Describe(NoAdsRule r) =>
            $"{NormalizePath(r.objectPath)} › {(TargetsGameObject(r) ? NoAdsRule.GameObjectType : r.componentType)}.{r.property}";

        public static bool TryParseBool(string text, out bool value)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "true":
                case "1":
                case "on":
                case "yes":
                    value = true;
                    return true;
                case "false":
                case "0":
                case "off":
                case "no":
                    value = false;
                    return true;
                default:
                    value = false;
                    return false;
            }
        }

        /// <summary>Tìm index enum theo tên / tên hiển thị (không phân biệt hoa thường) hoặc số index. -1 nếu không có.</summary>
        public static int EnumIndex(string[] names, string[] displayNames, string text)
        {
            var t = (text ?? "").Trim();
            for (var i = 0; i < names.Length; i++)
                if (string.Equals(names[i], t, StringComparison.OrdinalIgnoreCase)) return i;
            if (displayNames != null)
                for (var i = 0; i < displayNames.Length; i++)
                    if (string.Equals(displayNames[i], t, StringComparison.OrdinalIgnoreCase)) return i;
            return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx) &&
                   idx >= 0 && idx < names.Length
                ? idx
                : -1;
        }

        public static bool IsSupported(SerializedPropertyType t) =>
            t == SerializedPropertyType.Boolean || t == SerializedPropertyType.Integer ||
            t == SerializedPropertyType.Float || t == SerializedPropertyType.String ||
            t == SerializedPropertyType.Enum;

        // ---- SerializedProperty ----

        /// <summary>
        /// Parse text theo type của property. apply = false chỉ kiểm tra; changed = giá trị hiện tại khác giá trị đích.
        /// </summary>
        public static bool TrySet(SerializedProperty p, string text, bool apply, out bool changed, out string error)
        {
            changed = false;
            error = null;
            var inv = CultureInfo.InvariantCulture;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    if (!TryParseBool(text, out var b)) return Bad(out error, $"'{text}' không phải bool (true/false).");
                    changed = p.boolValue != b;
                    if (apply) p.boolValue = b;
                    return true;
                case SerializedPropertyType.Integer:
                    if (!long.TryParse(text.Trim(), NumberStyles.Integer, inv, out var l))
                        return Bad(out error, $"'{text}' không phải số nguyên.");
                    changed = p.longValue != l;
                    if (apply) p.longValue = l;
                    return true;
                case SerializedPropertyType.Float:
                    if (!double.TryParse(text.Trim(), NumberStyles.Float, inv, out var d))
                        return Bad(out error, $"'{text}' không phải số thực (dùng dấu chấm).");
                    changed = Math.Abs(p.doubleValue - d) > 1e-6;
                    if (apply) p.doubleValue = d;
                    return true;
                case SerializedPropertyType.String:
                    changed = p.stringValue != text;
                    if (apply) p.stringValue = text;
                    return true;
                case SerializedPropertyType.Enum:
                    var idx = EnumIndex(p.enumNames, p.enumDisplayNames, text);
                    if (idx < 0) return Bad(out error, $"'{text}' không có trong enum ({string.Join(", ", p.enumNames)}).");
                    changed = p.enumValueIndex != idx;
                    if (apply) p.enumValueIndex = idx;
                    return true;
                default:
                    return Bad(out error, $"Chưa hỗ trợ type {p.propertyType} (chỉ bool/int/float/string/enum).");
            }
        }

        static bool Bad(out string error, string msg)
        {
            error = msg;
            return false;
        }

        public static string ValueToString(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Float: return p.doubleValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length
                        ? p.enumNames[p.enumValueIndex]
                        : p.enumValueIndex.ToString();
                default: return "?";
            }
        }

        // ---- Tìm target ----

        public static string HierarchyPath(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Add(t.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Mọi Object (GameObject hoặc Component) khớp rule trong scene; missingComponent = có object nhưng không có component.</summary>
        public static List<Object> FindTargets(Scene scene, NoAdsRule r, out bool missingComponent)
        {
            missingComponent = false;
            var path = NormalizePath(r.objectPath);
            var result = new List<Object>();
            var gos = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .Where(t => HierarchyPath(t) == path)
                .Select(t => t.gameObject);

            foreach (var go in gos)
            {
                if (TargetsGameObject(r))
                {
                    result.Add(go);
                    continue;
                }

                var comps = go.GetComponents<Component>()
                    .Where(c => c != null && (c.GetType().FullName == r.componentType || c.GetType().Name == r.componentType))
                    .ToList();
                if (comps.Count == 0) missingComponent = true;
                result.AddRange(comps);
            }

            return result;
        }

        public class PropInfo
        {
            public string path;
            public string label;
            public SerializedPropertyType type;
            public string[] enumNames = new string[0];
        }

        /// <summary>Property hỗ trợ được trên target (cho dropdown UI).</summary>
        public static List<PropInfo> ListProperties(Object target)
        {
            var list = new List<PropInfo>();
            var so = new SerializedObject(target);
            var it = so.GetIterator();
            var enter = true;
            while (it.NextVisible(enter))
            {
                enter = false;
                if (it.propertyPath == "m_Script" || !IsSupported(it.propertyType)) continue;
                list.Add(new PropInfo
                {
                    path = it.propertyPath,
                    label = $"{it.displayName} ({it.propertyType})",
                    type = it.propertyType,
                    enumNames = it.propertyType == SerializedPropertyType.Enum ? it.enumNames : new string[0]
                });
            }

            if (target is GameObject && list.All(x => x.path != "m_IsActive"))
                list.Insert(0, new PropInfo { path = "m_IsActive", label = "Active (Boolean)", type = SerializedPropertyType.Boolean });
            return list;
        }

        // ---- Apply ----

        /// <summary>
        /// dryRun = true: mở scene, kiểm tra rule resolve được + parse được, ghi giá trị hiện tại → không sửa/không save.
        /// </summary>
        public static NoAdsRuleResult Apply(IList<NoAdsRule> rules, bool noAds, string[] scenePaths, bool dryRun)
        {
            var res = new NoAdsRuleResult();
            var active = (rules ?? new List<NoAdsRule>()).Where(r => IsActive(r, noAds)).ToList();
            if (active.Count == 0) return res;

            var hits = active.ToDictionary(r => r, _ => 0);
            foreach (var r in active)
            {
                if (string.IsNullOrWhiteSpace(r.objectPath) || string.IsNullOrWhiteSpace(r.property))
                    res.errors.Add($"No Ads rule '{Describe(r)}': thiếu Object path hoặc Property.");
                else if (!string.IsNullOrEmpty(r.scenePath) && !scenePaths.Contains(r.scenePath))
                    res.warnings.Add($"No Ads rule '{Describe(r)}': scene {r.scenePath} không nằm trong build list → bỏ qua.");
            }

            var valid = active.Where(r => !string.IsNullOrWhiteSpace(r.objectPath) && !string.IsNullOrWhiteSpace(r.property)).ToList();

            foreach (var path in scenePaths)
            {
                var sceneRules = valid.Where(r => AppliesToScene(r, path)).ToList();
                if (sceneRules.Count == 0) continue;

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var sceneOk = true;
                var changed = false;

                foreach (var r in sceneRules)
                {
                    var value = ValueFor(r, noAds);
                    var targets = FindTargets(scene, r, out var missingComp);
                    if (targets.Count == 0 && missingComp)
                    {
                        res.errors.Add($"{path}: '{NormalizePath(r.objectPath)}' không có component {r.componentType}.");
                        sceneOk = false;
                        hits[r]++; // đã thấy object, không báo "không tìm thấy" nữa
                        continue;
                    }

                    foreach (var t in targets)
                    {
                        hits[r]++;
                        var so = new SerializedObject(t);
                        var prop = so.FindProperty(r.property);
                        if (prop == null)
                        {
                            res.errors.Add($"{path}: '{Describe(r)}' không tìm thấy property '{r.property}'.");
                            sceneOk = false;
                            continue;
                        }

                        var before = ValueToString(prop);
                        if (!TrySet(prop, value, !dryRun, out var diff, out var err))
                        {
                            res.errors.Add($"{path}: '{Describe(r)}': {err}");
                            sceneOk = false;
                            continue;
                        }

                        if (dryRun)
                        {
                            res.infos.Add($"{path}: {Describe(r)} = {before}{(diff ? $" → sẽ set {value}" : " (đúng rồi)")}");
                            continue;
                        }

                        if (!diff) continue;
                        so.ApplyModifiedPropertiesWithoutUndo();
                        changed = true;

                        so.Update();
                        var check = so.FindProperty(r.property);
                        if (check == null || !TrySet(check, value, false, out var still, out _) || still)
                        {
                            res.errors.Add($"{path}: set '{Describe(r)}' = {value} nhưng đọc lại sai.");
                            sceneOk = false;
                        }
                        else
                        {
                            res.infos.Add($"{path}: {Describe(r)}: {before} → {value}");
                        }
                    }
                }

                if (!dryRun && sceneOk && changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene))
                        res.errors.Add($"{path}: SaveScene thất bại.");
                }
            }

            foreach (var r in valid.Where(r => hits[r] == 0 && (string.IsNullOrEmpty(r.scenePath) || scenePaths.Contains(r.scenePath))))
                res.errors.Add($"No Ads rule: không tìm thấy object '{NormalizePath(r.objectPath)}'" +
                               (string.IsNullOrEmpty(r.scenePath) ? " trong scene build list." : $" trong {r.scenePath}."));

            return res;
        }
    }
}
