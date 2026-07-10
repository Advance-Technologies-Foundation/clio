using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Test-only invocation shim for <see cref="SchemaSyncTool"/>.
/// </summary>
/// <remarks>
/// <see cref="SchemaSyncTool.SchemaSync"/> gained MCP-infrastructure parameters (server, request context,
/// cancellation token); this shim preserves the terse <c>tool.SchemaSync(args)</c> call shape. A
/// <c>null</c> server means no progress token, so the batch runs inline with a no-op reporter. Heartbeat/
/// stage-marker plumbing is covered by <see cref="McpProgressHeartbeatTests"/>.
/// </remarks>
internal static class SchemaSyncToolTestInvocationExtensions {

	public static Task<SchemaSyncResponse> SchemaSync(
		this SchemaSyncTool tool, SchemaSyncArgs args) =>
		tool.SchemaSync(args, server: null, requestContext: null, cancellationToken: default);
}
