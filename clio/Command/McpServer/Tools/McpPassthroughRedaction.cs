using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Shared MCP-boundary log hygiene for the credential-passthrough edge (FR-11, ENG-93208). Applies the
/// same sanitize-then-redact walk to a captured <see cref="LogMessage"/> snapshot before it crosses the
/// MCP boundary, so a command log line cannot leak the target host/URI (or credential material) into a
/// third-party LLM transcript on a passthrough request. Redaction is a no-op off passthrough (keyed on the
/// resolved tenant key), so the trusted stdio / <c>-e</c> path keeps full-fidelity logs.
/// <para>
/// Used by <see cref="BaseTool{T}"/> (the main execution path) AND by the tools that self-manage capture
/// and build their own result from <c>logger.LogMessages</c> instead of going through
/// <c>RunCommandUnderHeldLock</c> — those tools are not <see cref="BaseTool{T}"/> subclasses, so the logic
/// lives here rather than as a protected base member.
/// </para>
/// </summary>
internal static class McpPassthroughRedaction {

	/// <summary>
	/// True when <paramref name="tenantKey"/> identifies a credential-passthrough request — the signal that
	/// gates boundary redaction. Only a passthrough context produces a key with
	/// <see cref="ToolCommandResolver.PassthroughKeyPrefix"/>.
	/// </summary>
	/// <param name="tenantKey">The resolved per-tenant key.</param>
	/// <returns><see langword="true"/> on a passthrough request; otherwise <see langword="false"/>.</returns>
	internal static bool IsPassthroughKey(string tenantKey) =>
		tenantKey is not null
		&& tenantKey.StartsWith(ToolCommandResolver.PassthroughKeyPrefix, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// Projects every non-string <see cref="LogMessage.Value"/> to its rendered string form so the
	/// System.Text.Json serialization of the returned envelope never throws on a non-serializable value
	/// (e.g. a raw <c>ConsoleTable</c>). Mutates the snapshot in place; console rendering is untouched
	/// because the console reads from the live buffer, not this detached MCP snapshot.
	/// </summary>
	/// <param name="messages">The captured log messages to sanitize in place.</param>
	/// <returns>The same list, with non-string values stringified.</returns>
	internal static IReadOnlyList<LogMessage> SanitizeForSerialization(IReadOnlyList<LogMessage> messages) {
		foreach (LogMessage message in messages.Where(message => message.Value is not (null or string))) {
			message.Value = message.Value.ToString();
		}
		return messages;
	}

	/// <summary>
	/// Scrubs each string <see cref="LogMessage.Value"/> via <see cref="SensitiveErrorTextRedactor"/>, but
	/// ONLY on a credential-passthrough request (keyed on <paramref name="tenantKey"/>). Mutates the
	/// snapshot in place and runs AFTER <see cref="SanitizeForSerialization"/>, so a table-derived value
	/// already stringified is scrubbed too.
	/// </summary>
	/// <param name="messages">The captured log messages to redact in place on passthrough.</param>
	/// <param name="tenantKey">The resolved per-tenant key; a passthrough key triggers redaction.</param>
	/// <returns>The same list, redacted on passthrough and unchanged off passthrough.</returns>
	internal static IReadOnlyList<LogMessage> RedactForPassthrough(IReadOnlyList<LogMessage> messages, string tenantKey) {
		if (!IsPassthroughKey(tenantKey)) {
			return messages;
		}
		foreach (LogMessage message in messages) {
			if (message.Value is string text) {
				message.Value = SensitiveErrorTextRedactor.Redact(text);
			}
		}
		return messages;
	}

	/// <summary>
	/// Convenience combinator: <see cref="SanitizeForSerialization"/> then <see cref="RedactForPassthrough"/>
	/// — the exact MCP-boundary hygiene the main execution path applies. Reuse the tenant key the caller
	/// already resolved for its per-tenant lock.
	/// </summary>
	/// <param name="messages">The captured log messages to sanitize and (on passthrough) redact in place.</param>
	/// <param name="tenantKey">The resolved per-tenant key; a passthrough key triggers redaction.</param>
	/// <returns>The same list, sanitized and (on passthrough) redacted.</returns>
	internal static IReadOnlyList<LogMessage> SanitizeAndRedact(IReadOnlyList<LogMessage> messages, string tenantKey) =>
		RedactForPassthrough(SanitizeForSerialization(messages), tenantKey);
}
