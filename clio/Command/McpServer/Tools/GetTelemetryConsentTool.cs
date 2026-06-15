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
	[Description("Reads locally persisted product telemetry consent without storing any telemetry event.")]
	public TelemetryConsentResult GetTelemetryConsent()
	{
		return _telemetryService.GetConsentStatus();
	}
}
