using System;

namespace Clio.Command.McpServer.Progress;

/// <summary>
/// A source of typed <see cref="ClioStageEvent"/> progress events for a single deploy or uninstall run.
/// </summary>
/// <remarks>
/// This is the uniform seam the MCP layer subscribes to (ADR D3): a deploy/uninstall command exposes
/// <see cref="StageChanged"/>, an MCP tool attaches a handler via <c>configureCommand</c> (story 4),
/// and forwards each event as an MCP progress notification. The event surface follows the proven
/// <c>StartCommand.StatusChanged</c> pattern rather than an injected reporter. Emission is purely
/// observational: when no handler is attached the raising is a no-op and the operation is unchanged.
/// </remarks>
public interface IStageEventSource {

	/// <summary>
	/// Raised for every stage-progress transition of the current run: the up-front manifest, each
	/// per-stage <c>running</c>/<c>done</c>/<c>failed</c>/<c>skipped</c> transition, and the terminal
	/// <c>run-completed</c> event. All events of one run carry a stable <c>runId</c> and a
	/// monotonically increasing <c>sequence</c>.
	/// </summary>
	event EventHandler<ClioStageEvent> StageChanged;
}
