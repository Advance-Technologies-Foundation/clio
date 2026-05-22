using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public sealed class GetClientUnitSchemaTool(
	GetClientUnitSchemaCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<GetClientUnitSchemaOptions>(command, logger, commandResolver) {

	public GetClientUnitSchemaResponse GetSchema(
		[Description("Parameters: schema-name (required); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		GetClientUnitSchemaArgs args) {
		GetClientUnitSchemaOptions options = new() {
			SchemaName = args.SchemaName,
			OutputFile = args.OutputFile,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(() => {
			GetClientUnitSchemaCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<GetClientUnitSchemaCommand>(options);
			}
			catch (Exception ex) {
				return new GetClientUnitSchemaResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGetSchema(options, out GetClientUnitSchemaResponse response);
			return response;
		});
	}
}

public sealed record GetClientUnitSchemaArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("Client unit schema name, e.g. 'NetworkUtilities'")]
	[property: Required]
	string SchemaName
) : SchemaGetBaseArgs;
