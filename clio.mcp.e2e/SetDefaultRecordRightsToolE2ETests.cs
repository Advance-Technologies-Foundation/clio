using System.Collections.Generic;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;

namespace Clio.Mcp.E2E;

/// <summary>End-to-end contract tests for the destructive set-default-record-rights MCP tool (see the shared base).</summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(SetDefaultRecordRightsTool.ToolName)]
[NonParallelizable]
public sealed class SetDefaultRecordRightsToolE2ETests : ObjectRightsToolE2ETestsBase {

	protected override string ToolName => SetDefaultRecordRightsTool.ToolName;

	protected override bool ExpectedDestructive => true;

	protected override string UnknownArgumentName => "revok";

	protected override Dictionary<string, object?> InvalidEnvironmentArgs(string environmentName) => new() {
		["environment-name"] = environmentName,
		["entity-schema-name"] = "Contact",
		["author"] = "a29a3ba5-4b0d-de11-9a51-005056c00008",
		["grantee"] = "720b771c-e7a7-4f31-9cfb-52cd21c3739f",
		["operations"] = "read",
		["level"] = "delegated",
		["do-not-apply-for-manager"] = true,
		["enable-record-permissions"] = true,
		["preview"] = true
	};
}
