namespace Clio.Common;

/// <summary>
/// The JSON readers' own parse ceiling, named once so a walk that relies on it says so.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="System.Text.Json.JsonDocument"/> and Newtonsoft 13 both refuse a document nested deeper than
/// 64 levels, and several recursive walks over already-parsed JSON are bounded by nothing else. That is a
/// sound bound — a document deep enough to exhaust it cannot have been parsed in the first place — but only
/// while it is STATED. Left implicit it is an unlabelled coupling to a BCL default: the walk looks
/// unbounded, a reader cannot tell whether the omission was reasoned or forgotten, and the day a reader is
/// configured with a larger <c>MaxDepth</c> nothing points at the code that assumed otherwise.
/// </para>
/// <para>
/// Use it as the bound of a recursion over parsed JSON, and pass the parsed depth down. Do NOT use it for a
/// walk with a budget of its own, chosen for a different reason — <c>MaxTemplateDepth</c> bounds how far a
/// template chain is followed and would be wrong at 64.
/// </para>
/// </remarks>
internal static class JsonReaderLimits {

	/// <summary>
	/// Maximum nesting depth either JSON reader will parse. Do NOT raise it: the value's whole point is that
	/// the parser refuses before the budget does.
	/// </summary>
	internal const int MaxParseDepth = 64;
}
