using System.Text.Json;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>
/// Stand helpers for the web service E2E tests: the environment's schema-name prefix, which every generated code starts
/// with, and the removal of throwaway web services through the platform's own <c>ServiceSchemaRemoveRequest</c> (the web
/// service tools have no delete tool).
/// </summary>
internal static class WebServiceStand {

	/// <summary>Reads the stand's <c>SchemaNamePrefix</c>; empty when the setting is empty.</summary>
	/// <param name="settings">The E2E settings.</param>
	/// <param name="environmentName">The registered stand.</param>
	/// <param name="token">Cancels the CLI call.</param>
	/// <returns>The prefix, trimmed.</returns>
	public static async Task<string> ReadPrefixAsync(McpE2ESettings settings, string environmentName,
		CancellationToken token) {
		ClioCliCommandResult queried = await ClioCliCommandRunner.RunAsync(settings, [
			"call-service", "-e", environmentName, "--service-path", "DataService/json/SyncReply/QuerySysSettings",
			"-b", JsonSerializer.Serialize(new { sysSettingsNameCollection = new[] { "SchemaNamePrefix" } })
		], cancellationToken: token);
		queried.ExitCode.Should().Be(0,
			because: $"the codes are built from the stand's prefix; output: {queried.StandardOutput} {queried.StandardError}");
		using JsonDocument answer = ParseJson(queried, "the SchemaNamePrefix read");
		return answer.RootElement.GetProperty("values").TryGetProperty("SchemaNamePrefix", out JsonElement setting)
			&& setting.TryGetProperty("value", out JsonElement value) && value.ValueKind == JsonValueKind.String
				? value.GetString()!.Trim()
				: string.Empty;
	}

	/// <summary>
	/// Removes every web service with one of the codes. Each code is tried even when an earlier one fails; the first
	/// failure is reported after all were tried, so a teardown never leaves the rest behind silently.
	/// </summary>
	/// <param name="settings">The E2E settings.</param>
	/// <param name="environmentName">The registered stand.</param>
	/// <param name="codes">The service codes the test created.</param>
	/// <param name="token">Cancels the CLI calls.</param>
	public static async Task RemoveServicesAsync(McpE2ESettings settings, string environmentName,
		IEnumerable<string> codes, CancellationToken token) {
		var failures = new List<string>();
		foreach (string code in codes) {
			ClioCliCommandResult rows = await ClioCliCommandRunner.RunAsync(settings, [
				"call-service", "-e", environmentName, "--service-path", "DataService/json/SyncReply/SelectQuery",
				"-b", SelectByName(code)
			], cancellationToken: token);
			if (rows.ExitCode != 0 || rows.StandardOutput.IndexOf('{') < 0) {
				failures.Add($"{code}: the service could not be looked up ({rows.StandardError})");
				continue;
			}
			using JsonDocument answer = ParseJson(rows, $"the lookup of {code}");
			foreach (JsonElement row in answer.RootElement.GetProperty("rows").EnumerateArray()) {
				ClioCliCommandResult removed = await ClioCliCommandRunner.RunAsync(settings, [
					"call-service", "-e", environmentName, "--service-path",
					"DataService/json/SyncReply/ServiceSchemaRemoveRequest", "-b", JsonSerializer.Serialize(new {
						uId = row.GetProperty("UId").GetString(), packageUId = row.GetProperty("PackageUId").GetString()
					})
				], cancellationToken: token);
				if (removed.ExitCode != 0) {
					failures.Add($"{code}: the removal failed ({removed.StandardError})");
				}
			}
		}
		failures.Should().BeEmpty(because: "every throwaway web service is removed from the shared stand");
	}

	private static JsonDocument ParseJson(ClioCliCommandResult result, string what) {
		int start = result.StandardOutput.IndexOf('{');
		start.Should().BeGreaterThanOrEqualTo(0, because: $"{what} answers JSON; output: {result.StandardOutput}");
		return JsonDocument.Parse(result.StandardOutput[start..]);
	}

	private static string SelectByName(string code) =>
		JsonSerializer.Serialize(new {
			rootSchemaName = "VwWebServiceV2",
			operationType = 0,
			columns = new {
				items = new[] { "UId", "PackageUId" }.ToDictionary(c => c,
					c => (object)new { expression = new { expressionType = 0, columnPath = c } })
			},
			filters = new {
				rootSchemaName = "VwWebServiceV2", filterType = 6, logicalOperation = 0, isEnabled = true,
				items = new Dictionary<string, object> {
					["Name"] = new {
						filterType = 1, comparisonType = 3, isEnabled = true, dataValueType = 1,
						leftExpression = new { expressionType = 0, columnPath = "Name" },
						rightExpression = new { expressionType = 2, parameter = new { dataValueType = 1, value = code } }
					}
				}
			}
		});
}
