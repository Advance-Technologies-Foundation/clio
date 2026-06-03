using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Property("Module", "Command")]
public sealed class SettingsBootstrapServiceTests {
	[Test]
	[Category("Unit")]
	[Description("Detects an invalid ActiveEnvironmentKey as a configuration issue without auto-repairing it. Only the user may set the active environment.")]
	public void GetResult_Should_Detect_Invalid_Active_Environment_Key_Without_Repairing() {
		// Arrange
		string originalContent = File.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"));
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(originalContent));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		result.Report.Status.Should().Be("issues-detected",
			because: "an invalid ActiveEnvironmentKey is a user configuration problem that must be reported, not silently fixed by clio");
		result.Report.ActiveEnvironmentKey.Should().Be("wrong-dev",
			because: "the original configured key must be preserved so the user sees which key is wrong");
		result.Report.ResolvedActiveEnvironmentKey.Should().BeNull(
			because: "bootstrap must not auto-select a fallback environment — only the user may set the active environment");
		result.Report.Issues.Should().ContainSingle(issue => issue.Code == "invalid-active-environment",
			because: "the issue must be reported so diagnostics tools and error messages can surface it");
		result.Report.RepairsApplied.Should().BeEmpty(
			because: "no automatic repair should be applied — the user must explicitly call set-active-environment");
		persistedContent.Should().Be(originalContent,
			because: "appsettings.json must not be modified when bootstrap only detects issues without applying repairs");
	}

	[Test]
	[Category("Unit")]
	[Description("applyRepairs:false has no effect on ActiveEnvironmentKey detection — bootstrap never auto-repairs this regardless of the flag.")]
	public void GetResult_Should_Detect_Invalid_Active_Environment_Key_Regardless_Of_ApplyRepairs_Flag() {
		// Arrange
		string originalContent = File.ReadAllText(Path.Combine("Examples", "AppConfigs", "appsettings-with-wrong-active-key.json"));
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(originalContent));
		SettingsBootstrapService service = new(fileSystem, applyRepairs: false);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		result.Report.Status.Should().Be("issues-detected",
			because: "the applyRepairs flag must not change how an invalid ActiveEnvironmentKey is reported");
		result.Report.ResolvedActiveEnvironmentKey.Should().BeNull(
			because: "bootstrap must not resolve a fallback environment in memory either, regardless of the applyRepairs flag");
		result.Report.RepairsApplied.Should().BeEmpty(
			because: "no repair was applied so the list must be empty");
		persistedContent.Should().Be(originalContent,
			because: "appsettings.json must remain unchanged when applyRepairs is false");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns healthy bootstrap state when appsettings.json has no registered environments, without injecting a default environment.")]
	public void GetResult_Should_Return_Healthy_When_Environment_Map_Is_Empty() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "Environments": {}
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.Status.Should().Be("healthy",
			because: "an empty environment map is a valid initial state, not a structural error");
		result.Report.CanExecuteEnvTools.Should().BeFalse(
			because: "named-environment execution requires at least one registered environment");
		result.Report.EnvironmentCount.Should().Be(0,
			because: "bootstrap must not inject any default environments into the result");
		result.Report.ResolvedActiveEnvironmentKey.Should().BeNull(
			because: "there is no active environment when the environment map is empty");
	}

	[Test]
	[Category("Unit")]
	[Description("Creates an empty appsettings.json when the file does not exist and returns healthy bootstrap state.")]
	public void GetResult_Should_Create_Empty_Settings_File_When_Missing() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.Status.Should().Be("healthy",
			because: "a missing appsettings.json should be initialized as an empty file so commands can validate their own arguments");
		result.Report.CanExecuteEnvTools.Should().BeFalse(
			because: "named-environment execution requires at least one registered environment");
		result.Report.EnvironmentCount.Should().Be(0,
			because: "the created file must not contain any default environments");
		fileSystem.File.Exists(SettingsRepository.AppSettingsFile).Should().BeTrue(
			because: "bootstrap should create an empty appsettings.json on first run");
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

	[Test]
	[Category("Unit")]
	[Description("Clears a legacy Autoupdate=false artifact (serialized when the field was a non-nullable bool) and stamps the settings version, restoring the enabled opt-out default.")]
	public void GetResult_Should_Reset_Legacy_Autoupdate_False_And_Stamp_Version() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "Autoupdate": false,
			  "Environments": {}
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		Settings persisted = JsonConvert.DeserializeObject<Settings>(persistedContent);

		// Assert
		result.Settings.Autoupdate.Should().BeNull(
			because: "the legacy false must be cleared so GetAutoupdate() falls back to the enabled opt-out default");
		result.Report.RepairsApplied.Should().ContainSingle(repair => repair.Code == "autoupdate-legacy-default-reset",
			because: "the migration must surface that auto-update was re-enabled so the user is informed");
		persisted.Autoupdate.Should().BeNull(
			because: "the cleared value must be persisted — NullValueHandling.Ignore drops the key entirely");
		persisted.SettingsVersion.Should().Be(1,
			because: "the settings version must be stamped so the one-time migration never runs again");
		persistedContent.Should().NotContain("Autoupdate",
			because: "a null Autoupdate is omitted from the file, restoring the pristine default state");
	}

	[Test]
	[Category("Unit")]
	[Description("A deliberate Autoupdate=false on a file already at the current settings version is preserved across bootstrap and never reverted — proving the opt-out is not broken.")]
	public void GetResult_Should_Preserve_Deliberate_Autoupdate_False_After_Migration() {
		// Arrange: file already migrated (SettingsVersion=1) with a deliberate opt-out
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "Autoupdate": false,
			  "SettingsVersion": 1,
			  "Environments": {}
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		Settings persisted = JsonConvert.DeserializeObject<Settings>(persistedContent);

		// Assert
		result.Settings.Autoupdate.Should().BeFalse(
			because: "once the file is at the current settings version the migration must not touch a deliberate opt-out");
		persisted.Autoupdate.Should().BeFalse(
			because: "the deliberate --disable must survive bootstrap so 'clio autoupdate --disable' is not silently broken");
		result.Report.RepairsApplied.Should().BeEmpty(
			because: "no migration should run when the settings version is already current");
	}

	[Test]
	[Category("Unit")]
	[Description("With applyRepairs:false the legacy Autoupdate migration is neither applied nor reported, and the file is left untouched.")]
	public void GetResult_Should_Not_Migrate_Autoupdate_When_ApplyRepairs_False() {
		// Arrange
		const string originalContent = """
			{
			  "Autoupdate": false,
			  "Environments": {}
			}
			""";
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(originalContent));
		SettingsBootstrapService service = new(fileSystem, applyRepairs: false);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		result.Report.RepairsApplied.Should().BeEmpty(
			because: "read-only bootstrap must not report a repair it did not persist");
		persistedContent.Should().Be(originalContent,
			because: "appsettings.json must not be modified when applyRepairs is false");
	}

	[Test]
	[Category("Unit")]
	[Description("A freshly created appsettings.json is stamped with the current settings version so new installs are never treated as legacy.")]
	public void GetResult_Should_Stamp_Version_On_Created_Empty_Settings_File() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		Settings persisted = JsonConvert.DeserializeObject<Settings>(persistedContent);

		// Assert
		persisted.SettingsVersion.Should().Be(1,
			because: "new installs must be born at the current settings version so the legacy migration never runs for them");
		result.Settings.Autoupdate.Should().BeNull(
			because: "a fresh file has no Autoupdate key, so auto-update stays at the enabled opt-out default");
	}
}
