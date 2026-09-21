using System.Collections.Generic;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end tests for the destructive set-object-rights MCP tool (see the shared base).</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SetObjectRightsTool.ToolName)]
[NonParallelizable]
public sealed class SetObjectRightsToolE2ETests : ObjectRightsToolE2ETestsBase {

	protected override string ToolName => SetObjectRightsTool.ToolName;

	protected override bool ExpectedDestructive => true;

	protected override Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName) => new() {
		["environment-name"] = environmentName,
		["entity-schema-name"] = "Contact",
		["grantee"] = "720b771c-e7a7-4f31-9cfb-52cd21c3739f",
		["operations"] = "read"
	};
}
