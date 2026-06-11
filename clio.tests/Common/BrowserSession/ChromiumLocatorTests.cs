using System;
using Clio.Common;
using Clio.Common.BrowserSession;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common.BrowserSession;

/// <summary>
/// Story 9 (browser-session-handoff, Mode A): Chromium discovery for <c>open-web-app --authenticated</c> —
/// honors CHROME_PATH, probes standard OS install locations, and fails closed with an actionable error.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ChromiumLocatorTests {
	private const string ChromePathEnvVar = "CHROME_PATH";
	private string _originalChromePath;
	private IFileSystem _fileSystem;
	private ChromiumLocator _sut;

	[SetUp]
	public void Setup() {
		_originalChromePath = Environment.GetEnvironmentVariable(ChromePathEnvVar);
		Environment.SetEnvironmentVariable(ChromePathEnvVar, null);
		_fileSystem = Substitute.For<IFileSystem>();
		_sut = new ChromiumLocator(_fileSystem);
	}

	[TearDown]
	public void TearDown() => Environment.SetEnvironmentVariable(ChromePathEnvVar, _originalChromePath);

	[Test]
	[Description("Locate returns the CHROME_PATH value when it is set and points at an existing file.")]
	public void Locate_ShouldReturnChromePathEnvValue_WhenSetAndFileExists() {
		// Arrange
		const string explicitPath = "/custom/path/to/chrome";
		Environment.SetEnvironmentVariable(ChromePathEnvVar, explicitPath);
		_fileSystem.ExistsFile(explicitPath).Returns(true);

		// Act
		string result = _sut.Locate();

		// Assert
		result.Should().Be(explicitPath, "because an explicit CHROME_PATH override takes precedence over standard locations");
	}

	[Test]
	[Description("Locate falls back to a standard OS install path when CHROME_PATH is unset but a browser exists there.")]
	public void Locate_ShouldReturnStandardInstallPath_WhenChromePathUnsetButBrowserExists() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(true);

		// Act
		string result = _sut.Locate();

		// Assert
		result.Should().NotBeNullOrEmpty("because a standard install location was found for the current OS");
		_fileSystem.ReceivedWithAnyArgs().ExistsFile(default);
	}

	[Test]
	[Description("Locate throws ChromiumNotFoundException with an actionable message when no browser exists and CHROME_PATH is unset (AC-04).")]
	public void Locate_ShouldThrowChromiumNotFound_WhenNoBrowserExists() {
		// Arrange
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);

		// Act
		Action act = () => _sut.Locate();

		// Assert
		act.Should().Throw<ChromiumNotFoundException>(
				"because failing closed prevents a silent fallback to an unauthenticated browser")
			.WithMessage("*Chromium binary not found*", "because the error must be actionable for the user");
	}

	[Test]
	[Description("Locate ignores a CHROME_PATH that points at a non-existent file and falls through to discovery/failure.")]
	public void Locate_ShouldIgnoreChromePath_WhenItPointsAtMissingFile() {
		// Arrange
		Environment.SetEnvironmentVariable(ChromePathEnvVar, "/does/not/exist/chrome");
		_fileSystem.ExistsFile(Arg.Any<string>()).Returns(false);

		// Act
		Action act = () => _sut.Locate();

		// Assert
		act.Should().Throw<ChromiumNotFoundException>(
			"because a stale CHROME_PATH must not be returned when the file is gone");
	}
}
