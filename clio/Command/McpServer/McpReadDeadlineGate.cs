using System;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer;

/// <summary>
/// Single authority deciding whether an MCP tool is <em>retry-safe</em> and therefore eligible for the
/// read-response deadline (<see cref="McpReadResponseDeadline"/>, ENG-93373). Shared by the matched-tool
/// filter (<see cref="McpToolErrorFilter"/>) and the unmatched-name durable handler
/// (<see cref="McpDurableCallToolHandler"/>) so the classification can never drift between the two
/// dispatch paths.
/// </summary>
internal static class McpReadDeadlineGate {

	/// <summary>
	/// Returns <see langword="true"/> when a tool with the given safety hints is retry-safe: a caller
	/// can retry the call after a deadline without risking a duplicated or destructive effect.
	/// </summary>
	/// <remarks>
	/// The predicate is
	/// <c>!destructive &amp;&amp; !is-progress-streaming-read &amp;&amp; (readOnly || is-Creatio-read-with-local-write)</c>:
	/// <list type="bullet">
	/// <item>Destructive tools are always excluded — they own their own timeout contract (for example
	/// <c>create-app-section</c>'s <c>in-progress</c> / "do NOT retry" envelope), and a "safe to retry"
	/// timeout could duplicate their effect.</item>
	/// <item>Progress-streaming reads are excluded by name — today only <c>get-app-info</c>. It is
	/// <c>ReadOnly=true</c> but streams <c>notifications/progress</c> and already runs under the write-path
	/// heartbeat (<see cref="Tools.McpProgressHeartbeat"/>); its contract tells callers to await completion
	/// rather than retry on a perceived timeout. Wrapping it in the "safe to retry" read deadline would cut
	/// off a legitimately long read of a large app and contradict that contract (ENG-93373).</item>
	/// <item>Read-only tools (they do not mutate the Creatio environment) are the obvious case.</item>
	/// <item>A tool that reads from Creatio but writes only to the LOCAL filesystem is included by name —
	/// today that is only <c>get-page</c> (<c>ReadOnly=false</c> because it writes local
	/// <c>.clio-pages</c> files, but a retry re-reads Creatio and overwrites the files, so it is safe).</item>
	/// </list>
	/// The <c>Idempotent</c> hint is deliberately NOT part of the predicate: an idempotent NON-read write
	/// (for example <c>install-gate</c>, <c>generate-source-code</c>, <c>add-package-dependency</c>) is
	/// idempotent only for SEQUENTIAL re-runs, not for a retry issued while the abandoned first call is
	/// still mutating the server — bounding those with "safe to retry" guidance would invite a concurrent
	/// duplicate write. The read deadline therefore covers reads only, never server writes (ENG-93373).
	/// </remarks>
	/// <param name="toolName">The MCP tool name (used to admit the local-write read tools and exclude the progress-streaming reads by name).</param>
	/// <param name="readOnly">The tool's <c>ReadOnly</c> hint.</param>
	/// <param name="destructive">The tool's <c>Destructive</c> hint.</param>
	/// <returns><see langword="true"/> if the tool is retry-safe; otherwise <see langword="false"/>.</returns>
	internal static bool IsRetrySafe(string toolName, bool readOnly, bool destructive) =>
		!destructive
		&& !IsProgressStreamingRead(toolName)
		&& (readOnly || IsCreatioReadWithLocalWrite(toolName));

	/// <summary>
	/// Overload that reads the hints from an MCP <see cref="ToolAnnotations"/> block. A <see langword="null"/>
	/// annotations block (or one with no destructive hint) is treated as destructive/unknown and therefore
	/// NOT retry-safe — fail-closed, so an unannotated tool never gets the "safe to retry" deadline.
	/// </summary>
	/// <param name="toolName">The MCP tool name (used to admit the local-write read tools by name).</param>
	/// <param name="annotations">The tool's protocol annotations, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> if the annotated tool is retry-safe; otherwise <see langword="false"/>.</returns>
	internal static bool IsRetrySafe(string toolName, ToolAnnotations? annotations) =>
		annotations is not null
		&& IsRetrySafe(
			toolName,
			annotations.ReadOnlyHint ?? false,
			annotations.DestructiveHint ?? true);

	// The single curated allowlist of tools that read from Creatio but are annotated ReadOnly=false only
	// because they write to the LOCAL filesystem. A retry re-reads Creatio and overwrites the local files,
	// so they are retry-safe despite ReadOnly=false. Today this is exactly get-page; keep the set tiny and
	// explicit rather than widening the predicate to all idempotent tools (which would admit server writes).
	private static bool IsCreatioReadWithLocalWrite(string toolName) =>
		string.Equals(toolName, GetPageToolName, StringComparison.Ordinal);

	// The curated exclusion list of read-only tools that legitimately stream notifications/progress and run
	// under the write-path heartbeat (McpProgressHeartbeat): their contract is "await completion, do not
	// retry on a perceived timeout", so the "safe to retry" read deadline must never bound them. Today this
	// is exactly get-app-info; keep it a tiny explicit set rather than inferring it from the annotations.
	private static bool IsProgressStreamingRead(string toolName) =>
		string.Equals(toolName, GetAppInfoToolName, StringComparison.Ordinal);

	// Duplicated as a literal (not a reference to Tools.PageGetTool.ToolName) to keep this gate free of a
	// dependency on the Tools namespace; the name is asserted against PageGetTool.ToolName in the gate tests.
	private const string GetPageToolName = "get-page";

	// Duplicated as a literal (not a reference to Tools.ApplicationGetInfoTool.ApplicationGetInfoToolName)
	// for the same reason; the name is asserted against ApplicationGetInfoToolName in the gate tests.
	private const string GetAppInfoToolName = "get-app-info";
}
