using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Clio.Common;

namespace Clio.Command.McpServer;

/// <summary>
/// The edge API-key gate for MCP HTTP credential passthrough (Story 5, FR-09/FR-10).
/// Decides whether per-request <c>X-Integration-Credentials</c> passthrough may be
/// honored: passthrough is enabled only when at least one operator-configured platform
/// API key is present, and an individual request is authorized only when it presents an
/// <c>Authorization: Bearer &lt;key&gt;</c> matching one of the configured keys. The
/// comparison is constant-time (<see cref="CryptographicOperations.FixedTimeEquals"/>)
/// and never echoes key material.
/// </summary>
public interface IPlatformApiKeyGate
{
	/// <summary>
	/// Gets a value indicating whether credential passthrough is enabled, i.e. at least
	/// one platform API key is configured. When <see langword="false"/> the edge behaves
	/// exactly as 8.1.0.72 (fail-closed): the credential header is ignored downstream.
	/// </summary>
	bool PassthroughEnabled { get; }

	/// <summary>
	/// Determines whether the supplied <c>Authorization</c> header value carries a
	/// <c>Bearer</c> token matching one of the configured platform API keys. The scheme
	/// match is case-insensitive; a missing, blank, or non-<c>Bearer</c> value returns
	/// <see langword="false"/>. Every configured key is compared with a constant-time
	/// comparison and no early-out is taken, so the result does not leak which key matched.
	/// The key material is never logged or echoed (FR-11).
	/// </summary>
	/// <param name="authorizationHeaderValue">The raw <c>Authorization</c> header value.</param>
	/// <returns><see langword="true"/> when the presented key matches a configured key; otherwise <see langword="false"/>.</returns>
	bool IsAuthorized(string authorizationHeaderValue);
}

/// <summary>
/// Default <see cref="IPlatformApiKeyGate"/>. Constructed from an already-resolved set of
/// platform API keys (split, trimmed, non-empty) so it is free of configuration reading and
/// fully unit-testable with an explicit key set.
/// </summary>
public sealed class PlatformApiKeyGate : IPlatformApiKeyGate
{
	private const string BearerScheme = AuthenticationScheme.Bearer;

	private readonly byte[][] _keyBytes;

	/// <summary>
	/// Initializes a new instance of the <see cref="PlatformApiKeyGate"/> class.
	/// </summary>
	/// <param name="keys">
	/// The resolved platform API keys (already split, trimmed, and non-empty). An empty
	/// set means passthrough is disabled (fail-closed).
	/// </param>
	public PlatformApiKeyGate(IReadOnlyList<string> keys) {
		ArgumentNullException.ThrowIfNull(keys);
		// Store the SHA-256 of each key rather than its raw bytes so IsAuthorized compares two
		// FIXED-width (32-byte) digests: FixedTimeEquals then never takes a length-mismatch branch, so
		// the comparison leaks neither which key matched NOR the configured key length (review).
		byte[][] encoded = new byte[keys.Count][];
		for (int i = 0; i < keys.Count; i++) {
			encoded[i] = SHA256.HashData(Encoding.UTF8.GetBytes(keys[i]));
		}

		_keyBytes = encoded;
	}

	/// <inheritdoc />
	public bool PassthroughEnabled => _keyBytes.Length > 0;

	/// <inheritdoc />
	public bool IsAuthorized(string authorizationHeaderValue) {
		if (string.IsNullOrWhiteSpace(authorizationHeaderValue)) {
			return false;
		}

		string trimmed = authorizationHeaderValue.Trim();
		if (trimmed.Length <= BearerScheme.Length
			|| !trimmed.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase)
			|| !char.IsWhiteSpace(trimmed[BearerScheme.Length])) {
			// Missing scheme, wrong scheme, or no separator between scheme and token.
			return false;
		}

		string presented = trimmed[BearerScheme.Length..].Trim();
		if (presented.Length == 0) {
			return false;
		}

		// Hash the presented token to the same fixed 32-byte width as the stored key digests, so the
		// comparison below is truly constant-time (no length-mismatch branch, review).
		byte[] presentedBytes = SHA256.HashData(Encoding.UTF8.GetBytes(presented));

		// Compare against EVERY configured key digest without an early-out. Both operands are 32-byte
		// SHA-256 digests, so FixedTimeEquals never short-circuits on length; the OR-accumulate ensures
		// the loop does not reveal which key matched.
		bool matched = false;
		foreach (byte[] key in _keyBytes) {
			matched |= CryptographicOperations.FixedTimeEquals(presentedBytes, key);
		}

		return matched;
	}
}

/// <summary>
/// Resolves the platform API key set for the MCP HTTP edge gate from both configuration
/// sources — the <c>--platform-api-key</c> CLI flag and the
/// <c>CLIO_MCP_HTTP_PLATFORM_API_KEY</c> environment variable — each a comma-separated set.
/// The two sources are unioned, trimmed, and emptied entries dropped. A non-empty result
/// means passthrough mode is enabled (AC-05).
/// </summary>
public static class PlatformApiKeyConfiguration
{
	/// <summary>The environment variable carrying a comma-separated platform API key set.</summary>
	public const string EnvironmentVariableName = "CLIO_MCP_HTTP_PLATFORM_API_KEY";

	/// <summary>
	/// Builds the resolved key set by unioning both comma-separated sources, trimming each
	/// entry, and dropping empty entries.
	/// </summary>
	/// <param name="flagValue">The <c>--platform-api-key</c> flag value (comma-separated set); may be <see langword="null"/>.</param>
	/// <param name="environmentValue">The <c>CLIO_MCP_HTTP_PLATFORM_API_KEY</c> value (comma-separated set); may be <see langword="null"/>.</param>
	/// <returns>The resolved, trimmed, non-empty key set (possibly empty).</returns>
	public static IReadOnlyList<string> Resolve(string flagValue, string environmentValue) {
		// Union both sources verbatim (no cross-source de-duplication): each is split/trimmed the
		// same way via the shared CommaSet helper, and duplicate keys are harmless to the gate.
		List<string> keys = [];
		keys.AddRange(CommaSet.Split(flagValue));
		keys.AddRange(CommaSet.Split(environmentValue));
		return keys;
	}
}
