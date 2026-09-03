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
	[McpToolExecution(
		Location = McpToolExecutionLocation.InProcess,
		Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("Reads locally persisted product telemetry consent (granted, denied, or unknown) without storing any telemetry event. Telemetry covers AI-assisted Creatio work run through this MCP server; skip it only for non-agent use such as a plain script or a CI job. An agent working on a developer's behalf is in scope EVEN WHEN NO SKILL FILE IS LOADED - 'no skill loaded' is not 'ad-hoc use'. Call this before sending any telemetry event; until consent is granted, send-telemetry stores nothing, and while it reads unknown a send WITHOUT telemetry_consent is rejected with code telemetry-consent-required rather than dropped, so ask the developer and retry carrying the decision. Consent is per installation and persists across sessions; ask the developer only when it reads unknown.")]
	public TelemetryConsentResult GetTelemetryConsent()
	{
		return _telemetryService.GetConsentStatus();
	}
}
