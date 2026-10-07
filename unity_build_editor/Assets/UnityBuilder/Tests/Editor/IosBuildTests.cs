using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Raccoon.BuildEditor.Tests
{
    public class IosBuildTests
    {
        // ---- Podfile: pod FB sang target Unity-iPhone ----

        const string EdmPodfile =
            "source 'https://cdn.cocoapods.org/'\n\n" +
            "platform :ios, '13.0'\n\n" +
            "target 'UnityFramework' do\n" +
            "  pod 'FBSDKCoreKit', '~> 17.0'\n" +
            "  pod 'FBSDKCoreKit_Basics', '~> 17.0'\n" +
            "  pod 'FBAEMKit', '~> 17.0'\n" +
            "  pod 'Google-Mobile-Ads-SDK', '~> 11.0'\n" +
            "end\n" +
            "target 'Unity-iPhone' do\n" +
            "end\n" +
            "use_frameworks! :linkage => :static\n";

        static readonly string[] FbPrefixes = { "FBSDK", "FBAEMKit" };

        static List<string> PodsInTarget(string podfile, string target)
        {
            var lines = podfile.Split('\n');
            var start = System.Array.FindIndex(lines, l => l.Trim() == $"target '{target}' do");
            var pods = new List<string>();
            if (start < 0) return pods;
            for (var i = start + 1; i < lines.Length && lines[i].Trim() != "end"; i++)
                if (lines[i].Trim().StartsWith("pod ")) pods.Add(lines[i].Trim().Split('\'')[1]);
            return pods;
        }

        [Test]
        public void Podfile_CopiesFbPodsIntoExistingMainTarget()
        {
            var result = IosXcodePostProcess.PatchPodfile(EdmPodfile, FbPrefixes, out var added);
            CollectionAssert.AreEqual(new[] { "FBSDKCoreKit", "FBSDKCoreKit_Basics", "FBAEMKit" }, added);
            CollectionAssert.AreEqual(added, PodsInTarget(result, "Unity-iPhone"));
            // UnityFramework giữ nguyên, Google Ads không bị kéo sang.
            Assert.AreEqual(4, PodsInTarget(result, "UnityFramework").Count);
            Assert.IsTrue(result.Contains("  pod 'FBSDKCoreKit', '~> 17.0'"));
            Assert.IsTrue(result.EndsWith("use_frameworks! :linkage => :static\n"));
        }

        [Test]
        public void Podfile_Idempotent()
        {
            var once = IosXcodePostProcess.PatchPodfile(EdmPodfile, FbPrefixes, out _);
            var twice = IosXcodePostProcess.PatchPodfile(once, FbPrefixes, out var added);
            Assert.AreEqual(0, added.Count);
            Assert.AreEqual(once, twice);
        }

        [Test]
        public void Podfile_CreatesMainTargetBlockWhenMissing()
        {
            var podfile = "target 'UnityFramework' do\n  pod 'FBSDKLoginKit', '17.0.0'\nend\nuse_frameworks!\n";
            var result = IosXcodePostProcess.PatchPodfile(podfile, FbPrefixes, out var added);
            CollectionAssert.AreEqual(new[] { "FBSDKLoginKit" }, added);
            CollectionAssert.AreEqual(new[] { "FBSDKLoginKit" }, PodsInTarget(result, "Unity-iPhone"));
            Assert.Less(result.IndexOf("target 'Unity-iPhone'"), result.IndexOf("use_frameworks!"));
        }

        [Test]
        public void Podfile_NoMatch_Unchanged()
        {
            var podfile = "target 'UnityFramework' do\n  pod 'Firebase/Analytics', '10.0'\nend\n";
            Assert.AreEqual(podfile, IosXcodePostProcess.PatchPodfile(podfile, FbPrefixes, out var added));
            Assert.AreEqual(0, added.Count);
        }

        // ---- Icon A/B test ----

        [Test]
        public void IconName_Rules()
        {
            Assert.IsTrue(IosXcodePostProcess.IsValidIconName("AppIcon-B"));
            Assert.IsTrue(IosXcodePostProcess.IsValidIconName("Icon_2"));
            Assert.IsFalse(IosXcodePostProcess.IsValidIconName("AppIcon"));
            Assert.IsFalse(IosXcodePostProcess.IsValidIconName("Icon B"));
            Assert.IsFalse(IosXcodePostProcess.IsValidIconName(""));
            Assert.IsFalse(IosXcodePostProcess.IsValidIconName("-x"));
        }

        [Test]
        public void IconSetContents_SingleSize1024()
        {
            var json = IosXcodePostProcess.IconSetContents("icon.png");
            StringAssert.Contains("\"filename\" : \"icon.png\"", json);
            StringAssert.Contains("\"size\" : \"1024x1024\"", json);
            StringAssert.Contains("\"idiom\" : \"universal\"", json);
        }

        // ---- Validate ----

        static IosValidationInput ValidInput(BuildMode mode) => new IosValidationInput
        {
            preset = new IosBuildPreset { name = "P", appId = "com.raccoon.game", mode = mode },
            version = "1.0.0",
            scenePaths = new[] { "Assets/Scenes/Main.unity" },
            teamId = "ABCDE12345"
        };

        [Test]
        public void Valid_NoMessages()
        {
            Assert.AreEqual(0, BuildValidator.ValidateIos(ValidInput(BuildMode.Release)).Count);
        }

        [Test]
        public void MissingIosModule_IsError()
        {
            var i = ValidInput(BuildMode.Dev);
            i.iosModuleInstalled = false;
            Assert.IsTrue(BuildValidator.HasErrors(BuildValidator.ValidateIos(i)));
        }

        [Test]
        public void Signing_OnlyWarns()
        {
            var i = ValidInput(BuildMode.Release);
            i.teamId = "";
            var list = BuildValidator.ValidateIos(i);
            Assert.IsFalse(BuildValidator.HasErrors(list));
            Assert.IsTrue(list.Any(m => m.severity == Severity.Warning && m.text.Contains("Team ID")));

            i.teamId = "abc";
            Assert.IsTrue(BuildValidator.ValidateIos(i).Any(m => m.text.Contains("không đúng dạng")));

            i.automaticSigning = false;
            Assert.IsTrue(BuildValidator.ValidateIos(i).Any(m => m.text.Contains("Provisioning Profile")));
        }

        [Test]
        public void Facebook_WithoutMainTargetPods_Warns()
        {
            var i = ValidInput(BuildMode.Dev);
            i.facebookInstalled = true;
            i.podsToMainTarget = false;
            Assert.IsTrue(BuildValidator.ValidateIos(i).Any(m => m.text.Contains("Facebook")));
        }

        [Test]
        public void IconErrors_BlockBuild()
        {
            var i = ValidInput(BuildMode.Dev);
            i.iconErrors.Add("Icon A/B test 'x': lỗi");
            Assert.IsTrue(BuildValidator.HasErrors(BuildValidator.ValidateIos(i)));
        }

        [Test]
        public void Release_LocksNoAdsAndDevBuild()
        {
            var p = new IosBuildPreset { mode = BuildMode.Release, noAds = true, developmentBuild = true, useTestAd = true };
            BuildRules.EnforceModeLocks(p);
            Assert.IsFalse(p.noAds || p.developmentBuild || p.useTestAd);
        }

        [Test]
        public void BundleId_AllowsHyphen()
        {
            Assert.IsTrue(BuildRules.IsValidBundleId("com.DefaultCompany.unity-build-editor"));
            Assert.IsFalse(BuildRules.IsValidAppId("com.DefaultCompany.unity-build-editor"));
            Assert.IsFalse(BuildRules.IsValidBundleId("com.game_x.app"));
            Assert.IsFalse(BuildRules.IsValidBundleId("nodot"));
        }

        [Test]
        public void TeamId_Format()
        {
            Assert.IsTrue(BuildRules.IsValidTeamId("ABCDE12345"));
            Assert.IsFalse(BuildRules.IsValidTeamId("abcde12345"));
            Assert.IsFalse(BuildRules.IsValidTeamId("ABC"));
        }
    }
}
