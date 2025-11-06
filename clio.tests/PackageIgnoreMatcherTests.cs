using System.Collections.Generic;
using Clio.Common;
using NUnit.Framework;
using FluentAssertions;

namespace Clio.Tests
{
    [TestFixture]
    [Description("Checks mask matching logic for package ignore feature")] 
    public class PackageIgnoreMatcherTests
    {
        [Test]
        [Description("Package is ignored by exact name match")]
        public void Should_Ignore_ExactName()
        {
            var masks = new List<string> { "TestPackage" };
            PackageIgnoreMatcher.IsIgnored("TestPackage", masks)
                .Should().BeTrue("name matches the mask");
        }

        [Test]
    [Description("Package is not ignored if name does not match")]
        public void Should_Not_Ignore_DifferentName()
        {
            var masks = new List<string> { "TestPackage" };
            PackageIgnoreMatcher.IsIgnored("DemoPackage", masks)
                .Should().BeFalse("name does not match the mask");
        }

        [Test]
    [Description("Package is ignored by mask starting with *")]
        public void Should_Ignore_Mask_StartsWith()
        {
            var masks = new List<string> { "Demo*" };
            PackageIgnoreMatcher.IsIgnored("DemoPackage", masks)
                .Should().BeTrue("name starts with Demo");
        }

        [Test]
    [Description("Package is ignored by mask ending with *")]
        public void Should_Ignore_Mask_EndsWith()
        {
            var masks = new List<string> { "*Test" };
            PackageIgnoreMatcher.IsIgnored("MyTest", masks)
                .Should().BeTrue("name ends with Test");
        }

        [Test]
    [Description("Package is ignored by mask containing *")]
        public void Should_Ignore_Mask_Contains()
        {
            var masks = new List<string> { "*Demo*" };
            PackageIgnoreMatcher.IsIgnored("MyDemoPackage", masks)
                .Should().BeTrue("name contains Demo");
        }

        [Test]
    [Description("Package is ignored by mask with ?")]
        public void Should_Ignore_Mask_QuestionMark()
        {
            var masks = new List<string> { "Demo?" };
            PackageIgnoreMatcher.IsIgnored("Demo1", masks)
                .Should().BeTrue("? replaces one character");
            PackageIgnoreMatcher.IsIgnored("Demo12", masks)
                .Should().BeFalse("? replaces only one character");
        }

        [Test]
    [Description("Package is not ignored if no mask matches")]
        public void Should_Not_Ignore_IfNoMatch()
        {
            var masks = new List<string> { "Test*", "Demo?" };
            PackageIgnoreMatcher.IsIgnored("OtherPackage", masks)
                .Should().BeFalse("name does not match any mask");
        }
    }
}
