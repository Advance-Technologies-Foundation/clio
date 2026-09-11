namespace Clio.Common;

/// <summary>
/// A failure that kept a neutralized excerpt of the server text it was diagnosed from.
/// </summary>
/// <remarks>
/// Issue #1333: the excerpt must NOT appear in any caller-visible field - not in the exception message,
/// not in <c>error</c> / <c>cause</c>, and not in the MCP envelope. It exists so a handler that HAS a
/// logger can write it once at debug verbosity, beside the operation's correlation ID, which is the only
/// bridge from a reported failure back to what the server actually said.
/// <para>
/// Implemented only by exception types, so the DI auto-scan never sees it (BindingsModule skips every
/// <c>Exception</c> subtype).
/// </para>
/// </remarks>
public interface IServerDetailCarrier {

	/// <summary>The neutralized, length-capped excerpt of the server text, or <see langword="null"/>.</summary>
	string ServerDetail { get; }
}
