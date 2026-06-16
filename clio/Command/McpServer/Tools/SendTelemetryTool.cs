using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Clio.Common.Telemetry;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for product telemetry events.
/// </summary>
[McpServerToolType]
public sealed class SendTelemetryTool
{
	/// <summary>
	/// Stable MCP tool name for product telemetry events.
	/// </summary>
	internal const string ToolName = "send-telemetry";

	private readonly ITelemetryService _telemetryService;
	private readonly ITelemetryFlushScheduler _flushScheduler;

	/// <summary>
	/// Initializes a new instance of the <see cref="SendTelemetryTool"/> class.
	/// </summary>
	public SendTelemetryTool(ITelemetryService telemetryService, ITelemetryFlushScheduler flushScheduler)
	{
		_telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
		_flushScheduler = flushScheduler ?? throw new ArgumentNullException(nameof(flushScheduler));
	}

	/// <summary>
	/// Stores a single product telemetry event as a local event file.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = true)]
	[Description("""
				 Stores a single product telemetry event as a local OpenTelemetry-shaped JSON file.

				 Telemetry covers an AI-assisted Creatio app-development session run through this MCP server, driven by
				 a consuming skill/contract; if no such skill is active (ad-hoc clio use, scripts, or CI), do not call
				 this tool. Call get-telemetry-consent before using it. Use telemetry_consent only on first run after
				 asking the developer, so Clio can store the local consent decision. Until consent is granted, nothing
				 is stored, so events sent before consent is established are silently dropped. Which events to send, and
				 when, is defined by the consuming skill/contract, not by this tool. Delivery is non-blocking and
				 fire-and-forget: never wait on, retry, or surface this call.
				 """)]
	public TelemetryEventResult SendTelemetry(
		[Description("Product workflow telemetry metadata. Include telemetry_consent only when storing the first-run consent decision.")]
		[Required]
		TelemetryEventRequest args)
	{
		TelemetryEventResult result = _telemetryService.Send(args);
		if (result.Success && result.Status == TelemetryService.StatusRecorded) {
			// Opportunistic, fire-and-forget upload of the spooled events; never blocks the tool call.
			_flushScheduler.TryScheduleFlush();
		}
		return result;
	}
}
