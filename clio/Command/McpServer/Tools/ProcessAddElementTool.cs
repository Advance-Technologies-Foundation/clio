using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Clio.Command.ProcessDesigner;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for <c>process-add-element</c> — validates a planned graph and drives the live
/// Process Designer over CDP to append + configure a Read data element and save. Environment-sensitive.
/// </summary>
[McpServerToolType]
public sealed class ProcessAddElementTool(
	ProcessAddElementCommand command,
	ILogger logger,
	IToolCommandResolver commandResolver)
	: BaseTool<ProcessAddElementOptions>(command, logger, commandResolver) {

	/// <summary>Stable MCP tool name.</summary>
	internal const string ToolName = "process-add-element";

	/// <summary>
	/// Drives the designer to add a Read data element to a process and returns the execution result
	/// (the saved process identity is emitted as structured JSON in the output on success).
	/// </summary>
	// Destructive is statically false: the primary slice path creates a NEW process (omit process-id),
	// which is non-destructive. Supplying process-id modifies an existing process (then treat as destructive).
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
	[Description("Drives the live Creatio Process Designer over CDP to append and configure a Read data element and SAVE the process, returning the saved process code/UId/caption. element-type currently supports only 'read-data'. Validates the planned graph and aborts before opening a browser on any error. Creates a new process when process-id is omitted (non-destructive); supplying process-id modifies an existing process. Requires a forms-auth environment and a local Chromium.")]
	public CommandExecutionResult ProcessAddElement(
		[Description("process-add-element parameters")]
		[Required]
		ProcessAddElementArgs args) {
		ProcessAddElementOptions options = new() {
			ElementType = args.ElementType,
			ReadObject = args.ReadObject,
			ProcessId = args.ProcessId,
			ProcessCaption = args.ProcessCaption,
			Headed = args.Headed ?? true,
			Environment = args.EnvironmentName
		};
		try {
			return InternalExecute<ProcessAddElementCommand>(options);
		} catch (Exception exception) {
			return new CommandExecutionResult(1, [new ErrorMessage(exception.Message)]);
		}
	}
}

/// <summary>MCP arguments for the <c>process-add-element</c> tool.</summary>
public sealed record ProcessAddElementArgs(
	[property: JsonPropertyName("environment-name")]
	[property: Description("Registered clio environment name (forms-auth).")]
	[property: Required]
	string EnvironmentName,

	[property: JsonPropertyName("element-type")]
	[property: Description("Element type to add. Supported: read-data.")]
	[property: Required]
	string ElementType,

	[property: JsonPropertyName("read-object")]
	[property: Description("Object the Read data element reads, e.g. Contact.")]
	[property: Required]
	string ReadObject,

	[property: JsonPropertyName("process-id")]
	[property: Description("Existing process id to open; omit to create a new process.")]
	string ProcessId,

	[property: JsonPropertyName("process-caption")]
	[property: Description("Process caption (readback handle); auto-generated when omitted.")]
	string ProcessCaption,

	[property: JsonPropertyName("headed")]
	[property: Description("Run the browser headed (default true; headless unverified).")]
	bool? Headed
);
