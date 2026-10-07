using System.Collections.Generic;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Raccoon.BuildEditor
{
    /// <summary>Các bước build dùng chung cho Android và iOS.</summary>
    public static class BuildSteps
    {
        const string Log = "[RaccoonBuild] ";

        /// <summary>
        /// Ads (adpack) + No Ads rule của project: sửa thật + SAVE scene/asset, không restore.
        /// sceneSetup được gán trước khi mở scene khác → caller restore trong finally.
        /// Trả false khi hủy / lỗi (outcome.status + errors đã set).
        /// </summary>
        public static bool ApplyAds(List<NoAdsRule> rules, EffectiveSettings fx, string[] scenes, BuildOutcome outcome,
            ref SceneSetup[] sceneSetup)
        {
            var hasRules = NoAdsRuleApplier.HasActiveRules(rules, fx.noAds);
            if (!AdsSettingApplier.IsAdPackInstalled && !hasRules) return true;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                outcome.cancelled = true;
                outcome.status = "Đã hủy (chưa save scene)";
                return false;
            }

            sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            if (AdsSettingApplier.IsAdPackInstalled)
            {
                var adsErrors = AdsSettingApplier.Apply(fx.ads, scenes, outcome.warnings);
                if (adsErrors.Count > 0)
                {
                    outcome.errors.AddRange(adsErrors);
                    Fail(outcome, "Không set được Ads setting");
                    return false;
                }
            }

            if (hasRules)
            {
                var res = NoAdsRuleApplier.Apply(rules, fx.noAds, scenes, false);
                outcome.warnings.AddRange(res.warnings);
                foreach (var i in res.infos) Debug.Log(Log + "No Ads rule: " + i);
                if (res.errors.Count > 0)
                {
                    outcome.errors.AddRange(res.errors);
                    Fail(outcome, "Không set được No Ads rule");
                    return false;
                }
            }

            return true;
        }

        public static BuildOutcome Fail(BuildOutcome o, string status)
        {
            o.status = status;
            if (o.errors.Count == 0) o.errors.Add(status);
            return o;
        }
    }
}
