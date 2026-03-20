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
public sealed class PageListTool(
PageListCommand command,
ILogger logger,
IToolCommandResolver commandResolver)
: BaseTool<PageListOptions>(command, logger, commandResolver) {

internal const string ToolName = "page-list";

[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
[Description("List Freedom UI pages in Creatio")]
public PageListResponse ListPages([Required] PageListArgs args) {
PageListOptions options = new() {
PackageName = args.PackageName,
SearchPattern = args.SearchPattern,
Limit = args.Limit ?? 50,
Environment = args.EnvironmentName,
Uri = args.Uri,
Login = args.Login,
Password = args.Password
};
try {
PageListCommand resolvedCommand = ResolveCommand<PageListCommand>(options);
logger.PreserveMessages = true;
int exitCode = resolvedCommand.Execute(options);
logger.PreserveMessages = false;
var logMessage = logger.LogMessages.FirstOrDefault(m => m is InfoMessage);
if (logMessage != null && !string.IsNullOrWhiteSpace(logMessage.Value?.ToString())) {
var response = JsonConvert.DeserializeObject<PageListResponse>(logMessage.Value.ToString());
logger.ClearMessages();
return response;
}
logger.ClearMessages();
return new PageListResponse {
Success = false,
Error = "Failed to execute command"
};
}
catch (Exception ex) {
logger.ClearMessages();
return new PageListResponse {
Success = false,
Error = ex.Message
};
}
}
}

public sealed record PageListArgs(
[property: JsonPropertyName("packageName")] string? PackageName,
[property: JsonPropertyName("searchPattern")] string? SearchPattern,
[property: JsonPropertyName("limit")] int? Limit,
[property: JsonPropertyName("environmentName")] string? EnvironmentName,
[property: JsonPropertyName("uri")] string? Uri,
[property: JsonPropertyName("login")] string? Login,
[property: JsonPropertyName("password")] string? Password
);
