using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

public record CommandExecutionResult(
	
	[property:JsonPropertyName("exit-code"), Description("Command execution exit code")]
	int ExitCode,
	
	[property:JsonPropertyName("execution-log-messages"), Description("Command execution output")]
	IEnumerable<LogMessage> Output
	
);
