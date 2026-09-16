using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>Creates C# schemas after validating all independent name arguments locally.</summary>
[McpServerToolType]
public sealed class SchemaCreateTool(
	SourceCodeSchemaCreateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SourceCodeSchemaCreateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-schema";
	internal const string ValidArgumentsHint =
		". Valid arguments: schema-name, package-name (required); caption, description (optional); " +
		"environment-name or uri/login/password.";

	/// <summary>Reports all name validation errors before resolving the target environment.</summary>
	/// <param name="args">Schema metadata and connection arguments.</param>
	/// <returns>The created schema metadata or an actionable validation failure.</returns>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Create a new C# source-code schema on a remote Creatio environment. The schema is saved directly to the server — no local workspace files are created. Prefer `environment-name`; keep direct connection args only for bootstrap flows.")]
	public SourceCodeSchemaCreateResponse CreateSchema(
		[Description("Parameters: schema-name, package-name (required); caption, description (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] SchemaCreateArgs args) {
		SourceCodeSchemaCreateOptions options = new() {
			SchemaName = args.SchemaName,
			PackageName = args.PackageName,
			Caption = args.Caption,
			Description = args.Description,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		string validationError = SchemaDesignerHelper.ValidateCreateInput(options.SchemaName, options.PackageName);
		if (validationError is not null) {
			return new SourceCodeSchemaCreateResponse {
				Success = false,
				Error = validationError + ValidArgumentsHint
			};
		}
		return ExecuteWithCleanLog(options, () => {
			SourceCodeSchemaCreateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SourceCodeSchemaCreateCommand>(options);
			} catch (Exception ex) {
				return new SourceCodeSchemaCreateResponse { Success = false, Error = SensitiveErrorTextRedactor.Redact(ex.Message) };
			}
			resolvedCommand.TryCreate(options, out SourceCodeSchemaCreateResponse response);
			return response;
		});
	}
}

/// <summary>Metadata and connection arguments for creating a C# source-code schema.</summary>
/// <param name="SchemaName">The new schema's canonical name.</param>
/// <param name="PackageName">The package that will own the schema.</param>
public sealed record SchemaCreateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("New C# source-code schema name, e.g. 'UsrMyHelper'. Must start with a letter; letters, digits and underscores only.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name that will own the new schema.")]
	[property: Required]
	string PackageName
) : SchemaCreateBaseArgs;
