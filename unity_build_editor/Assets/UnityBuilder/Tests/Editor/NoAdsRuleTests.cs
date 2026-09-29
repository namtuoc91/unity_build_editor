using System.Collections.Generic;
using NUnit.Framework;

namespace Raccoon.BuildEditor.Tests
{
    public class NoAdsRuleTests
    {
        static NoAdsRule Rule(string noAds = "true", string ads = "false") => new NoAdsRule
        {
            objectPath = "Canvas/Shop/BtnRemoveAds", componentType = "ShopButton", property = "_hide",
            noAdsValue = noAds, adsValue = ads
        };

        [Test]
        public void ValueFor_PicksByFlag_EmptyIsNull()
        {
            var r = Rule("true", "");
            Assert.AreEqual("true", NoAdsRuleApplier.ValueFor(r, true));
            Assert.IsNull(NoAdsRuleApplier.ValueFor(r, false));
        }

        [Test]
        public void IsActive_RequiresEnabledAndValue()
        {
            var r = Rule("true", "");
            Assert.IsTrue(NoAdsRuleApplier.IsActive(r, true));
            Assert.IsFalse(NoAdsRuleApplier.IsActive(r, false));
            r.enabled = false;
            Assert.IsFalse(NoAdsRuleApplier.IsActive(r, true));
            Assert.IsFalse(NoAdsRuleApplier.HasActiveRules(new List<NoAdsRule> { r }, true));
        }

        [Test]
        public void AppliesToScene_EmptyMeansAll()
        {
            var r = Rule();
            Assert.IsTrue(NoAdsRuleApplier.AppliesToScene(r, "Assets/A.unity"));
            r.scenePath = "Assets/B.unity";
            Assert.IsFalse(NoAdsRuleApplier.AppliesToScene(r, "Assets/A.unity"));
            Assert.IsTrue(NoAdsRuleApplier.AppliesToScene(r, "Assets/B.unity"));
        }

        [Test]
        public void TargetsGameObject_EmptyOrKeyword()
        {
            var r = Rule();
            Assert.IsFalse(NoAdsRuleApplier.TargetsGameObject(r));
            r.componentType = "";
            Assert.IsTrue(NoAdsRuleApplier.TargetsGameObject(r));
            r.componentType = NoAdsRule.GameObjectType;
            Assert.IsTrue(NoAdsRuleApplier.TargetsGameObject(r));
        }

        [Test]
        public void NormalizePath_TrimsSlashesAndSpaces()
        {
            Assert.AreEqual("Canvas/Shop/Btn", NoAdsRuleApplier.NormalizePath("/Canvas/ Shop //Btn/"));
            Assert.AreEqual("", NoAdsRuleApplier.NormalizePath(null));
        }

        [TestCase("true", true)]
        [TestCase("TRUE", true)]
        [TestCase("1", true)]
        [TestCase("on", true)]
        [TestCase("false", false)]
        [TestCase(" 0 ", false)]
        [TestCase("off", false)]
        public void TryParseBool_Accepts(string text, bool expected)
        {
            Assert.IsTrue(NoAdsRuleApplier.TryParseBool(text, out var v));
            Assert.AreEqual(expected, v);
        }

        [TestCase("")]
        [TestCase("maybe")]
        [TestCase(null)]
        public void TryParseBool_Rejects(string text)
        {
            Assert.IsFalse(NoAdsRuleApplier.TryParseBool(text, out _));
        }

        [Test]
        public void EnumIndex_ByNameDisplayOrIndex()
        {
            var names = new[] { "None", "HideShop", "HideAll" };
            var display = new[] { "None", "Hide Shop", "Hide All" };
            Assert.AreEqual(1, NoAdsRuleApplier.EnumIndex(names, display, "hideshop"));
            Assert.AreEqual(2, NoAdsRuleApplier.EnumIndex(names, display, "Hide All"));
            Assert.AreEqual(0, NoAdsRuleApplier.EnumIndex(names, display, "0"));
            Assert.AreEqual(-1, NoAdsRuleApplier.EnumIndex(names, display, "5"));
            Assert.AreEqual(-1, NoAdsRuleApplier.EnumIndex(names, display, "Other"));
        }

        // ---- Validator ----

        static ValidationInput Input(params NoAdsRule[] rules) => new ValidationInput
        {
            preset = new BuildPreset { name = "P", appId = "com.raccoon.game", mode = BuildMode.Dev },
            version = "1.0.0",
            scenePaths = new[] { "Assets/Scenes/Main.unity" },
            noAdsRules = new List<NoAdsRule>(rules)
        };

        [Test]
        public void Validator_RuleMissingProperty_IsError()
        {
            var r = Rule();
            r.property = "";
            Assert.IsTrue(BuildValidator.HasErrors(BuildValidator.Validate(Input(r))));
        }

        [Test]
        public void Validator_DisabledIncompleteRule_Ignored()
        {
            var r = Rule();
            r.property = "";
            r.enabled = false;
            Assert.IsFalse(BuildValidator.HasErrors(BuildValidator.Validate(Input(r))));
        }

        [Test]
        public void Validator_SceneOutsideBuildList_IsWarning()
        {
            var r = Rule();
            r.scenePath = "Assets/Scenes/Other.unity";
            var list = BuildValidator.Validate(Input(r));
            Assert.IsFalse(BuildValidator.HasErrors(list));
            Assert.IsTrue(list.Exists(m => m.severity == Severity.Warning && m.text.Contains("Other.unity")));
        }

        [Test]
        public void Validator_NoAdsWithoutAdPack_WarnsOnlyWhenNoRules()
        {
            var i = Input();
            i.preset.noAds = true;
            Assert.IsTrue(BuildValidator.Validate(i).Exists(m => m.text.Contains("không có tác dụng")));
            i.noAdsRules.Add(Rule());
            Assert.IsFalse(BuildValidator.Validate(i).Exists(m => m.text.Contains("No Ads không có tác dụng")));
        }
    }
}
