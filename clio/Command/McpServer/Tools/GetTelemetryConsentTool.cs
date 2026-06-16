using System;
using System.ComponentModel;
using Clio.Common.Telemetry;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for reading local product telemetry consent state.
/// </summary>
[McpServerToolType]
public sealed class GetTelemetryConsentTool
{
	/// <summary>
	/// Stable MCP tool name for reading product telemetry consent.
	/// </summary>
	internal const string ToolName = "get-telemetry-consent";

	private readonly ITelemetryService _telemetryService;

	/// <summary>
	/// Initializes a new instance of the <see cref="GetTelemetryConsentTool"/> class.
	/// </summary>
	public GetTelemetryConsentTool(ITelemetryService telemetryService)
	{
		_telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
	}

	/// <summary>
	/// Reads locally persisted product telemetry consent without writing analytics.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Reads locally persisted product telemetry consent (granted, denied, or unknown) without storing any telemetry event. Telemetry covers an AI-assisted Creatio app-development session run through this MCP server, driven by a consuming skill/contract; if no such skill is active (ad-hoc clio use, scripts, or CI), do not call this tool or prompt for consent. When a consuming skill is active, call it before sending any telemetry event; until consent is granted, send-telemetry stores nothing, so events sent before consent is established are silently dropped.")]
	public TelemetryConsentResult GetTelemetryConsent()
	{
		return _telemetryService.GetConsentStatus();
	}
}
