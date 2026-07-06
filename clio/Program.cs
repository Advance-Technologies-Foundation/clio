using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Clio.Command;
using Clio.Command.ApplicationCommand;
using Clio.Command.CreatioInstallCommand;
using Clio.Command.IdentityServiceDeployment;
using Clio.Command.OAuthAppConfiguration;
using Clio.Command.McpServer;
using Clio.Command.PackageCommand;
using Clio.Command.SqlScriptCommand;
using Clio.Command.TIDE;
using Clio.Command.Update;
using Clio.Common;
using Clio.Help;
using Clio.Package;
using Clio.Query;
using Clio.UserEnvironment;
using CommandLine;
using Creatio.Client;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Clio;

internal class Program {

	#region Fields: Private

	private static bool? autoUpdate;

	private static bool useCreatioLogStreamer;

	private static readonly Type[] CommandOption = [
		typeof(RegAppOptions),
		typeof(UnregAppOptions),
		typeof(AppListOptions),
		typeof(ExecuteAssemblyOptions),
		typeof(CompressAppOptions),
		typeof(GeneratePkgZipOptions),
		typeof(UnzipPkgOptions),
		typeof(PushPkgOptions),
		typeof(PullPkgOptions),
		typeof(DeletePkgOptions),
		typeof(NewPkgOptions),
		typeof(ReferenceOptions),
		typeof(ConvertOptions),
		typeof(ExecuteSqlScriptOptions),
		typeof(InstallGateOptions),
		typeof(AddItemOptions),
		typeof(DeveloperModeOptions),
		typeof(SysSettingsOptions),
		typeof(FeatureOptions),
		typeof(SetFileContentStorageConnectionStringOptions),
		typeof(PingAppOptions),
		typeof(ShowLocalEnvironmentsOptions),
		typeof(EnvManageUiOptions),
		typeof(ClearLocalEnvironmentOptions),
		typeof(OpenAppOptions),
		typeof(GetBrowserSessionOptions),
		typeof(ClearBrowserSessionOptions),
		// Package development
		typeof(PkgListOptions),
		typeof(CompileOptions),
		typeof(PushNuGetPkgsOptions),
		typeof(PackNuGetPkgOptions),
		typeof(RestoreNugetPkgOptions),
		typeof(InstallNugetPkgOptions),
		typeof(SetPackageVersionOptions),
		typeof(GetPackageVersionOptions),
		typeof(CheckNugetUpdateOptions),
		typeof(UpdateCliOptions),
		typeof(SetAutoupdateOptions),
		typeof(ExperimentalOptions),
		typeof(CreateWorkspaceCommandOptions),
		typeof(RestoreWorkspaceOptions),
		typeof(PushWorkspaceCommandOptions),
		typeof(LoadPackagesToFileSystemOptions),
		typeof(UploadLicensesOptions),
		typeof(LoadPackagesToDbOptions),
		typeof(HealthCheckOptions),
		typeof(ComponentRegistryRefreshOptions),
		typeof(ComponentInfoCommandOptions),
		typeof(GetUserCultureCommandOptions),
		typeof(AddPackageOptions),
		typeof(CreateDataBindingOptions),
		typeof(AddDataBindingRowOptions),
		typeof(RemoveDataBindingRowOptions),
		typeof(CreateDataBindingDbOptions),
		typeof(UpsertDataBindingRowDbOptions),
		typeof(RemoveDataBindingRowDbOptions),
		typeof(UnlockPackageOptions),
		typeof(LockPackageOptions),
		typeof(DeactivatePkgOptions),
		typeof(CompilePackageOptions),
		typeof(CompileConfigurationOptions),
		typeof(DataServiceQueryOptions),
		typeof(CallServiceCommandOptions),
		typeof(RestoreFromPackageBackupOptions),
		typeof(CreateUiProjectOptions),
		typeof(DownloadConfigurationCommandOptions),
		typeof(DeployCommandOptions),
		typeof(InfoCommandOptions),
		typeof(ExternalLinkOptions),
		typeof(OpenCfgOptions),
		typeof(Link2RepoOptions),
		typeof(Link4RepoOptions),
		typeof(LinkPackageStoreOptions),
		typeof(TurnFsmCommandOptions),
		typeof(TurnFarmModeOptions),
		typeof(SetFsmConfigOptions),
		typeof(ScenarioRunnerOptions),
		typeof(InstallApplicationOptions),
		typeof(CreateAppSectionOptions),
		typeof(UpdateAppSectionOptions),
		typeof(DeleteAppSectionOptions),
		typeof(ApplicationSectionGetListOptions),
		typeof(IdentityProviderListOptions),
		typeof(IdentityProviderUpsertOptions),
		typeof(IdentityProviderSetSecretOptions),
		typeof(IdentityProviderDeleteOptions),
		typeof(IdentityProviderSetDefaultOptions),
		typeof(IdentityProviderBindOptions),
		typeof(IdentityProviderUnbindOptions),
		typeof(IdentityProviderServicesOptions),
		typeof(CreateAppOptions),
		typeof(GetAppInfoOptions),
		typeof(CreateLookupOptions),
		typeof(PageListOptions),
		typeof(PageGetOptions),
		typeof(PageUpdateOptions),
		typeof(PageCreateOptions),
		typeof(SourceCodeSchemaCreateOptions),
		typeof(SourceCodeSchemaUpdateOptions),
		typeof(GetSourceCodeSchemaOptions),
		typeof(ClientUnitSchemaCreateOptions),
		typeof(PageTemplatesListOptions),
		typeof(ClientUnitSchemaUpdateOptions),
		typeof(GetClientUnitSchemaOptions),
		typeof(SqlSchemaCreateOptions),
		typeof(SqlSchemaGetOptions),
		typeof(SqlSchemaUpdateOptions),
		typeof(SqlSchemaInstallOptions),
		typeof(ConfigureWorkspaceOptions),
		typeof(GitSyncOptions),
		typeof(BuildInfoOptions),
		typeof(BuildDockerImageOptions),
		typeof(InstallSkillsOptions),
		typeof(UpdateSkillOptions),
		typeof(DeleteSkillOptions),
		typeof(DeployIdentityOptions),
		typeof(GetIdentityServiceConfigOptions),
		typeof(ResolveOAuthSystemUserOptions),
		typeof(CreateOAuthTechnicalUserOptions),
		typeof(CreateServerToServerOAuthAppOptions),
		typeof(VerifyOAuthAppOptions),
		typeof(PfInstallerOptions),
		typeof(CreateInfrastructureOptions),
		typeof(DeployInfrastructureOptions),
		typeof(DeleteInfrastructureOptions),
		typeof(OpenInfrastructureOptions),
		typeof(CheckWindowsFeaturesOptions),
		typeof(ManageWindowsFeaturesOptions),
		typeof(CreateTestProjectOptions),
		typeof(ListenOptions),
		typeof(ShowPackageFileContentOptions),
		typeof(SwitchNugetToDllOptions),
		typeof(UninstallAppOptions),
		typeof(DownloadAppOptions),
		typeof(DeployAppOptions),
		typeof(ListInstalledAppsOptions),
		typeof(RestoreDbCommandOptions),
		typeof(SetWebServiceUrlOptions),
		typeof(ActivatePkgOptions),
		typeof(PackageHotFixCommandOptions),
		typeof(PublishWorkspaceCommandOptions),
		typeof(GetCreatioInfoCommandOptions),
		typeof(GetWebServiceUrlOptions),
		typeof(ApplyEnvironmentManifestOptions),
		typeof(SaveSettingsToManifestOptions),
		typeof(CloneEnvironmentOptions),
		typeof(ShowDiffEnvironmentsOptions),
		typeof(MockDataCommandOptions),
		typeof(UninstallCreatioCommandOptions),
		typeof(AddSchemaOptions),
		typeof(CreateEntitySchemaOptions),
		typeof(UpdateEntitySchemaOptions),
		typeof(ModifyEntitySchemaColumnOptions),
		typeof(GetEntitySchemaColumnPropertiesOptions),
		typeof(GetEntitySchemaPropertiesOptions),
		typeof(FindEntitySchemaOptions),
		typeof(FindAppOptions),
		typeof(CreateUserTaskOptions),
		typeof(ModifyUserTaskParametersOptions),
		typeof(DeleteSchemaOptions),
		typeof(SetApplicationVersionOption),
		typeof(SetApplicationIconOption),
		typeof(RestartOptions),
		typeof(StartOptions),
		typeof(StopOptions),
		typeof(HostsOptions),
		typeof(ClearRedisOptions),
		typeof(LastCompilationLogOptions),
		typeof(UploadLicenseCommandOptions),
		typeof(RegisterOptions),
		typeof(UnregisterOptions),
		typeof(LinkWorkspaceWithTideRepositoryOptions),
		typeof(CheckWebFarmNodeConfigurationsOptions),
		typeof(CustomizeDataProtectionCommandOptions),
		typeof(GetAppHashCommandOptions),
		typeof(MergeWorkspacesCommandOptions),
		typeof(GenerateProcessModelCommandOptions),
		typeof(GetProcessSignatureOptions),
		typeof(LinkCoreSrcOptions),
		typeof(AssertOptions),
		typeof(McpServerCommandOptions),
		typeof(McpHttpServerCommandOptions),
		typeof(QuizCommandOptions),
		typeof(GenerateSourceCodeOptions),
		typeof(AddPackageDependencyOptions),
		typeof(RemovePackageDependencyOptions),
		typeof(GetIdentityAssertionOptions),
		typeof(GetIdentityPublicJwkOptions),
		typeof(RegenerateIdentitySigningKeyOptions),
		typeof(CheckAuthCodeFlowOptions),


	];
	private static readonly Lazy<IReadOnlyList<CommandSuggestionEntry>> CommandSuggestionsCatalog =
		new(CreateCommandSuggestionsCatalog);
	private const int CommandSuggestionLimit = 10;

	/// <summary>
	/// Distinct, stable process exit code returned when a command is refused because the target
	/// environment does not satisfy its declarative Creatio platform version requirement
	/// (<see cref="RequiresCreatioVersionAttribute"/>). Deliberately different from the generic
	/// failure code <c>1</c> so callers and automation can branch on a version-gate refusal
	/// specifically; the human message additionally carries the stable
	/// <see cref="CreatioVersionRequirementException.ErrorCode"/>.
	/// </summary>
	internal const int CreatioVersionRequirementExitCode = 78;

	internal static bool IsCfgOpenCommand;
	internal static bool IsMcpServerMode { get; set; }
	public static IAppUpdater _appUpdater;

	private sealed record CommandSuggestionEntry(string CanonicalName, IReadOnlyList<string> SearchTerms);
	private sealed record CommandSuggestionScore(string CanonicalName, string DisplayName, int TokenOverlap, int EditDistance);

	internal static IReadOnlyList<Type> GetCommandOptionTypes() => CommandOption;

	private static string[] NormalizeCommandLineArgs(string[] args) {
		string[] result = args;
		if (args.Length >= 3 &&
			string.Equals(args[0], "create-data-binding", StringComparison.OrdinalIgnoreCase)) {
			string[] normalizedArgs = (string[])args.Clone();
			for (int index = 1; index < normalizedArgs.Length; index++) {
				if (string.Equals(normalizedArgs[index], "--environment", StringComparison.OrdinalIgnoreCase)) {
					normalizedArgs[index] = "-e";
				}
			}

			result = normalizedArgs;
		}

		return NormalizeJsonFlagArgs(result);
	}

	// The --json option is declared as bool? (its established public form is `--json true|false`).
	// To ALSO accept a bare `--json` additively — without breaking the value form or letting a bare
	// `--json` swallow a positional argument — inject an explicit `true` after any --json/-j/--Json
	// token that is not already followed by true|false. This keeps `--json true|false` byte-identical
	// (strict back-compat) while making bare `--json` work everywhere.
	internal static string[] NormalizeJsonFlagArgs(string[] args) {
		if (args is null || args.Length == 0) {
			return args;
		}
		var output = new List<string>(args.Length + 2);
		for (int index = 0; index < args.Length; index++) {
			string token = args[index];
			output.Add(token);
			if (!IsJsonFlagToken(token)) {
				continue;
			}
			bool nextIsBoolLiteral = index + 1 < args.Length
				&& (string.Equals(args[index + 1], "true", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(args[index + 1], "false", StringComparison.OrdinalIgnoreCase));
			if (!nextIsBoolLiteral) {
				output.Add("true");
			}
		}
		return output.ToArray();
	}

	private static bool IsJsonFlagToken(string token) =>
		string.Equals(token, "--json", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(token, "-j", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(token, "--Json", StringComparison.OrdinalIgnoreCase);

	// JSON output is ON only when a --json/-j/--Json flag resolves to the value 'true' (bare flags are
	// normalized to true by NormalizeJsonFlagArgs; an explicit `--json false` stays off). Used to route
	// decorated diagnostics to stderr so stdout is exactly one JSON object.
	internal static bool IsJsonOutputRequested(string[] args) {
		string[] normalized = NormalizeJsonFlagArgs(args);
		if (normalized is null) {
			return false;
		}
		for (int index = 0; index + 1 < normalized.Length; index++) {
			if (IsJsonFlagToken(normalized[index])
				&& string.Equals(normalized[index + 1], "true", StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}
		return false;
	}

	public static Func<object, int> ExecuteCommandWithOption = instance => {
		// Defense-in-depth feature gate at the single dispatch chokepoint: every path that runs a
		// command (normal parse, scenario runner, any future caller) flows through here, so a
		// gated-off command is unreachable on all surfaces — not only the filtered parse array.
		// IFeatureToggleService is resolved through the same Resolve mechanism the switch uses to get
		// commands (passing the options so the container bootstraps with the matching profile), so we
		// never introduce a second container-bootstrap pattern. The decision is delegated entirely to
		// IsEnabled (the single rule); the [FeatureToggle] attribute is only re-read here to format the
		// user-facing message, and in the disabled branch it is guaranteed present.
		IFeatureToggleService featureToggleService = Resolve<IFeatureToggleService>(instance);
		if (TryGetDisabledFeatureName(instance, featureToggleService, out string disabledFeatureName)) {
			string verb = instance.GetType().GetCustomAttribute<VerbAttribute>()?.Name ?? "undefined-command";
			ConsoleLogger.Instance.WriteError(
				$"Error: command '{verb}' is part of a disabled experimental feature '{disabledFeatureName}'. "
				+ $"Enable it with: clio experimental --name {disabledFeatureName} --enable");
			return 1;
		}
		// Creatio-version gate at the same single dispatch chokepoint, ordered BEFORE the package gate
		// (feature-toggle → creatio-version → package → execute). Zero-cost: the cheap, network-free
		// IsDefinedOn pre-check runs first, so a command without [RequiresCreatioVersion] never resolves
		// the checker and never contacts the environment. This is intentionally stricter than the
		// package gate (which resolves its checker unconditionally) because the version check exists
		// solely to add an environment round-trip. On a refusal we return a DISTINCT, STABLE exit code
		// (not the generic 1) and surface the machine-readable ErrorCode in the message.
		if (RequiresCreatioVersionAttribute.IsDefinedOn(instance.GetType())) {
			ICreatioVersionChecker creatioVersionChecker = Resolve<ICreatioVersionChecker>(instance);
			if (TryGetCreatioVersionRequirementError(instance, creatioVersionChecker, out string versionRequirementError)) {
				ConsoleLogger.Instance.WriteError(versionRequirementError);
				return CreatioVersionRequirementExitCode;
			}
		}
		// Package-requirement gate at the same single dispatch chokepoint: every path that runs a
		// command (normal parse, scenario runner, any future caller) flows through here, so a command
		// whose options class carries [RequiresPackage] is validated against the target environment
		// before it runs — on all surfaces, not just the CLI parse. IRequiredPackageChecker is
		// resolved through the same Resolve mechanism (passing the options so the container bootstraps
		// with the matching environment profile). Commands without [RequiresPackage] early-return inside
		// EnsureRequirements and incur no extra HTTP call.
		IRequiredPackageChecker requiredPackageChecker = Resolve<IRequiredPackageChecker>(instance);
		if (TryGetPackageRequirementError(instance, requiredPackageChecker, out string packageRequirementError)) {
			ConsoleLogger.Instance.WriteError(packageRequirementError);
			return 1;
		}
		return instance switch {
			ExecuteAssemblyOptions opts => CreateRemoteCommand<AssemblyCommand>(opts).Execute(opts),
			RestartOptions opts => Resolve<RestartCommand>(opts).Execute(opts),
			StartOptions opts => Resolve<StartCommand>(opts).Execute(opts),
			ClearRedisOptions opts => Resolve<RedisCommand>(opts).Execute(opts),
			UploadLicenseCommandOptions opts => Resolve<UploadLicenseCommand>(opts).Execute(opts),
			RegAppOptions opts => Resolve<RegAppCommand>(opts).Execute(opts),
			AppListOptions opts => Resolve<ShowAppListCommand>().Execute(opts),
			UnregAppOptions opts => Resolve<UnregAppCommand>().Execute(opts),
			GeneratePkgZipOptions opts => Resolve<CompressPackageCommand>().Execute(opts),
			PushPkgOptions opts => Resolve<PushPackageCommand>(opts).Execute(opts),
			InstallApplicationOptions opts => Resolve<InstallApplicationCommand>(opts).Execute(opts),
			CreateAppSectionOptions opts => Resolve<CreateAppSectionCommand>(opts).Execute(opts),
			UpdateAppSectionOptions opts => Resolve<UpdateAppSectionCommand>(opts).Execute(opts),
			DeleteAppSectionOptions opts => Resolve<DeleteAppSectionCommand>(opts).Execute(opts),
			ApplicationSectionGetListOptions opts => Resolve<GetAppSectionsCommand>(opts).Execute(opts),
			IdentityProviderListOptions opts => Resolve<IdentityProviderListCommand>(opts).Execute(opts),
			IdentityProviderUpsertOptions opts => Resolve<IdentityProviderUpsertCommand>(opts).Execute(opts),
			IdentityProviderSetSecretOptions opts => Resolve<IdentityProviderSetSecretCommand>(opts).Execute(opts),
			IdentityProviderDeleteOptions opts => Resolve<IdentityProviderDeleteCommand>(opts).Execute(opts),
			IdentityProviderSetDefaultOptions opts => Resolve<IdentityProviderSetDefaultCommand>(opts).Execute(opts),
			IdentityProviderBindOptions opts => Resolve<IdentityProviderBindCommand>(opts).Execute(opts),
			IdentityProviderUnbindOptions opts => Resolve<IdentityProviderUnbindCommand>(opts).Execute(opts),
			IdentityProviderServicesOptions opts => Resolve<IdentityProviderServicesCommand>(opts).Execute(opts),
			CreateAppOptions opts => Resolve<CreateAppCommand>(opts).Execute(opts),
			GetAppInfoOptions opts => Resolve<GetAppInfoCommand>(opts).Execute(opts),
			CreateLookupOptions opts => Resolve<CreateLookupCommand>(opts).Execute(opts),
			DeletePkgOptions opts => Resolve<DeletePackageCommand>(opts).Execute(opts),
			ReferenceOptions opts => Resolve<ReferenceCommand>().Execute(opts),
			NewPkgOptions opts => Resolve<NewPkgCommand>().Execute(opts),
			ConvertOptions opts => ConvertPackage(opts),
			RegisterOptions opts => Resolve<RegisterCommand>().Execute(opts),
			UnregisterOptions opts => Resolve<UnregisterCommand>().Execute(opts),
			PullPkgOptions opts => DownloadZipPackages(opts),
			ExecuteSqlScriptOptions opts => Resolve<SqlScriptCommand>(opts).Execute(opts),
			InstallGateOptions opts => Resolve<InstallGateCommand>(opts).Execute(opts),
			AddItemOptions opts => Resolve<AddItemCommand>(opts).Execute(opts),
			DeveloperModeOptions opts => SetDeveloperMode(opts),
			SysSettingsOptions opts => Resolve<SysSettingsCommand>(opts).Execute(opts),
			FeatureOptions opts => Resolve<FeatureCommand>(opts).Execute(opts),
			SetFileContentStorageConnectionStringOptions opts =>
				Resolve<SetFileContentStorageConnectionStringCommand>(opts).Execute(opts),
			UnzipPkgOptions opts => Resolve<ExtractPackageCommand>().Execute(opts),
			PingAppOptions opts => CreateRemoteCommand<PingAppCommand>(opts).Execute(opts),
			OpenAppOptions opts => Resolve<OpenAppCommand>(opts).Execute(opts),
			GetBrowserSessionOptions opts => Resolve<GetBrowserSessionCommand>(opts).Execute(opts),
			ClearBrowserSessionOptions opts => Resolve<ClearBrowserSessionCommand>(opts).Execute(opts),
			PkgListOptions opts => Resolve<GetPkgListCommand>(opts).Execute(opts),
			ShowLocalEnvironmentsOptions opts => Resolve<ShowLocalEnvironmentsCommand>().Execute(opts),
			EnvManageUiOptions opts => Resolve<EnvManageUiCommand>().Execute(opts),
			ClearLocalEnvironmentOptions opts => Resolve<ClearLocalEnvironmentCommand>().Execute(opts),
			CompileOptions opts => Resolve<CompileWorkspaceCommand>(opts).Execute(opts),
			PushNuGetPkgsOptions opts => Resolve<PushNuGetPackagesCommand>(opts).Execute(opts),
			PackNuGetPkgOptions opts => Resolve<PackNuGetPackageCommand>(opts).Execute(opts),
			RestoreNugetPkgOptions opts => Resolve<RestoreNugetPackageCommand>(opts).Execute(opts),
			InstallNugetPkgOptions opts => Resolve<InstallNugetPackageCommand>(opts).Execute(opts),
			SetPackageVersionOptions opts => Resolve<SetPackageVersionCommand>().Execute(opts),
			GetPackageVersionOptions opts => Resolve<GetPackageVersionCommand>().Execute(opts),
			CheckNugetUpdateOptions opts => Resolve<CheckNugetUpdateCommand>(opts).Execute(opts),
			UpdateCliOptions opts => Resolve<UpdateCliCommand>(opts).Execute(opts),
			SetAutoupdateOptions opts => Resolve<SetAutoupdateCommand>().Execute(opts),
			ExperimentalOptions opts => Resolve<ExperimentalCommand>().Execute(opts),
			RestoreWorkspaceOptions opts => Resolve<RestoreWorkspaceCommand>(opts).Execute(opts),
			CreateWorkspaceCommandOptions opts => Resolve<CreateWorkspaceCommand>(opts).Execute(opts),
			PushWorkspaceCommandOptions opts => Resolve<PushWorkspaceCommand>(opts).Execute(opts),
			LoadPackagesToFileSystemOptions opts => Resolve<LoadPackagesToFileSystemCommand>(opts).Execute(opts),
			LoadPackagesToDbOptions opts => Resolve<LoadPackagesToDbCommand>(opts).Execute(opts),
			UploadLicensesOptions opts => Resolve<UploadLicensesCommand>(opts).Execute(opts),
			HealthCheckOptions opts => Resolve<HealthCheckCommand>(opts).Execute(opts),
			ComponentRegistryRefreshOptions opts => Resolve<ComponentRegistryRefreshCommand>().Execute(opts),
			ComponentInfoCommandOptions opts => Resolve<ComponentInfoCommand>().Execute(opts),
			GetUserCultureCommandOptions opts => Resolve<GetUserCultureCommand>().Execute(opts),
			AddPackageOptions opts => Resolve<AddPackageCommand>(opts).Execute(opts),
			CreateDataBindingOptions opts => Resolve<CreateDataBindingCommand>(opts).Execute(opts),
			AddDataBindingRowOptions opts => Resolve<AddDataBindingRowCommand>().Execute(opts),
			RemoveDataBindingRowOptions opts => Resolve<RemoveDataBindingRowCommand>().Execute(opts),
			CreateDataBindingDbOptions opts => Resolve<CreateDataBindingDbCommand>(opts).Execute(opts),
			UpsertDataBindingRowDbOptions opts => Resolve<UpsertDataBindingRowDbCommand>(opts).Execute(opts),
			RemoveDataBindingRowDbOptions opts => Resolve<RemoveDataBindingRowDbCommand>(opts).Execute(opts),
			UnlockPackageOptions opts => Resolve<UnlockPackageCommand>(opts).Execute(opts),
			LockPackageOptions opts => Resolve<LockPackageCommand>(opts).Execute(opts),
			DataServiceQueryOptions opts => Resolve<DataServiceQuery>(opts).Execute(opts),
			CallServiceCommandOptions opts => Resolve<CallServiceCommand>(opts).Execute(opts),
			RestoreFromPackageBackupOptions opts => Resolve<RestoreFromPackageBackupCommand>(opts).Execute(opts),
			CreateUiProjectOptions opts => Resolve<CreateUiProjectCommand>(opts).Execute(opts),
			DownloadConfigurationCommandOptions opts => Resolve<DownloadConfigurationCommand>(opts).Execute(opts),
			DeployCommandOptions opts => Resolve<DeployCommand>(opts).Execute(opts),
			InfoCommandOptions opts => Resolve<InfoCommand>(opts).Execute(opts),
			ExternalLinkOptions opts => Resolve<ExternalLinkCommand>(opts).Execute(opts),
			OpenCfgOptions opts => Resolve<OpenCfgCommand>().Execute(opts),
			CompileConfigurationOptions opts => Resolve<CompileConfigurationCommand>(opts).Execute(opts),
			Link2RepoOptions opts => Resolve<Link2RepoCommand>().Execute(opts),
			Link4RepoOptions opts => Resolve<Link4RepoCommand>(opts).Execute(opts),
			TurnFsmCommandOptions opts => Resolve<TurnFsmCommand>(opts).Execute(opts),
			TurnFarmModeOptions opts => Resolve<TurnFarmModeCommand>(opts).Execute(opts),
			SetFsmConfigOptions opts => Resolve<SetFsmConfigCommand>(opts).Execute(opts),
			CompressAppOptions opts => Resolve<CompressAppCommand>().Execute(opts),
			ScenarioRunnerOptions opts => Resolve<ScenarioRunnerCommand>(opts).Execute(opts),
			ConfigureWorkspaceOptions opts => Resolve<ConfigureWorkspaceCommand>(opts).Execute(opts),
			GitSyncOptions opts => Resolve<GitSyncCommand>(opts).Execute(opts),
			BuildInfoOptions opts => Resolve<BuildInfoCommand>(opts).Execute(opts),
			BuildDockerImageOptions opts => Resolve<BuildDockerImageCommand>().Execute(opts),
			InstallSkillsOptions opts => Resolve<InstallSkillsCommand>().Execute(opts),
			UpdateSkillOptions opts => Resolve<UpdateSkillCommand>().Execute(opts),
			DeleteSkillOptions opts => Resolve<DeleteSkillCommand>().Execute(opts),
			DeployIdentityOptions opts => Resolve<DeployIdentityCommand>(opts).Execute(opts),
			GetIdentityServiceConfigOptions opts => Resolve<GetIdentityServiceConfigCommand>(opts).Execute(opts),
			ResolveOAuthSystemUserOptions opts => Resolve<ResolveOAuthSystemUserCommand>(opts).Execute(opts),
			CreateOAuthTechnicalUserOptions opts => Resolve<CreateOAuthTechnicalUserCommand>(opts).Execute(opts),
			CreateServerToServerOAuthAppOptions opts => Resolve<CreateServerToServerOAuthAppCommand>(opts).Execute(opts),
			VerifyOAuthAppOptions opts => Resolve<VerifyOAuthAppCommand>(opts).Execute(opts),
			PfInstallerOptions opts => Resolve<InstallerCommand>(opts).Execute(opts),
			CreateInfrastructureOptions opts => Resolve<CreateInfrastructureCommand>().Execute(opts),
			DeployInfrastructureOptions opts => Resolve<DeployInfrastructureCommand>().Execute(opts),
			DeleteInfrastructureOptions opts => Resolve<DeleteInfrastructureCommand>().Execute(opts),
			OpenInfrastructureOptions opts => Resolve<OpenInfrastructureCommand>().Execute(opts),
			CheckWindowsFeaturesOptions opts => Resolve<CheckWindowsFeaturesCommand>().Execute(opts),
			ManageWindowsFeaturesOptions opts => Resolve<ManageWindowsFeaturesCommand>().Execute(opts),
			CreateTestProjectOptions opts => Resolve<CreateTestProjectCommand>(opts).Execute(opts),
			DeactivatePkgOptions opts => Resolve<DeactivatePackageCommand>(opts).Execute(opts),
			ListenOptions opts => Resolve<ListenCommand>(opts).Execute(opts),
			ShowPackageFileContentOptions opts => Resolve<ShowPackageFileContentCommand>(opts).Execute(opts),
			SwitchNugetToDllOptions opts => Resolve<SwitchNugetToDllCommand>(opts).Execute(opts),
			CompilePackageOptions opts => Resolve<CompilePackageCommand>(opts).Execute(opts),
			UninstallAppOptions opts => Resolve<UninstallAppCommand>(opts).Execute(opts),
			DownloadAppOptions opts => Resolve<DownloadAppCommand>(opts).Execute(opts),
			DeployAppOptions opts => Resolve<DeployAppCommand>(opts).Execute(opts),
			ListInstalledAppsOptions opts => Resolve<ListInstalledAppsCommand>(opts).Execute(opts),
			RestoreDbCommandOptions opts => Resolve<RestoreDbCommand>(opts).Execute(opts),
			SetWebServiceUrlOptions opts => Resolve<SetWebServiceUrlCommand>(opts).Execute(opts),
			PublishWorkspaceCommandOptions opts => Resolve<PublishWorkspaceCommand>(opts).Execute(opts),
			GetCreatioInfoCommandOptions opts => Resolve<GetCreatioInfoCommand>(opts).Execute(opts),
			ActivatePkgOptions opts => Resolve<ActivatePackageCommand>(opts).Execute(opts),
			PackageHotFixCommandOptions opts => Resolve<PackageHotFixCommand>(opts).Execute(opts),
			SetApplicationVersionOption opts => Resolve<SetApplicationVersionCommand>(opts).Execute(opts),
			ApplyEnvironmentManifestOptions opts => ResolveEnvSettings<ApplyEnvironmentManifestCommand>(opts).Execute(opts),
			GetWebServiceUrlOptions opts => Resolve<GetWebServiceUrlCommand>(opts).Execute(opts),
			SaveSettingsToManifestOptions opts => Resolve<SaveSettingsToManifestCommand>(opts).Execute(opts),
			CloneEnvironmentOptions opts => Resolve<CloneEnvironmentCommand>(opts).Execute(opts),
			ShowDiffEnvironmentsOptions opts => Resolve<ShowDiffEnvironmentsCommand>(opts).Execute(opts),
			MockDataCommandOptions opts => Resolve<MockDataCommand>(opts).Execute(opts),
			UninstallCreatioCommandOptions opts => Resolve<UninstallCreatioCommand>(opts).Execute(opts),
			AddSchemaOptions opts => Resolve<AddSchemaCommand>(opts).Execute(opts),
			CreateEntitySchemaOptions opts => Resolve<CreateEntitySchemaCommand>(opts).Execute(opts),
			UpdateEntitySchemaOptions opts => Resolve<UpdateEntitySchemaCommand>(opts).Execute(opts),
			ModifyEntitySchemaColumnOptions opts => Resolve<ModifyEntitySchemaColumnCommand>(opts).Execute(opts),
			GetEntitySchemaColumnPropertiesOptions opts => Resolve<GetEntitySchemaColumnPropertiesCommand>(opts).Execute(opts),
			GetEntitySchemaPropertiesOptions opts => Resolve<GetEntitySchemaPropertiesCommand>(opts).Execute(opts),
			FindEntitySchemaOptions opts => Resolve<FindEntitySchemaCommand>(opts).Execute(opts),
			FindAppOptions opts => Resolve<FindAppCommand>(opts).Execute(opts),
			CreateUserTaskOptions opts => Resolve<CreateUserTaskCommand>(opts).Execute(opts),
			ModifyUserTaskParametersOptions opts => Resolve<ModifyUserTaskParametersCommand>(opts).Execute(opts),
			DeleteSchemaOptions opts => Resolve<DeleteSchemaCommand>(opts).Execute(opts),
			SetApplicationIconOption opts => Resolve<SetApplicationIconCommand>(opts).Execute(opts),
			LastCompilationLogOptions opts => Resolve<LastCompilationLogCommand>(opts).Execute(opts),
			CustomizeDataProtectionCommandOptions opts => Resolve<CustomizeDataProtectionCommand>(opts).Execute(opts),
			LinkWorkspaceWithTideRepositoryOptions opts => Resolve<LinkWorkspaceWithTideRepositoryCommand>(opts).Execute(opts),
			CheckWebFarmNodeConfigurationsOptions opts => Resolve<CheckWebFarmNodeConfigurationsCommand>(opts).Execute(opts),
			GetAppHashCommandOptions opts => Resolve<GetAppHashCommand>(opts).Execute(opts),
			MergeWorkspacesCommandOptions opts => Resolve<MergeWorkspacesCommand>(opts).Execute(opts),
			GenerateProcessModelCommandOptions opts => Resolve<GenerateProcessModelCommand>(opts).Execute(opts),
			GetProcessSignatureOptions opts => Resolve<GetProcessSignatureCommand>(opts).Execute(opts),
			StopOptions opts => Resolve<StopCommand>(opts).Execute(opts),
			HostsOptions opts => Resolve<HostsCommand>(opts).Execute(opts),
			LinkCoreSrcOptions opts => Resolve<LinkCoreSrcCommand>(opts).Execute(opts),
			AssertOptions opts => Resolve<AssertCommand>(opts).Execute(opts),
			LinkPackageStoreOptions opts => Resolve<LinkPackageStoreCommand>(opts).Execute(opts),
			McpServerCommandOptions opts => Resolve<McpServerCommand>(opts).Execute(opts),
			McpHttpServerCommandOptions opts => McpHttpServerCommand.Run(opts),
			PageCreateOptions opts => Resolve<PageCreateCommand>(opts).Execute(opts),
			SourceCodeSchemaCreateOptions opts => Resolve<SourceCodeSchemaCreateCommand>(opts).Execute(opts),
			SourceCodeSchemaUpdateOptions opts => Resolve<SourceCodeSchemaUpdateCommand>(opts).Execute(opts),
			GetSourceCodeSchemaOptions opts => Resolve<GetSourceCodeSchemaCommand>(opts).Execute(opts),
			ClientUnitSchemaCreateOptions opts => Resolve<ClientUnitSchemaCreateCommand>(opts).Execute(opts),
			ClientUnitSchemaUpdateOptions opts => Resolve<ClientUnitSchemaUpdateCommand>(opts).Execute(opts),
			GetClientUnitSchemaOptions opts => Resolve<GetClientUnitSchemaCommand>(opts).Execute(opts),
			SqlSchemaCreateOptions opts => Resolve<SqlSchemaCreateCommand>(opts).Execute(opts),
			SqlSchemaGetOptions opts => Resolve<SqlSchemaGetCommand>(opts).Execute(opts),
			SqlSchemaUpdateOptions opts => Resolve<SqlSchemaUpdateCommand>(opts).Execute(opts),
			SqlSchemaInstallOptions opts => Resolve<SqlSchemaInstallCommand>(opts).Execute(opts),
			PageTemplatesListOptions opts => Resolve<PageTemplatesListCommand>(opts).Execute(opts),
			PageGetOptions opts => Resolve<PageGetCommand>(opts).Execute(opts),
			PageUpdateOptions opts => Resolve<PageUpdateCommand>(opts).Execute(opts),
			PageListOptions opts => Resolve<PageListCommand>(opts).Execute(opts),
			QuizCommandOptions opts => Resolve<QuizCommand>().Execute(opts),
			GenerateSourceCodeOptions opts => Resolve<GenerateSourceCodeCommand>(opts).Execute(opts),
			AddPackageDependencyOptions opts => Resolve<AddPackageDependencyCommand>(opts).Execute(opts),
			RemovePackageDependencyOptions opts => Resolve<RemovePackageDependencyCommand>(opts).Execute(opts),
			GetIdentityAssertionOptions opts => Resolve<GetIdentityAssertionCommand>(opts).Execute(opts),
			GetIdentityPublicJwkOptions opts => Resolve<GetIdentityPublicJwkCommand>(opts).Execute(opts),
			RegenerateIdentitySigningKeyOptions opts => Resolve<RegenerateIdentitySigningKeyCommand>(opts).Execute(opts),
			CheckAuthCodeFlowOptions opts => Resolve<CheckAuthCodeFlowCommand>(opts).Execute(opts),
			var _ => 1
		};
	};

	/// <summary>
	/// Determines whether the supplied options object's type is gated behind a feature flag that is
	/// currently disabled, and if so yields the feature name for messaging.
	/// </summary>
	/// <param name="options">The parsed command options object whose type carries the gate.</param>
	/// <param name="featureToggleService">The service that decides whether the type is enabled.</param>
	/// <param name="featureName">
	/// When the method returns <c>true</c>, the disabled feature key; otherwise <c>null</c>.
	/// </param>
	/// <returns>
	/// <c>true</c> when the type is gated and its feature flag is off (dispatch must be refused);
	/// <c>false</c> when the type is ungated or its flag is on.
	/// </returns>
	internal static bool TryGetDisabledFeatureName(
		object options, IFeatureToggleService featureToggleService, out string featureName) {
		featureName = null;
		if (options is null || featureToggleService is null) {
			return false;
		}
		Type optionsType = options.GetType();
		if (featureToggleService.IsEnabled(optionsType)) {
			return false;
		}
		// IsEnabled returns true for an unattributed type, so reaching here guarantees the attribute.
		featureName = optionsType.GetCustomAttribute<FeatureToggleAttribute>(inherit: false)?.FeatureName;
		return true;
	}

	/// <summary>
	/// Validates the declarative package requirements (<see cref="RequiresPackageAttribute"/>) of an
	/// options instance against the target environment at the single dispatch chokepoint.
	/// </summary>
	/// <param name="options">The command options instance whose type carries any requirements.</param>
	/// <param name="checker">The resolved package-requirement checker.</param>
	/// <param name="errorMessage">The user-facing refusal message when a requirement is unmet.</param>
	/// <returns>
	/// <c>true</c> when a requirement is not satisfied (dispatch must be refused);
	/// <c>false</c> when the options type has no requirement or every requirement is satisfied.
	/// </returns>
	/// <remarks>
	/// The decision is delegated entirely to <see cref="IRequiredPackageChecker.EnsureRequirements"/>,
	/// which early-returns (no package-list fetch / no HTTP) for an options type without
	/// <see cref="RequiresPackageAttribute"/> — see
	/// <c>RequiredPackageCheckerTests.EnsureRequirements_ShouldNotFetchPackages_WhenTypeHasNoAttribute</c>.
	/// </remarks>
	internal static bool TryGetPackageRequirementError(
		object options, IRequiredPackageChecker checker, out string errorMessage) {
		errorMessage = null;
		if (options is null || checker is null) {
			return false;
		}
		try {
			checker.EnsureRequirements(options);
			return false;
		}
		catch (PackageRequirementException ex) {
			errorMessage = ex.Message;
			return true;
		}
		catch (Exception ex) {
			// Mirror the MCP gate: a non-PackageRequirementException (e.g. the target environment is
			// unreachable so GetPackages() throws an HTTP/connection/auth exception) must not escape as a
			// raw stack trace. Surface a readable message and refuse dispatch (caller maps true to exit 1).
			errorMessage = ex.GetReadableMessageException(IsDebugMode);
			return true;
		}
	}

	/// <summary>
	/// Validates the declarative Creatio platform version requirements
	/// (<see cref="RequiresCreatioVersionAttribute"/>) of an options instance against the target
	/// environment at the single dispatch chokepoint.
	/// </summary>
	/// <param name="options">The command options instance whose type carries any requirements.</param>
	/// <param name="checker">The resolved Creatio-version checker.</param>
	/// <param name="errorMessage">
	/// The user-facing refusal message when a requirement is unmet. It embeds the stable, machine-readable
	/// <see cref="CreatioVersionRequirementException.ErrorCode"/> so automation can branch on the failure
	/// class without parsing the human message.
	/// </param>
	/// <returns>
	/// <c>true</c> when a triggered requirement is not satisfied (dispatch must be refused with
	/// <see cref="CreatioVersionRequirementExitCode"/>); <c>false</c> when every triggered requirement is
	/// satisfied.
	/// </returns>
	/// <remarks>
	/// Only <see cref="CreatioVersionRequirementException"/> is caught here — that is the single failure
	/// class this gate owns. A malformed <c>[RequiresCreatioVersion]</c> declaration surfaces as an
	/// <see cref="InvalidOperationException"/>: a developer error that must NOT be mapped to the version
	/// exit code, so it is deliberately left to propagate as a normal error. The provider behind the
	/// checker swallows transport failures and reports an undeterminable version (fail-closed) rather than
	/// throwing, so an unreachable environment becomes a
	/// <see cref="CreatioVersionRequirementException.VersionUndeterminableCode"/> refusal here, not a raw
	/// stack trace.
	/// </remarks>
	internal static bool TryGetCreatioVersionRequirementError(
		object options, ICreatioVersionChecker checker, out string errorMessage) {
		errorMessage = null;
		if (options is null || checker is null) {
			return false;
		}
		try {
			checker.EnsureRequirements(options);
			return false;
		}
		catch (CreatioVersionRequirementException ex) {
			errorMessage = $"{ex.Message} [{ex.ErrorCode}]";
			return true;
		}
	}

	private static string[] OriginalArgs;

	#endregion

	#region Properties: Private

	private static CreatioClient _creatioClientInstance {
		get {
			if (string.IsNullOrEmpty(ClientId)) {
				return new CreatioClient(Url, UserName, UserPassword, true, CreatioEnvironment.IsNetCore);
			}
			return CreatioClient.CreateOAuth20Client(Url, AuthAppUrl, ClientId, ClientSecret,
				CreatioEnvironment.IsNetCore);
		}
	}

	private static string ApiVersionUrl => AppUrl + @"/rest/CreatioApiGateway/GetApiVersion";

	private static string AppUrl {
		get {
			if (CreatioEnvironment.IsNetCore) {
				return Url;
			}
			return Url + @"/0";
		}
	}

	private static string AuthAppUrl => CreatioEnvironment.Settings.AuthAppUri;

	private static string ClientId => CreatioEnvironment.Settings.ClientId;

	private static string ClientSecret => CreatioEnvironment.Settings.ClientSecret;

	private static string DeleteExistsPackagesZipUrl => AppUrl + @"/rest/PackagesGateway/DeleteExistsPackagesZip";

	private static string DownloadExistsPackageZipUrl => AppUrl + @"/rest/PackagesGateway/DownloadExistsPackageZip";

	private static string ExistsPackageZipUrl => AppUrl + @"/rest/PackagesGateway/ExistsPackageZip";

	private static string GetZipPackageUrl => AppUrl + @"/ServiceModel/PackageInstallerService.svc/GetZipPackages";

	private static string Url => CreatioEnvironment.Settings.Uri; // Should be obtained from config

	private static string UserName => CreatioEnvironment.Settings.Login;

	private static string UserPassword => CreatioEnvironment.Settings.Password;

	#endregion

	#region Properties: Internal

	internal static IServiceProvider Container { get; set; }

	#endregion

	#region Properties: Public

	public static bool AddTimeStampToOutput { get; internal set; }

	public static IAppUpdater AppUpdater {
		get {
			if (_appUpdater == null) {
				_appUpdater = Container.GetRequiredService<IAppUpdater>();
			}
			return _appUpdater;
		}
		set { _appUpdater = value; }
	}

	public static bool AutoUpdate {
	get { return autoUpdate.HasValue ? autoUpdate.Value : Resolve<ISettingsRepository>().GetAutoupdate(); }
	set { autoUpdate = value; }
}

	public static bool IsDebugMode { get; set; }

	/// <summary>
	/// True when a command was invoked with <c>--json</c> (or the <c>-j</c> alias). In this mode the
	/// <see cref="ConsoleLogger"/> routes decorated diagnostic lines ([INF]/[WAR]/[DBG]) to stderr so
	/// stdout carries exactly one JSON object — the unified command envelope. Set once during startup.
	/// </summary>
	public static bool IsJsonOutputMode { get; set; }

	public static bool IsEnvironmentReported { get; set; }

	public static bool Safe { get; private set; } = true;

	#endregion

	#region Methods: Private

	/// <summary>
	/// Configures the environment with the specified options.
	/// </summary>
	/// <param name="options">Environment configuration options</param>
	/// <param name="checkEnvExist">If true, verifies that the environment exists before proceeding</param>
	/// <exception cref="ArgumentException">Thrown when the environment doesn't exist and checkEnvExist is true</exception>
	private static void Configure(EnvironmentOptions options, bool checkEnvExist = false){
		ISettingsRepository settingsRepository = Resolve<ISettingsRepository>();
		if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrEmpty(options.Uri)) {
			string activeEnvName = settingsRepository.GetDefaultEnvironmentName();
			if (!string.IsNullOrWhiteSpace(activeEnvName) && settingsRepository.IsEnvironmentExists(activeEnvName)) {
				options.Environment = activeEnvName;
			}
		}
		CreatioEnvironment.EnvironmentName = options.Environment;
		if (checkEnvExist) {
			bool isEnvironmentExists = settingsRepository.IsEnvironmentExists(options.Environment);
			if (!isEnvironmentExists) {
				throw new ArgumentException($"Cannot find environment with name {options.Environment}",
					nameof(options.Environment));
			}
		}
		CreatioEnvironment.Settings = settingsRepository.GetEnvironment(options);
		ICreatioEnvironment creatioEnvironment = Resolve<ICreatioEnvironment>();
	}

	/// <summary>
	/// Converts a package using the specified options.
	/// </summary>
	/// <param name="opts">Package conversion options</param>
	/// <returns>Result code from the conversion operation</returns>
	private static int ConvertPackage(ConvertOptions opts){
		return Resolve<IPackageConverter>().Convert(opts);
	}

	/// <summary>
	/// Creates a remote command with a client connection to the Creatio environment.
	/// </summary>
	/// <typeparam name="TCommand">Type of command to create</typeparam>
	/// <param name="options">Environment options</param>
	/// <param name="additionalConstructorArgs">Additional arguments to pass to the constructor</param>
	/// <returns>Instantiated command with connection to remote environment</returns>
	private static TCommand CreateRemoteCommand<TCommand>(EnvironmentOptions options,
		params object[] additionalConstructorArgs){
		EnvironmentSettings settings = GetEnvironmentSettings(options);
		CreatioClient creatioClient = string.IsNullOrEmpty(settings.ClientId) ? new CreatioClient(settings.Uri,
				settings.Login, settings.Password, true, settings.IsNetCore) :
			CreatioClient.CreateOAuth20Client(settings.Uri, settings.AuthAppUri, settings.ClientId,
				settings.ClientSecret, settings.IsNetCore);
		CreatioClientAdapter clientAdapter = new(creatioClient);
		object[] constructorArgs = new object[] {clientAdapter, settings}.Concat(additionalConstructorArgs).ToArray();
		return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
	}

	/// <summary>
	/// Creates a remote command without a client connection to the Creatio environment.
	/// </summary>
	/// <typeparam name="TCommand">Type of command to create</typeparam>
	/// <param name="options">Environment options</param>
	/// <param name="additionalConstructorArgs">Additional arguments to pass to the constructor</param>
	/// <returns>Instantiated command without connection to remote environment</returns>
	private static TCommand CreateRemoteCommandWithoutClient<TCommand>(EnvironmentOptions options,
		params object[] additionalConstructorArgs){
		EnvironmentSettings settings = GetEnvironmentSettings(options);
		object[] constructorArgs = new object[] {settings}.Concat(additionalConstructorArgs).ToArray();
		return (TCommand)Activator.CreateInstance(typeof(TCommand), constructorArgs);
	}

	/// <summary>
	/// Downloads packages from the Creatio environment to the specified destination.
	/// </summary>
	/// <param name="packageName">Name of the package to download</param>
	/// <param name="destinationPath">Path where the downloaded package will be saved</param>
	/// <param name="_async">If true, performs the download asynchronously</param>
	private static void DownloadZipPackagesInternal(string packageName, string destinationPath, bool _async){
		try {
			Console.WriteLine("Start download packages ({0}).", packageName);
			int count = 0;
			string packageNames
				= string.Format("\"{0}\"", packageName.Replace(" ", string.Empty).Replace(",", "\",\""));
			string requestData = "[" + packageNames + "]";
			if (!_async) {
				_creatioClientInstance.DownloadFile(GetZipPackageUrl, destinationPath, requestData, 600000);
			}
			else {
				_creatioClientInstance.ExecutePostRequest(DeleteExistsPackagesZipUrl, string.Empty);
				new Thread(() => {
					try {
						_creatioClientInstance.DownloadFile(GetZipPackageUrl, Path.GetTempFileName(), requestData,
							2000);
					}
					catch { }
				}).Start();
				bool again = false;
				do {
					Thread.Sleep(2000);
					again = !bool.Parse(_creatioClientInstance.ExecutePostRequest(ExistsPackageZipUrl, string.Empty));
					if (++count > 600) {
						throw new TimeoutException("Timeout exception");
					}
				} while (again);
				Thread.Sleep(1000);
				_creatioClientInstance.DownloadFile(DownloadExistsPackageZipUrl, destinationPath, requestData, 60000);
			}
			Console.WriteLine("Download packages ({0}) completed.", packageName);
		}
		catch (Exception) {
			Console.WriteLine("Download packages ({0}) not completed.", packageName);
		}
	}

	/// <summary>
	/// Finds environment settings based on the environment name in the options.
	/// </summary>
	/// <param name="options">Environment options containing the environment name</param>
	/// <returns>Environment settings if found, null otherwise</returns>
	private static EnvironmentSettings FindEnvironmentSettings(EnvironmentOptions options){
	SettingsRepository settingsRepository = new();
	return settingsRepository.FindEnvironment(options.Environment);
}

	/// <summary>
	/// Gets the API version from the configured Creatio environment.
	/// </summary>
	/// <returns>API version, or 0.0.0.0 if the version cannot be determined</returns>
	private static Version GetAppApiVersion(){
		Version apiVersion = new("0.0.0.0");
		try {
			string appVersionResponse = _creatioClientInstance.ExecuteGetRequest(ApiVersionUrl).Trim('"');
			apiVersion = new Version(appVersionResponse);
		}
		catch (Exception) { }
		return apiVersion;
	}

	/// <summary>
	/// Gets environment settings based on the provided options.
	/// </summary>
	/// <param name="options">Environment options</param>
	/// <returns>Environment settings</returns>
	private static EnvironmentSettings GetEnvironmentSettings(EnvironmentOptions options){
	SettingsRepository settingsRepository = new();
	if (string.IsNullOrWhiteSpace(options.Environment) && string.IsNullOrEmpty(options.Uri)) {
		string activeEnvName = settingsRepository.GetDefaultEnvironmentName();
		if (!string.IsNullOrWhiteSpace(activeEnvName) && settingsRepository.IsEnvironmentExists(activeEnvName)) {
			options.Environment = activeEnvName;
		}
	}
	return settingsRepository.GetEnvironment(options);
}

	/// <summary>
	/// Handles errors that occur during command-line parsing.
	/// </summary>
	/// <param name="errs">Collection of parsing errors</param>
	/// <returns>Exit code based on the type of errors encountered</returns>
	private static int HandleParseError(IEnumerable<Error> errs){
		Error[] errors = errs.ToArray();
		int exitCode = 1;

		List<ErrorType> notRealErrors = new() {
			ErrorType.VersionRequestedError,
			ErrorType.HelpRequestedError,
			ErrorType.HelpVerbRequestedError
		};

		bool isNotRealError = errors.Select(err => err.Tag)
								.Intersect(notRealErrors)
								.Any();

		if (isNotRealError) {
			exitCode = 0;
		}
		else {
			BadVerbSelectedError badVerbError = errors.OfType<BadVerbSelectedError>().FirstOrDefault();
			if (badVerbError != null) {
				WriteUnknownCommandSuggestions(badVerbError.Token);
			}
		}

		return exitCode;
	}

	private static void WriteUnknownCommandSuggestions(string requestedCommand) {
		string[] suggestions = GetUnknownCommandSuggestions(requestedCommand);
		TextWriter output = Console.Out;
		output.WriteLine();
		if (suggestions.Length > 0) {
			output.WriteLine("Maybe you meant:");
			foreach (string suggestion in suggestions) {
				output.WriteLine($"  clio {suggestion}");
			}
			output.WriteLine();
		}
		output.WriteLine("See all commands: clio help");
		output.WriteLine("See command help: clio <command> --help");
	}

	private static string[] GetUnknownCommandSuggestions(string requestedCommand) {
		if (string.IsNullOrWhiteSpace(requestedCommand)) {
			return [];
		}
		CommandSuggestionScore[] scores = CommandSuggestionsCatalog.Value
			.Select(entry => BuildCommandSuggestionScore(requestedCommand, entry))
			.OrderByDescending(score => score.TokenOverlap)
			.ThenBy(score => score.EditDistance)
			.ThenBy(score => score.DisplayName, StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (scores.Length == 0) {
			return [];
		}
		string comparableRequestedCommand = NormalizeComparableCommandName(requestedCommand);
		int suggestionDistanceThreshold = GetSuggestionDistanceThreshold(comparableRequestedCommand.Length);
		CommandSuggestionScore[] relevantScores = scores
			.Where(score => score.TokenOverlap > 0 || score.EditDistance <= suggestionDistanceThreshold)
			.ToArray();
		if (relevantScores.Length == 0) {
			return [];
		}
		return relevantScores
			.Take(CommandSuggestionLimit)
			.Select(score => score.DisplayName)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static CommandSuggestionScore BuildCommandSuggestionScore(string requestedCommand,
		CommandSuggestionEntry entry) {
		string[] requestedTokens = TokenizeCommandName(requestedCommand);
		string comparableRequestedCommand = NormalizeComparableCommandName(requestedCommand);
		int bestTokenOverlap = 0;
		int bestEditDistance = int.MaxValue;
		string bestSearchTerm = entry.CanonicalName;
		foreach (string searchTerm in entry.SearchTerms) {
			string normalizedSearchTerm = NormalizeComparableCommandName(searchTerm);
			int tokenOverlap = CountTokenOverlap(requestedTokens, TokenizeCommandName(searchTerm));
			int editDistance = ComputeDistance(comparableRequestedCommand, normalizedSearchTerm);
			editDistance = GetEffectiveEditDistance(comparableRequestedCommand, entry.CanonicalName, searchTerm,
				normalizedSearchTerm, tokenOverlap, editDistance);
			if (tokenOverlap > bestTokenOverlap || tokenOverlap == bestTokenOverlap && editDistance < bestEditDistance) {
				bestTokenOverlap = tokenOverlap;
				bestEditDistance = editDistance;
				bestSearchTerm = searchTerm;
			}
		}
		return new CommandSuggestionScore(entry.CanonicalName, bestSearchTerm, bestTokenOverlap, bestEditDistance);
	}

	private static int GetEffectiveEditDistance(string comparableRequestedCommand, string canonicalName, string searchTerm,
		string normalizedSearchTerm, int tokenOverlap, int editDistance) {
		if (tokenOverlap > 0 || editDistance <= 1 || string.Equals(searchTerm, canonicalName, StringComparison.OrdinalIgnoreCase)) {
			return editDistance;
		}
		if (comparableRequestedCommand.Length < 5 || normalizedSearchTerm.Length > 4) {
			return editDistance;
		}
		return editDistance + comparableRequestedCommand.Length;
	}

	private static IReadOnlyList<CommandSuggestionEntry> CreateCommandSuggestionsCatalog() {
		Dictionary<string, HashSet<string>> searchTermsByCanonicalName = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type optionType in CommandOption) {
			VerbAttribute verbAttribute = optionType.GetCustomAttribute<VerbAttribute>();
			if (verbAttribute == null || verbAttribute.Hidden || string.IsNullOrWhiteSpace(verbAttribute.Name)) {
				continue;
			}
			if (!searchTermsByCanonicalName.TryGetValue(verbAttribute.Name, out HashSet<string> searchTerms)) {
				searchTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				searchTermsByCanonicalName[verbAttribute.Name] = searchTerms;
			}
			searchTerms.Add(verbAttribute.Name);
			if (verbAttribute.Aliases == null) {
				continue;
			}
			foreach (string alias in verbAttribute.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias) && !alias.Any(char.IsWhiteSpace))) {
				searchTerms.Add(alias);
			}
		}
		return searchTermsByCanonicalName
			.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
			.Select(entry => new CommandSuggestionEntry(entry.Key,
				entry.Value.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray()))
			.ToArray();
	}

	private static int CountTokenOverlap(IEnumerable<string> requestedTokens, IEnumerable<string> candidateTokens) {
		HashSet<string> requested = requestedTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
		HashSet<string> candidate = candidateTokens.ToHashSet(StringComparer.OrdinalIgnoreCase);
		requested.IntersectWith(candidate);
		return requested.Count;
	}

	private static string[] TokenizeCommandName(string commandName) {
		if (string.IsNullOrWhiteSpace(commandName)) {
			return [];
		}
		List<string> tokens = [];
		StringBuilder currentToken = new();
		for (int index = 0; index < commandName.Length; index++) {
			char current = commandName[index];
			if (!char.IsLetterOrDigit(current)) {
				FlushToken(tokens, currentToken);
				continue;
			}
			if (currentToken.Length > 0 && char.IsUpper(current) && char.IsLower(currentToken[currentToken.Length - 1])) {
				FlushToken(tokens, currentToken);
			}
			currentToken.Append(char.ToLowerInvariant(current));
		}
		FlushToken(tokens, currentToken);
		return tokens.ToArray();
	}

	private static void FlushToken(ICollection<string> tokens, StringBuilder currentToken) {
		if (currentToken.Length == 0) {
			return;
		}
		tokens.Add(NormalizeCommandToken(currentToken.ToString()));
		currentToken.Clear();
	}

	private static string NormalizeCommandToken(string token) {
		if (string.IsNullOrWhiteSpace(token) || token.Length <= 3) {
			return token;
		}
		if (token.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && token.Length > 4) {
			return string.Concat(token.AsSpan(0, token.Length - 3), "y");
		}
		if (token.EndsWith('s') && !token.EndsWith("ss", StringComparison.OrdinalIgnoreCase)) {
			return token[..^1];
		}
		return token;
	}

	private static string NormalizeComparableCommandName(string commandName) {
		if (string.IsNullOrWhiteSpace(commandName)) {
			return string.Empty;
		}
		StringBuilder normalized = new(commandName.Length);
		foreach (char current in commandName.Where(char.IsLetterOrDigit)) {
			normalized.Append(char.ToLowerInvariant(current));
		}
		return normalized.ToString();
	}

	private static int GetSuggestionDistanceThreshold(int commandLength) {
		return commandLength switch {
			<= 2 => 0,
			<= 6 => 1,
			<= 10 => 2,
			_ => 3
		};
	}

	private static int ComputeDistance(string source, string target) {
		if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase)) {
			return 0;
		}
		int[,] matrix = new int[source.Length + 1, target.Length + 1];
		for (int row = 0; row <= source.Length; row++) {
			matrix[row, 0] = row;
		}
		for (int column = 0; column <= target.Length; column++) {
			matrix[0, column] = column;
		}
		for (int row = 1; row <= source.Length; row++) {
			for (int column = 1; column <= target.Length; column++) {
				int cost = source[row - 1] == target[column - 1] ? 0 : 1;
				matrix[row, column] = Math.Min(
					Math.Min(matrix[row - 1, column] + 1, matrix[row, column - 1] + 1),
					matrix[row - 1, column - 1] + cost);
			}
		}
		return matrix[source.Length, target.Length];
	}

	private static bool IsMcpCommand(string[] args) {
		string commandName = args.FirstOrDefault(arg => !string.IsNullOrWhiteSpace(arg) && !arg.StartsWith("-", StringComparison.Ordinal));
		return string.Equals(commandName, "mcp-server", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(commandName, "mcp", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTruthyEnvironmentFlag(string variableName) {
		string? value = Environment.GetEnvironmentVariable(variableName);
		return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
	}

	/// <summary>
	/// Main entry point for the application.
	/// </summary>
	/// <param name="args">Command line arguments</param>
	/// <returns>Exit code indicating success (0) or failure (non-zero)</returns>
	public static int Main(string[] args){
		bool loggerStarted = false;
		try {
			string logTarget = string.Empty;
			bool isLog = args.Contains("--log");
			if (isLog) {
				int logIndex = Array.IndexOf(args, "--log");
				logTarget = args[logIndex + 1];
				args = args.Where(x => x != "--log" && x != logTarget).ToArray();
			}

			string[] clearArgs = args.Where(x => x.ToLower() != "--debug" && x.ToLower() != "--ts").ToArray();
			if (clearArgs.Length > 0 && string.Equals(clearArgs[0], "__generate-help-artifacts", StringComparison.OrdinalIgnoreCase)) {
				return ExportHelpArtifacts();
			}
			if (TryHandleBuiltInVersion(clearArgs, out int versionExitCode)) {
				return versionExitCode;
			}
			bool isMcp = IsMcpCommand(clearArgs);
			IsMcpServerMode = isMcp;
			IsDebugMode = args.Any(x => x.ToLower() == "--debug");
			AddTimeStampToOutput = args.Any(x => x.ToLower() == "--ts");
			// Detect json output early (before the background updater logs) so decorated diagnostics are
			// routed to stderr and stdout stays a single JSON envelope. Honors --json true|false and a
			// bare --json (normalized to true); an explicit --json false stays off.
			IsJsonOutputMode = IsJsonOutputRequested(args);
			OriginalArgs = args;
			
			// Set IsCfgOpenCommand based on input arguments
			IsCfgOpenCommand = (args.Length >= 2 && args[0] == "cfg" && args[1] == "open");
			
			if (isMcp) {
				// Neutralize any ambient HTTP(S)/ALL_PROXY for all outbound HttpClient calls when running
				// as an MCP server. AI-agent sandboxes frequently inject process proxy env vars (sometimes
				// pointing at a dead/poisoned address); clio always targets an explicitly configured Creatio
				// URL that must be reached directly, so an inherited proxy must not break it. An empty
				// WebProxy bypasses every host. CLI mode is unchanged (a CLI user may legitimately need the
				// proxy). See DataForgeStatus_Should_Ignore_Poisoned_Proxy_Environment_Variables (ENG-90640).
				// Opt out (fail-safe default is to bypass) by setting CLIO_MCP_RESPECT_AMBIENT_PROXY=true|1
				// — for an org that mandates an inspecting/DLP egress proxy even for the MCP server.
				if (!IsTruthyEnvironmentFlag("CLIO_MCP_RESPECT_AMBIENT_PROXY")) {
					System.Net.Http.HttpClient.DefaultProxy = new System.Net.WebProxy();
				}
				ConsoleLogger.Instance.PreserveMessages = true;
			}
			
				if (logTarget.ToLower() == "creatio") {
					useCreatioLogStreamer = true;
					ConsoleLogger.Instance.StartWithStream();
					loggerStarted = true;
				}  
				else {
					ConsoleLogger.Instance.Start(logTarget);
					loggerStarted = true;
				}
				return ExecuteCommands(clearArgs);
		}
		catch (Exception e) {
			ConsoleLogger.Instance.WriteError(e.GetReadableMessageException(IsDebugMode));
			return 1;
			}
			finally {
				if (loggerStarted) {
					ConsoleLogger.Instance.Stop();
				}
			}
		}

	/// <summary>
	/// Displays a colored message to the console.
	/// </summary>
	/// <param name="text">Text to display</param>
	/// <param name="color">Color to use for the text</param>
	private static void MessageToConsole(string text, ConsoleColor color){
		ConsoleColor currentColor = Console.ForegroundColor;
		Console.ForegroundColor = color;
		Console.WriteLine(text);
		Console.ForegroundColor = currentColor;
	}

	/// <summary>
	/// Resolves environment settings from a manifest file and creates an instance of the specified type.
	/// </summary>
	/// <typeparam name="T">Type to resolve</typeparam>
	/// <param name="options">Options containing the manifest file path</param>
	/// <returns>Resolved instance</returns>
	private static T ResolveEnvSettings<T>(ApplyEnvironmentManifestOptions options = null){
		EnvironmentOptions optionFromFile = ReadEnvironmentOptionsFromManifestFile(options.ManifestFilePath);
		EnvironmentOptions combinedOption = CombinedOption(optionFromFile, options);
		return Resolve<T>(combinedOption, true);
	}

	/// <summary>
	/// Enables developer mode for the specified environment.
	/// </summary>
	/// <param name="opts">Developer mode options</param>
	/// <returns>0 if the operation succeeds, 1 otherwise</returns>
	private static int SetDeveloperMode(DeveloperModeOptions opts){
	try {
		SetupAppConnection(opts, true);
		ISettingsRepository repository = Resolve<ISettingsRepository>();
		CreatioEnvironment.Settings.DeveloperModeEnabled = true;
		repository.ConfigureEnvironment(CreatioEnvironment.EnvironmentName, CreatioEnvironment.Settings);
		SysSettingsOptions sysSettingOptions = new() {
			Code = "Maintainer",
			Value = CreatioEnvironment.Settings.Maintainer
		};
		SysSettingsCommand sysSettingsCommand = Resolve<SysSettingsCommand>(opts);
		sysSettingsCommand.TryUpdateSysSetting(sysSettingOptions, CreatioEnvironment.Settings);
		UnlockMaintainerPackageInternal(opts);
		Resolve<RestartCommand>(opts).Execute(new RestartOptions());
		Console.WriteLine("Done");
		return 0;
	}
	catch (Exception e) {
		Console.WriteLine(e);
		return 1;
	}
}

	/// <summary>
	/// Unlocks the maintainer package in the specified environment.
	/// </summary>
	/// <param name="environmentOptions">Environment options</param>
	private static void UnlockMaintainerPackageInternal(EnvironmentOptions environmentOptions){
		IPackageLockManager packageLockManager = Resolve<IPackageLockManager>(environmentOptions);
		packageLockManager.Unlock();
	}

	/// <summary>
	/// Unzips a package file to the default location.
	/// </summary>
	/// <param name="zipFilePath">Path to the zip file</param>
	// private static void UnZip(string zipFilePath){
	// 	IPackageArchiver packageArchiver = Resolve<IPackageArchiver>();
	// 	packageArchiver.UnZip(zipFilePath, true);
	// }

	/// <summary>
	/// Extracts packages from a zip file to the specified destination.
	/// </summary>
	/// <param name="zipFilePath">Path to the zip file containing packages</param>
	/// <param name="destinationPath">Destination directory for extracted packages</param>
	private static void UnZipPackages(string zipFilePath, string destinationPath){
		IPackageArchiver packageArchiver = Resolve<IPackageArchiver>();
		packageArchiver.ExtractPackages(zipFilePath, true, true, true, false, destinationPath);
	}

	#endregion

	#region Methods: Internal

	/// <summary>
	/// Downloads and optionally extracts packages from the Creatio environment.
	/// </summary>
	/// <param name="options">Options specifying which packages to download and how to process them</param>
	/// <returns>0 if the operation succeeds, 1 otherwise</returns>
	internal static int DownloadZipPackages(PullPkgOptions options){
		try {
			SetupAppConnection(options);
			string packageName = options.Name;
			if (options.Unzip) {
				string destPath = options.DestPath ?? Environment.CurrentDirectory;
				IWorkingDirectoriesProvider workingDirectoriesProvider = Resolve<IWorkingDirectoriesProvider>();
				workingDirectoriesProvider.CreateTempDirectory(tempDirectory => {
					string zipFilePath = Path.Combine(tempDirectory, $"{packageName}.zip");
					DownloadZipPackagesInternal(packageName, zipFilePath, options.Async);
					UnZipPackages(zipFilePath, destPath);
				});
			}
			else {
				string destPath = options.DestPath ?? Path.Combine(Environment.CurrentDirectory, $"{packageName}.zip");
				if (Directory.Exists(destPath)) {
					destPath = Path.Combine(destPath, $"{packageName}.zip");
				}
				DownloadZipPackagesInternal(packageName, destPath, options.Async);
			}
			Console.WriteLine("Done");
			return 0;
		}
		catch (Exception e) {
			Console.WriteLine(e);
			return 1;
		}
	}

	/// <summary>
	/// Executes commands based on the provided command line arguments.
	/// Sets up the command-line parser with appropriate settings and processes the arguments.
	/// </summary>
	/// <param name="args">Command line arguments to process</param>
	/// <returns>Exit code from the executed command, or a parse error code</returns>
	internal static int ExecuteCommands(string[] args){
		CreatioEnvironment creatioEnv = new();
		const string helpFolderName = "help";
		string envPath = creatioEnv.GetAssemblyFolderPath();
		string helpDirectoryPath = Path.Combine(envPath ?? string.Empty, helpFolderName);
		Parser.Default.Settings.ShowHeader = false;
		Parser.Default.Settings.HelpDirectory = helpDirectoryPath;
		IServiceProvider bm = new BindingsModule().Register(applyBootstrapRepairs: false);
		if (TryHandleBuiltInHelp(args, bm, out int helpExitCode)) {
			return helpExitCode;
		}
		if(args.Length >= 2 && (args[1] == "--WEB" || args[1] == "-W")) {
			Parser.Default.Settings.CustomHelpViewer = bm.GetRequiredService<WikiHelpViewer>();
		}
		else {
			Parser.Default.Settings.CustomHelpViewer = bm.GetRequiredService<LocalHelpViewer>();
		}
		
		RunStartupUpdateCheck(args, bm);
		string[] normalizedArgs = NormalizeCommandLineArgs(args);
		// Feature gate: only enabled command option types reach the parser. A verb whose
		// [FeatureToggle] flag is off is filtered out here, so the parser treats it as unknown
		// (indistinguishable from a typo). Types without [FeatureToggle] are always kept.
		IFeatureToggleService featureToggleService = bm.GetRequiredService<IFeatureToggleService>();
		Type[] enabledCommandOption = FeatureToggleFilter.GetEnabled(CommandOption, featureToggleService);
		ParserResult<object> parserResult = Parser.Default.ParseArguments(normalizedArgs, enabledCommandOption);
		if (parserResult is Parsed<object> parsed) {
			return ExecuteCommandWithOption(parsed.Value);
		}
		return HandleParseError(((NotParsed<object>)parserResult).Errors);
	}

	private static bool ShouldSkipUpdateCheck(string[] args) {
		if (IsMcpServerMode) return true;
		// Honor an opt-out env var so harnesses (e.g. the MCP e2e suite) can suppress the
		// background self-update for every spawned clio process from a single seam, instead of
		// relying on per-process appsettings.json edits. Any non-empty, non-"false" value enables.
		string? noUpdate = Environment.GetEnvironmentVariable("CLIO_NO_UPDATE_CHECK");
		if (!string.IsNullOrWhiteSpace(noUpdate)
			&& !string.Equals(noUpdate, "false", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(noUpdate, "0", StringComparison.Ordinal)) {
			return true;
		}
		if (args == null || args.Length == 0) return true;
		string first = args[0];
		if (string.Equals(first, "update-cli", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "update", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "autoupdate", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(first, "mcp-http", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		return args.Any(arg =>
			string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(arg, "--version", StringComparison.OrdinalIgnoreCase));
	}

	private static void RunStartupUpdateCheck(string[] args, IServiceProvider serviceProvider) {
		if (ShouldSkipUpdateCheck(args)) return;
		try {
			IAppUpdater appUpdater = serviceProvider.GetRequiredService<IAppUpdater>();
			ISettingsRepository settingsRepository = serviceProvider.GetRequiredService<ISettingsRepository>();
			string cacheFolder = SettingsRepository.AppSettingsFolderPath;
			(bool available, string latestVersion) = appUpdater
				.CheckForUpdateWithCacheAsync(cacheFolder)
				.GetAwaiter().GetResult();

			if (!available || string.IsNullOrEmpty(latestVersion)) return;

			string currentVersion = appUpdater.GetCurrentVersion();
			if (settingsRepository.GetAutoupdate()) {
				ConsoleLogger.Instance.WriteInfo(
					$"Updating clio {currentVersion} -> {latestVersion} in background...");
				appUpdater.UpdateInBackgroundAsync().GetAwaiter().GetResult();
			} else {
				ConsoleLogger.Instance.WriteWarning(
					RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
						? $"clio {latestVersion} is available. Run 'dotnet tool update clio -g' to update." 
						: $"clio {latestVersion} is available. Run 'clio update' to update.");
			}
		} catch {
			// startup update check must never crash the tool
		}
	}

	private static bool TryHandleBuiltInHelp(string[] args, IServiceProvider serviceProvider, out int exitCode) {
		CommandHelpRenderer renderer = serviceProvider.GetRequiredService<CommandHelpRenderer>();
		string[] normalizedArgs = NormalizeCommandLineArgs(args);
		if (normalizedArgs.Length == 0
			|| normalizedArgs.Length == 1 && IsRootHelpToken(normalizedArgs[0])) {
			Console.Out.Write(renderer.RenderRootHelp(RootHelpRenderMode.Runtime));
			exitCode = 0;
			return true;
		}
		if (normalizedArgs.Length >= 2 && string.Equals(normalizedArgs[0], "help", StringComparison.OrdinalIgnoreCase)) {
			if (renderer.TryRenderCommandHelp(normalizedArgs[1]) is string commandHelp) {
				Console.Out.Write(commandHelp);
				exitCode = 0;
				return true;
			}
			Console.Out.Write(renderer.RenderRootHelp(RootHelpRenderMode.Runtime));
			exitCode = 0;
			return true;
		}
		exitCode = 1;
		return false;
	}

	private static bool TryHandleBuiltInVersion(string[] args, out int exitCode) {
		string[] normalizedArgs = NormalizeCommandLineArgs(args);
		if (normalizedArgs.Length == 1 && IsRootVersionToken(normalizedArgs[0])) {
			Console.Out.WriteLine(GetBuiltInVersionOutput());
			exitCode = 0;
			return true;
		}
		exitCode = 1;
		return false;
	}

	private static bool IsRootHelpToken(string value) =>
		string.Equals(value, "help", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
		|| string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);

	private static bool IsRootVersionToken(string value) =>
		string.Equals(value, "--version", StringComparison.OrdinalIgnoreCase);

	private static string GetBuiltInVersionOutput() {
		Assembly clioAssembly = Assembly.GetExecutingAssembly();
		FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(clioAssembly.Location);
		return versionInfo.FileVersion;
	}

	private static int ExportHelpArtifacts() {
		BindingsModule bindingsModule = new();
		IServiceProvider serviceProvider = bindingsModule.Register();
		IWorkingDirectoriesProvider workingDirectoriesProvider = serviceProvider.GetRequiredService<IWorkingDirectoriesProvider>();
		string repositoryRoot = FindRepositoryRoot(workingDirectoriesProvider.ExecutingDirectory);
		// Generate docs with the deterministic export-baseline feature service (gated commands are
		// treated as off) so committed artifacts never depend on the local appsettings.json flags of
		// whoever runs the regeneration. The runtime help path keeps the live settings-backed service.
		System.IO.Abstractions.IFileSystem fileSystem = serviceProvider.GetRequiredService<System.IO.Abstractions.IFileSystem>();
		CommandHelpCatalog catalog = serviceProvider.GetRequiredService<CommandHelpCatalog>();
		CommandHelpRenderer renderer = new(fileSystem, catalog, new ExportFeatureToggleService());
		HelpArtifactExporter exporter = new(fileSystem, catalog, renderer);
		return exporter.Export(repositoryRoot);
	}

	private static string FindRepositoryRoot(string startDirectory) {
		string currentDirectory = Path.GetFullPath(startDirectory);
		while (!string.IsNullOrWhiteSpace(currentDirectory)) {
			if (Directory.Exists(Path.Combine(currentDirectory, "clio"))
				&& Directory.Exists(Path.Combine(currentDirectory, "clio.tests"))
				&& File.Exists(Path.Combine(currentDirectory, "clio", "Commands.md"))) {
				return currentDirectory;
			}
			string parentDirectory = Path.GetDirectoryName(currentDirectory);
			if (string.IsNullOrWhiteSpace(parentDirectory) || string.Equals(parentDirectory, currentDirectory, StringComparison.Ordinal)) {
				break;
			}
			currentDirectory = parentDirectory;
		}
		return Path.GetFullPath(startDirectory);
	}

	/// <summary>
	/// Resolves an instance of the specified type from the dependency injection container.
	/// If needed, configures the environment settings based on the provided options.
	/// </summary>
	/// <typeparam name="T">Type to resolve from the container</typeparam>
	/// <param name="options">Options used to configure the environment settings</param>
	/// <param name="logAndSettings">If true, logs the environment URI</param>
	/// <returns>Resolved instance of the specified type</returns>
	internal static T Resolve<T>(object options = null, bool logAndSettings = false){
		EnvironmentSettings settings = null;
		if (options is EnvironmentOptions environmentOptions && !IsCfgOpenCommand) {
			if (environmentOptions.RequiredEnvironment || !string.IsNullOrEmpty(environmentOptions.Uri)) {
				settings = GetEnvironmentSettings(environmentOptions);
			}
			else {
				settings = FindEnvironmentSettings(environmentOptions)
					?? new EnvironmentSettings {
						Login = "default"
					};
			}
		}
		if (logAndSettings) {
			ConsoleLogger.Instance.WriteInfo(settings.Uri);
		}
		if (Container == null) {
			BindingsModuleRegistrationProfile profile = settings is null
				? BindingsModuleRegistrationProfile.Bootstrap
				: BindingsModuleRegistrationProfile.EnvironmentScoped;
			// registerMcpHost is threaded explicitly (never read inside BindingsModule) and is true only
			// here, the single build from which McpServerCommand is resolved. In a live MCP session this
			// is the Bootstrap-profile build that backs mcp-server; every other command leaves it false,
			// and ToolCommandResolver's per-environment builds pass false too, so they skip the MCP host.
			Container = new BindingsModule().Register(settings, profile: profile, registerMcpHost: IsMcpServerMode);
		}
		if (useCreatioLogStreamer) {
			ConsoleLogger.Instance.SetCreatioLogStreamer(Container.GetRequiredService<ILogStreamer>());
		}
		return Container.GetRequiredService<T>();
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Checks the API version of the connected Creatio environment against the local API version.
	/// Displays warning messages if the API is missing or outdated.
	/// </summary>
	public static void CheckApiVersion(){
		string dir = AppDomain.CurrentDomain.BaseDirectory;
		string versionFilePath = Path.Combine(dir, "cliogate", "version.txt");
		Version localApiVersion = new(File.ReadAllText(versionFilePath));
		Version appApiVersion = GetAppApiVersion();
		if (appApiVersion == new Version("0.0.0.0")) {
			MessageToConsole($"Your app does not contain clio API." +
				$"{Environment.NewLine}You should consider install it via the \'clio install-gate\' command.",
				ConsoleColor.DarkYellow);
		}
		else if (localApiVersion > appApiVersion) {
			MessageToConsole(
				$"You are using clio api version {appApiVersion}, however version {localApiVersion} is available." +
				$"{Environment.NewLine}You should consider upgrading via the \'clio update-gate\' command.",
				ConsoleColor.DarkYellow);
		}
	}

	/// <summary>
	/// Combines environment options from a file and from command line arguments,
	/// giving priority to command line values when both are specified.
	/// </summary>
	/// <param name="optionFromFile">Environment options from a file</param>
	/// <param name="optionsFromCommandLine">Environment options from the command line</param>
	/// <returns>Combined environment options</returns>
	public static EnvironmentOptions CombinedOption(EnvironmentOptions optionFromFile,
		EnvironmentOptions optionsFromCommandLine){
		if (optionFromFile == null && optionsFromCommandLine == null) {
			return null;
		}
		if (optionFromFile == null && optionsFromCommandLine.IsEmpty()) {
			return optionsFromCommandLine;
		}
		if (string.IsNullOrEmpty(optionsFromCommandLine.Environment)) {
			EnvironmentNameOptions result = new();
			result.Uri = optionsFromCommandLine.Uri ?? optionFromFile.Uri;
			result.Login = optionsFromCommandLine.Login ?? optionFromFile.Login;
			result.Password = optionsFromCommandLine.Password ?? optionFromFile.Password;
			result.AuthAppUri = optionsFromCommandLine.AuthAppUri ?? optionFromFile.AuthAppUri;
			result.ClientId = optionsFromCommandLine.ClientId ?? optionFromFile.ClientId;
			result.ClientSecret = optionsFromCommandLine.ClientSecret ?? optionFromFile.ClientSecret;
			result.IsNetCore = optionsFromCommandLine.IsNetCore.HasValue ? optionsFromCommandLine.IsNetCore
				: optionFromFile.IsNetCore;
			return result;
		}
		return optionsFromCommandLine;
	}

	/// <summary>
	/// Reads environment options from a manifest file.
	/// </summary>
	/// <param name="manifestFilePath">Path to the manifest file</param>
	/// <param name="fileSystem">Optional file system for reading the manifest file</param>
	/// <returns>Environment options extracted from the manifest file</returns>
	public static EnvironmentOptions ReadEnvironmentOptionsFromManifestFile(string manifestFilePath,
		IFileSystem fileSystem = null){
		IDeserializer deserializer = new DeserializerBuilder()
									.WithNamingConvention(UnderscoredNamingConvention.Instance)
									.IgnoreUnmatchedProperties()
									.Build();
		string manifest = fileSystem is null ? File.ReadAllText(manifestFilePath)
			: fileSystem.ReadAllText(manifestFilePath);
		EnvironmentManifest envManifest = deserializer.Deserialize<EnvironmentManifest>(manifest);
		EnvironmentSettings envManifestSettings = envManifest.EnvironmentSettings;
		if (envManifestSettings == null) {
			return null;
		}
		EnvironmentOptions environmnetOptions = new() {
			Uri = envManifestSettings.Uri,
			Login = envManifestSettings.Login,
			Password = envManifestSettings.Password,
			ClientId = envManifestSettings.ClientId,
			ClientSecret = envManifestSettings.ClientSecret,
			AuthAppUri = envManifestSettings.AuthAppUri,
			IsNetCore = envManifestSettings.IsNetCore
		};
		return environmnetOptions;
	}

	/// <summary>
	/// Sets up the connection to the Creatio application with the specified options.
	/// </summary>
	/// <param name="options">Environment options for connecting to the application</param>
	/// <param name="checkEnvExist">If true, verifies that the environment exists before proceeding</param>
	public static void SetupAppConnection(EnvironmentOptions options, bool checkEnvExist = false){
		Configure(options, checkEnvExist);
		CheckApiVersion();
	}

	#endregion

}


