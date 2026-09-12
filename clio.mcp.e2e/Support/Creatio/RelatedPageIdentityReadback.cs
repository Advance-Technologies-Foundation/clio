using System.Text.Json.Nodes;
using Clio.Mcp.E2E.Support.Configuration;
using FluentAssertions;

namespace Clio.Mcp.E2E.Support.Creatio;

/// <summary>Independent persisted entity identity oracle for related-page tests.</summary>
internal static class RelatedPageIdentityReadback {
	internal static async Task<JsonObject> ReadRootAsync(McpE2ESettings settings, string entity, CancellationToken token) {
		JsonObject query = JsonNode.Parse("""
			{"rootSchemaName":"SysSchema","columns":{"items":{
			"UId":{"expression":{"expressionType":0,"columnPath":"UId"}},
			"PackageName":{"expression":{"expressionType":0,"columnPath":"SysPackage.Name"}}}},
			"filters":{"filterType":6,"logicalOperation":0,"items":{
			"name":{"filterType":1,"comparisonType":3,"leftExpression":{"expressionType":0,"columnPath":"Name"},"rightExpression":{"expressionType":2,"parameter":{"dataValueType":1,"value":""}}},
			"manager":{"filterType":1,"comparisonType":3,"leftExpression":{"expressionType":0,"columnPath":"ManagerName"},"rightExpression":{"expressionType":2,"parameter":{"dataValueType":1,"value":"EntitySchemaManager"}}},
			"root":{"filterType":1,"comparisonType":3,"leftExpression":{"expressionType":0,"columnPath":"ExtendParent"},"rightExpression":{"expressionType":2,"parameter":{"dataValueType":12,"value":false}}}
			}}}
			""")!.AsObject();
		query["filters"]!["items"]!["name"]!["rightExpression"]!["parameter"]!["value"] = entity;
		ClioCliCommandResult result = await ClioCliCommandRunner.RunAsync(settings,
			["call-service", "-e", settings.Sandbox.EnvironmentName!, "--service-path",
				"DataService/json/SyncReply/SelectQuery", "-b", query.ToJsonString()], cancellationToken: token);
		result.ExitCode.Should().Be(0, because: $"the independent SysSchema query must succeed: {result.StandardOutput}");
		string output = result.StandardOutput;
		int start = output.IndexOf('{');
		int end = output.LastIndexOf('}');
		start.Should().BeGreaterThanOrEqualTo(0, because: "the successful oracle call must include a JSON response");
		end.Should().BeGreaterThan(start, because: "the oracle response must include a complete JSON object");
		JsonObject response = JsonNode.Parse(output[start..(end + 1)])!.AsObject();
		response["success"]!.GetValue<bool>().Should().BeTrue(because: "the identity oracle must be a successful persisted read");
		JsonArray rows = response["rows"]!.AsArray();
		rows.Should().ContainSingle(because: "the named entity must have exactly one base row, independently of replacements");
		return rows[0]!.AsObject();
	}
}
