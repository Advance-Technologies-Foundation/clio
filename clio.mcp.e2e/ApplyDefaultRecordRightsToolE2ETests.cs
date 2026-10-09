using System.Collections.Generic;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end contract tests for the destructive apply-default-record-rights MCP tool (see the shared base).</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ApplyDefaultRecordRightsTool.ToolName)]
[NonParallelizable]
public sealed class ApplyDefaultRecordRightsToolE2ETests : ObjectRightsToolE2ETestsBase {

	protected override string ToolName => ApplyDefaultRecordRightsTool.ToolName;

	protected override bool ExpectedDestructive => true;

	protected override string UnknownArgumentName => "timeout";

	protected override Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName) => new() {
		["environment-name"] = environmentName,
		["entity-schema-name"] = "Contact",
		["wait"] = false,
		["timeout-seconds"] = 5
	};
}
