using System;
using System.ComponentModel;
using Clio.Common.Telemetry;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool for withdrawing local product telemetry consent (the user-facing opt-out).
/// </summary>
[McpServerToolType]
public sealed class WithdrawTelemetryConsentTool
{
	/// <summary>
	/// Stable MCP tool name for withdrawing product telemetry consent.
	/// </summary>
	internal const string ToolName = "withdraw-telemetry-consent";

	private readonly ITelemetryService _telemetryService;

	/// <summary>
	/// Initializes a new instance of the <see cref="WithdrawTelemetryConsentTool"/> class.
	/// </summary>
	public WithdrawTelemetryConsentTool(ITelemetryService telemetryService)
	{
		_telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
	}

	/// <summary>
	/// Sets local product telemetry consent to denied and purges not-yet-uploaded local events.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Withdraws product telemetry consent: sets the locally stored decision to denied and deletes any not-yet-uploaded local telemetry events, so no new events are collected and no further uploads start. Call this when the developer asks to stop, turn off, opt out of, or withdraw product telemetry. Forward-looking: it does not delete events already uploaded to Creatio (those expire on the server-side retention timer). Idempotent and safe to call from any state; after it succeeds get-telemetry-consent returns denied and the workflow continues without telemetry.")]
	public TelemetryConsentWithdrawalResult WithdrawTelemetryConsent()
	{
		return _telemetryService.WithdrawConsent();
	}
}
