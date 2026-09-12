using System.IO;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Infrastructure;
using Clio.UserEnvironment;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
		result.Report.RepairsApplied.Should().ContainSingle(
			repair => repair.Code == "deploy-creatio-site-port-range-added",
			because: "the unrelated settings-version migration may run without repairing the invalid active environment");
		Settings persisted = JsonConvert.DeserializeObject<Settings>(persistedContent);
		persisted.ActiveEnvironmentKey.Should().Be("wrong-dev",
			because: "the settings migration must preserve the invalid key for an explicit user decision");
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
	[Description("Preserves legacy Autoupdate=false while stamping the settings version.")]
	public void GetResult_ShouldPreserveLegacyAutoupdateFalse_WhenMigratingSettings() {
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
		result.Settings.Autoupdate.Clio.Enabled.Should().BeFalse(
			because: "legacy false must remain disabled with opt-in updates");
		result.Report.RepairsApplied.Should().NotContain(repair => repair.Code == "autoupdate-legacy-default-reset",
			because: "migration must never re-enable auto-update");
		persisted.Autoupdate.Clio.Enabled.Should().BeFalse(
			because: "the clio policy must remain disabled");
		persisted.SettingsVersion.Should().Be(3,
			because: "the settings version must be stamped so the one-time migration never runs again");
		persistedContent.Should().Contain("\"autoupdate\"",
			because: "the scalar setting is replaced by the scheduled policy object");
	}

	[Test]
	[Category("Unit")]
	[Description("A deliberate Autoupdate=false on a file already at the current settings version is preserved across bootstrap and never reverted — proving the opt-out is not broken.")]
	public void GetResult_Should_Preserve_Deliberate_Autoupdate_False_After_Migration() {
		// Arrange: file already migrated (SettingsVersion=2) with a deliberate opt-out
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "Autoupdate": false,
			  "SettingsVersion": 2,
			  "deploy-creatio-defaults": {
			    "site-port-range": [40100, 40199]
			  },
			  "Environments": {}
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		Settings persisted = JsonConvert.DeserializeObject<Settings>(persistedContent);

		// Assert
		result.Settings.Autoupdate.Clio.Enabled.Should().BeFalse(
			because: "once the file is at the current settings version the migration must not touch a deliberate opt-out");
		persisted.Autoupdate.Clio.Enabled.Should().BeFalse(
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
		JArray persistedRange = (JArray)JObject.Parse(persistedContent)["deploy-creatio-defaults"]!["site-port-range"]!;

		// Assert
		persisted.SettingsVersion.Should().Be(3,
			because: "new installs must be born at the current settings version so the legacy migration never runs for them");
		result.Settings.Autoupdate.Clio.Enabled.Should().BeFalse(
			because: "a fresh file must default to disabled clio updates");
		persisted.DeployCreatioDefaults.Should().NotBeNull(
			because: "fresh installations must visibly persist deploy-creatio-defaults in appsettings.json");
		persisted.DeployCreatioDefaults.SitePortRange.Should().Equal(new[] { 40100, 40199 },
			because: "fresh installations must receive the built-in inclusive automatic IIS port range");
		persistedRange.Values<int>().Should().Equal(new[] { 40100, 40199 },
			because: "the generated JSON must use the exact documented site-port-range key");
	}

	[Test]
	[Category("Unit")]
	[Description("Upgrading a version-1 settings file materializes deploy-creatio-defaults.site-port-range and preserves existing deployment defaults.")]
	public void GetResult_Should_Add_Default_Site_Port_Range_When_Upgrading_Version_One_Settings() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "SettingsVersion": 1,
			  "Environments": {},
			  "deploy-creatio-defaults": {
			    "db-server-name": "postgres-local",
			    "site-port": 40018
			  }
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);
		Settings persisted = JsonConvert.DeserializeObject<Settings>(persistedContent);
		JArray persistedRange = (JArray)JObject.Parse(persistedContent)["deploy-creatio-defaults"]!["site-port-range"]!;

		// Assert
		result.Report.RepairsApplied.Should().ContainSingle(
			repair => repair.Code == "deploy-creatio-site-port-range-added",
			because: "the version-2 migration should report the newly materialized automatic range");
		persisted.SettingsVersion.Should().Be(3,
			because: "the upgraded file must be stamped so the migration runs once");
		persisted.DeployCreatioDefaults.DbServerName.Should().Be("postgres-local",
			because: "upgrading the range must preserve the configured database default");
		persisted.DeployCreatioDefaults.SitePort.Should().Be(40018,
			because: "the backward-compatible fixed port must remain configured and take precedence");
		persisted.DeployCreatioDefaults.SitePortRange.Should().Equal(new[] { 40100, 40199 },
			because: "an absent range must be visibly populated with Clio's built-in range");
		persistedRange.Values<int>().Should().Equal(new[] { 40100, 40199 },
			because: "the migration must persist the exact documented site-port-range key");
	}

	[Test]
	[Category("Unit")]
	[Description("Upgrading preserves an explicitly configured empty site-port range so deployment validation can report it instead of silently replacing it.")]
	public void GetResult_ShouldPreserveEmptySitePortRange_WhenUpgrading() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "SettingsVersion": 1,
			  "Environments": {},
			  "deploy-creatio-defaults": {
			    "site-port-range": []
			  }
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		Settings persisted = JsonConvert.DeserializeObject<Settings>(
			fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		persisted.DeployCreatioDefaults.SitePortRange.Should().BeEmpty(
			because: "migration must distinguish an invalid configured value from an absent value");
		result.Report.RepairsApplied.Should().NotContain(
			repair => repair.Code == "deploy-creatio-site-port-range-added",
			because: "an explicitly configured range must not be overwritten during upgrade");
	}

	[Test]
	[Category("Unit")]
	[Description("Upgrading settings preserves a user-configured deploy-creatio site-port-range instead of replacing it with the built-in range.")]
	public void GetResult_Should_Preserve_Custom_Site_Port_Range_When_Upgrading() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "SettingsVersion": 1,
			  "Environments": {},
			  "deploy-creatio-defaults": {
			    "site-port-range": [41000, 41010]
			  }
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		Settings persisted = JsonConvert.DeserializeObject<Settings>(
			fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile));

		// Assert
		persisted.DeployCreatioDefaults.SitePortRange.Should().Equal(new[] { 41000, 41010 },
			because: "settings migration must never overwrite a user's configured range");
		result.Report.RepairsApplied.Should().NotContain(
			repair => repair.Code == "deploy-creatio-site-port-range-added",
			because: "no range was added when a user-configured range already existed");
	}

	/// <summary>
	/// A settings file that is valid JSON but whose autoupdate section has a shape this build cannot bind —
	/// exactly what a newer clio writes under an older resident MCP worker (issue #1462).
	/// </summary>
	private const string FutureShapedSettings = """
		{
		  "$schema": "./schema.json",
		  "ActiveEnvironmentKey": "dev",
		  "SettingsVersion": 3,
		  "autoupdate": {
		    "clio": { "enabled": { "future": true }, "frequency-minutes": 480 }
		  },
		  "Environments": {
		    "dev": {
		      "Uri": "http://localhost",
		      "Login": "Supervisor",
		      "Password": "Supervisor"
		    }
		  }
		}
		""";

	[Test]
	[Category("Unit")]
	[Description("Reports settings-shape-mismatch, not settings-file-unreadable, for a valid JSON file whose section this clio build cannot bind.")]
	public void GetResult_Should_Report_Shape_Mismatch_When_Section_Cannot_Bind() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(FutureShapedSettings));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.Issues.Should().ContainSingle(issue =>
				issue.Code == SettingsBootstrapService.SettingsShapeMismatchCode,
			because: "the file parses as JSON, so the failure is a version/shape mismatch and not an unreadable file");
		result.Report.Issues.Should().NotContain(issue => issue.Code == SettingsBootstrapService.SettingsFileUnreadableCode,
			because: "settings-file-unreadable must stay reserved for a file that is genuinely not parseable");
		SettingsIssue issue = result.Report.Issues[0];
		issue.Message.Should().Contain("autoupdate",
			because: "the message must name the section that failed to bind so the reader can see what changed");
		issue.Message.Should().Contain("this clio version",
			because: "the message must attribute the mismatch to the running build, not to the file");
		issue.Message.Should().Contain("by hand",
			because: "this fixture carries no evidence of a newer writer, so the honest advice is to correct the member");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps environments usable when an unrelated section fails to bind: status is issues-detected and environment-scoped tools may still run.")]
	public void GetResult_Should_Keep_Environments_Usable_When_Unrelated_Section_Cannot_Bind() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(FutureShapedSettings));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.Status.Should().Be("issues-detected",
			because: "an unbindable side section degrades the configuration; it does not destroy it");
		result.Report.EnvironmentCount.Should().Be(1,
			because: "the environments in the file are intact and must still be bound");
		result.Report.CanExecuteEnvTools.Should().BeTrue(
			because: "the active environment resolved, so environment-scoped tools must remain available");
		result.ResolvedEnvironment!.Uri.Should().Be("http://localhost",
			because: "the resolved environment must carry the values the file actually holds");
	}

	[Test]
	[Category("Unit")]
	[Description("Never writes the settings file back while a section could not be bound, even when a migration is pending.")]
	public void GetResult_Should_Not_Write_File_Back_When_Section_Cannot_Bind() {
		// Arrange
		string originalContent = FutureShapedSettings.Replace("\"SettingsVersion\": 3", "\"SettingsVersion\": 1");
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(originalContent));
		SettingsBootstrapService service = new(fileSystem, applyRepairs: true);

		// Act
		SettingsBootstrapResult result = service.GetResult();
		string persistedContent = fileSystem.File.ReadAllText(SettingsRepository.AppSettingsFile);

		// Assert
		persistedContent.Should().Be(originalContent,
			because: "rewriting the file from a model that dropped an unbindable section would destroy the settings a newer clio wrote");
		result.Report.RepairsApplied.Should().BeEmpty(
			because: "no repair may be reported when the degraded mode deliberately suppressed the write");
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to broken only when the environment collection itself cannot be bound.")]
	public void GetResult_Should_Report_Broken_When_Environments_Cannot_Bind() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Environments": "not-a-dictionary"
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.Status.Should().Be("broken",
			because: "without an environment collection there is nothing an environment-scoped tool could run against");
		result.Report.CanExecuteEnvTools.Should().BeFalse(
			because: "a broken environment collection must stop environment-scoped execution");
		result.Report.Issues.Should().ContainSingle(issue =>
				issue.Code == SettingsBootstrapService.SettingsShapeMismatchCode,
			because: "the file is still valid JSON, so the reason is a shape mismatch even when it is fatal");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps settings-file-unreadable for a file that is not valid JSON at all.")]
	public void GetResult_Should_Report_Unreadable_When_File_Is_Not_Valid_Json() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("{ this is not json"));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.Status.Should().Be("broken",
			because: "a damaged file cannot be used and the caller must be told to repair it");
		result.Report.Issues.Should().ContainSingle(issue => issue.Code == SettingsBootstrapService.SettingsFileUnreadableCode,
			because: "a genuine parse failure must keep the code that sends the reader to the file");
	}

	private static string EnvironmentSettingsWith(string environmentsJson) => """
		{
		  "ActiveEnvironmentKey": "prod",
		  "SettingsVersion": 3,
		  "Environments": __ENVIRONMENTS__
		}
		""".Replace("__ENVIRONMENTS__", environmentsJson);

	[TestCase("""{ "prod": { "Uri": "https://prod", "Safe": "yes" } }""", "Safe")]
	[TestCase("""{ "prod": { "Uri": { "host": "prod" } } }""", "Uri")]
	[TestCase("""{ "prod": "oops" }""", "prod")]
	[Category("Unit")]
	[Description("Refuses to load environments at all when any member inside them fails to bind, because a member that silently took its default would change what commands do.")]
	public void GetResult_Should_Report_Broken_When_An_Environment_Member_Cannot_Bind(
		string environmentsJson, string expectedMemberName) {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile,
			new MockFileData(EnvironmentSettingsWith(environmentsJson)));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.Status.Should().Be("broken",
			because: "an environment whose Safe flag or Uri quietly defaulted would run destructive commands against the wrong stand without confirmation");
		result.Report.CanExecuteEnvTools.Should().BeFalse(
			because: "no environment-scoped tool may run against a half-bound environment");
		result.Report.ShapeMismatch.Should().NotBeNull(
			because: "the file is valid JSON, so the reason is still a shape mismatch");
		result.Report.ShapeMismatch!.Message.Should().Contain(expectedMemberName,
			because: "the message must name the member the user has to correct");
		result.Report.ShapeMismatch.Message.Should().Contain("by hand",
			because: "an environment entry is hand-edited far more often than a newer clio rewrites it, so the advice must be to correct it");
	}

	[Test]
	[Category("Unit")]
	[Description("Names the environment key, not just the member path, when one environment entry fails to bind.")]
	public void GetResult_Should_Name_The_Environment_Key_That_Cannot_Bind() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData(
			EnvironmentSettingsWith("""{ "prod": { "Uri": "https://prod", "Safe": "yes" } }""")));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.ShapeMismatch!.Message.Should().Contain("'prod'",
			because: "with several environments configured, the key is what tells the user which entry to open");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the version-skew wording only with evidence of a newer writer: a settings version this build does not know.")]
	public void GetResult_Should_Use_Version_Skew_Wording_When_SettingsVersion_Is_From_The_Future() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "SettingsVersion": 999,
			  "Environments": {},
			  "autoupdate": { "clio": { "enabled": { "future": true } } }
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.ShapeMismatch!.Message.Should().Contain("A newer clio has written the file",
			because: "a settings version this build does not know is direct evidence of a newer writer");
		result.Report.ShapeMismatch.Message.Should().Contain("update-cli",
			because: "a user with no MCP session to restart still needs a way out of the skew");
	}

	[Test]
	[Category("Unit")]
	[Description("Uses the correct-it-by-hand wording with no evidence of a newer writer, instead of telling the user to wait for a restart that fixes nothing.")]
	public void GetResult_Should_Use_Hand_Correction_Wording_Without_Evidence_Of_A_Newer_Writer() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "SettingsVersion": 3,
			  "Environments": {},
			  "autoupdate": { "clio": { "frequency-minutes": "soon" } }
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.ShapeMismatch!.Message.Should().Contain("by hand",
			because: "a typo in a known member is corrected by the user, not by restarting anything");
		result.Report.ShapeMismatch.Message.Should().NotContain("A newer clio",
			because: "claiming a newer clio wrote the file, with no evidence, sends the user to wait instead of to fix");
	}

	[Test]
	[Category("Unit")]
	[Description("Treats unknown members carried in the same section as the failure as evidence that a newer clio wrote the file.")]
	public void GetResult_Should_Use_Version_Skew_Wording_When_The_Section_Carries_Unknown_Members() {
		// Arrange
		MockFileSystem fileSystem = TestFileSystem.MockFileSystem();
		fileSystem.AddFile(SettingsRepository.AppSettingsFile, new MockFileData("""
			{
			  "SettingsVersion": 3,
			  "Environments": {},
			  "autoupdate": {
			    "clio": { "enabled": { "future": true } },
			    "browser": { "enabled": true }
			  }
			}
			"""));
		SettingsBootstrapService service = new(fileSystem);

		// Act
		SettingsBootstrapResult result = service.GetResult();

		// Assert
		result.Report.ShapeMismatch!.Message.Should().Contain("A newer clio has written the file",
			because: "a member this build has never heard of, in the same section as the failure, is the evidence a newer writer leaves behind");
	}
}
