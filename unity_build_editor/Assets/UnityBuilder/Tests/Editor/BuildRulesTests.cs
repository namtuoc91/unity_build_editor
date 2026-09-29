using System;
using NUnit.Framework;
using UnityEditor;

namespace Raccoon.BuildEditor.Tests
{
    public class BuildRulesTests
    {
        // ---- Architecture / format theo Mode ----

        [Test]
        public void Dev_IsApk_Arm64Only()
        {
            Assert.IsFalse(BuildRules.IsAab(BuildMode.Dev));
            Assert.AreEqual("apk", BuildRules.Extension(BuildMode.Dev));
            Assert.AreEqual(AndroidArchitecture.ARM64, BuildRules.Architectures(BuildMode.Dev));
        }

        [Test]
        public void Release_IsAab_Armv7AndArm64()
        {
            Assert.IsTrue(BuildRules.IsAab(BuildMode.Release));
            Assert.AreEqual("aab", BuildRules.Extension(BuildMode.Release));
            Assert.AreEqual(AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64, BuildRules.Architectures(BuildMode.Release));
        }

        [Test]
        public void Release_ForcesDevBuildAndAdsOff()
        {
            var p = new BuildPreset
            {
                mode = BuildMode.Release, developmentBuild = true, scriptDebugging = true,
                autoconnectProfiler = true, noAds = true, useTestAd = true
            };
            var fx = BuildRules.Resolve(p);
            Assert.IsFalse(fx.development);
            Assert.IsFalse(fx.scriptDebugging);
            Assert.IsFalse(fx.autoconnectProfiler);
            Assert.IsFalse(fx.noAds);

            BuildRules.EnforceModeLocks(p);
            Assert.IsFalse(p.developmentBuild || p.scriptDebugging || p.autoconnectProfiler || p.noAds || p.useTestAd);
        }

        [Test]
        public void Dev_DebuggingRequiresDevelopmentBuild()
        {
            var p = new BuildPreset { mode = BuildMode.Dev, developmentBuild = false, scriptDebugging = true, autoconnectProfiler = true };
            var fx = BuildRules.Resolve(p);
            Assert.IsFalse(fx.scriptDebugging);
            Assert.IsFalse(fx.autoconnectProfiler);

            p.developmentBuild = true;
            fx = BuildRules.Resolve(p);
            Assert.IsTrue(fx.development && fx.scriptDebugging && fx.autoconnectProfiler);
        }

        // ---- Ads setting theo Mode ----

        [Test]
        public void Ads_Release_ForcesCreativeOffAndTestAdOff()
        {
            var t = BuildRules.ResolveAds(BuildMode.Release, true, true);
            Assert.IsFalse(t.creativeMode);
            Assert.AreEqual(false, t.useTestAd);
        }

        [Test]
        public void Ads_DevNoAds_CreativeOn_DoesNotTouchTestAd()
        {
            var t = BuildRules.ResolveAds(BuildMode.Dev, true, true);
            Assert.IsTrue(t.creativeMode);
            Assert.IsNull(t.useTestAd);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Ads_DevWithAds_CreativeOff_TestAdFollowsCheckbox(bool useTestAd)
        {
            var t = BuildRules.ResolveAds(BuildMode.Dev, false, useTestAd);
            Assert.IsFalse(t.creativeMode);
            Assert.AreEqual(useTestAd, t.useTestAd);
        }

        // ---- Version code ----

        [TestCase(5, 3, 5)]
        [TestCase(3, 7, 7)]
        [TestCase(0, 0, 1)]
        public void VersionCode_CurrentIsMax(int config, int player, int expected)
        {
            Assert.AreEqual(expected, BuildRules.CurrentVersionCode(config, player));
        }

        [Test]
        public void VersionCode_Increment()
        {
            Assert.AreEqual(11, BuildRules.NextVersionCode(10, true));
            Assert.AreEqual(10, BuildRules.NextVersionCode(10, false));
        }

        [Test]
        public void DefaultPresets_ReleaseAutoIncrement_DevNot()
        {
            Assert.IsTrue(BuildPreset.CreateDefaultRelease("com.a.b").autoIncrement);
            Assert.IsFalse(BuildPreset.CreateDefaultDev("com.a.b").autoIncrement);
        }

        // ---- Tên file ----

        [Test]
        public void FileName_DefaultTemplate()
        {
            var t = new DateTime(2026, 9, 29, 13, 5, 9);
            var name = BuildRules.FormatFileName(BuildPreset.DefaultFileNameTemplate, "My Game", "1.2.0", 42, t, BuildMode.Dev, true);
            Assert.AreEqual("My_Game_1.2.0_42_20260929_130509_dev_noads", name);
        }

        [Test]
        public void FileName_ReleaseWithoutNoAds()
        {
            var t = new DateTime(2026, 1, 2, 3, 4, 5);
            var name = BuildRules.FormatFileName("{product}-{code}-{mode}{noads}", "Game", "1.0", 7, t, BuildMode.Release, false);
            Assert.AreEqual("Game-7-release", name);
        }

        [Test]
        public void FileName_SanitizesInvalidChars()
        {
            var name = BuildRules.FormatFileName("{product}", "A/B:C", "1", 1, DateTime.Now, BuildMode.Dev, false);
            Assert.AreEqual("A_B_C", name);
        }

        // ---- App ID ----

        [TestCase("com.company.game", true)]
        [TestCase("com.company.game_2", true)]
        [TestCase("game", false)]
        [TestCase("com..game", false)]
        [TestCase("com.1game", false)]
        [TestCase("", false)]
        public void AppId_Validation(string id, bool valid)
        {
            Assert.AreEqual(valid, BuildRules.IsValidAppId(id));
        }
    }
}
