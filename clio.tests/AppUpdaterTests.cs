using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests;

[TestFixture]
[Category("Unit")]
[Property("Module", "Core")]
public class AppUpdaterTests {

	[Test]
	[Description("NormalizeInstalledVersion should extract the semantic version from the version command output")]
	public void NormalizeInstalledVersion_WhenVersionCommandReturnsCliPrefixAndMetadata_ReturnsBaseVersion() {
		// Arrange
		const string standardOutput = "clio 8.0.2.51+9a045403c967fe023bb105269f116dd234b1d394d";

		// Act
		string result = AppUpdater.NormalizeInstalledVersion(standardOutput);

		// Assert
		result.Should().Be("8.0.2.51", "because verification should compare the installed semantic version without git metadata");
	}

	[Test]
	[Description("NormalizeInstalledVersion should extract the semantic version from the info command output")]
	public void NormalizeInstalledVersion_WhenInfoCommandReturnsLabelledVersion_ReturnsBaseVersion() {
		// Arrange
		const string standardOutput = "clio:   8.0.2.51";

		// Act
		string result = AppUpdater.NormalizeInstalledVersion(standardOutput);

		// Assert
		result.Should().Be("8.0.2.51", "because updater verification uses the canonical info command output");
	}

	[Test]
	[Description("NormalizeInstalledVersion should ignore logger prefixes before labelled version output")]
	public void NormalizeInstalledVersion_WhenInfoCommandIncludesLoggerPrefix_ReturnsBaseVersion() {
		// Arrange
		const string standardOutput = "[INF] - clio:   8.0.2.51";

		// Act
		string result = AppUpdater.NormalizeInstalledVersion(standardOutput);

		// Assert
		result.Should().Be("8.0.2.51", "because installed tool output is written through the console logger");
	}

	[Test]
	[Description("NormalizeInstalledVersion should find the clio semantic version even when it is not the last output line")]
	public void NormalizeInstalledVersion_WhenOutputContainsMultipleInfoLines_ReturnsFirstClioVersion() {
		// Arrange
		const string standardOutput =
			"[INF] - clio:   8.0.2.55\r\n[INF] - gate:   2.0.0.41\r\n[INF] - settings file path: /tmp/appsettings.json";

		// Act
		string result = AppUpdater.NormalizeInstalledVersion(standardOutput);

		// Assert
		result.Should().Be("8.0.2.55", "because verification should not depend on the clio version line being the final line in the command output");
	}

	[Test]
	[Description("NormalizeInstalledVersion should fall back to stderr when the version command writes there")]
	public void NormalizeInstalledVersion_WhenStdoutIsEmptyAndStderrHasVersion_ReturnsVersion() {
		// Arrange
		const string standardError = "clio 8.0.2.51+9a045403c967fe023bb105269f116dd234b1d394d";

		// Act
		string result = AppUpdater.NormalizeInstalledVersion(string.Empty, standardError);

		// Assert
		result.Should().Be("8.0.2.51", "because the verifier should tolerate version output from either stream");
	}

	[Test]
	[Description("GetCurrentVersion should return a version string for the running assembly")]
	public void GetCurrentVersion_WhenCalled_ReturnsAssemblyFileVersion() {
		// Arrange
		var logger = Substitute.For<ILogger>();
		var processExecutor = Substitute.For<IProcessExecutor>();
		var updater = new AppUpdater(logger, processExecutor);

		// Act
		string result = updater.GetCurrentVersion();

		// Assert
		result.Should().NotBeNullOrWhiteSpace("because the updater needs a concrete installed version for comparisons");
	}

	[TestCase("8.0.1.80", "9.0.0.0", "MAJOR")]
	[TestCase("8.0.1.80", "8.1.0.0", "minor")]
	[TestCase("8.0.1.80", "8.0.2.0", "patch")]
	[TestCase("8.0.1.80", "8.0.1.85", "build")]
	[Description("GetUpdateType should correctly classify version change type")]
	public void GetUpdateType_WhenVersionsCompared_ReturnsCorrectType(
		string current, string latest, string expectedType) {
		// Arrange
		var logger = Substitute.For<ILogger>();
		var processExecutor = Substitute.For<IProcessExecutor>();
		var updater = new AppUpdater(logger, processExecutor);

		// Act
		string result = updater.GetUpdateType(current, latest);

		// Assert
		result.Should().Be(expectedType);
	}

	[Test]
	[Description("GetUpdateType should handle invalid version strings gracefully")]
	public void GetUpdateType_WhenInvalidVersions_ReturnsUpdate() {
		// Arrange
		var logger = Substitute.For<ILogger>();
		var processExecutor = Substitute.For<IProcessExecutor>();
		var updater = new AppUpdater(logger, processExecutor);

		// Act
		string result = updater.GetUpdateType("invalid", "also-invalid");

		// Assert
		result.Should().Be("update");
	}
}
