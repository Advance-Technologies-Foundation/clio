using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Clio.Command;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Provides non-resident inspection and configuration of knowledge-feedback standing approval.
/// </summary>
/// <remarks>
/// The tools are intentionally omitted from <see cref="McpCoreToolProfile.CoreToolTypes"/> because
/// policy administration is infrequent. Agents discover them through <c>get-tool-contract</c> and
/// invoke them through <c>clio-run</c>.
/// </remarks>
[McpServerToolType]
internal sealed class KnowledgeFeedbackPolicyTools {
	internal const string GetToolName = "get-knowledge-feedback-policy";
	internal const string ConfigureToolName = "configure-knowledge-feedback-policy";

	private readonly IKnowledgeFeedbackPolicyService _service;

	public KnowledgeFeedbackPolicyTools(IKnowledgeFeedbackPolicyService service) {
		_service = service ?? throw new ArgumentNullException(nameof(service));
	}

	[McpServerTool(Name = GetToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[Description("Returns configured and effective knowledge-feedback mode, destination, reporting scope, current reporting-policy hash, standing approval, and any reason auto approval is stale. This is a non-resident tool; invoke it through clio-run.")]
	public KnowledgeFeedbackPolicy Get() => _service.GetPolicy();

	[McpServerTool(Name = ConfigureToolName, ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
	[Description("Updates knowledge-feedback mode, GitHub repository destination, or reporting scope. Explicit mode=auto records standing approval for the current knowledge-feedback article hash. Selecting or retargeting auto requires confirmed=true plus the exact policy hash, destination, and scope shown to the user. This is a non-resident tool; invoke it through clio-run.")]
	public KnowledgeFeedbackConfigureResponse Configure(KnowledgeFeedbackConfigureArgs args) {
		try {
			KnowledgeFeedbackPolicy current = _service.GetPolicy();
			string? requestedMode = args.Mode?.Trim();
			bool selectsAuto = string.Equals(
				requestedMode,
				KnowledgeFeedbackPolicyService.AutoMode,
				StringComparison.OrdinalIgnoreCase);
			bool retargetsExistingAuto = string.Equals(
				current.ConfiguredMode,
				KnowledgeFeedbackPolicyService.AutoMode,
				StringComparison.Ordinal)
				&& (string.IsNullOrWhiteSpace(requestedMode) || selectsAuto)
				&& (args.Destination is not null || args.ReportingScope is not null);
			if ((selectsAuto || retargetsExistingAuto) && !args.Confirmed) {
				return new KnowledgeFeedbackConfigureResponse(
					false,
					"Selecting auto or changing its destination/reporting scope requires confirmed=true after the user approves the displayed configuration.",
					current);
			}
			if ((selectsAuto || retargetsExistingAuto) && (string.IsNullOrWhiteSpace(args.ExpectedPolicyHash)
					|| string.IsNullOrWhiteSpace(args.ExpectedDestination)
					|| string.IsNullOrWhiteSpace(args.ExpectedReportingScope))) {
				return new KnowledgeFeedbackConfigureResponse(
					false,
					"Confirmed approval must include expected-policy-hash, expected-destination, and expected-reporting-scope exactly as shown to the user.",
					current);
			}
			KnowledgeFeedbackConsent? consent = args.Confirmed
				? new KnowledgeFeedbackConsent(
					args.ExpectedPolicyHash ?? string.Empty,
					args.ExpectedDestination ?? string.Empty,
					args.ExpectedReportingScope ?? string.Empty)
				: null;
			KnowledgeFeedbackPolicy policy = _service.Configure(new KnowledgeFeedbackPolicyUpdate(
				args.Mode,
				args.Destination,
				args.ReportingScope), requireConsent: true, consent: consent);
			return new KnowledgeFeedbackConfigureResponse(true, null, policy);
		} catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) {
			return new KnowledgeFeedbackConfigureResponse(false, exception.Message, _service.GetPolicy());
		}
	}
}

/// <summary>Arguments for updating knowledge-feedback policy.</summary>
public sealed record KnowledgeFeedbackConfigureArgs(
	[property: JsonPropertyName("mode")]
	[property: Description("Optional mode: ask, auto, or off. Explicit auto refreshes standing approval for the current policy hash.")]
	string? Mode = null,
	[property: JsonPropertyName("destination")]
	[property: Description("Optional exact credential-free HTTPS GitHub repository URL.")]
	string? Destination = null,
	[property: JsonPropertyName("reporting-scope")]
	[property: Description("Optional report detail policy: full for comprehensive internal evidence, or sanitized for public-safe reports.")]
	string? ReportingScope = null,
	[property: JsonPropertyName("confirmed")]
	[property: Description("Must be true when selecting auto or changing destination/reporting-scope while auto is configured.")]
	bool Confirmed = false,
	[property: JsonPropertyName("expected-policy-hash")]
	[property: Description("Exact reporting-policy hash shown to the user; required with confirmed=true for auto authorization or retargeting.")]
	string? ExpectedPolicyHash = null,
	[property: JsonPropertyName("expected-destination")]
	[property: Description("Exact normalized destination shown to the user; required with confirmed=true for auto authorization or retargeting.")]
	string? ExpectedDestination = null,
	[property: JsonPropertyName("expected-reporting-scope")]
	[property: Description("Exact reporting scope shown to the user; required with confirmed=true for auto authorization or retargeting.")]
	string? ExpectedReportingScope = null);

/// <summary>Result of an attempted knowledge-feedback policy update.</summary>
public sealed record KnowledgeFeedbackConfigureResponse(
	[property: JsonPropertyName("success")] bool Success,
	[property: JsonPropertyName("error")] string? Error,
	[property: JsonPropertyName("policy")] KnowledgeFeedbackPolicy Policy);
