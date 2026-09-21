using System.Collections.Generic;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end tests for the read-only get-object-rights MCP tool (see the shared base).</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(GetObjectRightsTool.ToolName)]
[NonParallelizable]
public sealed class GetObjectRightsToolE2ETests : ObjectRightsToolE2ETestsBase {

	protected override string ToolName => GetObjectRightsTool.ToolName;

	protected override bool ExpectedDestructive => false;

	protected override Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName) => new() {
		["environment-name"] = environmentName,
		["entity-schema-name"] = "Contact"
	};
}
