using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Environment-aware adapter for native package SQL creation.</summary>
[McpServerToolType]
public sealed class SqlSchemaCreateTool(
	SqlSchemaCreateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SqlSchemaCreateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-sql-schema";

	/// <summary>Creates an empty package SQL script using the requested environment.</summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	// The schema is saved on the server, not into a local workspace, so this is an environment call despite
	// looking like scaffolding.
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Create a new SQL script schema on a remote Creatio environment via SqlScriptSchemaDesignerService. " +
		"The schema is saved directly to the server — no local workspace files are created. " +
		"Prefer `environment-name`; keep direct connection args only for bootstrap flows.")]
	public SqlSchemaCreateResponse CreateSchema(
		[Description("Parameters: schema-name, package-name (required); db-engine-type and install-type (optional); legacy caption/description are rejected; environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		SqlSchemaCreateArgs args) {
		SqlSchemaCreateOptions options = new() {
			SchemaName = args.SchemaName,
			PackageName = args.PackageName,
			DbEngineType = args.DbEngineType,
			InstallType = args.InstallType,
			Caption = args.Caption,
			Description = args.Description,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(options, () => {
			SqlSchemaCreateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SqlSchemaCreateCommand>(options);
			}
			catch (Exception ex) {
				return new SqlSchemaCreateResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryCreate(options, out SqlSchemaCreateResponse response);
			return response;
		});
	}
}

/// <summary>Arguments for native package SQL creation.</summary>
public sealed record SqlSchemaCreateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("New SQL script schema name, e.g. 'UsrMySqlScript'. Must start with a letter; letters, digits and underscores only.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name that will own the new schema.")]
	[property: Required]
	string PackageName
) : SchemaCreateBaseArgs {
	/// <summary>Native database engine, or null to detect the target engine.</summary>
	[JsonPropertyName("db-engine-type")]
	[Description("0 MSSql, 1 Oracle, 2 PostgreSql; omitted means detect from the environment.")]
	public int? DbEngineType { get; init; }

	/// <summary>Native package installation phase.</summary>
	[JsonPropertyName("install-type")]
	[Description("0 before package, 1 after package (default), 2 after schema data, 3 uninstall app.")]
	public int InstallType { get; init; } = 1;
}
