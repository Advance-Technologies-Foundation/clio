#region

using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

#endregion

namespace Clio.Tests;

[TestFixture]
[Description("Tests for workspace package filtering functionality")]
public class WorkspacePackageFilterTests{
	#region Fields: Private

	private WorkspacePackageFilter _filter;
	private ILogger _mockLogger;

	#endregion

	#region Methods: Public

	[Test]
	[Description("Returns empty collection when all packages are ignored")]
	public void FilterPackages_WhenAllPackagesIgnored_ReturnsEmpty() {
		// Arrange
		List<string> packages = new() { "TestPackage1", "TestPackage2" };
		List<string> ignorePatterns = new() { "Test*" };

		// Act
		List<string> result = _filter.FilterPackages(packages, ignorePatterns).ToList();

		// Assert
		result.Should().BeEmpty("all packages should be filtered");
	}

	[Test]
	[Description("Filters packages using direct ignore patterns list")]
	public void FilterPackages_WithIgnorePatternsList_FiltersCorrectly() {
		// Arrange
		List<string> packages = new() { "Package1", "TestPackage", "DemoPackage" };
		List<string> ignorePatterns = new() { "Test*" };

		// Act
		List<string> result = _filter.FilterPackages(packages, ignorePatterns).ToList();

		// Assert
		result.Should().HaveCount(2, "one package should be filtered");
		result.Should().Contain("Package1", "Package1 should not be filtered");
		result.Should().Contain("DemoPackage", "DemoPackage should not be filtered");
		result.Should().NotContain("TestPackage", "TestPackage should be filtered by Test* pattern");
	}

	[Test]
	[Description("Handles null ignore patterns gracefully")]
	public void FilterPackages_WithNullIgnorePatterns_ReturnsAllPackages() {
		// Arrange
		List<string> packages = new() { "Package1", "Package2" };

		// Act
		List<string> result = _filter.FilterPackages(packages, (IList<string>)null).ToList();

		// Assert
		result.Should().BeEquivalentTo(packages, "all packages should be returned when ignore patterns are null");
	}

	[Test]
	[Description("Handles null or empty packages collection gracefully")]
	public void FilterPackages_WithNullOrEmptyPackages_ReturnsEmpty() {
		// Arrange
		List<string> ignorePatterns = new() { "Test*" };

		// Act
		List<string> resultNull = _filter.FilterPackages(null, ignorePatterns).ToList();
		List<string> resultEmpty = _filter.FilterPackages(new List<string>(), ignorePatterns).ToList();

		// Assert
		resultNull.Should().BeEmpty("null packages should return empty");
		resultEmpty.Should().BeEmpty("empty packages should return empty");
	}

	[Test]
	[Description("Returns all packages when no ignore patterns are defined")]
	public void FilterPackages_WithoutIgnorePatterns_ReturnsAllPackages() {
		// Arrange
		List<string> packages = new() { "Package1", "Package2", "Package3" };
		WorkspaceSettings workspaceSettings = new() {
			Packages = packages, IgnorePackages = new List<string>()
		};

		// Act
		List<string> result = _filter.FilterPackages(packages, workspaceSettings).ToList();

		// Assert
		result.Should().HaveCount(3, "all packages should be returned when no ignore patterns");
		result.Should().BeEquivalentTo(packages, "original package list should be preserved");
	}

	[Test]
	[Description("Filters packages when WorkspaceSettings has ignore patterns")]
	public void FilterPackages_WithWorkspaceSettings_FiltersCorrectly() {
		// Arrange
		List<string> packages = new() { "Package1", "TestPackage", "DemoPackage", "Production" };
		WorkspaceSettings workspaceSettings = new() {
			Packages = packages, IgnorePackages = new List<string> { "Test*", "Demo*" }
		};

		// Act
		List<string> result = _filter.FilterPackages(packages, workspaceSettings).ToList();

		// Assert
		result.Should().HaveCount(2, "two packages should remain after filtering");
		result.Should().Contain("Package1", "Package1 should not be filtered");
		result.Should().Contain("Production", "Production should not be filtered");
		result.Should().NotContain("TestPackage", "TestPackage should be filtered by Test* pattern");
		result.Should().NotContain("DemoPackage", "DemoPackage should be filtered by Demo* pattern");
	}

	[Test]
	[Description("Handles duplicate packages in input and workspace settings")]
	public void IncludedPackages_Duplicates_ReturnsCorrectIntersection() {
		// Arrange
		List<string> workspacePackages = new() { "A", "B", "B", "C" };
		List<string> inputPackages = new() { "B", "B", "C", "C" };
		WorkspaceSettings settings = new() { Packages = workspacePackages };

		// Act
		List<string> result = _filter.IncludedPackages(inputPackages, settings).ToList();

		// Assert
		result.Should().BeEquivalentTo(["B", "B", "C", "C"], "duplicates should be preserved in intersection");
	}

	[Test]
	[Description("Returns empty when input package list is empty")]
	public void IncludedPackages_EmptyInput_ReturnsEmpty() {
		// Arrange
		List<string> workspacePackages = new() { "A", "B" };
		WorkspaceSettings settings = new() { Packages = workspacePackages };

		// Act
		List<string> result = _filter.IncludedPackages(new List<string>(), settings).ToList();

		// Assert
		result.Should().BeEmpty("empty input should return empty");
	}

	[Test]
	[Description("Returns empty when workspace settings has no packages")]
	public void IncludedPackages_EmptyWorkspaceSettings_ReturnsEmpty() {
		// Arrange
		List<string> inputPackages = new() { "A", "B" };
		WorkspaceSettings settings = new() { Packages = new List<string>() };

		// Act
		List<string> result = _filter.IncludedPackages(inputPackages, settings).ToList();

		// Assert
		result.Should().BeEmpty("workspace settings with no packages should return empty");
	}

	[Test]
	[Description("Returns empty when no input packages are present in workspace settings")]
	public void IncludedPackages_NoIntersection_ReturnsEmpty() {
		// Arrange
		List<string> workspacePackages = new() { "A", "B" };
		List<string> inputPackages = new() { "C", "D" };
		WorkspaceSettings settings = new() { Packages = workspacePackages };

		// Act
		List<string> result = _filter.IncludedPackages(inputPackages, settings).ToList();

		// Assert
		result.Should().BeEmpty("no input packages are present in workspace settings");
	}

	[Test]
	[Description("Handles null workspace settings gracefully")]
	public void IncludedPackages_NullWorkspaceSettings_ReturnsEmpty() {
		// Arrange
		List<string> inputPackages = ["A", "B"];

		// Act
		IEnumerable<string> result = _filter.IncludedPackages(inputPackages, null);

		// Assert
		IEnumerable<string> enumerable = result.ToList();
		enumerable.Should().NotBeNull("null workspace settings should return empty");
		enumerable.Should().BeEmpty("null workspace settings should return empty");
	}


	[Test]
	[Description("Handles null packages in workspace settings gracefully")]
	public void IncludedPackages_NullWorkspaceSettingsPackages_ReturnsEmpty() {
		// Arrange
		List<string> inputPackages = ["A", "B"];
		WorkspaceSettings settings = new() {
			Packages = null
		};

		// Act
		IEnumerable<string> result = _filter.IncludedPackages(inputPackages, settings);

		// Assert
		IEnumerable<string> enumerable = result.ToList();
		enumerable.Should().NotBeNull("null workspace settings should return empty");
		enumerable.Should().BeEmpty("null workspace settings should return empty");
	}

	[Test]
	[Description("Returns only packages present in both workspace settings and input list")]
	public void IncludedPackages_ReturnsIntersection() {
		// Arrange
		List<string> workspacePackages = new() { "A", "B", "C" };
		List<string> inputPackages = new() { "B", "C", "D" };
		WorkspaceSettings settings = new() { Packages = workspacePackages };

		// Act
		List<string> result = _filter.IncludedPackages(inputPackages, settings).ToList();

		// Assert
		result.Should().BeEquivalentTo(new[] { "B", "C" }, "only packages present in both should be returned");
	}

	[SetUp]
	public void SetUp() {
		_mockLogger = Substitute.For<ILogger>();
		_filter = new WorkspacePackageFilter(_mockLogger);
	}

	#endregion
}
