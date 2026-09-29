using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Raccoon.BuildEditor.Tests
{
    public class ValidatorTests
    {
        static ValidationInput ValidInput(BuildMode mode) => new ValidationInput
        {
            preset = new BuildPreset { name = "P", appId = "com.raccoon.game", mode = mode },
            version = "1.0.0",
            scenePaths = new[] { "Assets/Scenes/Main.unity" },
            keystorePath = "user.keystore",
            keystoreExists = true,
            keystoreAlias = "alias",
            keystorePass = "pass",
            keyAliasPass = "pass"
        };

        static bool HasError(List<ValidationMessage> l) => BuildValidator.HasErrors(l);

        [Test]
        public void Valid_Release_NoErrors()
        {
            Assert.IsFalse(HasError(BuildValidator.Validate(ValidInput(BuildMode.Release))));
        }

        [Test]
        public void NoScenes_IsError()
        {
            var i = ValidInput(BuildMode.Dev);
            i.scenePaths = new string[0];
            Assert.IsTrue(HasError(BuildValidator.Validate(i)));
        }

        [Test]
        public void InvalidAppId_IsError()
        {
            var i = ValidInput(BuildMode.Dev);
            i.preset.appId = "bad";
            Assert.IsTrue(HasError(BuildValidator.Validate(i)));
        }

        [Test]
        public void MissingNdk_IsError()
        {
            var i = ValidInput(BuildMode.Dev);
            i.ndkFound = false;
            Assert.IsTrue(HasError(BuildValidator.Validate(i)));
        }

        [Test]
        public void Keystore_MissingPassword_ReleaseError_DevInfoOnly()
        {
            var rel = ValidInput(BuildMode.Release);
            rel.keystorePass = "";
            Assert.IsTrue(HasError(BuildValidator.Validate(rel)));

            var dev = ValidInput(BuildMode.Dev);
            dev.keystorePass = "";
            var msgs = BuildValidator.Validate(dev);
            Assert.IsFalse(HasError(msgs));
            Assert.IsTrue(msgs.Any(m => m.severity == Severity.Info));
        }

        [Test]
        public void SwitchPlatform_IsWarningOnly()
        {
            var i = ValidInput(BuildMode.Dev);
            i.activeTargetIsAndroid = false;
            var msgs = BuildValidator.Validate(i);
            Assert.IsFalse(HasError(msgs));
            Assert.IsTrue(msgs.Any(m => m.severity == Severity.Warning));
        }

        static MediationReport ReportWith(MediationStatus status)
        {
            var r = new MediationReport { adMobInstalled = true };
            r.rows.Add(new MediationRow { network = "AppLovin", status = status, xmlVersion = "1", gradleVersion = "2" });
            return r;
        }

        [TestCase(MediationStatus.VersionMismatch)]
        [TestCase(MediationStatus.MissingInGradle)]
        public void Mediation_MismatchOrMissing_BlocksRelease_WarnsDev(MediationStatus status)
        {
            var rel = ValidInput(BuildMode.Release);
            rel.mediation = ReportWith(status);
            Assert.IsTrue(HasError(BuildValidator.Validate(rel)));

            var dev = ValidInput(BuildMode.Dev);
            dev.mediation = ReportWith(status);
            var msgs = BuildValidator.Validate(dev);
            Assert.IsFalse(HasError(msgs));
            Assert.IsTrue(msgs.Any(m => m.severity == Severity.Warning));
        }

        [Test]
        public void Mediation_ExtraLib_WarningBothModes()
        {
            foreach (var mode in new[] { BuildMode.Dev, BuildMode.Release })
            {
                var i = ValidInput(mode);
                i.mediation = ReportWith(MediationStatus.ExtraInGradle);
                var msgs = BuildValidator.Validate(i);
                Assert.IsFalse(HasError(msgs), mode.ToString());
                Assert.IsTrue(msgs.Any(m => m.severity == Severity.Warning), mode.ToString());
            }
        }
    }

    public class MediationParserTests
    {
        const string Xml = @"<dependencies>
  <androidPackages>
    <androidPackage spec=""com.google.ads.mediation:applovin:[13.4.0.1]"">
      <repositories/>
    </androidPackage>
  </androidPackages>
</dependencies>";

        const string Gradle = @"dependencies {
    implementation fileTree(dir: 'libs', include: ['*.jar'])
// Android Resolver Dependencies Start
    implementation 'com.google.android.gms:play-services-ads:24.5.0' // Assets/GoogleMobileAds/Editor/GoogleMobileAdsDependencies.xml:7
    implementation 'com.google.android.ump:user-messaging-platform:3.2.0' // Assets/GoogleMobileAds/Editor/GoogleUmpDependencies.xml:7
    implementation 'com.google.ads.mediation:applovin:13.4.0.1' // Assets/GoogleMobileAds/Mediation/AppLovin/Editor/AppLovinMediationDependencies.xml:24
    implementation 'com.google.ads.mediation:facebook:6.20.0.0' // Assets/GoogleMobileAds/Mediation/MetaAudienceNetwork/Editor/MetaAudienceNetworkMediationDependencies.xml:24
// Android Resolver Dependencies End
**DEPS**}";

        [Test]
        public void ParseXml_StripsBrackets()
        {
            var deps = MediationReview.ParseDependenciesXml(Xml);
            Assert.AreEqual(1, deps.Count);
            Assert.AreEqual("applovin", deps[0].artifact);
            Assert.AreEqual("13.4.0.1", deps[0].version);
        }

        [Test]
        public void ParseGradle_ReadsOnlyResolverBlock()
        {
            var deps = MediationReview.ParseResolverBlock(Gradle);
            Assert.AreEqual(4, deps.Count);
            var applovin = deps.Single(d => d.artifact == "applovin");
            Assert.AreEqual("com.google.ads.mediation", applovin.group);
            Assert.AreEqual("13.4.0.1", applovin.version);
            StringAssert.Contains("AppLovinMediationDependencies.xml", applovin.comment);
        }

        [Test]
        public void ParseGradle_NoBlock_ReturnsNull()
        {
            Assert.IsNull(MediationReview.ParseResolverBlock("dependencies { }"));
        }

        [Test]
        public void ParseAar_FileNames()
        {
            var deps = MediationReview.ParseAarFileNames(new[]
            {
                "Assets/Plugins/Android/com.google.ads.mediation.applovin-13.4.0.1.aar",
                "Assets/Plugins/Android/com.google.android.gms.play-services-ads-24.5.0.aar",
                "Assets/Plugins/Android/other-lib-1.0.aar"
            });
            Assert.AreEqual(2, deps.Count);
            Assert.AreEqual("applovin", deps[0].artifact);
            Assert.AreEqual("13.4.0.1", deps[0].version);
            Assert.AreEqual("play-services-ads", deps[1].artifact);
        }

        [Test]
        public void BuildRows_ComputesStatuses()
        {
            var xml = new List<XmlDep>
            {
                new XmlDep { network = "AppLovin", artifact = "applovin", version = "13.4.0.1", source = "UPM" },
                new XmlDep { network = "Mintegral", artifact = "mintegral", version = "16.0.0.0", source = "UPM" },
                new XmlDep { network = "Pangle", artifact = "pangle", version = "7.0.0.0", source = "Assets" }
            };
            var gradle = new List<GradleDep>
            {
                new GradleDep { group = "com.google.ads.mediation", artifact = "applovin", version = "13.4.0.1" },
                new GradleDep { group = "com.google.ads.mediation", artifact = "mintegral", version = "15.0.0.0" },
                new GradleDep { group = "com.google.ads.mediation", artifact = "vungle", version = "7.5.0.0" },
                new GradleDep { group = "com.google.android.gms", artifact = "play-services-ads", version = "24.5.0" }
            };

            var rows = MediationReview.BuildRows(xml, gradle, null);
            Assert.AreEqual(MediationStatus.Ok, rows.Single(r => r.network == "AppLovin").status);
            Assert.AreEqual(MediationStatus.VersionMismatch, rows.Single(r => r.network == "Mintegral").status);
            Assert.AreEqual(MediationStatus.MissingInGradle, rows.Single(r => r.network == "Pangle").status);
            Assert.AreEqual(MediationStatus.ExtraInGradle, rows.Single(r => r.network == "vungle").status);
            Assert.AreEqual(4, rows.Count, "core lib không được tính là mediation");
        }
    }

    public class BuildHistoryTests
    {
        static BuildHistoryEntry Entry(bool aab, int i) =>
            new BuildHistoryEntry { isAab = aab, versionCode = i, path = $"/tmp/none_{aab}_{i}" };

        [Test]
        public void Apk_KeepsLatest20()
        {
            var h = new BuildHistory();
            for (var i = 1; i <= 25; i++) h.Add(Entry(false, i), deleteFiles: false);
            Assert.AreEqual(BuildHistory.MaxApk, h.entries.Count);
            Assert.AreEqual(25, h.entries.First().versionCode);
            Assert.AreEqual(6, h.entries.Last().versionCode);
        }

        [Test]
        public void Aab_KeepsLatest10_IndependentOfApk()
        {
            var h = new BuildHistory();
            for (var i = 1; i <= 5; i++) h.Add(Entry(false, i), deleteFiles: false);
            for (var i = 1; i <= 12; i++) h.Add(Entry(true, i), deleteFiles: false);
            Assert.AreEqual(BuildHistory.MaxAab, h.entries.Count(e => e.isAab));
            Assert.AreEqual(5, h.entries.Count(e => !e.isAab));
        }

        [Test]
        public void Overflow_DeletesOldFileOnDisk()
        {
            var dir = Path.Combine(Path.GetTempPath(), "RaccoonBuildHistoryTest");
            Directory.CreateDirectory(dir);
            try
            {
                var h = new BuildHistory();
                var first = Path.Combine(dir, "first.aab");
                File.WriteAllText(first, "x");
                h.Add(new BuildHistoryEntry { isAab = true, path = first });
                for (var i = 0; i < BuildHistory.MaxAab; i++)
                    h.Add(new BuildHistoryEntry { isAab = true, path = Path.Combine(dir, $"b{i}.aab") });

                Assert.IsFalse(File.Exists(first));
                Assert.IsFalse(h.entries.Any(e => e.path == first));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
