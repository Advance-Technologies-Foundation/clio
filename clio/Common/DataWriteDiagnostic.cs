using System;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Clio.Common;

/// <summary>Optional diagnostic context without an inferred HTTP status or offending field.</summary>
public sealed record DataWriteDiagnostic(
	[property: JsonPropertyName("operation")] string Operation,
	[property: JsonPropertyName("entity")] string Entity,
	[property: JsonPropertyName("item-index")] int? ItemIndex,
	[property: JsonPropertyName("write-attempted")] bool WriteAttempted,
	[property: JsonPropertyName("transport-outcome")] string TransportOutcome,
	[property: JsonPropertyName("side-effect")] string SideEffect,
	[property: JsonPropertyName("retry-advice")] string RetryAdvice,
	[property: JsonPropertyName("message")] string Message) {
	/// <summary>Builds bounded context from observed write boundaries, preserving uncertainty after submission.</summary>
	public static DataWriteDiagnostic Create(string operation, string entity, int? itemIndex, bool attempted,
		bool responseReceived, bool acknowledged, string message) {
		string transport = (attempted, responseReceived) switch {
			(false, _) => "not-attempted", (true, true) => "response-received", _ => "unknown"
		};
		string effect = (attempted, acknowledged) switch {
			(false, _) => "not-attempted", (true, true) => "acknowledged", _ => "unknown"
		};
		string advice = effect switch {
			"not-attempted" => "Correct the input or permissions before submitting.",
			"acknowledged" => "Verify important values by readback; do not repeat solely because later inspection fails.",
			_ => "A write may have applied despite failure. Read affected records before deciding whether to resubmit; no automatic retry was made."
		};
		// Legacy write errors may append an unrecognized response body. Keep that preview
		// out of the new diagnostic, which is bounded context rather than a payload dump.
		int preview = message?.IndexOf(" Response: ", StringComparison.Ordinal) ?? -1;
		if (preview >= 0) {
			message = message[..preview];
		}
		entity = entity?.Trim();
		string safeEntity = entity is not null && Regex.IsMatch(entity, "^[A-Za-z_][A-Za-z0-9_]{0,127}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
			? entity : SensitiveErrorTextRedactor.RedactUntrustedOrNull(entity);
		return new(operation, safeEntity, itemIndex, attempted,
			transport, effect, advice, SensitiveErrorTextRedactor.RedactUntrustedOrNull(message));
	}
}
