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
		SourceCodeSchemaCreateResponse response;
		lock (CommandExecutionSyncRoot) {
			SourceCodeSchemaCreateCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SourceCodeSchemaCreateCommand>(options);
			} catch (Exception ex) {
				return new SourceCodeSchemaCreateResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryCreate(options, out response);
		}
		return response;
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
) : SchemaCreateBaseArgs;
