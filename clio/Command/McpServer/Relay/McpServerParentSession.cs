using System;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using McpServerLib = ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Relay;

/// <summary>
/// The one production <see cref="IParentMcpSession"/>: the live MCP server the real client is talking to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is a struct.</b> The assembly interface scan in <c>BindingsModule</c> auto-registers every
/// CLASS that implements a <c>Clio</c>-namespaced <c>I…</c> interface, and the container is built with
/// <c>ValidateOnBuild</c>. A class here would therefore be registered into EVERY graph — including the
/// ordinary CLI one, where the MCP host is not registered and
/// <see cref="McpServerLib.McpServer"/> cannot be resolved — and validation would refuse to build the
/// provider at all. A struct is skipped by that scan (it takes classes only), so this adapter can take the
/// live server the only way it can be obtained: from the request context, at runtime. The alternative is a
/// skip-list entry for this namespace in <c>BindingsModule</c>, the way the worker-supervisor namespace
/// already has one — and that entry now exists, so this adapter is belt AND braces: a struct cannot be
/// picked up by the scan even if the skip-list entry is ever removed while refactoring.
/// </para>
/// <para>
/// <b>Why the MCP9005 suppressions live here and nowhere else.</b> Sampling is deprecated as of protocol
/// revision <c>2026-07-28</c> (SEP-2577) and every member it needs is <c>[Obsolete]</c> in SDK 2.2.0. It
/// still works — 121/121 relayed runs — so ADR rule 1 is implementable today, and the semantic review in
/// <c>update-page</c> / <c>sync-pages</c> depends on it. Confining the suppression to this adapter keeps
/// the deprecation visible and countable: when OQ-6 migrates to <c>InputRequest</c> /
/// <c>ResolveInputRequestsAsync</c>, this file is the whole change. Do not build anything new on it.
/// </para>
/// </remarks>
public readonly struct McpServerParentSession : IParentMcpSession, IEquatable<McpServerParentSession> {

	private readonly McpServerLib.McpServer _server;

	/// <summary>
	/// Initializes a new instance of the <see cref="McpServerParentSession"/> struct.
	/// </summary>
	/// <param name="server">
	/// The live server session the real client is connected to — in a tool, <c>RequestContext.Server</c>.
	/// </param>
	/// <exception cref="ArgumentNullException"><paramref name="server"/> is <c>null</c>.</exception>
	public McpServerParentSession(McpServerLib.McpServer server) =>
		_server = server ?? throw new ArgumentNullException(nameof(server));

	/// <inheritdoc/>
	public bool SupportsSampling =>
		// MCP9005: reading the client's advertised sampling capability. Deprecated in 2.2.0 but still the
		// only way to know whether a child's sampling request can be served; see the remarks above (OQ-6).
#pragma warning disable MCP9005
		_server is not null && _server.ClientCapabilities?.Sampling is not null;
#pragma warning restore MCP9005

	/// <inheritdoc/>
	public Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken) =>
		Server().SendMessageAsync(message, cancellationToken);

	// MCP9005: forwarding the child's sampling request to the REAL client. Without this the page semantic
	// review degrades to Skipped=true with no error anywhere (ADR rule 1); see the remarks above for why
	// the deprecation is accepted rather than suppressed silently (OQ-6).
#pragma warning disable MCP9005
	/// <inheritdoc/>
	public ValueTask<CreateMessageResult> SampleAsync(CreateMessageRequestParams requestParams,
		CancellationToken cancellationToken) => Server().SampleAsync(requestParams, cancellationToken);
#pragma warning restore MCP9005

	/// <inheritdoc/>
	public bool Equals(McpServerParentSession other) => ReferenceEquals(_server, other._server);

	/// <inheritdoc/>
	public override bool Equals(object obj) => obj is McpServerParentSession other && Equals(other);

	/// <inheritdoc/>
	public override int GetHashCode() => _server?.GetHashCode() ?? 0;

	private McpServerLib.McpServer Server() =>
		_server ?? throw new InvalidOperationException(
			"This McpServerParentSession carries no server: it was default-constructed rather than given "
			+ "the live session from the request context.");
}
