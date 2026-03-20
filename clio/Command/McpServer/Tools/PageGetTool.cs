using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using Clio.Common;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

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
public PageGetResponse GetPage([Required] PageGetArgs args) {
PageGetOptions options = new() {
SchemaName = args.SchemaName,
Environment = args.EnvironmentName,
Uri = args.Uri,
Login = args.Login,
Password = args.Password
};
try {
PageGetCommand resolvedCommand = ResolveCommand<PageGetCommand>(options);
logger.PreserveMessages = true;
int exitCode = resolvedCommand.Execute(options);
logger.PreserveMessages = false;
var logMessage = logger.LogMessages.FirstOrDefault(m => m is InfoMessage);
if (logMessage != null && !string.IsNullOrWhiteSpace(logMessage.Value?.ToString())) {
var response = JsonConvert.DeserializeObject<PageGetResponse>(logMessage.Value.ToString());
logger.ClearMessages();
return response;
}
logger.ClearMessages();
return new PageGetResponse {
Success = false,
Error = "Failed to execute command"
};
}
catch (Exception ex) {
logger.ClearMessages();
return new PageGetResponse {
Success = false,
Error = ex.Message
};
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
