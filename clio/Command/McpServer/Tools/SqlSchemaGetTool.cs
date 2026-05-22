using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

public sealed class SqlSchemaGetTool(
	SqlSchemaGetCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<SqlSchemaGetOptions>(command, logger, commandResolver) {

	public SqlSchemaGetResponse GetSchema(
		[Description("Parameters: schema-name (required); output-file (optional); environment-name preferred; uri/login/password emergency fallback only.")]
		[Required]
		SqlSchemaGetArgs args) {
		SqlSchemaGetOptions options = new() {
			SchemaName = args.SchemaName,
			OutputFile = args.OutputFile,
			Environment = args.EnvironmentName,
			Uri = args.Uri,
			Login = args.Login,
			Password = args.Password
		};
		return ExecuteWithCleanLog(() => {
			SqlSchemaGetCommand resolvedCommand;
			try {
				resolvedCommand = ResolveCommand<SqlSchemaGetCommand>(options);
			}
			catch (Exception ex) {
				return new SqlSchemaGetResponse { Success = false, Error = ex.Message };
			}
			resolvedCommand.TryGetSchema(options, out SqlSchemaGetResponse response);
			return response;
		});
	}
}

public sealed record SqlSchemaGetArgs(
	[property: JsonPropertyName("schema-name")]
	[property: Description("SQL script schema name, e.g. 'UsrMySqlScript'")]
	[property: Required]
	string SchemaName
) : SchemaGetBaseArgs;
