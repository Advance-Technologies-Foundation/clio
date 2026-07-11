using System.Threading;
using System.Threading.Tasks;
using Clio.Command.McpServer.Tools;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Test-only invocation shims for the application MCP tools.
/// </summary>
/// <remarks>
/// ENG-91274 added MCP-infrastructure parameters (the MCP server,
/// <c>RequestContext&lt;CallToolRequestParams&gt;</c>, and <see cref="CancellationToken"/>) to the
/// application tool methods so they can stream <c>notifications/progress</c>. The SDK injects those
/// at call time in production; they are not relevant to the backend-mapping unit tests, which only
/// assert how a tool maps service results into the structured response envelope.
/// <para>
/// These extensions preserve the original, terse call shapes used throughout
/// <c>ApplicationToolTests</c>. They are selected by overload resolution only because the real
/// instance methods are not applicable for the shorter argument lists (the SDK parameters are
/// required and the omitted <c>requestContext</c> has no default), so they never shadow a genuine
/// production call. A <c>null</c> server means no progress token, so the heartbeat wrapper runs the
/// work inline — exactly the path a progress-less client exercises. The dedicated heartbeat behavior
/// is covered by <see cref="McpProgressHeartbeatTests"/>.
/// </para>
/// </remarks>
internal static class ApplicationToolTestInvocationExtensions {

	public static ApplicationContextResponse ApplicationGetInfo(
		this ApplicationGetInfoTool tool, ApplicationGetInfoArgs args) =>
		tool.ApplicationGetInfo(args, server: null, requestContext: null, cancellationToken: default)
			.GetAwaiter().GetResult();

	public static Task<ApplicationContextResponse> ApplicationCreate(
		this ApplicationCreateTool tool, ApplicationCreateArgs args) =>
		tool.ApplicationCreate(args, server: null, requestContext: null, cancellationToken: default);

	public static Task<ApplicationSectionContextResponse> ApplicationSectionCreate(
		this ApplicationSectionCreateTool tool,
		ApplicationSectionCreateArgs args,
		global::ModelContextProtocol.Server.McpServer server) =>
		tool.ApplicationSectionCreate(args, server, requestContext: null, cancellationToken: default);

	public static Task<ApplicationSectionContextResponse> ApplicationSectionCreate(
		this ApplicationSectionCreateTool tool,
		ApplicationSectionCreateArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		CancellationToken cancellationToken) =>
		tool.ApplicationSectionCreate(args, server, requestContext: null, cancellationToken: cancellationToken);

	public static Task<ApplicationSectionUpdateContextResponse> ApplicationSectionUpdate(
		this ApplicationSectionUpdateTool tool,
		ApplicationSectionUpdateArgs args,
		global::ModelContextProtocol.Server.McpServer server) =>
		tool.ApplicationSectionUpdate(args, server, requestContext: null, cancellationToken: default);

	public static Task<ApplicationSectionUpdateContextResponse> ApplicationSectionUpdate(
		this ApplicationSectionUpdateTool tool,
		ApplicationSectionUpdateArgs args,
		global::ModelContextProtocol.Server.McpServer server,
		CancellationToken cancellationToken) =>
		tool.ApplicationSectionUpdate(args, server, requestContext: null, cancellationToken: cancellationToken);

	public static ApplicationSectionDeleteContextResponse ApplicationSectionDelete(
		this ApplicationSectionDeleteTool tool, ApplicationSectionDeleteArgs args) =>
		tool.ApplicationSectionDelete(args, server: null, requestContext: null, cancellationToken: default)
			.GetAwaiter().GetResult();

	public static ApplicationSectionListContextResponse ApplicationSectionGetList(
		this ApplicationSectionGetListTool tool, ApplicationSectionGetListArgs args) =>
		tool.ApplicationSectionGetList(args, server: null, requestContext: null, cancellationToken: default)
			.GetAwaiter().GetResult();
}
