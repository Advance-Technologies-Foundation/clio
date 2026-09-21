namespace Clio.Common;

/// <summary>
/// A failure whose caller-visible message exists in two renderings, because its two sinks have
/// different readers.
/// </summary>
/// <remarks>
/// PR #1374 review. Issue #1333 lets exactly one kind of server text through to a caller-visible field -
/// the platform's own validation prose - and neutralizes it on the way. For an MCP envelope that
/// neutralization includes the <c>[untrusted-source-text begin] … [untrusted-source-text end]</c> fence,
/// because a model reads the field and must be able to tell observed data from an instruction. A terminal
/// has no such reader: there the fence has no audience, and
/// <c>Failed updating sys-setting: [untrusted-source-text begin] Column 'Name' is required.
/// [untrusted-source-text end]</c> reads as clio malfunctioning.
/// <para>
/// So the failure carries both and each sink picks, rather than one rendering being wrong somewhere.
/// <see cref="System.Exception.Message"/> stays the agent rendering, so every existing consumer -
/// the MCP envelope included - is unchanged; only a console-only sink reads
/// <see cref="ConsoleMessage"/>.
/// </para>
/// </remarks>
public interface IConsoleRenderedFailure {

	/// <summary>
	/// The message as it should read at a terminal: scrubbed and capped like
	/// <see cref="System.Exception.Message"/>, but without the agent fence.
	/// </summary>
	string ConsoleMessage { get; }
}
