using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// ENG-90312 Phase 2 — consolidated MCP dispatcher tool. Folds every non-read-only Phase-1
/// tool into a single MCP entry point keyed by the <c>command</c> discriminator on
/// <see cref="ClioRunArgs"/>. The 23 read-only tools remain registered flat so the host
/// can keep their <c>ReadOnly = true</c> safety class.
/// </summary>
[McpServerToolType]
public sealed class ClioRunTool {
	internal const string ToolName = "clio-run";

	private readonly IServiceProvider _sp;

	public ClioRunTool(IServiceProvider sp) {
		_sp = sp;
	}

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
	[Description(
		"Executes a clio MCP command. Use 'command' to select the operation; remaining args are command-specific (see anyOf branches). " +
		"For read-only operations, prefer the dedicated tools (list-environments, get-schema, apps, sys-setting, dataforge-find, …) " +
		"which the host can auto-approve.")]
	public async Task<object> Apply(
		[Description("clio-run dispatch envelope. Use 'command' to pick the operation.")] [Required] ClioRunArgs args,
		RequestContext<CallToolRequestParams> requestContext,
		global::ModelContextProtocol.Server.McpServer server,
		CancellationToken cancellationToken = default
	) {
		try {
		return args switch {
			AddItemModelRunArgs a => _sp.GetRequiredService<AddItemModelTool>().AddItemModel(a),
			AddPackageRunArgs a => _sp.GetRequiredService<WorkspacePackageTool>().AddPackage(a),
			AppSectionRunArgs a => await _sp.GetRequiredService<AppSectionTool>().Apply(a, server, requestContext, cancellationToken),
			ClearRedisDbRunArgs a => _sp.GetRequiredService<ClearRedisTool>().ClearRedis(a),
			CompileCreatioRunArgs a => _sp.GetRequiredService<CompileCreatioTool>().CompileCreatio(a),
			ApplicationCreateRunArgs a => await _sp.GetRequiredService<ApplicationCreateTool>().ApplicationCreate(a, server, requestContext, cancellationToken),
			CreateDataBindingRunArgs a => _sp.GetRequiredService<CreateDataBindingTool>().CreateDataBinding(a),
			CreateDataBindingDbRunArgs a => _sp.GetRequiredService<CreateDataBindingDbTool>().CreateDataBindingDb(a),
			CreateEntityBusinessRuleRunArgs a => _sp.GetRequiredService<CreateEntityBusinessRuleTool>().BusinessRuleCreate(a),
			PageCreateRunArgs a => _sp.GetRequiredService<PageCreateTool>().CreatePage(a),
			CreatePageBusinessRuleRunArgs a => _sp.GetRequiredService<CreatePageBusinessRuleTool>().BusinessRuleCreate(a),
			SchemaCreateRunArgs a => await _sp.GetRequiredService<SchemaCreateTool>().Create(a),
			CreateUserTaskRunArgs a => _sp.GetRequiredService<CreateUserTaskTool>().CreateUserTask(a),
			CreateWorkspaceRunArgs a => _sp.GetRequiredService<CreateWorkspaceTool>().CreateWorkspace(a),
			DataBindingRowRunArgs a => _sp.GetRequiredService<DataBindingRowTool>().Apply(a),
			DataBindingRowDbRunArgs a => _sp.GetRequiredService<DataBindingRowDbTool>().Apply(a),
			DataForgeInitializeRunArgs a => _sp.GetRequiredService<DataForgeTool>().Initialize(new DataForgeMaintenanceArgs { EnvironmentName = a.EnvironmentName }),
			DataForgeUpdateRunArgs a => _sp.GetRequiredService<DataForgeTool>().Update(new DataForgeMaintenanceArgs { EnvironmentName = a.EnvironmentName }),
			ApplicationDeleteRunArgs a => _sp.GetRequiredService<ApplicationDeleteTool>().DeleteApplication(a),
			DeleteSchemaRunArgs a => _sp.GetRequiredService<DeleteSchemaTool>().DeleteSchema(a),
			DeleteSkillRunArgs a => _sp.GetRequiredService<DeleteSkillTool>().DeleteSkill(a),
			DeployCreatioRunArgs a => _sp.GetRequiredService<InstallerCommandTool>().DeployCreatio(a),
			DownloadConfigurationRunArgs a => _sp.GetRequiredService<DownloadConfigurationTool>().DownloadConfiguration(a),
			FinishHotfixRunArgs a => _sp.GetRequiredService<PackageHotfixTool>().FinishHotfix(new PackageHotfixArgs(a.PackageName, a.EnvironmentName)),
			GenerateProcessModelRunArgs a => _sp.GetRequiredService<GenerateProcessModelTool>().GenerateProcessModel(a),
			GenerateSourceCodeRunArgs a => _sp.GetRequiredService<GenerateSourceCodeTool>().GenerateSourceCode(a),
			PageGetRunArgs a => _sp.GetRequiredService<PageGetTool>().GetPage(a),
			InstallApplicationRunArgs a => _sp.GetRequiredService<InstallApplicationTool>().InstallApplication(a),
			InstallSkillsRunArgs a => _sp.GetRequiredService<InstallSkillsTool>().InstallSkills(a),
			SqlSchemaInstallRunArgs a => _sp.GetRequiredService<SqlSchemaInstallTool>().InstallSchema(a),
			LinkFromRepositoryRunArgs a => _sp.GetRequiredService<LinkFromRepositoryTool>().LinkFromRepository(a),
			ModifyEntitySchemaColumnRunArgs a => _sp.GetRequiredService<ModifyEntitySchemaColumnTool>().ModifyEntitySchemaColumn(a),
			ModifyUserTaskParametersRunArgs a => _sp.GetRequiredService<ModifyUserTaskParametersTool>().ModifyUserTaskParameters(a),
			CreateTestProjectRunArgs a => _sp.GetRequiredService<CreateTestProjectTool>().CreateTestProject(a),
			PkgModeRunArgs a => _sp.GetRequiredService<LoadPackagesTool>().Apply(a),
			PushWorkspaceRunArgs a => _sp.GetRequiredService<PushWorkspaceTool>().PushWorkspace(a),
			RegWebAppRunArgs a => _sp.GetRequiredService<RegWebAppTool>().RegisterWebApp(a),
			RestartCreatioRunArgs a => _sp.GetRequiredService<RestartTool>().Restart(a),
			RestoreDbRunArgs a => _sp.GetRequiredService<RestoreDbTool>().Restore(a),
			RestoreWorkspaceRunArgs a => _sp.GetRequiredService<RestoreWorkspaceTool>().RestoreWorkspace(a),
			SetFsmModeRunArgs a => _sp.GetRequiredService<FsmModeTool>().SetFsmMode(a),
			StartCreatioRunArgs a => _sp.GetRequiredService<StartTool>().StartCreatioByName(requestContext, a.EnvironmentName),
			StopAllCreatioRunArgs => _sp.GetRequiredService<StopTool>().StopAllCreatio(requestContext),
			StopCreatioRunArgs a => _sp.GetRequiredService<StopTool>().StopCreatioByName(requestContext, a.EnvironmentName),
			PageSyncRunArgs a => await _sp.GetRequiredService<PageSyncTool>().SyncPages(a, server, cancellationToken),
			SchemaSyncRunArgs a => await _sp.GetRequiredService<SchemaSyncTool>().SchemaSync(a),
			UninstallCreatioRunArgs a => _sp.GetRequiredService<UninstallCreatioTool>().UninstallCreatio(a),
			UnlockForHotfixRunArgs a => _sp.GetRequiredService<PackageHotfixTool>().UnlockForHotfix(new PackageHotfixArgs(a.PackageName, a.EnvironmentName)),
			PageUpdateRunArgs a => await _sp.GetRequiredService<PageUpdateTool>().UpdatePage(a, server, cancellationToken),
			SchemaUpdateRunArgs a => await _sp.GetRequiredService<SchemaUpdateTool>().Update(a),
			UpdateSkillRunArgs a => _sp.GetRequiredService<UpdateSkillTool>().UpdateSkill(a),
			UpsertSysSettingRunArgs a => _sp.GetRequiredService<SysSettingUpsertTool>().Upsert(a),
			AddPackageDependencyRunArgs a => _sp.GetRequiredService<AddPackageDependencyTool>().AddPackageDependency(a),
			InstallGateRunArgs a => _sp.GetRequiredService<InstallGateTool>().InstallGate(a),
			CreateUiProjectRunArgs a => _sp.GetRequiredService<CreateUiProjectTool>().CreateUiProject(a),
			ODataCreateRunArgs a => _sp.GetRequiredService<ODataCreateTool>().Create(a),
			ODataUpdateRunArgs a => _sp.GetRequiredService<ODataUpdateTool>().Update(a),
			ODataDeleteRunArgs a => _sp.GetRequiredService<ODataDeleteTool>().Delete(a),
			_ => CommandExecutionResult.FromError(
				$"clio-run: unhandled ClioRunArgs subtype {args.GetType().Name}. " +
				"This indicates a [JsonDerivedType] was registered without a matching switch arm — Z7 reflection test should have caught this."),
		};
		} catch (Exception ex) {
			return CommandExecutionResult.FromException(ex);
		}
	}
}
