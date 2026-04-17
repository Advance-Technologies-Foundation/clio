using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

public record CommandExecutionResult(

	[property: JsonPropertyName("exit-code"), Description("Command execution exit code")]
	int ExitCode,

	[property: JsonPropertyName("execution-log-messages"), Description("Command execution output")]
	IEnumerable<LogMessage> Output,

	[property: JsonPropertyName("log-file-path"), Description("Optional path to the generated database operation log file")]
	string LogFilePath = null,

	[property: JsonPropertyName("dataforge")]
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	[property: Description("Optional Data Forge enrichment diagnostics returned when DataForge was queried during the operation.")]
	ApplicationDataForgeResult DataForge = null
) {
	/// <summary>
	/// Creates a failed <see cref="CommandExecutionResult"/> with a single error message.
	/// </summary>
	public static CommandExecutionResult FromError(string message) =>
		new(-1, [new ErrorMessage(message)]);
}
