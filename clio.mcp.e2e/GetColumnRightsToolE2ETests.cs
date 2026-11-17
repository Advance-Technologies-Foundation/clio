using System.Collections.Generic;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end tests for the read-only get-column-rights MCP tool (see the shared base).</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetColumnRightsTool.ToolName)]
[NonParallelizable]
public sealed class GetColumnRightsToolE2ETests : ObjectRightsToolE2ETestsBase {

	protected override string ToolName => GetColumnRightsTool.ToolName;

	protected override bool ExpectedDestructive => false;

	protected override string UnknownArgumentName => "column-name";

	protected override Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName) => new() {
		["environment-name"] = environmentName,
		["entity-schema-name"] = "Contact"
	};
}
