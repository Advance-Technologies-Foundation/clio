using System;

namespace Clio.Common;

/// <summary>
/// Issues the correlation ID that ties one operation's failure envelope to the log line written for it.
/// </summary>
/// <remarks>
/// The MCP generic path already stamps a correlation ID onto <c>CommandExecutionResult</c> - twelve hex
/// characters, surfaced as <c>correlation-id</c>, and reused as the log-notification category suffix
/// (<c>BaseTool.RunCommandUnderHeldLock</c> / <c>McpLogNotifier.ForwardMessages</c>). The typed sys-setting
/// tools do NOT run through that path: they resolve a command and return a record, so no ID was ever
/// issued for them. This service is that same ID - same format, same <c>correlation-id</c> field name,
/// same "appears in the log line and in the envelope" meaning - made available to a command.
/// <para>
/// Deliberately stateless: every caller needs the ID in the same method that writes the log line and
/// builds the result, so an ambient scope would add a lifetime question nobody asks.
/// </para>
/// </remarks>
public interface IOperationCorrelationIdProvider {

	/// <summary>Returns a fresh correlation ID for one operation.</summary>
	string New();
}

/// <inheritdoc cref="IOperationCorrelationIdProvider"/>
public sealed class OperationCorrelationIdProvider : IOperationCorrelationIdProvider {

	/// <summary>
	/// Length of the emitted ID. Matches the MCP generic path's <c>Guid.NewGuid().ToString("N")[..12]</c>
	/// so an operator grepping a log for a correlation ID does not have to know which path produced it.
	/// </summary>
	private const int CorrelationIdLength = 12;

	/// <inheritdoc/>
	public string New() => Guid.NewGuid().ToString("N")[..CorrelationIdLength];
}
