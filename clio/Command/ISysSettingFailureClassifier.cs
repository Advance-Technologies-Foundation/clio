using System;
using Clio.Common;

namespace Clio.Command;

/// <summary>
/// Classifies a sys-setting operation failure into the structured envelope the results carry, and writes
/// the one log line whose correlation ID the envelope quotes.
/// </summary>
/// <remarks>
/// Issue #1379. This capability used to live as <c>internal static</c> members of
/// <c>SysSettingsCommand</c>, which meant every MCP tool that needed a classified failure took an
/// <see cref="ILogger"/> and an <see cref="IOperationCorrelationIdProvider"/> only to feed those statics,
/// and named an unrelated command type from its own catch block. It is a cross-module capability - five
/// MCP tool classes plus the CLI command - so it is an injected service like any other behaviour class.
/// <para>
/// The interface is four members rather than the one "classify" suggests: minting the correlation ID and
/// writing the line that carries it are ONE operation (a token that finds nothing is worse than no
/// token), and the command's two non-exception report paths and its partial-create path need exactly
/// those two writes without a classification. Exposing them here is what keeps a single implementation
/// of each; duplicating them back into the command would be the drift this extraction removes.
/// Rendering the line is NOT on the interface - it has no caller outside
/// <see cref="LogFailureLine"/> - and neither is the legacy message-only overload, which had none at
/// all in production.
/// </para>
/// </remarks>
public interface ISysSettingFailureClassifier {

	/// <summary>
	/// Classifies a failure into the structured envelope the sys-setting results carry: the legacy
	/// message, the category an agent branches on, a cause, a recovery action, and the correlation ID
	/// that finds the log line written for the same failure.
	/// </summary>
	/// <param name="ex">The failure to classify.</param>
	/// <param name="operationLabel">The operation, as it reads inside the legacy message.</param>
	/// <param name="correlationId">The ID issued for this operation, or <see langword="null"/>.</param>
	/// <returns>The classified failure.</returns>
	SysSettingFailure Categorize(Exception ex, string operationLabel, string correlationId);

	/// <summary>
	/// Classifies a failure AND writes the one log line that carries its correlation ID, plus the
	/// neutralized server excerpt on the debug channel beside the same ID.
	/// </summary>
	/// <remarks>
	/// A correlation ID on a result that no log line mentions is worse than no ID at all: it invites the
	/// caller to quote a token that finds nothing. So minting the ID and writing the line are one
	/// operation, and every site that reports a classified failure goes through here.
	/// </remarks>
	/// <param name="ex">The failure to classify.</param>
	/// <param name="operationLabel">The operation, as it reads inside the legacy message.</param>
	/// <returns>The classified failure, carrying the freshly minted correlation ID.</returns>
	SysSettingFailure CategorizeAndLog(Exception ex, string operationLabel);

	/// <summary>
	/// Writes the one log line carrying the failure's correlation ID, and on the MCP path ALSO sends it
	/// to the client as a <c>notifications/message</c> under the <c>clio.tool.{correlationId}</c>
	/// category. For failures that arrive as DATA rather than as an exception, so there is nothing to
	/// classify but the same record still has to exist.
	/// </summary>
	/// <remarks>
	/// PR #1373 review: writing the line alone was not enough to make the ID resolvable. Running as an
	/// MCP server every ordinary sink is closed - <c>ConsoleLogger</c> suppresses console writes under
	/// <c>Program.IsMcpServerMode</c>, the log file exists only when the operator passed <c>--log</c>,
	/// and the sys-setting tools and <c>SchemaNamePrefixTool</c> are plain <c>[McpServerToolType]</c>
	/// classes that never flush the way <c>BaseTool</c> does. So the line reached nobody, while the
	/// shipped recovery text tells the caller to quote the ID.
	/// The notification is built from the line directly rather than by draining the shared
	/// <c>PreserveMessages</c> buffer: that buffer belongs to whatever flow is capturing (a
	/// <c>BaseTool</c> parent may be), and clearing it here would swallow messages this failure did not
	/// produce. <c>ForwardMessages</c> no-ops when no MCP server is active, so the CLI path is unchanged.
	/// </remarks>
	/// <param name="failure">The classified failure whose line is written.</param>
	void LogFailureLine(SysSettingFailure failure);

	/// <summary>
	/// Writes the neutralized server excerpt on the DEBUG channel only, tagged with the same correlation
	/// ID the failure envelope carries. No-ops when the failure carries no server text.
	/// </summary>
	/// <remarks>
	/// Issue #1333. The excerpt is server-authored text, so it may never appear in <c>error</c>,
	/// <c>cause</c>, the MCP envelope or the default log line - the fixed local diagnostic goes there
	/// instead. It still has to be recoverable, because an operator who cannot see what Creatio actually
	/// said cannot tell an expired password from a misconfigured proxy. The channel is debug-gated
	/// (<c>ConsoleLogger.WriteDebug</c> returns early unless <c>--debug</c> was passed) and its console
	/// drain is suppressed under MCP server mode, and the correlation ID is the bridge from the reported
	/// failure to the line.
	/// </remarks>
	/// <param name="ex">The failure whose chain is searched for a server excerpt.</param>
	/// <param name="correlationId">The ID the excerpt is tagged with.</param>
	void LogServerDetail(Exception ex, string correlationId);
}
