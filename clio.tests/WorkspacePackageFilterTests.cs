using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Workspaces;
using NUnit.Framework;
using FluentAssertions;
using NSubstitute;

namespace Clio.Tests
{
    [TestFixture]
    [Description("Tests for workspace package filtering functionality")]
    public class WorkspacePackageFilterTests
    {
        private ILogger _mockLogger;
        private WorkspacePackageFilter _filter;

        [SetUp]
        public void SetUp()
        {
            _mockLogger = Substitute.For<ILogger>();
            _filter = new WorkspacePackageFilter(_mockLogger);
        }

        [Test]
        [Description("Filters packages when WorkspaceSettings has ignore patterns")]
        public void FilterPackages_WithWorkspaceSettings_FiltersCorrectly()
        {
            // Arrange
            var packages = new List<string> { "Package1", "TestPackage", "DemoPackage", "Production" };
            var workspaceSettings = new WorkspaceSettings
            {
                Packages = packages,
                IgnorePackages = new List<string> { "Test*", "Demo*" }
            };

            // Act
            var result = _filter.FilterPackages(packages, workspaceSettings).ToList();

            // Assert
            result.Should().HaveCount(2, "two packages should remain after filtering");
            result.Should().Contain("Package1", "Package1 should not be filtered");
            result.Should().Contain("Production", "Production should not be filtered");
            result.Should().NotContain("TestPackage", "TestPackage should be filtered by Test* pattern");
            result.Should().NotContain("DemoPackage", "DemoPackage should be filtered by Demo* pattern");
        }

        [Test]
        [Description("Returns all packages when no ignore patterns are defined")]
        public void FilterPackages_WithoutIgnorePatterns_ReturnsAllPackages()
        {
            // Arrange
            var packages = new List<string> { "Package1", "Package2", "Package3" };
            var workspaceSettings = new WorkspaceSettings
            {
                Packages = packages,
                IgnorePackages = new List<string>()
            };

            // Act
            var result = _filter.FilterPackages(packages, workspaceSettings).ToList();

            // Assert
            result.Should().HaveCount(3, "all packages should be returned when no ignore patterns");
            result.Should().BeEquivalentTo(packages, "original package list should be preserved");
        }

        [Test]
        [Description("Filters packages using direct ignore patterns list")]
        public void FilterPackages_WithIgnorePatternsList_FiltersCorrectly()
        {
            // Arrange
            var packages = new List<string> { "Package1", "TestPackage", "DemoPackage" };
            var ignorePatterns = new List<string> { "Test*" };

            // Act
            var result = _filter.FilterPackages(packages, ignorePatterns).ToList();

            // Assert
            result.Should().HaveCount(2, "one package should be filtered");
            result.Should().Contain("Package1", "Package1 should not be filtered");
            result.Should().Contain("DemoPackage", "DemoPackage should not be filtered");
            result.Should().NotContain("TestPackage", "TestPackage should be filtered by Test* pattern");
        }

        [Test]
        [Description("Returns empty collection when all packages are ignored")]
        public void FilterPackages_WhenAllPackagesIgnored_ReturnsEmpty()
        {
            // Arrange
            var packages = new List<string> { "TestPackage1", "TestPackage2" };
            var ignorePatterns = new List<string> { "Test*" };

            // Act
            var result = _filter.FilterPackages(packages, ignorePatterns).ToList();

            // Assert
            result.Should().BeEmpty("all packages should be filtered");
        }

        [Test]
        [Description("Handles null or empty packages collection gracefully")]
        public void FilterPackages_WithNullOrEmptyPackages_ReturnsEmpty()
        {
            // Arrange
            var ignorePatterns = new List<string> { "Test*" };

            // Act
            var resultNull = _filter.FilterPackages(null, ignorePatterns).ToList();
            var resultEmpty = _filter.FilterPackages(new List<string>(), ignorePatterns).ToList();

            // Assert
            resultNull.Should().BeEmpty("null packages should return empty");
            resultEmpty.Should().BeEmpty("empty packages should return empty");
        }

        [Test]
        [Description("Handles null ignore patterns gracefully")]
        public void FilterPackages_WithNullIgnorePatterns_ReturnsAllPackages()
        {
            // Arrange
            var packages = new List<string> { "Package1", "Package2" };

            // Act
            var result = _filter.FilterPackages(packages, (IList<string>)null).ToList();

            // Assert
            result.Should().BeEquivalentTo(packages, "all packages should be returned when ignore patterns are null");
        }
    }
}