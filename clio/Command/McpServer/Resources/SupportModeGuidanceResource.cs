using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Resources;

/// <summary>
/// Provides canonical AI-facing guidance for diagnostic-first behavior during support-mode runs against clio MCP.
/// </summary>
[McpServerResourceType]
public sealed class SupportModeGuidanceResource {
	private const string DocsScheme = "docs";
	private const string ResourcePath = "mcp/guides/support-mode";
	private const string ResourceUri = DocsScheme + "://" + ResourcePath;

	/// <summary>
	/// Returns the canonical guidance article for support-mode diagnostic-first behavior, severity routing, and fail-fast evidence.
	/// </summary>
	[McpServerResource(UriTemplate = ResourceUri, Name = "support-mode-guidance")]
	[Description("Returns canonical MCP guidance for diagnostic-first execution under support mode: severity routing, confirmation probes, fail-fast evidence, and reporting.")]
	public ResourceContents GetGuide() => Guide;

	internal static readonly TextResourceContents Guide = new() {
			Uri = ResourceUri,
			MimeType = "text/plain",
			Text = """
			       clio MCP support-mode guide

			       Scope
			       - Apply this guide only when the consumer agent has activated support mode.
			       - Support mode is a diagnostic policy overlay. Its purpose is to surface real CLIO MCP defects and produce trustworthy evidence, not to complete user-facing work through workarounds.
			       - Support mode does not alter business gates, approvals, BA structure, or the canonical execution stage order. It overrides only delegation/background behavior and retry/fallback heuristics during the active stage.

			       Diagnostic-first execution
			       - Keep execution in the main thread/session. Do not start delegated, background, or subagent actions while support mode is on.
			       - If no main-thread equivalent exists for a required step, allow exactly one unavoidable support-mode exception record carrying:
			         - `attempted_action`
			         - `no_main_thread_equivalent_reason`
			         - `main_thread_evidence_captured`
			       - When an unavoidable non-main-thread action completes, surface its result in the main-thread support output before proceeding or stopping.
			       - For any stage-critical failure in the current active stage, create a canonical failure record immediately.
			       - Allow at most one confirmation probe per failure, and only when it uses the same tool and the same contract path as the failed call.
			       - In CLIO-focused support runs, attempt at least one real MCP tool invocation before concluding, unless blocked by an unresolvable environment failure after the bounded retry budget.

			       Severity routing
			       - `clio_mcp_issue` is the primary critical-by-default target defect category. Keep strict diagnostic handling: canonical incident record, one same-path confirmation probe, then fail-fast when blocking.
			       - `instruction_issue` covers guidance or expected-pattern defects, including incorrect generated/edit strategies. Non-critical by default.
			       - `environment_issue` covers auth, network, runtime reachability, or preflight failures. Non-critical by default. For transient site reachability errors, retry the same registration/healthcheck path up to 3 additional attempts with 15-second delays before fail-fast classification.
			       - `orchestration_tool_failure` covers caller or wrapper invocation faults such as args shape, adapter, or normalizer issues. Non-critical by default. May run one canonicalization pass before fail-fast, limited to call-shape normalization (argument format, wrapper invocation shape, serialization wrapper shape) on the same tool path.
			       - Canonicalization is not a workaround branch switch. It must not change the target tool, branch, business logic, or stage.
			       - Escalation rule: any non-critical category becomes fail-fast only when it prevents trustworthy CLIO MCP tool invocation or contract verification, or leaves evidence unreliable for the current run.
			       - For `clio_mcp_issue` critical failures, do not switch to alternate workaround branches, fallback strategy changes, or different mutation paths after the first failed attempt.
			       - For non-critical categories, bounded recovery is allowed on the same target path within the configured retry budget.

			       Page-sync classification rule
			       - Classify client-side validation issues caused by generated/edit strategy or known binding patterns as `instruction_issue`.
			       - Classify as `clio_mcp_issue` only when sync-pages tool/backend behavior violates advertised contract semantics.

			       Fail-fast evidence
			       - After escalation conditions are met, stop the blocked stage and emit:
			         - `exit_decision=fail_fast`
			         - `blocked_stage=<current_active_stage_label>`
			         - `why_continue_is_unsafe=<reason>`
			       - Contract/transport mismatches must be tagged `category=clio_mcp_issue` with a normalized `error_signature` (for example `html_instead_of_json_response`).

			       Canonical failure record
			       - One canonical failure record is mandatory for every unique support-mode incident.
			       - Required fields:
			         - `category`
			         - `what_failed`
			         - `evidence`
			         - `expected_behavior`
			         - `fix_target` (one of `instructions`, `clio_mcp`, `tooling`, `environment`)
			         - `next_recovery_attempt`
			         - `error_signature` (short normalized signature)
			         - `repeat_count`
			         - `timestamps` (optional when `repeat_count > 1`)
			       - Treat incidents as identical when `error_signature` and tool/context match. Repeats increment `repeat_count` and optionally append `timestamps` instead of repeating raw dumps.

			       Reporting contract
			       - Reporting is mandatory under support mode but must stay concise.
			       - Log only actionable failures: `orchestration_tool_failure`, `instruction_issue`, `clio_mcp_issue`, `environment_issue`.
			       - Do not emit heartbeat or progress chatter for successful or unchanged steps. Prefer phase checkpoints only: `env`, `gates`, `schema`, `pages`, `final`.
			       - Emit interim status only when a timeout threshold is crossed or a recovery path changes.
			       - Keep `Confirmed failures` focused on unresolved blockers and target defects.
			       - Do not list resolved or temporary instruction/tooling friction in `Confirmed failures`. Place it in `Non-target friction` when needed.

			       Final response sections (in this exact order)
			       - `Confirmed failures`
			       - `Unresolved blockers`
			       - `Next recovery attempts`
			       - `Support-mode exceptions`
			       - `Non-target friction`

			       Zero-state rule
			       - When a required section has no items, include the section and set its value to `None` instead of omitting it.
			       - Missing any required final section is a support-mode reporting failure.

			       Category prioritization in final reporting
			       - `clio_mcp_issue`
			       - `environment_issue`
			       - `orchestration_tool_failure`
			       - `instruction_issue`

			       Handoff
			       - On a support-mode run that returns a final response (success or failure), append: `Support mode is on. Please share this session with support for analysis.`
			       - The handoff line is mandatory even when support-mode exceptions occurred.
			       - Private internal chain-of-thought is non-contractual and must not be required under support mode.
			       """
		};
}
