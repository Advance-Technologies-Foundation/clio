using System;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ModelContextProtocol.Protocol;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// The params a worker sends with <see cref="WorkerOperationSignalContract.NotificationMethod"/>.
/// </summary>
/// <param name="OperationFamily">
/// The <see cref="McpToolOperationFamily"/> whose work has ended, as its enum name.
/// </param>
/// <param name="ExitCode">The exit code the operation finished with.</param>
/// <remarks>
/// <para>
/// A data-only carrier, so it is a <see langword="record"/> per the DI policy. It is a declared type
/// rather than an anonymous object because it crosses a process boundary and is the thing the parent
/// parses.
/// </para>
/// <para>
/// <b>It carries no operation identity, no environment name and no message.</b> The parent does not need
/// them — it already knows which worker sent this, because the signal arrives on that worker's own
/// session — and every one of them would be a fact about a customer's environment travelling on a channel
/// that exists only to say "you may reap me now".
/// </para>
/// </remarks>
public sealed record WorkerOperationCompletedParams(
	[property: JsonPropertyName(WorkerOperationSignalContract.OperationFamilyPropertyName)]
	string OperationFamily,
	[property: JsonPropertyName(WorkerOperationSignalContract.ExitCodePropertyName)]
	int ExitCode);

/// <summary>
/// The PRIVATE completion signal between a sticky worker and its parent (ADR rule 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a private signal rather than "reap on terminal status".</b> Only two operation registries exist
/// — <c>ICompileOperationRegistry</c> and <c>IRestartOperationRegistry</c>. <c>install-process-builder</c>
/// and <c>create-app-section</c> have none, and <c>restart-by-credentials</c> is deliberately
/// unreportable, so three of the four long-running families have no terminal status a supervisor could
/// poll. Reaping on a registry would therefore work for compile and restart-by-name and leak a process
/// per call for everything else — the worst failure shape available, because it is invisible in exactly
/// the families nobody can observe.
/// </para>
/// <para>
/// <b>PRIVATE means consumed, not forwarded.</b> The parent reads this inside
/// <c>WorkerRelayOptions.NotificationTap</c> — the read loop's serial observation point — and returns
/// <see langword="false"/>, so it never reaches the real MCP client. Forwarding it would put clio's own
/// process plumbing into a client's notification stream, which is a contract change no client asked for
/// and which ADR rule 1's "relay notifications verbatim" was never about.
/// </para>
/// <para>
/// <b>The method name is namespaced under <c>clio/</c>.</b> MCP reserves <c>notifications/</c> but not
/// vendor sub-paths, and an un-namespaced name risks colliding with a future SDK notification, which
/// would make the parent reap a worker on somebody else's event.
/// </para>
/// </remarks>
public static class WorkerOperationSignalContract {

	/// <summary>
	/// The JSON-RPC notification method a worker sends when its long-running operation has finished.
	/// </summary>
	public const string NotificationMethod = "notifications/clio/worker-operation-completed";

	/// <summary>The params property naming the operation family.</summary>
	public const string OperationFamilyPropertyName = "operation-family";

	/// <summary>The params property carrying the operation's exit code.</summary>
	public const string ExitCodePropertyName = "exit-code";

	/// <summary>
	/// Builds the params a worker sends.
	/// </summary>
	/// <param name="family">The operation family whose work has ended.</param>
	/// <param name="exitCode">The exit code the operation finished with.</param>
	/// <returns>The params.</returns>
	public static WorkerOperationCompletedParams BuildParams(McpToolOperationFamily family, int exitCode) =>
		new(family.ToString(), exitCode);

	/// <summary>
	/// Reads a completion signal off a notification the worker sent.
	/// </summary>
	/// <param name="notification">The notification taken off the worker's pipe.</param>
	/// <param name="family">The family named by the signal; <see cref="McpToolOperationFamily.Unspecified"/> when absent or unrecognised.</param>
	/// <param name="exitCode">The exit code carried by the signal, or <see langword="null"/> when absent.</param>
	/// <returns><see langword="true"/> when this notification IS the completion signal.</returns>
	/// <remarks>
	/// A signal whose family is missing or unrecognised is still a signal: the parent already knows which
	/// worker sent it, so the family is a cross-check rather than the routing key, and refusing to reap on
	/// a payload it could not fully parse would leak the very process this exists to reap.
	/// </remarks>
	public static bool TryRead(JsonRpcNotification notification, out McpToolOperationFamily family,
		out int? exitCode) {
		family = McpToolOperationFamily.Unspecified;
		exitCode = null;
		if (notification is null
			|| !string.Equals(notification.Method, NotificationMethod, StringComparison.Ordinal)) {
			return false;
		}
		if (notification.Params is JsonObject payload) {
			if (payload.TryGetPropertyValue(OperationFamilyPropertyName, out JsonNode familyNode)
				&& Enum.TryParse(familyNode?.GetValue<string>(), ignoreCase: false,
					out McpToolOperationFamily parsedFamily)) {
				family = parsedFamily;
			}
			if (payload.TryGetPropertyValue(ExitCodePropertyName, out JsonNode exitCodeNode)
				&& exitCodeNode is JsonValue exitCodeValue
				&& exitCodeValue.TryGetValue(out int parsedExitCode)) {
				exitCode = parsedExitCode;
			}
		}
		return true;
	}
}
