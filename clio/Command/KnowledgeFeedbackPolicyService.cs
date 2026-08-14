using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Clio.Command.McpServer.Knowledge;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>
/// Resolves and updates the effective policy for reporting discrepancies in Clio knowledge.
/// </summary>
public interface IKnowledgeFeedbackPolicyService {
	/// <summary>Returns the configured and effective policy against the active reporting article.</summary>
	/// <returns>The current feedback policy and standing-approval state.</returns>
	KnowledgeFeedbackPolicy GetPolicy();

	/// <summary>Applies a partial policy update and returns the resulting effective policy.</summary>
	/// <param name="update">Fields to update; omitted fields retain their current values.</param>
	/// <param name="requireConsent">When true, automatic authorization requires a bound consent snapshot.</param>
	/// <param name="consent">Exact policy snapshot shown to the user, when consent is required.</param>
	/// <returns>The persisted and re-evaluated policy.</returns>
	KnowledgeFeedbackPolicy Configure(
		KnowledgeFeedbackPolicyUpdate update,
		bool requireConsent = false,
		KnowledgeFeedbackConsent? consent = null);
}

/// <summary>Partial update accepted by the knowledge-feedback policy service.</summary>
/// <param name="Mode">Requested mode: ask, auto, or off.</param>
/// <param name="Destination">Exact GitHub repository URL.</param>
/// <param name="ReportingScope">Report detail policy: full or sanitized.</param>
public sealed record KnowledgeFeedbackPolicyUpdate(
	string? Mode = null,
	string? Destination = null,
	string? ReportingScope = null);

/// <summary>Immutable policy snapshot to which an MCP consent assertion is bound.</summary>
public sealed record KnowledgeFeedbackConsent(
	string PolicyHash,
	string Destination,
	string ReportingScope);

/// <summary>Effective knowledge-feedback policy returned to agents and CLI callers.</summary>
public sealed record KnowledgeFeedbackPolicy(
	[property: JsonPropertyName("configuredMode")] string ConfiguredMode,
	[property: JsonPropertyName("effectiveMode")] string EffectiveMode,
	[property: JsonPropertyName("destination")] string Destination,
	[property: JsonPropertyName("reportingScope")] string ReportingScope,
	[property: JsonPropertyName("reportingPolicyHash")] string? ReportingPolicyHash,
	[property: JsonPropertyName("standingApproval")] KnowledgeFeedbackApprovalView? StandingApproval,
	[property: JsonPropertyName("approvalState")] string ApprovalState);

/// <summary>Agent-readable view of the persisted standing approval.</summary>
public sealed record KnowledgeFeedbackApprovalView(
	[property: JsonPropertyName("policyHash")] string PolicyHash);

internal sealed class KnowledgeFeedbackPolicyService : IKnowledgeFeedbackPolicyService {
	internal const string ReportingGuidanceName = "knowledge-feedback";
	internal const string AskMode = "ask";
	internal const string AutoMode = "auto";
	internal const string OffMode = "off";
	internal const string FullScope = "full";
	internal const string SanitizedScope = "sanitized";

	private readonly ISettingsRepository _settingsRepository;
	private readonly IKnowledgeGuidanceSource _guidanceSource;

	public KnowledgeFeedbackPolicyService(
		ISettingsRepository settingsRepository,
		IKnowledgeGuidanceSource guidanceSource) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
		_guidanceSource = guidanceSource ?? throw new ArgumentNullException(nameof(guidanceSource));
	}

	public KnowledgeFeedbackPolicy GetPolicy() {
		_settingsRepository.Reload();
		KnowledgeFeedbackSettings settings = _settingsRepository.GetKnowledgeFeedbackSettings();
		return Resolve(settings, TryGetReportingPolicyHash());
	}

	public KnowledgeFeedbackPolicy Configure(
		KnowledgeFeedbackPolicyUpdate update,
		bool requireConsent = false,
		KnowledgeFeedbackConsent? consent = null) {
		ArgumentNullException.ThrowIfNull(update);
		string? policyHash = TryGetReportingPolicyHash();
		KnowledgeFeedbackSettings settings = _settingsRepository.UpdateKnowledgeFeedbackSettings(current => {
			bool modeWasSupplied = !string.IsNullOrWhiteSpace(update.Mode);
			string mode = modeWasSupplied ? NormalizeMode(update.Mode!) : NormalizeMode(current.Mode);
			bool disablesFeedback = string.Equals(mode, OffMode, StringComparison.Ordinal);
			string destination = update.Destination is null && disablesFeedback
				? current.Destination
				: update.Destination is null
				? NormalizeDestination(current.Destination)
				: NormalizeDestination(update.Destination);
			string reportingScope = update.ReportingScope is null && disablesFeedback
				? current.ReportingScope
				: update.ReportingScope is null
				? NormalizeReportingScope(current.ReportingScope)
				: NormalizeReportingScope(update.ReportingScope);
			bool selectsAuto = modeWasSupplied && string.Equals(mode, AutoMode, StringComparison.Ordinal);
			bool retargetsAuto = string.Equals(current.Mode?.Trim(), AutoMode, StringComparison.OrdinalIgnoreCase)
				&& (!modeWasSupplied || selectsAuto)
				&& (update.Destination is not null || update.ReportingScope is not null);
			if (requireConsent && (selectsAuto || retargetsAuto)) {
				if (consent is null) {
					throw new InvalidOperationException(
						"Selecting auto or changing its destination/reporting scope requires confirmed=true and the exact policy snapshot shown to the user.");
				}
				string approvedDestination = NormalizeDestination(consent.Destination);
				string approvedScope = NormalizeReportingScope(consent.ReportingScope);
				if (!string.Equals(consent.PolicyHash, policyHash, StringComparison.Ordinal)
						|| !string.Equals(approvedDestination, destination, StringComparison.Ordinal)
						|| !string.Equals(approvedScope, reportingScope, StringComparison.Ordinal)) {
					throw new InvalidOperationException(
						"The knowledge-feedback policy changed after it was shown. Read the current policy and ask for approval again.");
				}
			}
			current.Mode = mode;
			current.Destination = destination;
			current.ReportingScope = reportingScope;
			if (selectsAuto) {
				current.StandingApproval = new KnowledgeFeedbackStandingApproval {
					PolicyHash = policyHash ?? throw new InvalidOperationException(
						$"Cannot approve automatic feedback because '{ReportingGuidanceName}' guidance is unavailable.")
				};
			} else if (modeWasSupplied) {
				current.StandingApproval = null;
			}
			return current;
		});
		return Resolve(settings, TryGetReportingPolicyHash());
	}

	private KnowledgeFeedbackPolicy Resolve(KnowledgeFeedbackSettings settings, string? currentPolicyHash) {
		string configuredMode;
		try {
			configuredMode = NormalizeMode(settings.Mode);
		} catch (ArgumentException exception) {
			return new KnowledgeFeedbackPolicy(
				settings.Mode ?? AskMode,
				AskMode,
				settings.Destination ?? string.Empty,
				settings.ReportingScope ?? SanitizedScope,
				currentPolicyHash,
				ToApprovalView(settings.StandingApproval),
				$"invalid-configuration: {exception.Message}");
		}
		KnowledgeFeedbackApprovalView? approval = ToApprovalView(settings.StandingApproval);
		if (string.Equals(configuredMode, OffMode, StringComparison.Ordinal)) {
			return new KnowledgeFeedbackPolicy(
				configuredMode,
				OffMode,
				settings.Destination ?? string.Empty,
				settings.ReportingScope ?? SanitizedScope,
				currentPolicyHash,
				approval,
				"disabled");
		}
		string destination;
		string reportingScope;
		try {
			destination = NormalizeDestination(settings.Destination);
			reportingScope = NormalizeReportingScope(settings.ReportingScope);
		} catch (ArgumentException exception) {
			return new KnowledgeFeedbackPolicy(
				settings.Mode ?? AskMode,
				AskMode,
				settings.Destination ?? string.Empty,
				settings.ReportingScope ?? SanitizedScope,
				currentPolicyHash,
				ToApprovalView(settings.StandingApproval),
				$"invalid-configuration: {exception.Message}");
		}

		if (!string.Equals(configuredMode, AutoMode, StringComparison.Ordinal)) {
			return new KnowledgeFeedbackPolicy(
				configuredMode,
				configuredMode,
				destination,
				reportingScope,
				currentPolicyHash,
				approval,
				string.Equals(configuredMode, OffMode, StringComparison.Ordinal) ? "disabled" : "ask-each-time");
		}
		if (approval is null) {
			return CreateApprovalRequiredPolicy(
				configuredMode, destination, reportingScope, currentPolicyHash, null, "approval-missing");
		}
		if (currentPolicyHash is null) {
			return new KnowledgeFeedbackPolicy(
				configuredMode,
				AutoMode,
				destination,
				reportingScope,
				null,
				approval,
				"approved-policy-unavailable");
		}
		if (!string.Equals(currentPolicyHash, approval.PolicyHash, StringComparison.Ordinal)) {
			return CreateApprovalRequiredPolicy(
				configuredMode, destination, reportingScope, currentPolicyHash, approval, "reporting-policy-changed");
		}
		return new KnowledgeFeedbackPolicy(
			configuredMode,
			AutoMode,
			destination,
			reportingScope,
			currentPolicyHash,
			approval,
			"approved");
	}

	private static KnowledgeFeedbackPolicy CreateApprovalRequiredPolicy(
		string configuredMode,
		string destination,
		string reportingScope,
		string? currentPolicyHash,
		KnowledgeFeedbackApprovalView? approval,
		string reason) => new(
			configuredMode,
			AskMode,
			destination,
			reportingScope,
			currentPolicyHash,
			approval,
			reason);

	private string? TryGetReportingPolicyHash() {
		try {
			KnowledgeArticleLookup lookup = _guidanceSource.FindByName(ReportingGuidanceName);
			return lookup.Status == KnowledgeArticleLookupStatus.Active
				? ComputePolicyHash(lookup.Article.Text)
				: null;
		} catch (Exception) {
			// Feedback projection is optional enrichment on every guidance response. A damaged or
			// temporarily unavailable knowledge store must not fail the requested guidance lookup.
			return null;
		}
	}

	internal static string ComputePolicyHash(string articleText) {
		ArgumentNullException.ThrowIfNull(articleText);
		byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(articleText));
		return $"sha256:{Convert.ToHexString(digest).ToLowerInvariant()}";
	}

	internal static string NormalizeDestination(string destination) {
		if (!Uri.TryCreate(destination?.Trim(), UriKind.Absolute, out Uri? uri)
				|| !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
				|| string.IsNullOrWhiteSpace(uri.Host)
				|| !string.IsNullOrEmpty(uri.UserInfo)
				|| !string.IsNullOrEmpty(uri.Query)
				|| !string.IsNullOrEmpty(uri.Fragment)) {
			throw new ArgumentException(
				"Knowledge-feedback destination must be a credential-free HTTPS GitHub repository URL.",
				nameof(destination));
		}
		string path = uri.AbsolutePath.Trim('/');
		if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
			path = path[..^4];
		}
		if (path.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2) {
			throw new ArgumentException(
				"Knowledge-feedback destination must identify a repository as owner/repository.",
				nameof(destination));
		}
		string authority = uri.IsDefaultPort ? uri.IdnHost : $"{uri.IdnHost}:{uri.Port}";
		return $"https://{authority.ToLowerInvariant()}/{path}";
	}

	private static string NormalizeMode(string mode) {
		string normalized = mode?.Trim().ToLowerInvariant() ?? string.Empty;
		return normalized is AskMode or AutoMode or OffMode
			? normalized
			: throw new ArgumentException("Knowledge-feedback mode must be ask, auto, or off.", nameof(mode));
	}

	private static string NormalizeReportingScope(string reportingScope) {
		string normalized = reportingScope?.Trim().ToLowerInvariant() ?? string.Empty;
		return normalized is FullScope or SanitizedScope
			? normalized
			: throw new ArgumentException(
				"Knowledge-feedback reporting scope must be full or sanitized.",
				nameof(reportingScope));
	}

	private static KnowledgeFeedbackApprovalView? ToApprovalView(
		KnowledgeFeedbackStandingApproval? approval) {
		string hash = approval?.PolicyHash;
		if (hash is null || hash.Length != 71 || !hash.StartsWith("sha256:", StringComparison.Ordinal)
				|| !hash.AsSpan(7).ToString().All(character =>
					character is >= '0' and <= '9' or >= 'a' and <= 'f')) {
			return null;
		}
		return new KnowledgeFeedbackApprovalView(hash);
	}
}
