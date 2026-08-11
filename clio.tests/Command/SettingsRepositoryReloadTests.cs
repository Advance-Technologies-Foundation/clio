using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Covers the explicit <see cref="ISettingsRepository.Reload"/> step that lets a long-lived host
/// (the MCP server) answer from the settings file instead of the snapshot taken at process start.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class SettingsRepositoryReloadTests {

	private const string TwoEnvironmentsSettings = """
		{
		  "ActiveEnvironmentKey": "netcore-env",
		  "Environments": {
		    "netcore-env": { "Uri": "http://localhost:5001", "Login": "Supervisor", "Password": "Supervisor" },
		    "framework-env": { "Uri": "http://remote-host:88/site", "Login": "Supervisor", "Password": "Supervisor" }
		  }
		}
		""";

	private MockFileSystem _fileSystem;

	[SetUp]
	public void SetUp() {
		_fileSystem = TestFileSystem.MockFileSystem();
		_fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(TwoEnvironmentsSettings));
	}

	[Test]
	[Description("Sees an environment appended to appsettings.json after the repository was constructed.")]
	public void Reload_Should_See_Environment_Added_To_File_After_Construction() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		bool visibleBeforeReload = sut.IsEnvironmentExists("added-later");
		WriteSettings(TwoEnvironmentsSettings.Replace(
			"\"netcore-env\": {",
			"\"added-later\": { \"Uri\": \"http://added-later\", \"Login\": \"Supervisor\", \"Password\": \"Supervisor\" },\n    \"netcore-env\": {"));

		// Act
		SettingsReloadResult result = sut.Reload();

		// Assert
		visibleBeforeReload.Should().BeFalse(
			because: "the constructor snapshot cannot contain an environment that was written afterwards");
		result.Reloaded.Should().BeTrue(
			because: "a readable settings file must replace the in-memory snapshot");
		result.Warning.Should().BeNull(
			because: "a successful reload has nothing to warn about");
		sut.IsEnvironmentExists("added-later").Should().BeTrue(
			because: "list-environments and MCP environment resolution must answer from the file as of the call");
		sut.GetAllEnvironments().Keys.Should().Contain("netcore-env",
			because: "reloading must not drop environments that were already registered");
	}

	[Test]
	[Description("Sees a changed uri for an environment that was re-pointed in appsettings.json after construction.")]
	public void Reload_Should_See_Changed_Uri_Of_Existing_Environment() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		WriteSettings(TwoEnvironmentsSettings.Replace("http://localhost:5001", "http://moved-here"));

		// Act
		sut.Reload();

		// Assert
		sut.FindEnvironment("netcore-env")!.Uri.Should().Be("http://moved-here",
			because: "the whole environment object is snapshotted, so a re-pointed uri must be picked up too");
	}

	[Test]
	[Description("Keeps the previously loaded environments and returns a warning when appsettings.json became unreadable.")]
	public void Reload_Should_Keep_Previous_State_And_Warn_When_File_Is_Corrupt() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		WriteSettings("{ this is not json");

		// Act
		SettingsReloadResult result = sut.Reload();

		// Assert
		result.Reloaded.Should().BeFalse(
			because: "an unreadable file must never replace a valid in-memory state");
		result.Warning.Should().NotBeNullOrWhiteSpace(
			because: "the caller has to learn that the answer is the last state clio managed to read");
		result.Warning.Should().Contain(SettingsRepository.AppSettingsFile,
			because: "the warning must name the file the operator has to repair");
		sut.GetAllEnvironments().Keys.Should().Contain("netcore-env",
			because: "the previously loaded environments must stay usable while the file is broken");
	}

	[Test]
	[Description("Keeps feature-flag lookups case-insensitive after a reload replaced the settings snapshot.")]
	public void Reload_Should_Preserve_Case_Insensitive_Feature_Lookup() {
		// Arrange
		SettingsRepository sut = new(_fileSystem);
		WriteSettings(TwoEnvironmentsSettings.Replace(
			"\"Environments\": {",
			"\"features\": { \"AiAssist\": true },\n  \"Environments\": {"));

		// Act
		sut.Reload();

		// Assert
		sut.IsFeatureEnabled("aiassist").Should().BeTrue(
			because: "reloading must apply the same post-load normalization as the constructor, "
				+ "otherwise feature keys silently become case-sensitive");
	}

	private void WriteSettings(string content) {
		_fileSystem.File.WriteAllText(SettingsRepository.AppSettingsFile, content);
	}
}
