using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Results;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Resolves a seeded installed application by code against a list-apps response.
/// Fires <see cref="Assert.Ignore(string)"/> when the seeded application is absent
/// from the target environment.
/// </summary>
internal static class SeededApplicationResolver {
	/// <summary>
	/// Looks up the seeded installed application inside a pre-parsed list-apps envelope.
	/// Use this overload when the caller already has the list-apps response in scope.
	/// </summary>
	public static ApplicationListItemEnvelope GetOrIgnore(
		ApplicationListResponseEnvelope listResponse,
		string applicationCode,
		string environmentName) {
		ApplicationListItemEnvelope? installedApplication = listResponse.Applications?
			.FirstOrDefault(application => string.Equals(
				application.Code,
				applicationCode,
				StringComparison.OrdinalIgnoreCase));
		if (installedApplication is not null) {
			return installedApplication;
		}

		Assert.Ignore(
			$"Seeded application with code '{applicationCode}' was not found on environment '{environmentName}'. Install the seed package before running E2E tests.");
		return null!;
	}

	/// <summary>
	/// Calls list-apps via the MCP session and resolves the seeded installed application using
	/// <see cref="GetOrIgnore"/>. Use this overload when the caller does not already have a parsed
	/// list-apps envelope.
	/// </summary>
	public static async Task<ApplicationListItemEnvelope> ResolveOrIgnoreAsync(
		McpServerSession session,
		CancellationToken cancellationToken,
		string environmentName,
		string applicationCode) {
		CallToolResult callResult = await session.CallToolAsync(
			ApplicationGetListTool.ApplicationGetListToolName,
			new Dictionary<string, object?> {
				["args"] = new Dictionary<string, object?> {
					["environment-name"] = environmentName
				}
			},
			cancellationToken);
		ApplicationListResponseEnvelope response = ApplicationResultParser.ExtractList(callResult);
		return GetOrIgnore(response, applicationCode, environmentName);
	}
}
