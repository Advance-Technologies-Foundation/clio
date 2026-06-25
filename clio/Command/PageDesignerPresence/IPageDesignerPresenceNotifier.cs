namespace Clio.Command;

/// <summary>
/// Publishes best-effort Designer Presence save notifications for page writes.
/// </summary>
public interface IPageDesignerPresenceNotifier {
	/// <summary>
	/// Attempts to publish a Designer Presence <c>save</c> event for the specified page.
	/// </summary>
	/// <param name="schemaName">Logical page schema name shown to collaborators.</param>
	/// <param name="schemaCaption">Optional page caption. When omitted, <paramref name="schemaName"/> is used.</param>
	/// <returns>
	/// <see langword="null"/> when the notification was sent successfully; otherwise a human-readable
	/// warning explaining why the push was skipped or failed. The caller must treat the warning as
	/// non-fatal because the page save has already succeeded.
	/// </returns>
	/// <remarks>
	/// This is a synchronous facade over an async publish that blocks via <c>GetAwaiter().GetResult()</c>.
	/// It is deadlock-free because both call sites run without a captured <see cref="System.Threading.SynchronizationContext"/>:
	/// the MCP host executes tools on the thread pool and the CLI executes on the console default context.
	/// All network steps inside are bounded by an internal timeout, so a blocked call cannot stall the caller indefinitely.
	/// </remarks>
	string? TryNotifyPageSaved(string schemaName, string? schemaCaption = null);
}
