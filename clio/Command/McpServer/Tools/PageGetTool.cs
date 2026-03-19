using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

[McpServerToolType]
public sealed class PageGetTool(
PageGetCommand command,
ILogger logger,
IToolCommandResolver commandResolver)
: BaseTool<PageGetOptions>(command, logger, commandResolver) {

internal const string ToolName = "page-get";

[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
[Description("Get Freedom UI page schema body")]
public CommandExecutionResult GetPage([Required] PageGetArgs args) {
PageGetOptions options = new() {
SchemaName = args.SchemaName,
Environment = args.EnvironmentName,
Uri = args.Uri,
Login = args.Login,
Password = args.Password
};
try {
return InternalExecute<PageGetCommand>(options);
}
catch (Exception ex) {
return new CommandExecutionResult(1, [new ErrorMessage(ex.Message)]);
}
}
}

public sealed record PageGetArgs(
[property: JsonPropertyName("schemaName")][property: Required] string SchemaName,
[property: JsonPropertyName("environmentName")] string? EnvironmentName,
[property: JsonPropertyName("uri")] string? Uri,
[property: JsonPropertyName("login")] string? Login,
[property: JsonPropertyName("password")] string? Password
);
