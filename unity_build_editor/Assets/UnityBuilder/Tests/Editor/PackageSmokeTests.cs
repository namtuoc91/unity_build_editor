using NUnit.Framework;

namespace Raccoon.BuildEditor.Tests
{
    public class PackageSmokeTests
    {
        [Test]
        public void AndroidBuildWindow_TypeExists()
        {
            Assert.IsNotNull(typeof(AndroidBuildWindow));
        }
    }
}
