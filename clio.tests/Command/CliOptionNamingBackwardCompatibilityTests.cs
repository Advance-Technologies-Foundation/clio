using Clio.Command;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using CommandLine;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Backward-compatibility tests for CLI option renames: every camelCase/PascalCase
/// option must accept both the legacy form (hidden alias) and the new kebab-case form.
/// TDD workflow: new-form tests fail first (RED), fix adds kebab alias, both pass (GREEN).
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
internal sealed class CliOptionNamingBackwardCompatibilityTests {

	// ─── EnvironmentOptions ────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --Password still parses after primary rename to --password")]
	public void EnvironmentOptions_Password_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--Password", "secret"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Password backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Password.Should().Be("secret", because: "legacy --Password must map to the Password property");
	}

	[Test]
	[Description("New --password primary option parses correctly")]
	public void EnvironmentOptions_Password_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--password", "secret"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--password must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Password.Should().Be("secret", because: "new --password must map to the Password property");
	}

	[Test]
	[Description("Legacy --Login still parses after primary rename to --login")]
	public void EnvironmentOptions_Login_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--Login", "admin"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Login backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Login.Should().Be("admin", because: "legacy --Login must map to the Login property");
	}

	[Test]
	[Description("New --login primary option parses correctly")]
	public void EnvironmentOptions_Login_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--login", "admin"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--login must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Login.Should().Be("admin", because: "new --login must map to the Login property");
	}

	[Test]
	[Description("Legacy --Environment still parses after primary rename to --environment")]
	public void EnvironmentOptions_Environment_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--Environment", "prod"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Environment backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Environment.Should().Be("prod", because: "legacy --Environment must map to the Environment property");
	}

	[Test]
	[Description("New --environment primary option parses correctly")]
	public void EnvironmentOptions_Environment_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--environment", "prod"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--environment must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Environment.Should().Be("prod", because: "new --environment must map to the Environment property");
	}

	[Test]
	[Description("Legacy --Maintainer still parses after primary rename to --maintainer")]
	public void EnvironmentOptions_Maintainer_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--Maintainer", "team"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Maintainer backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Maintainer.Should().Be("team", because: "legacy --Maintainer must map to the Maintainer property");
	}

	[Test]
	[Description("New --maintainer primary option parses correctly")]
	public void EnvironmentOptions_Maintainer_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--maintainer", "team"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--maintainer must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Maintainer.Should().Be("team", because: "new --maintainer must map to the Maintainer property");
	}

	[Test]
	[Description("Legacy --WorkspacePathes still parses after primary rename to --workspace-pathes")]
	public void EnvironmentOptions_WorkspacePathes_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--WorkspacePathes", "/ws"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--WorkspacePathes backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.WorkspacePathes.Should().Be("/ws", because: "legacy --WorkspacePathes must map to the WorkspacePathes property");
	}

	[Test]
	[Description("New --workspace-pathes primary option parses correctly")]
	public void EnvironmentOptions_WorkspacePathes_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--workspace-pathes", "/ws"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--workspace-pathes must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.WorkspacePathes.Should().Be("/ws", because: "new --workspace-pathes must map to the WorkspacePathes property");
	}

	[Test]
	[Description("Legacy --Safe still parses after primary rename to --safe")]
	public void EnvironmentOptions_Safe_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--Safe", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Safe backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Safe.Should().Be("true", because: "legacy --Safe must map to the Safe property");
	}

	[Test]
	[Description("New --safe primary option parses correctly")]
	public void EnvironmentOptions_Safe_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--safe", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--safe must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Safe.Should().Be("true", because: "new --safe must map to the Safe property");
	}

	[Test]
	[Description("Legacy --clientId still parses after primary rename to --client-id")]
	public void EnvironmentOptions_ClientId_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--clientId", "abc"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--clientId backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ClientId.Should().Be("abc", because: "legacy --clientId must map to the ClientId property");
	}

	[Test]
	[Description("New --client-id primary option parses correctly")]
	public void EnvironmentOptions_ClientId_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--client-id", "abc"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--client-id must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ClientId.Should().Be("abc", because: "new --client-id must map to the ClientId property");
	}

	[Test]
	[Description("Legacy --clientSecret still parses after primary rename to --client-secret")]
	public void EnvironmentOptions_ClientSecret_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--clientSecret", "xyz"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--clientSecret backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ClientSecret.Should().Be("xyz", because: "legacy --clientSecret must map to the ClientSecret property");
	}

	[Test]
	[Description("New --client-secret primary option parses correctly")]
	public void EnvironmentOptions_ClientSecret_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--client-secret", "xyz"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--client-secret must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ClientSecret.Should().Be("xyz", because: "new --client-secret must map to the ClientSecret property");
	}

	[Test]
	[Description("Legacy --authAppUri still parses after primary rename to --auth-app-uri")]
	public void EnvironmentOptions_AuthAppUri_LegacyForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--authAppUri", "https://auth"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--authAppUri backward compat alias must be accepted");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AuthAppUri.Should().Be("https://auth", because: "legacy --authAppUri must map to the AuthAppUri property");
	}

	[Test]
	[Description("New --auth-app-uri primary option parses correctly")]
	public void EnvironmentOptions_AuthAppUri_KebabForm_Parses() {
		ParserResult<EnvironmentOptions> result = Parser.Default.ParseArguments<EnvironmentOptions>(["--auth-app-uri", "https://auth"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--auth-app-uri must be the primary accepted form");
		EnvironmentOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AuthAppUri.Should().Be("https://auth", because: "new --auth-app-uri must map to the AuthAppUri property");
	}

	// ─── PushPkgOptions ────────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --InstallSqlScript still parses after primary rename to --install-sql-script")]
	public void PushPkgOptions_InstallSqlScript_LegacyForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--InstallSqlScript", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--InstallSqlScript backward compat alias must be accepted");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.InstallSqlScript.Should().BeTrue(because: "legacy --InstallSqlScript must set the property to true");
	}

	[Test]
	[Description("New --install-sql-script primary option parses correctly")]
	public void PushPkgOptions_InstallSqlScript_KebabForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--install-sql-script", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--install-sql-script must be the primary accepted form");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.InstallSqlScript.Should().BeTrue(because: "new --install-sql-script must set the property to true");
	}

	[Test]
	[Description("Legacy --InstallPackageData still parses after primary rename to --install-package-data")]
	public void PushPkgOptions_InstallPackageData_LegacyForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--InstallPackageData", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--InstallPackageData backward compat alias must be accepted");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.InstallPackageData.Should().BeTrue(because: "legacy --InstallPackageData must set the property to true");
	}

	[Test]
	[Description("New --install-package-data primary option parses correctly")]
	public void PushPkgOptions_InstallPackageData_KebabForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--install-package-data", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--install-package-data must be the primary accepted form");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.InstallPackageData.Should().BeTrue(because: "new --install-package-data must set the property to true");
	}

	[Test]
	[Description("Legacy --ContinueIfError still parses after primary rename to --continue-if-error")]
	public void PushPkgOptions_ContinueIfError_LegacyForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--ContinueIfError", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--ContinueIfError backward compat alias must be accepted");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ContinueIfError.Should().BeTrue(because: "legacy --ContinueIfError must set the property to true");
	}

	[Test]
	[Description("New --continue-if-error primary option parses correctly")]
	public void PushPkgOptions_ContinueIfError_KebabForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--continue-if-error", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--continue-if-error must be the primary accepted form");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ContinueIfError.Should().BeTrue(because: "new --continue-if-error must set the property to true");
	}

	[Test]
	[Description("Legacy --SkipConstraints still parses after primary rename to --skip-constraints")]
	public void PushPkgOptions_SkipConstraints_LegacyForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--SkipConstraints", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--SkipConstraints backward compat alias must be accepted");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SkipConstraints.Should().BeTrue(because: "legacy --SkipConstraints must set the property to true");
	}

	[Test]
	[Description("New --skip-constraints primary option parses correctly")]
	public void PushPkgOptions_SkipConstraints_KebabForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--skip-constraints", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--skip-constraints must be the primary accepted form");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SkipConstraints.Should().BeTrue(because: "new --skip-constraints must set the property to true");
	}

	[Test]
	[Description("Legacy --SkipValidateActions still parses after primary rename to --skip-validate-actions")]
	public void PushPkgOptions_SkipValidateActions_LegacyForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--SkipValidateActions", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--SkipValidateActions backward compat alias must be accepted");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SkipValidateActions.Should().BeTrue(because: "legacy --SkipValidateActions must set the property to true");
	}

	[Test]
	[Description("New --skip-validate-actions primary option parses correctly")]
	public void PushPkgOptions_SkipValidateActions_KebabForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--skip-validate-actions", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--skip-validate-actions must be the primary accepted form");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SkipValidateActions.Should().BeTrue(because: "new --skip-validate-actions must set the property to true");
	}

	[Test]
	[Description("Legacy --ExecuteValidateActions still parses after primary rename to --execute-validate-actions")]
	public void PushPkgOptions_ExecuteValidateActions_LegacyForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--ExecuteValidateActions", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--ExecuteValidateActions backward compat alias must be accepted");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ExecuteValidateActions.Should().BeTrue(because: "legacy --ExecuteValidateActions must set the property to true");
	}

	[Test]
	[Description("New --execute-validate-actions primary option parses correctly")]
	public void PushPkgOptions_ExecuteValidateActions_KebabForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--execute-validate-actions", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--execute-validate-actions must be the primary accepted form");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ExecuteValidateActions.Should().BeTrue(because: "new --execute-validate-actions must set the property to true");
	}

	[Test]
	[Description("Legacy --IsForceUpdateAllColumns still parses after primary rename to --is-force-update-all-columns")]
	public void PushPkgOptions_IsForceUpdateAllColumns_LegacyForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--IsForceUpdateAllColumns", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--IsForceUpdateAllColumns backward compat alias must be accepted");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsForceUpdateAllColumns.Should().BeTrue(because: "legacy --IsForceUpdateAllColumns must set the property to true");
	}

	[Test]
	[Description("New --is-force-update-all-columns primary option parses correctly")]
	public void PushPkgOptions_IsForceUpdateAllColumns_KebabForm_Parses() {
		ParserResult<PushPkgOptions> result = Parser.Default.ParseArguments<PushPkgOptions>(["--is-force-update-all-columns", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--is-force-update-all-columns must be the primary accepted form");
		PushPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsForceUpdateAllColumns.Should().BeTrue(because: "new --is-force-update-all-columns must set the property to true");
	}

	// ─── WorkspaceOptions (RestoreWorkspaceCommand) ────────────────────────────

	[Test]
	[Description("Legacy --IsNugetRestore still parses after primary rename to --is-nuget-restore")]
	public void WorkspaceOptions_IsNugetRestore_LegacyForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--IsNugetRestore", "false"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--IsNugetRestore backward compat alias must be accepted");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsNugetRestore.Should().BeFalse(because: "legacy --IsNugetRestore must map to the IsNugetRestore property");
	}

	[Test]
	[Description("New --is-nuget-restore primary option parses correctly")]
	public void WorkspaceOptions_IsNugetRestore_KebabForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--is-nuget-restore", "false"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--is-nuget-restore must be the primary accepted form");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsNugetRestore.Should().BeFalse(because: "new --is-nuget-restore must map to the IsNugetRestore property");
	}

	[Test]
	[Description("Legacy --IsCreateSolution still parses after primary rename to --is-create-solution")]
	public void WorkspaceOptions_IsCreateSolution_LegacyForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--IsCreateSolution", "false"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--IsCreateSolution backward compat alias must be accepted");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsCreateSolution.Should().BeFalse(because: "legacy --IsCreateSolution must map to the property");
	}

	[Test]
	[Description("New --is-create-solution primary option parses correctly")]
	public void WorkspaceOptions_IsCreateSolution_KebabForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--is-create-solution", "false"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--is-create-solution must be the primary accepted form");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsCreateSolution.Should().BeFalse(because: "new --is-create-solution must map to the property");
	}

	[Test]
	[Description("Legacy --AppCode still parses after primary rename to --app-code")]
	public void WorkspaceOptions_AppCode_LegacyForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--AppCode", "MyApp"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--AppCode backward compat alias must be accepted");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AppCode.Should().Be("MyApp", because: "legacy --AppCode must map to the AppCode property");
	}

	[Test]
	[Description("New --app-code primary option parses correctly")]
	public void WorkspaceOptions_AppCode_KebabForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--app-code", "MyApp"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--app-code must be the primary accepted form");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AppCode.Should().Be("MyApp", because: "new --app-code must map to the AppCode property");
	}

	[Test]
	[Description("Legacy --AddBuildProps still parses after primary rename to --add-build-props")]
	public void WorkspaceOptions_AddBuildProps_LegacyForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--AddBuildProps"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--AddBuildProps backward compat alias must be accepted");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AddBuildProps.Should().BeTrue(because: "legacy --AddBuildProps flag must set AddBuildProps to true");
	}

	[Test]
	[Description("New --add-build-props primary option parses correctly")]
	public void WorkspaceOptions_AddBuildProps_KebabForm_Parses() {
		ParserResult<WorkspaceOptions> result = Parser.Default.ParseArguments<WorkspaceOptions>(["--add-build-props"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--add-build-props must be the primary accepted form");
		WorkspaceOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AddBuildProps.Should().BeTrue(because: "new --add-build-props flag must set AddBuildProps to true");
	}

	// ─── FeatureOptions ────────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --SysAdminUnitName still parses after primary rename to --sys-admin-unit-name")]
	public void FeatureOptions_SysAdminUnitName_LegacyForm_Parses() {
		ParserResult<FeatureOptions> result = Parser.Default.ParseArguments<FeatureOptions>(["FeatureCode", "1", "--SysAdminUnitName", "SysAdminUnit"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--SysAdminUnitName backward compat alias must be accepted");
		FeatureOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SysAdminUnitName.Should().Be("SysAdminUnit", because: "legacy --SysAdminUnitName must map to the property");
	}

	[Test]
	[Description("New --sys-admin-unit-name primary option parses correctly")]
	public void FeatureOptions_SysAdminUnitName_KebabForm_Parses() {
		ParserResult<FeatureOptions> result = Parser.Default.ParseArguments<FeatureOptions>(["FeatureCode", "1", "--sys-admin-unit-name", "SysAdminUnit"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--sys-admin-unit-name must be the primary accepted form");
		FeatureOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SysAdminUnitName.Should().Be("SysAdminUnit", because: "new --sys-admin-unit-name must map to the property");
	}

	[Test]
	[Description("Legacy --UseFeatureWebService still parses after primary rename to --use-feature-web-service")]
	public void FeatureOptions_UseFeatureWebService_LegacyForm_Parses() {
		ParserResult<FeatureOptions> result = Parser.Default.ParseArguments<FeatureOptions>(["FeatureCode", "1", "--UseFeatureWebService"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--UseFeatureWebService backward compat alias must be accepted");
		FeatureOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.UseFeatureWebService.Should().BeTrue(because: "legacy --UseFeatureWebService must set the property to true");
	}

	[Test]
	[Description("New --use-feature-web-service primary option parses correctly")]
	public void FeatureOptions_UseFeatureWebService_KebabForm_Parses() {
		ParserResult<FeatureOptions> result = Parser.Default.ParseArguments<FeatureOptions>(["FeatureCode", "1", "--use-feature-web-service"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--use-feature-web-service must be the primary accepted form");
		FeatureOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.UseFeatureWebService.Should().BeTrue(because: "new --use-feature-web-service must set the property to true");
	}

	// ─── InstallOptions (ReportPath) ───────────────────────────────────────────

	[Test]
	[Description("Legacy --ReportPath still parses after primary rename to --report-path")]
	public void InstallOptions_ReportPath_LegacyForm_Parses() {
		ParserResult<InstallOptions> result = Parser.Default.ParseArguments<InstallOptions>(["-r", "/tmp/report.log"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "-r short flag must still be accepted");
		InstallOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ReportPath.Should().Be("/tmp/report.log", because: "-r short flag must map to ReportPath");
	}

	[Test]
	[Description("Legacy --ReportPath long form still parses after primary rename to --report-path")]
	public void InstallOptions_ReportPath_LegacyLongForm_Parses() {
		ParserResult<InstallOptions> result = Parser.Default.ParseArguments<InstallOptions>(["--ReportPath", "/tmp/report.log"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--ReportPath backward compat alias must be accepted");
		InstallOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ReportPath.Should().Be("/tmp/report.log", because: "legacy --ReportPath must map to ReportPath property");
	}

	[Test]
	[Description("New --report-path primary option parses correctly")]
	public void InstallOptions_ReportPath_KebabForm_Parses() {
		ParserResult<InstallOptions> result = Parser.Default.ParseArguments<InstallOptions>(["--report-path", "/tmp/report.log"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--report-path must be the primary accepted form");
		InstallOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ReportPath.Should().Be("/tmp/report.log", because: "new --report-path must map to ReportPath property");
	}

	// ─── RegAppOptions ─────────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --ActiveEnvironment still parses after primary rename to --active-environment")]
	public void RegAppOptions_ActiveEnvironment_LegacyForm_Parses() {
		ParserResult<RegAppOptions> result = Parser.Default.ParseArguments<RegAppOptions>(["-a", "prod"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "-a short flag must still be accepted");
		RegAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ActiveEnvironment.Should().Be("prod", because: "legacy -a short flag must map to the ActiveEnvironment property");
	}

	[Test]
	[Description("New --active-environment primary option parses correctly")]
	public void RegAppOptions_ActiveEnvironment_KebabForm_Parses() {
		ParserResult<RegAppOptions> result = Parser.Default.ParseArguments<RegAppOptions>(["--active-environment", "prod"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--active-environment must be the primary accepted form");
		RegAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ActiveEnvironment.Should().Be("prod", because: "new --active-environment must map to the ActiveEnvironment property");
	}

	[Test]
	[Description("Legacy --checkLogin still parses after primary rename to --check-login")]
	public void RegAppOptions_CheckLogin_LegacyForm_Parses() {
		ParserResult<RegAppOptions> result = Parser.Default.ParseArguments<RegAppOptions>(["--checkLogin"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--checkLogin backward compat alias must be accepted");
		RegAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.CheckLogin.Should().BeTrue(because: "legacy --checkLogin must set the property to true");
	}

	[Test]
	[Description("New --check-login primary option parses correctly")]
	public void RegAppOptions_CheckLogin_KebabForm_Parses() {
		ParserResult<RegAppOptions> result = Parser.Default.ParseArguments<RegAppOptions>(["--check-login"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--check-login must be the primary accepted form");
		RegAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.CheckLogin.Should().BeTrue(because: "new --check-login must set the property to true");
	}

	// ─── RestoreDbCommandOptions ───────────────────────────────────────────────

	[Test]
	[Description("Legacy --dbName still parses after primary rename to --db-name")]
	public void RestoreDbCommandOptions_DbName_LegacyForm_Parses() {
		ParserResult<RestoreDbCommandOptions> result = Parser.Default.ParseArguments<RestoreDbCommandOptions>(["--dbName", "MyDb"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--dbName backward compat alias must be accepted");
		RestoreDbCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DbName.Should().Be("MyDb", because: "legacy --dbName must map to the DbName property");
	}

	[Test]
	[Description("New --db-name primary option parses correctly")]
	public void RestoreDbCommandOptions_DbName_KebabForm_Parses() {
		ParserResult<RestoreDbCommandOptions> result = Parser.Default.ParseArguments<RestoreDbCommandOptions>(["--db-name", "MyDb"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--db-name must be the primary accepted form");
		RestoreDbCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DbName.Should().Be("MyDb", because: "new --db-name must map to the DbName property");
	}

	[Test]
	[Description("Legacy --backupPath still parses after primary rename to --backup-path")]
	public void RestoreDbCommandOptions_BackupPath_LegacyForm_Parses() {
		ParserResult<RestoreDbCommandOptions> result = Parser.Default.ParseArguments<RestoreDbCommandOptions>(["--backupPath", "/tmp/backup.bak"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--backupPath backward compat alias must be accepted");
		RestoreDbCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.BackupPath.Should().Be("/tmp/backup.bak", because: "legacy --backupPath must map to the BackupPath property");
	}

	[Test]
	[Description("New --backup-path primary option parses correctly")]
	public void RestoreDbCommandOptions_BackupPath_KebabForm_Parses() {
		ParserResult<RestoreDbCommandOptions> result = Parser.Default.ParseArguments<RestoreDbCommandOptions>(["--backup-path", "/tmp/backup.bak"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--backup-path must be the primary accepted form");
		RestoreDbCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.BackupPath.Should().Be("/tmp/backup.bak", because: "new --backup-path must map to the BackupPath property");
	}

	[Test]
	[Description("Legacy --dbServerName still parses after primary rename to --db-server-name")]
	public void RestoreDbCommandOptions_DbServerName_LegacyForm_Parses() {
		ParserResult<RestoreDbCommandOptions> result = Parser.Default.ParseArguments<RestoreDbCommandOptions>(["--dbServerName", "localhost"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--dbServerName backward compat alias must be accepted");
		RestoreDbCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DbServerName.Should().Be("localhost", because: "legacy --dbServerName must map to the DbServerName property");
	}

	[Test]
	[Description("New --db-server-name primary option parses correctly")]
	public void RestoreDbCommandOptions_DbServerName_KebabForm_Parses() {
		ParserResult<RestoreDbCommandOptions> result = Parser.Default.ParseArguments<RestoreDbCommandOptions>(["--db-server-name", "localhost"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--db-server-name must be the primary accepted form");
		RestoreDbCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DbServerName.Should().Be("localhost", because: "new --db-server-name must map to the DbServerName property");
	}

	// ─── SetFsmConfigOptions ───────────────────────────────────────────────────

	[Test]
	[Description("Legacy --physicalPath still parses after primary rename to --physical-path")]
	public void SetFsmConfigOptions_PhysicalPath_LegacyForm_Parses() {
		ParserResult<SetFsmConfigOptions> result = Parser.Default.ParseArguments<SetFsmConfigOptions>(["--physicalPath", "/app"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--physicalPath backward compat alias must be accepted");
		SetFsmConfigOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.PhysicalPath.Should().Be("/app", because: "legacy --physicalPath must map to the PhysicalPath property");
	}

	[Test]
	[Description("New --physical-path primary option parses correctly")]
	public void SetFsmConfigOptions_PhysicalPath_KebabForm_Parses() {
		ParserResult<SetFsmConfigOptions> result = Parser.Default.ParseArguments<SetFsmConfigOptions>(["--physical-path", "/app"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--physical-path must be the primary accepted form");
		SetFsmConfigOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.PhysicalPath.Should().Be("/app", because: "new --physical-path must map to the PhysicalPath property");
	}

	// ─── AddItemOptions ────────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --DestinationPath still parses in AddItemOptions after primary rename to --destination-path")]
	public void AddItemOptions_DestinationPath_LegacyForm_Parses() {
		ParserResult<AddItemOptions> result = Parser.Default.ParseArguments<AddItemOptions>(["Entity", "Test", "--DestinationPath", "/tmp"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationPath backward compat alias must be accepted");
		AddItemOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/tmp", because: "legacy --DestinationPath must map to the DestinationPath property");
	}

	[Test]
	[Description("New --destination-path primary option parses correctly in AddItemOptions")]
	public void AddItemOptions_DestinationPath_KebabForm_Parses() {
		ParserResult<AddItemOptions> result = Parser.Default.ParseArguments<AddItemOptions>(["Entity", "Test", "--destination-path", "/tmp"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-path must be the primary accepted form");
		AddItemOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/tmp", because: "new --destination-path must map to the DestinationPath property");
	}

	[Test]
	[Description("Legacy --Namespace still parses in AddItemOptions after primary rename to --namespace")]
	public void AddItemOptions_Namespace_LegacyForm_Parses() {
		ParserResult<AddItemOptions> result = Parser.Default.ParseArguments<AddItemOptions>(["Entity", "Test", "--Namespace", "MyNs"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Namespace backward compat alias must be accepted");
		AddItemOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Namespace.Should().Be("MyNs", because: "legacy --Namespace must map to the Namespace property");
	}

	[Test]
	[Description("New --namespace primary option parses correctly in AddItemOptions")]
	public void AddItemOptions_Namespace_KebabForm_Parses() {
		ParserResult<AddItemOptions> result = Parser.Default.ParseArguments<AddItemOptions>(["Entity", "Test", "--namespace", "MyNs"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--namespace must be the primary accepted form");
		AddItemOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Namespace.Should().Be("MyNs", because: "new --namespace must map to the Namespace property");
	}

	[Test]
	[Description("Legacy --Culture still parses in AddItemOptions after primary rename to --culture")]
	public void AddItemOptions_Culture_LegacyForm_Parses() {
		ParserResult<AddItemOptions> result = Parser.Default.ParseArguments<AddItemOptions>(["Entity", "Test", "--Culture", "ru-RU"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Culture backward compat alias must be accepted");
		AddItemOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Culture.Should().Be("ru-RU", because: "legacy --Culture must map to the Culture property");
	}

	[Test]
	[Description("New --culture primary option parses correctly in AddItemOptions")]
	public void AddItemOptions_Culture_KebabForm_Parses() {
		ParserResult<AddItemOptions> result = Parser.Default.ParseArguments<AddItemOptions>(["Entity", "Test", "--culture", "ru-RU"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--culture must be the primary accepted form");
		AddItemOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Culture.Should().Be("ru-RU", because: "new --culture must map to the Culture property");
	}

	// ─── AddPackageOptions ─────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --asApp still parses in AddPackageOptions after primary rename to --as-app")]
	public void AddPackageOptions_AsApp_LegacyForm_Parses() {
		ParserResult<AddPackageOptions> result = Parser.Default.ParseArguments<AddPackageOptions>(["TestPkg", "--asApp"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--asApp backward compat alias must be accepted");
		AddPackageOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AsApp.Should().BeTrue(because: "legacy --asApp must set the AsApp property to true");
	}

	[Test]
	[Description("New --as-app primary option parses correctly in AddPackageOptions")]
	public void AddPackageOptions_AsApp_KebabForm_Parses() {
		ParserResult<AddPackageOptions> result = Parser.Default.ParseArguments<AddPackageOptions>(["TestPkg", "--as-app"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--as-app must be the primary accepted form");
		AddPackageOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.AsApp.Should().BeTrue(because: "new --as-app must set the AsApp property to true");
	}

	// ─── ListenOptions ─────────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --logPattern still parses after primary rename to --log-pattern")]
	public void ListenOptions_LogPattern_LegacyForm_Parses() {
		ParserResult<ListenOptions> result = Parser.Default.ParseArguments<ListenOptions>(["--logPattern", "ExceptNoisyLoggers"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--logPattern backward compat alias must be accepted");
		ListenOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.LogPattern.Should().Be("ExceptNoisyLoggers", because: "legacy --logPattern must map to the LogPattern property");
	}

	[Test]
	[Description("New --log-pattern primary option parses correctly")]
	public void ListenOptions_LogPattern_KebabForm_Parses() {
		ParserResult<ListenOptions> result = Parser.Default.ParseArguments<ListenOptions>(["--log-pattern", "ExceptNoisyLoggers"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--log-pattern must be the primary accepted form");
		ListenOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.LogPattern.Should().Be("ExceptNoisyLoggers", because: "new --log-pattern must map to the LogPattern property");
	}

	[Test]
	[Description("Legacy --FileName still parses in ListenOptions after primary rename to --file-name")]
	public void ListenOptions_FileName_LegacyForm_Parses() {
		ParserResult<ListenOptions> result = Parser.Default.ParseArguments<ListenOptions>(["--FileName", "/tmp/logs.txt"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--FileName backward compat alias must be accepted");
		ListenOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.FileName.Should().Be("/tmp/logs.txt", because: "legacy --FileName must map to the FileName property");
	}

	[Test]
	[Description("New --file-name primary option parses correctly in ListenOptions")]
	public void ListenOptions_FileName_KebabForm_Parses() {
		ParserResult<ListenOptions> result = Parser.Default.ParseArguments<ListenOptions>(["--file-name", "/tmp/logs.txt"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--file-name must be the primary accepted form");
		ListenOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.FileName.Should().Be("/tmp/logs.txt", because: "new --file-name must map to the FileName property");
	}

	// NOTE: ListenOptions.Silent (--Silent) conflicts with inherited EnvironmentOptions.IsSilent (--silent)
	// Renaming --Silent → --silent would create a duplicate-option error; handled separately.

	// ─── GetPkgListOptions (PkgListOptions) ────────────────────────────────────

	[Test]
	[Description("Legacy --Filter still parses in PkgListOptions after primary rename to --filter")]
	public void PkgListOptions_Filter_LegacyForm_Parses() {
		ParserResult<PkgListOptions> result = Parser.Default.ParseArguments<PkgListOptions>(["--Filter", "test"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Filter backward compat alias must be accepted");
		PkgListOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SearchPattern.Should().Be("test", because: "legacy --Filter must map to the SearchPattern property");
	}

	[Test]
	[Description("New --filter primary option parses correctly")]
	public void PkgListOptions_Filter_KebabForm_Parses() {
		ParserResult<PkgListOptions> result = Parser.Default.ParseArguments<PkgListOptions>(["--filter", "test"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--filter must be the primary accepted form");
		PkgListOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SearchPattern.Should().Be("test", because: "new --filter must map to the SearchPattern property");
	}

	[Test]
	[Description("Legacy --Json still parses in PkgListOptions after primary rename to --json")]
	public void PkgListOptions_Json_LegacyForm_Parses() {
		ParserResult<PkgListOptions> result = Parser.Default.ParseArguments<PkgListOptions>(["--Json", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Json backward compat alias must be accepted");
		PkgListOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Json.Should().BeTrue(because: "legacy --Json must set the Json property to true");
	}

	[Test]
	[Description("New --json primary option parses correctly")]
	public void PkgListOptions_Json_KebabForm_Parses() {
		ParserResult<PkgListOptions> result = Parser.Default.ParseArguments<PkgListOptions>(["--json", "true"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--json must be the primary accepted form");
		PkgListOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Json.Should().BeTrue(because: "new --json must set the Json property to true");
	}

	// ─── HealthCheckOptions ────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --WebHost still parses in HealthCheckOptions after primary rename to --web-host")]
	public void HealthCheckOptions_WebHost_LegacyForm_Parses() {
		ParserResult<HealthCheckOptions> result = Parser.Default.ParseArguments<HealthCheckOptions>(["--WebHost", "localhost"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--WebHost backward compat alias must be accepted");
		HealthCheckOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.WebHost.Should().Contain("localhost", because: "legacy --WebHost must map to the WebHost property");
	}

	[Test]
	[Description("New --web-host primary option parses correctly")]
	public void HealthCheckOptions_WebHost_KebabForm_Parses() {
		ParserResult<HealthCheckOptions> result = Parser.Default.ParseArguments<HealthCheckOptions>(["--web-host", "localhost"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--web-host must be the primary accepted form");
		HealthCheckOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.WebHost.Should().Contain("localhost", because: "new --web-host must map to the WebHost property");
	}

	[Test]
	[Description("Legacy --WebApp still parses in HealthCheckOptions after primary rename to --web-app")]
	public void HealthCheckOptions_WebApp_LegacyForm_Parses() {
		ParserResult<HealthCheckOptions> result = Parser.Default.ParseArguments<HealthCheckOptions>(["--WebApp", "myapp"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--WebApp backward compat alias must be accepted");
		HealthCheckOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.WebApp.Should().Be("myapp", because: "legacy --WebApp must map to the WebApp property");
	}

	[Test]
	[Description("New --web-app primary option parses correctly")]
	public void HealthCheckOptions_WebApp_KebabForm_Parses() {
		ParserResult<HealthCheckOptions> result = Parser.Default.ParseArguments<HealthCheckOptions>(["--web-app", "myapp"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--web-app must be the primary accepted form");
		HealthCheckOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.WebApp.Should().Be("myapp", because: "new --web-app must map to the WebApp property");
	}

	// ─── PingAppOptions ────────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --Endpoint still parses in PingAppOptions after primary rename to --endpoint")]
	public void PingAppOptions_Endpoint_LegacyForm_Parses() {
		ParserResult<PingAppOptions> result = Parser.Default.ParseArguments<PingAppOptions>(["--Endpoint", "/api/ping"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Endpoint backward compat alias must be accepted");
		PingAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Endpoint.Should().Be("/api/ping", because: "legacy --Endpoint must map to the Endpoint property");
	}

	[Test]
	[Description("New --endpoint primary option parses correctly")]
	public void PingAppOptions_Endpoint_KebabForm_Parses() {
		ParserResult<PingAppOptions> result = Parser.Default.ParseArguments<PingAppOptions>(["--endpoint", "/api/ping"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--endpoint must be the primary accepted form");
		PingAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Endpoint.Should().Be("/api/ping", because: "new --endpoint must map to the Endpoint property");
	}

	// ─── PkgListOptions (SysSettings --GET) ────────────────────────────────────

	[Test]
	[Description("Legacy --GET still parses in SysSettingsOptions after primary rename to --get")]
	public void SysSettingsOptions_GET_LegacyForm_Parses() {
		ParserResult<SysSettingsOptions> result = Parser.Default.ParseArguments<SysSettingsOptions>(["SettingCode", "--GET"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--GET backward compat alias must be accepted");
		SysSettingsOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsGet.Should().BeTrue(because: "legacy --GET must set the IsGet property to true");
	}

	[Test]
	[Description("New --get primary option parses correctly")]
	public void SysSettingsOptions_GET_KebabForm_Parses() {
		ParserResult<SysSettingsOptions> result = Parser.Default.ParseArguments<SysSettingsOptions>(["SettingCode", "--get"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--get must be the primary accepted form");
		SysSettingsOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.IsGet.Should().BeTrue(because: "new --get must set the IsGet property to true");
	}

	// ─── CompressAppOptions ────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --SourcePath still parses in CompressAppOptions after primary rename to --source-path")]
	public void CompressAppOptions_SourcePath_LegacyForm_Parses() {
		ParserResult<CompressAppOptions> result = Parser.Default.ParseArguments<CompressAppOptions>(
			["--SourcePath", "/src", "--Packages", "pkg", "--DestinationPath", "/dst"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--SourcePath backward compat alias must be accepted");
		CompressAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.RepositoryFolderPath.Should().Be("/src", because: "legacy --SourcePath must map to the RepositoryFolderPath property");
	}

	[Test]
	[Description("New --source-path primary option parses correctly")]
	public void CompressAppOptions_SourcePath_KebabForm_Parses() {
		ParserResult<CompressAppOptions> result = Parser.Default.ParseArguments<CompressAppOptions>(
			["--source-path", "/src", "--packages", "pkg", "--destination-path", "/dst"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--source-path must be the primary accepted form");
		CompressAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.RepositoryFolderPath.Should().Be("/src", because: "new --source-path must map to the RepositoryFolderPath property");
	}

	[Test]
	[Description("Legacy --SkipPdb still parses in CompressAppOptions after primary rename to --skip-pdb")]
	public void CompressAppOptions_SkipPdb_LegacyForm_Parses() {
		ParserResult<CompressAppOptions> result = Parser.Default.ParseArguments<CompressAppOptions>(
			["--SourcePath", "/src", "--Packages", "pkg", "--DestinationPath", "/dst", "--SkipPdb"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--SkipPdb backward compat alias must be accepted");
		CompressAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SkipPdb.Should().BeTrue(because: "legacy --SkipPdb must set the SkipPdb property to true");
	}

	[Test]
	[Description("New --skip-pdb primary option parses correctly in CompressAppOptions")]
	public void CompressAppOptions_SkipPdb_KebabForm_Parses() {
		ParserResult<CompressAppOptions> result = Parser.Default.ParseArguments<CompressAppOptions>(
			["--source-path", "/src", "--packages", "pkg", "--destination-path", "/dst", "--skip-pdb"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--skip-pdb must be the primary accepted form");
		CompressAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SkipPdb.Should().BeTrue(because: "new --skip-pdb must set the SkipPdb property to true");
	}

	// ─── GeneratePkgZipOptions (CompressPackageCommand) ────────────────────────

	[Test]
	[Description("Legacy --DestinationPath still parses in GeneratePkgZipOptions after primary rename to --destination-path")]
	public void GeneratePkgZipOptions_DestinationPath_LegacyForm_Parses() {
		ParserResult<GeneratePkgZipOptions> result = Parser.Default.ParseArguments<GeneratePkgZipOptions>(["--DestinationPath", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationPath backward compat alias must be accepted");
		GeneratePkgZipOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/out", because: "legacy --DestinationPath must map to the DestinationPath property");
	}

	[Test]
	[Description("New --destination-path primary option parses correctly in GeneratePkgZipOptions")]
	public void GeneratePkgZipOptions_DestinationPath_KebabForm_Parses() {
		ParserResult<GeneratePkgZipOptions> result = Parser.Default.ParseArguments<GeneratePkgZipOptions>(["--destination-path", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-path must be the primary accepted form");
		GeneratePkgZipOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/out", because: "new --destination-path must map to the DestinationPath property");
	}

	// ─── PkgListOptions (filter / json) covered above
	// ─── GitSyncOptions ────────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --Direction still parses in GitSyncOptions after primary rename to --direction")]
	public void GitSyncOptions_Direction_LegacyForm_Parses() {
		ParserResult<GitSyncOptions> result = Parser.Default.ParseArguments<GitSyncOptions>(["--Direction", "ToGit"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Direction backward compat alias must be accepted");
		GitSyncOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Direction.Should().Be("ToGit", because: "legacy --Direction must map to the Direction property");
	}

	[Test]
	[Description("New --direction primary option parses correctly")]
	public void GitSyncOptions_Direction_KebabForm_Parses() {
		ParserResult<GitSyncOptions> result = Parser.Default.ParseArguments<GitSyncOptions>(["--direction", "ToGit"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--direction must be the primary accepted form");
		GitSyncOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Direction.Should().Be("ToGit", because: "new --direction must map to the Direction property");
	}

	// ─── PullPkgOptions (DownloadPackageCommand) ───────────────────────────────

	[Test]
	[Description("Legacy --DestinationPath still parses in PullPkgOptions after primary rename to --destination-path")]
	public void PullPkgOptions_DestinationPath_LegacyForm_Parses() {
		ParserResult<PullPkgOptions> result = Parser.Default.ParseArguments<PullPkgOptions>(["pkg-name", "--DestinationPath", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationPath backward compat alias must be accepted");
		PullPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestPath.Should().Be("/out", because: "legacy --DestinationPath must map to DestPath property");
	}

	[Test]
	[Description("New --destination-path primary option parses correctly in PullPkgOptions")]
	public void PullPkgOptions_DestinationPath_KebabForm_Parses() {
		ParserResult<PullPkgOptions> result = Parser.Default.ParseArguments<PullPkgOptions>(["pkg-name", "--destination-path", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-path must be the primary accepted form");
		PullPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestPath.Should().Be("/out", because: "new --destination-path must map to DestPath property");
	}

	[Test]
	[Description("Legacy --UnZip still parses in PullPkgOptions after primary rename to --unzip")]
	public void PullPkgOptions_UnZip_LegacyForm_Parses() {
		ParserResult<PullPkgOptions> result = Parser.Default.ParseArguments<PullPkgOptions>(["pkg-name", "--UnZip"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--UnZip backward compat alias must be accepted");
		PullPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Unzip.Should().BeTrue(because: "legacy --UnZip must set the Unzip property to true");
	}

	[Test]
	[Description("New --unzip primary option parses correctly")]
	public void PullPkgOptions_UnZip_KebabForm_Parses() {
		ParserResult<PullPkgOptions> result = Parser.Default.ParseArguments<PullPkgOptions>(["pkg-name", "--unzip"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--unzip must be the primary accepted form");
		PullPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.Unzip.Should().BeTrue(because: "new --unzip must set the Unzip property to true");
	}

	// ─── UnzipPkgOptions (ExtractPackageCommand) ───────────────────────────────

	[Test]
	[Description("Legacy --DestinationPath still parses in UnzipPkgOptions after primary rename to --destination-path")]
	public void UnzipPkgOptions_DestinationPath_LegacyForm_Parses() {
		ParserResult<UnzipPkgOptions> result = Parser.Default.ParseArguments<UnzipPkgOptions>(["--DestinationPath", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationPath backward compat alias must be accepted");
		UnzipPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/out", because: "legacy --DestinationPath must map to DestinationPath property");
	}

	[Test]
	[Description("New --destination-path primary option parses correctly in UnzipPkgOptions")]
	public void UnzipPkgOptions_DestinationPath_KebabForm_Parses() {
		ParserResult<UnzipPkgOptions> result = Parser.Default.ParseArguments<UnzipPkgOptions>(["--destination-path", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-path must be the primary accepted form");
		UnzipPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/out", because: "new --destination-path must map to DestinationPath property");
	}

	// ─── SetPackageVersionOptions ──────────────────────────────────────────────

	[Test]
	[Description("Legacy --PackageVersion still parses after primary rename to --package-version")]
	public void SetPackageVersionOptions_PackageVersion_LegacyForm_Parses() {
		ParserResult<SetPackageVersionOptions> result = Parser.Default.ParseArguments<SetPackageVersionOptions>(["path/to/pkg", "--PackageVersion", "1.2.3"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--PackageVersion backward compat alias must be accepted");
		SetPackageVersionOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.PackageVersion.Should().Be("1.2.3", because: "legacy --PackageVersion must map to the PackageVersion property");
	}

	[Test]
	[Description("New --package-version primary option parses correctly")]
	public void SetPackageVersionOptions_PackageVersion_KebabForm_Parses() {
		ParserResult<SetPackageVersionOptions> result = Parser.Default.ParseArguments<SetPackageVersionOptions>(["path/to/pkg", "--package-version", "1.2.3"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--package-version must be the primary accepted form");
		SetPackageVersionOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.PackageVersion.Should().Be("1.2.3", because: "new --package-version must map to the PackageVersion property");
	}

	// ─── ValidationPkgOptions ──────────────────────────────────────────────────

	[Test]
	[Description("Legacy --DestinationResult still parses after primary rename to --destination-result")]
	public void ValidationPkgOptions_DestinationResult_LegacyForm_Parses() {
		ParserResult<ValidationPkgOptions> result = Parser.Default.ParseArguments<ValidationPkgOptions>(["--DestinationResult", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationResult backward compat alias must be accepted");
		ValidationPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationResult.Should().Be("/out", because: "legacy --DestinationResult must map to the DestinationResult property");
	}

	[Test]
	[Description("New --destination-result primary option parses correctly")]
	public void ValidationPkgOptions_DestinationResult_KebabForm_Parses() {
		ParserResult<ValidationPkgOptions> result = Parser.Default.ParseArguments<ValidationPkgOptions>(["--destination-result", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-result must be the primary accepted form");
		ValidationPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationResult.Should().Be("/out", because: "new --destination-result must map to the DestinationResult property");
	}

	// ─── PackNuGetPkgOptions ───────────────────────────────────────────────────

	[Test]
	[Description("Legacy --NupkgDirectory still parses after primary rename to --nupkg-directory")]
	public void PackNuGetPkgOptions_NupkgDirectory_LegacyForm_Parses() {
		ParserResult<PackNuGetPkgOptions> result = Parser.Default.ParseArguments<PackNuGetPkgOptions>(["path/to/pkg", "--NupkgDirectory", "/nuget"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--NupkgDirectory backward compat alias must be accepted");
		PackNuGetPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.NupkgDirectory.Should().Be("/nuget", because: "legacy --NupkgDirectory must map to the NupkgDirectory property");
	}

	[Test]
	[Description("New --nupkg-directory primary option parses correctly")]
	public void PackNuGetPkgOptions_NupkgDirectory_KebabForm_Parses() {
		ParserResult<PackNuGetPkgOptions> result = Parser.Default.ParseArguments<PackNuGetPkgOptions>(["path/to/pkg", "--nupkg-directory", "/nuget"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--nupkg-directory must be the primary accepted form");
		PackNuGetPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.NupkgDirectory.Should().Be("/nuget", because: "new --nupkg-directory must map to the NupkgDirectory property");
	}

	// ─── PushNuGetPkgsOptions ──────────────────────────────────────────────────

	[Test]
	[Description("Legacy --ApiKey still parses after primary rename to --api-key")]
	public void PushNuGetPkgsOptions_ApiKey_LegacyForm_Parses() {
		ParserResult<PushNuGetPkgsOptions> result = Parser.Default.ParseArguments<PushNuGetPkgsOptions>(
			["pkg.nupkg", "--ApiKey", "mykey", "--Source", "https://nuget.org"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--ApiKey backward compat alias must be accepted");
		PushNuGetPkgsOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ApiKey.Should().Be("mykey", because: "legacy --ApiKey must map to the ApiKey property");
	}

	[Test]
	[Description("New --api-key primary option parses correctly")]
	public void PushNuGetPkgsOptions_ApiKey_KebabForm_Parses() {
		ParserResult<PushNuGetPkgsOptions> result = Parser.Default.ParseArguments<PushNuGetPkgsOptions>(
			["pkg.nupkg", "--api-key", "mykey", "--source", "https://nuget.org"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--api-key must be the primary accepted form");
		PushNuGetPkgsOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ApiKey.Should().Be("mykey", because: "new --api-key must map to the ApiKey property");
	}

	[Test]
	[Description("Legacy --Source still parses in PushNuGetPkgsOptions after primary rename to --source")]
	public void PushNuGetPkgsOptions_Source_LegacyForm_Parses() {
		ParserResult<PushNuGetPkgsOptions> result = Parser.Default.ParseArguments<PushNuGetPkgsOptions>(
			["pkg.nupkg", "--ApiKey", "k", "--Source", "https://nuget.org"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--Source backward compat alias must be accepted");
		PushNuGetPkgsOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SourceUrl.Should().Be("https://nuget.org", because: "legacy --Source must map to the SourceUrl property");
	}

	[Test]
	[Description("New --source primary option parses correctly in PushNuGetPkgsOptions")]
	public void PushNuGetPkgsOptions_Source_KebabForm_Parses() {
		ParserResult<PushNuGetPkgsOptions> result = Parser.Default.ParseArguments<PushNuGetPkgsOptions>(
			["pkg.nupkg", "--api-key", "k", "--source", "https://nuget.org"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--source must be the primary accepted form");
		PushNuGetPkgsOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SourceUrl.Should().Be("https://nuget.org", because: "new --source must map to the SourceUrl property");
	}

	// ─── LinkPackageStoreOptions ───────────────────────────────────────────────

	[Test]
	[Description("Legacy --packageStorePath still parses after primary rename to --package-store-path")]
	public void LinkPackageStoreOptions_PackageStorePath_LegacyForm_Parses() {
		ParserResult<LinkPackageStoreOptions> result = Parser.Default.ParseArguments<LinkPackageStoreOptions>(
			["--packageStorePath", "/store"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--packageStorePath backward compat alias must be accepted");
		LinkPackageStoreOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.PackageStorePath.Should().Be("/store", because: "legacy --packageStorePath must map to the PackageStorePath property");
	}

	[Test]
	[Description("New --package-store-path primary option parses correctly")]
	public void LinkPackageStoreOptions_PackageStorePath_KebabForm_Parses() {
		ParserResult<LinkPackageStoreOptions> result = Parser.Default.ParseArguments<LinkPackageStoreOptions>(
			["--package-store-path", "/store"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--package-store-path must be the primary accepted form");
		LinkPackageStoreOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.PackageStorePath.Should().Be("/store", because: "new --package-store-path must map to the PackageStorePath property");
	}

	[Test]
	[Description("Legacy --envPkgPath still parses in LinkPackageStoreOptions after primary rename to --env-pkg-path")]
	public void LinkPackageStoreOptions_EnvPkgPath_LegacyForm_Parses() {
		ParserResult<LinkPackageStoreOptions> result = Parser.Default.ParseArguments<LinkPackageStoreOptions>(
			["--packageStorePath", "/store", "--envPkgPath", "/env"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--envPkgPath backward compat alias must be accepted");
		LinkPackageStoreOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.EnvPkgPath.Should().Be("/env", because: "legacy --envPkgPath must map to the EnvPkgPath property");
	}

	[Test]
	[Description("New --env-pkg-path primary option parses correctly in LinkPackageStoreOptions")]
	public void LinkPackageStoreOptions_EnvPkgPath_KebabForm_Parses() {
		ParserResult<LinkPackageStoreOptions> result = Parser.Default.ParseArguments<LinkPackageStoreOptions>(
			["--package-store-path", "/store", "--env-pkg-path", "/env"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--env-pkg-path must be the primary accepted form");
		LinkPackageStoreOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.EnvPkgPath.Should().Be("/env", because: "new --env-pkg-path must map to the EnvPkgPath property");
	}

	// ─── PfInstallerOptions (CreatioInstall) ───────────────────────────────────

	[Test]
	[Description("Legacy --SiteName still parses in PfInstallerOptions after primary rename to --site-name")]
	public void PfInstallerOptions_SiteName_LegacyForm_Parses() {
		ParserResult<PfInstallerOptions> result = Parser.Default.ParseArguments<PfInstallerOptions>(["--SiteName", "mysite"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--SiteName backward compat alias must be accepted");
		PfInstallerOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SiteName.Should().Be("mysite", because: "legacy --SiteName must map to the SiteName property");
	}

	[Test]
	[Description("New --site-name primary option parses correctly")]
	public void PfInstallerOptions_SiteName_KebabForm_Parses() {
		ParserResult<PfInstallerOptions> result = Parser.Default.ParseArguments<PfInstallerOptions>(["--site-name", "mysite"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--site-name must be the primary accepted form");
		PfInstallerOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SiteName.Should().Be("mysite", because: "new --site-name must map to the SiteName property");
	}

	[Test]
	[Description("Legacy --SitePort still parses in PfInstallerOptions after primary rename to --site-port")]
	public void PfInstallerOptions_SitePort_LegacyForm_Parses() {
		ParserResult<PfInstallerOptions> result = Parser.Default.ParseArguments<PfInstallerOptions>(["--SitePort", "8080"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--SitePort backward compat alias must be accepted");
		PfInstallerOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SitePort.Should().Be(8080, because: "legacy --SitePort must map to the SitePort property");
	}

	[Test]
	[Description("New --site-port primary option parses correctly")]
	public void PfInstallerOptions_SitePort_KebabForm_Parses() {
		ParserResult<PfInstallerOptions> result = Parser.Default.ParseArguments<PfInstallerOptions>(["--site-port", "8080"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--site-port must be the primary accepted form");
		PfInstallerOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.SitePort.Should().Be(8080, because: "new --site-port must map to the SitePort property");
	}

	[Test]
	[Description("Legacy --ZipFile still parses in PfInstallerOptions after primary rename to --zip-file")]
	public void PfInstallerOptions_ZipFile_LegacyForm_Parses() {
		ParserResult<PfInstallerOptions> result = Parser.Default.ParseArguments<PfInstallerOptions>(["--ZipFile", "/pkg.zip"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--ZipFile backward compat alias must be accepted");
		PfInstallerOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ZipFile.Should().Be("/pkg.zip", because: "legacy --ZipFile must map to the ZipFile property");
	}

	[Test]
	[Description("New --zip-file primary option parses correctly")]
	public void PfInstallerOptions_ZipFile_KebabForm_Parses() {
		ParserResult<PfInstallerOptions> result = Parser.Default.ParseArguments<PfInstallerOptions>(["--zip-file", "/pkg.zip"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--zip-file must be the primary accepted form");
		PfInstallerOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.ZipFile.Should().Be("/pkg.zip", because: "new --zip-file must map to the ZipFile property");
	}

	[Test]
	[Description("Explorer ZIP deployment preserves the database's forced-password-change state when disable-reset-password is omitted")]
	public void PfInstallerOptions_ShouldDefaultDisableResetPasswordToFalse_WhenExplorerContextArgumentsOmitIt() {
		// Arrange
		string[] arguments = ["--ZipFile", "/pkg.zip"];

		// Act
		ParserResult<PfInstallerOptions> result = Parser.Default.ParseArguments<PfInstallerOptions>(arguments);
		PfInstallerOptions? options = null;
		result.WithParsed(parsed => options = parsed);

		// Assert
		result.Tag.Should().Be(ParserResultType.Parsed,
			because: "the Windows Explorer registry command should parse when the hidden password-reset option is omitted");
		options!.DisableResetPassword.Should().BeFalse(
			because: "CLI and Explorer deployments should not clear the database's existing forced-password-change state by default");
	}

	// ─── ExecuteSqlScriptOptions ───────────────────────────────────────────────

	[Test]
	[Description("Legacy --DestinationPath still parses in ExecuteSqlScriptOptions after primary rename to --destination-path")]
	public void ExecuteSqlScriptOptions_DestinationPath_LegacyForm_Parses() {
		ParserResult<ExecuteSqlScriptOptions> result = Parser.Default.ParseArguments<ExecuteSqlScriptOptions>(["--DestinationPath", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationPath backward compat alias must be accepted");
		ExecuteSqlScriptOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestPath.Should().Be("/out", because: "legacy --DestinationPath must map to DestPath property");
	}

	[Test]
	[Description("New --destination-path primary option parses correctly in ExecuteSqlScriptOptions")]
	public void ExecuteSqlScriptOptions_DestinationPath_KebabForm_Parses() {
		ParserResult<ExecuteSqlScriptOptions> result = Parser.Default.ParseArguments<ExecuteSqlScriptOptions>(["--destination-path", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-path must be the primary accepted form");
		ExecuteSqlScriptOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestPath.Should().Be("/out", because: "new --destination-path must map to DestPath property");
	}

	// ─── GenerateProcessModelCommandOptions ───────────────────────────────────

	[Test]
	[Description("Legacy --DestinationPath still parses in GenerateProcessModelCommandOptions after primary rename to --destination-path")]
	public void GenerateProcessModelCommandOptions_DestinationPath_LegacyForm_Parses() {
		ParserResult<GenerateProcessModelCommandOptions> result = Parser.Default.ParseArguments<GenerateProcessModelCommandOptions>(["ProcessCode", "--DestinationPath", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationPath backward compat alias must be accepted");
		GenerateProcessModelCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/out", because: "legacy --DestinationPath must map to DestinationPath property");
	}

	[Test]
	[Description("New --destination-path primary option parses correctly in GenerateProcessModelCommandOptions")]
	public void GenerateProcessModelCommandOptions_DestinationPath_KebabForm_Parses() {
		ParserResult<GenerateProcessModelCommandOptions> result = Parser.Default.ParseArguments<GenerateProcessModelCommandOptions>(["ProcessCode", "--destination-path", "/out"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-path must be the primary accepted form");
		GenerateProcessModelCommandOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationPath.Should().Be("/out", because: "new --destination-path must map to DestinationPath property");
	}

	// ─── RestoreNugetPkgOptions ────────────────────────────────────────────────

	[Test]
	[Description("Legacy --DestinationDirectory still parses after primary rename to --destination-directory")]
	public void RestoreNugetPkgOptions_DestinationDirectory_LegacyForm_Parses() {
		ParserResult<RestoreNugetPkgOptions> result = Parser.Default.ParseArguments<RestoreNugetPkgOptions>(["PackageName", "--DestinationDirectory", "/restore"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--DestinationDirectory backward compat alias must be accepted");
		RestoreNugetPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationDirectory.Should().Be("/restore", because: "legacy --DestinationDirectory must map to the property");
	}

	[Test]
	[Description("New --destination-directory primary option parses correctly")]
	public void RestoreNugetPkgOptions_DestinationDirectory_KebabForm_Parses() {
		ParserResult<RestoreNugetPkgOptions> result = Parser.Default.ParseArguments<RestoreNugetPkgOptions>(["PackageName", "--destination-directory", "/restore"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--destination-directory must be the primary accepted form");
		RestoreNugetPkgOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.DestinationDirectory.Should().Be("/restore", because: "new --destination-directory must map to the property");
	}

	// ─── DownloadAppOptions ────────────────────────────────────────────────────

	[Test]
	[Description("Legacy --FilePath still parses in DownloadAppOptions after primary rename to --file-path")]
	public void DownloadAppOptions_FilePath_LegacyForm_Parses() {
		ParserResult<DownloadAppOptions> result = Parser.Default.ParseArguments<DownloadAppOptions>(["AppName", "--FilePath", "/tmp/app.gz"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--FilePath backward compat alias must be accepted");
		DownloadAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.FilePath.Should().Be("/tmp/app.gz", because: "legacy --FilePath must map to the FilePath property");
	}

	[Test]
	[Description("New --file-path primary option parses correctly")]
	public void DownloadAppOptions_FilePath_KebabForm_Parses() {
		ParserResult<DownloadAppOptions> result = Parser.Default.ParseArguments<DownloadAppOptions>(["AppName", "--file-path", "/tmp/app.gz"]);
		result.Tag.Should().Be(ParserResultType.Parsed, because: "--file-path must be the primary accepted form");
		DownloadAppOptions? opts = null;
		result.WithParsed(o => opts = o);
		opts!.FilePath.Should().Be("/tmp/app.gz", because: "new --file-path must map to the FilePath property");
	}
}
