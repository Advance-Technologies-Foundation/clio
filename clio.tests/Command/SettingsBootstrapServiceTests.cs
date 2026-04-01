using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
public sealed class SettingsBootstrapServiceTests {
	[Test]
	[Category("Unit")]
	[Description("Repairs an invalid ActiveEnvironmentKey by selecting the first configured environment and persisting the repaired settings file.")]
	public void GetResult_Should_Repair_Invalid_Active_Environment_Key() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(
			File.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"))));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		Settings persistedSettings = JsonConvert.DeserializeObject<Settings>(
			fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		result.Report.Status.Should().Be("repaired",
			because: "an invalid ActiveEnvironmentKey is a parseable structural problem that should be repaired automatically");
		result.Report.ResolvedActiveEnvironmentKey.Should().Be("dev",
			because: "the first configured environment should become the deterministic bootstrap fallback");
		result.Report.RepairsApplied.Should().ContainSingle(repair => repair.Code == "set-active-environment",
			because: "the repair report should explain how bootstrap recovered the active environment");
		persistedSettings.ActiveEnvironmentKey.Should().Be("dev",
			because: "the repaired ActiveEnvironmentKey should be persisted back to appsettings.json");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves a stale ActiveEnvironmentKey without overwriting appsettings.json when repairs are disabled for pre-parse bootstrap flows.")]
	public void GetResult_Should_Not_Persist_Repairs_When_Auto_Repair_Is_Disabled() {
		// Arrange
		string originalContent = File.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"));
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(originalContent));
		SettingsBootstrapService service = new(fileSystem, applyRepairs: false);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		result.Report.Status.Should().Be("repaired",
			because: "bootstrap should still resolve a deterministic active environment even when persistence is deferred");
		result.Report.ActiveEnvironmentKey.Should().Be("wrong-dev",
			because: "the report should preserve the original stale active environment key before repair");
		result.Report.ResolvedActiveEnvironmentKey.Should().Be("dev",
			because: "the in-memory bootstrap result should still expose the deterministic repaired environment");
		persistedContent.Should().Be(originalContent,
			because: "pre-parse bootstrap should not mutate appsettings.json before the real command startup runs");
	}

	[Test]
	[Category("Unit")]
	[Description("Creates and persists the default dev environment when the settings file is parseable but does not contain any environments.")]
	public void GetResult_Should_Create_Default_Environment_When_Environment_Map_Is_Empty() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "ActiveEnvironmentKey": "missing",
			  "Environments": {}
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		Settings persistedSettings = JsonConvert.DeserializeObject<Settings>(
			fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		result.Report.Status.Should().Be("repaired",
			because: "missing environments are a safe structural problem that bootstrap can repair automatically");
		result.Report.EnvironmentCount.Should().Be(1,
			because: "bootstrap should create exactly one default environment for an empty configuration");
		result.Report.ResolvedActiveEnvironmentKey.Should().Be("dev",
			because: "the default environment should become active after repair");
		persistedSettings.Environments.Should().ContainKey("dev",
			because: "the default dev environment should be written to appsettings.json");
		persistedSettings.ActiveEnvironmentKey.Should().Be("dev",
			because: "the persisted settings file should select the newly created default environment");
	}

	[Test]
	[Category("Unit")]
	[Description("Reports broken bootstrap state and preserves the original file content when appsettings.json is not valid JSON.")]
	public void GetResult_Should_Report_Broken_Status_For_Invalid_Json_Without_Overwriting_File() {
		// Arrange
		const string invalidJson = "{ invalid-json";
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(invalidJson));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		result.Report.Status.Should().Be("broken",
			because: "bootstrap must not silently overwrite invalid JSON files");
		result.Report.CanExecuteEnvTools.Should().BeFalse(
			because: "named-environment execution should stay blocked while appsettings.json is unreadable");
		persistedContent.Should().Be(invalidJson,
			because: "broken bootstrap should preserve the original file content until an explicit repair command changes it");
	}

	[Test]
	[Category("Unit")]
	[Description("Recomputes bootstrap health after the settings file is repaired during the same process lifetime.")]
	public void GetResult_Should_Recompute_After_Settings_File_Changes() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("{ invalid-json"));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult initialResult = service.GetResult();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Environments": {
			    "dev": {
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "Uri": "http://localhost"
			    }
			  }
			}
			"""));
		SettingsBootstrapResult repairedResult = service.GetResult();

		// Assert
		initialResult.Report.Status.Should().Be("broken",
			because: "the first read should reflect the invalid bootstrap file");
		repairedResult.Report.Status.Should().Be("healthy",
			because: "the same singleton service should observe a repaired settings file without requiring process restart");
		repairedResult.Report.CanExecuteEnvTools.Should().BeTrue(
			because: "named-environment MCP tools should start working again after the file becomes valid");
		repairedResult.Report.ResolvedActiveEnvironmentKey.Should().Be("dev",
			because: "the repaired bootstrap result should expose the newly valid active environment");
	}
}
