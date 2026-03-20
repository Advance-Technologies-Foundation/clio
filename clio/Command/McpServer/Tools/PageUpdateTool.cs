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
public sealed class PageUpdateTool(
PageUpdateCommand command,
ILogger logger,
IToolCommandResolver commandResolver)
: BaseTool<PageUpdateOptions>(command, logger, commandResolver) {

internal const string ToolName = "page-update";

[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
[Description("Update Freedom UI page schema body")]
public PageUpdateResponse UpdatePage([Required] PageUpdateArgs args) {
PageUpdateOptions options = new() {
SchemaName = args.SchemaName,
Body = args.Body,
DryRun = args.DryRun ?? false,
Environment = args.EnvironmentName,
Uri = args.Uri,
Login = args.Login,
Password = args.Password
};
try {
PageUpdateCommand resolvedCommand = ResolveCommand<PageUpdateCommand>(options);
logger.PreserveMessages = true;
int exitCode = resolvedCommand.Execute(options);
logger.PreserveMessages = false;
var logMessage = logger.LogMessages.FirstOrDefault(m => m is InfoMessage);
if (logMessage != null && !string.IsNullOrWhiteSpace(logMessage.Value?.ToString())) {
var response = JsonConvert.DeserializeObject<PageUpdateResponse>(logMessage.Value.ToString());
logger.ClearMessages();
return response;
}
logger.ClearMessages();
return new PageUpdateResponse {
Success = false,
Error = "Failed to execute command"
};
}
catch (Exception ex) {
logger.ClearMessages();
return new PageUpdateResponse {
Success = false,
Error = ex.Message
};
}
}
}

public sealed record PageUpdateArgs(
[property: JsonPropertyName("schemaName")][property: Required] string SchemaName,
[property: JsonPropertyName("body")][property: Required] string Body,
[property: JsonPropertyName("dryRun")] bool? DryRun,
[property: JsonPropertyName("environmentName")] string? EnvironmentName,
[property: JsonPropertyName("uri")] string? Uri,
[property: JsonPropertyName("login")] string? Login,
[property: JsonPropertyName("password")] string? Password
);
