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
	// Process-local: the event is written as a local file and the flush is fire-and-forget against the
	// telemetry endpoint, never a Creatio environment. Rule 11 additionally forbids a worker from running the
	// host's telemetry flush/drain, so this surface stays in the host.
	[McpToolExecution(
		Location = McpToolExecutionLocation.InProcess,
		Lifetime = McpToolExecutionLifetime.NotApplicable,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.None,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description("""
				 Stores a single product telemetry event as a local OpenTelemetry-shaped JSON file.

				 Telemetry covers AI-assisted Creatio work run through this MCP server. Skip it only for non-agent
				 use - a plain script or a CI job. An agent working on a developer's behalf is in scope even when
				 no skill file is loaded; treating that as "ad-hoc use" is what left whole workflows unreported.
				 It applies to EVERY workflow, not just app creation. event_name is a flow-agnostic STAGE
				 (workflow_started, plan_presented, plan_approved, build_started, work_item_completed,
				 workflow_completed, workflow_failed, ...) and `workflow` says which flow it was
				 (classic-to-freedom-migration, mobile-page-conversion, branding, app-creation, app-maintenance, ...),
				 so send the stage plus your workflow rather than inventing a per-flow event name. Optional `variant`
				 carries a bounded per-stage qualifier the flow defines (a migration scope, a blocked reason) - both
				 fields are short lowercase tokens, never free text and never customer data. Optional `model` names the
				 model driving the run (for example claude-opus-5), lowercased, same token shape. The legacy
				 app-creation-specific names still work but are deprecated. Call get-telemetry-consent before using it. Use telemetry_consent only on first run after
				 asking the developer, so Clio can store the local consent decision. Nothing is stored until consent is
				 granted, and the two unconsented outcomes differ: while consent reads unknown a call WITHOUT
				 telemetry_consent is rejected with code telemetry-consent-required (ask the developer, then retry with
				 the decision), whereas once the decision is denied a call returns success with status consent-denied
				 and stores nothing. Neither is a task failure. Which events to send, and
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
