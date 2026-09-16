using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class SchemaCreateTool(
	SourceCodeSchemaCreateCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SourceCodeSchemaCreateOptions>(command, logger, commandResolver) {

	internal const string ToolName = "create-schema";

	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Create a new C# source-code schema on a remote Creatio environment. Optional body or body-file supplies the initial C# source; body-file takes precedence and is read on the MCP server host. Omit both to keep the platform's blank template. The schema is saved directly to the server — no local workspace files are created. Prefer `environment-name`; keep direct connection args only for bootstrap flows.")]
	public SourceCodeSchemaCreateResponse CreateSchema(
		[Description("Parameters: schema-name, package-name (required); body, body-file, caption, description (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required] SchemaCreateArgs args) {
		SourceCodeSchemaCreateOptions options = new() {
			SchemaName = args.SchemaName,
			PackageName = args.PackageName,
			Body = args.Body,
			BodyFile = args.BodyFile,
			Caption = args.Caption,
			Description = args.Description,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			return new SourceCodeSchemaCreateResponse {
				Success = false,
				Error = "schema-name is required"
			};
		}
		if (!PageSchemaMetadataHelper.IsValidSchemaName(options.SchemaName)) {
			return new SourceCodeSchemaCreateResponse {
				Success = false,
				Error = PageSchemaMetadataHelper.SchemaNameFormatError
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

public sealed record SchemaCreateArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("New C# source-code schema name, e.g. 'UsrMyHelper'. Must start with a letter; letters, digits and underscores only.")]
	[property: Required]
	string SchemaName,

	[property: JsonPropertyName("package-name")]
	[property: Description("Target package name that will own the new schema.")]
	[property: Required]
	string PackageName
) : SchemaCreateBaseArgs {
	/// <summary>Gets the optional initial C# source body.</summary>
	[JsonPropertyName("body")]
	[Description("Optional initial C# source. Must not be empty when supplied; omit both body inputs to keep the platform template.")]
	public string? Body { get; init; }

	/// <summary>Gets a source-file path on the MCP server host; its content takes precedence over Body.</summary>
	[JsonPropertyName("body-file")]
	[Description("Optional absolute path to a UTF-8 C# file on the MCP server host. Takes precedence over body. Missing, unreadable, or empty files fail before schema creation.")]
	public string? BodyFile { get; init; }
}
