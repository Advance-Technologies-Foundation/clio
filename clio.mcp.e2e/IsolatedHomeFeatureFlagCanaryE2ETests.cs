using System.Text.Json;
using Allure.NUnit;
using Allure.NUnit.Attributes;
using Clio.Command.McpServer.Tools;
using Clio.Mcp.E2E.Support.Configuration;
using Clio.Mcp.E2E.Support.Mcp;
using Clio.Mcp.E2E.Support.Results;
using FluentAssertions;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E;

/// <summary>
/// Base fixture for isolated-home feature flag canaries.
/// </summary>
public abstract class IsolatedHomeFeatureFlagCanaryBase : McpContractFixtureBase {
	private const string ToolName = ExperimentalTool.ToolName;
	private const string FeatureKey = "mcp-e2e-isolated-home-canary";
	private string? _clioHome;

	/// <summary>
	/// Gets the expected feature flag value for this canary fixture.
	/// </summary>
	protected abstract bool ExpectedFeatureValue { get; }

	/// <inheritdoc />
	private protected override void ConfigureMcpServerSettings(McpE2ESettings settings) {
		_clioHome = CreateIsolatedClioHome(
			"""
			{
			  "ActiveEnvironmentKey": "dev",
			  "Autoupdate": false,
			  "Features": {},
			  "Environments": {
			    "dev": {
			      "Uri": "http://localhost",
			      "Login": "Supervisor",
			      "Password": "Supervisor",
			      "IsNetCore": true
			    }
			  }
			}
			""",
			GetType().Name);
		settings.ProcessEnvironmentVariables["CLIO_HOME"] = _clioHome;
	}

	[Test]
	[AllureTag(ToolName)]
	[AllureName("isolated CLIO_HOME keeps feature flags fixture-local")]
	[AllureDescription("Starts the real clio MCP server with an isolated CLIO_HOME and toggles a shared feature key to prove fixture-local appsettings isolation.")]
	[Description("Verifies that a shared MCP fixture writes feature flags only to its isolated CLIO_HOME.")]
	public async Task Experimental_ShouldPersistFeatureFlagInIsolatedHome_WhenFixtureUsesSharedServer() {
		// Arrange
		await using var context = Arrange();

		// Act
		CallToolResult callResult = await context.Session.CallToolAsync(
			ToolName,
			new Dictionary<string, object?> {
				["name"] = FeatureKey,
				[ExpectedFeatureValue ? "enable" : "disable"] = true
			},
			context.CancellationTokenSource.Token);
		CommandExecutionEnvelope execution = McpCommandExecutionParser.Extract(callResult);

		// Assert
		callResult.IsError.Should().NotBeTrue(
			because: "the experimental tool should toggle a feature flag inside the isolated clio home");
		execution.ExitCode.Should().Be(0,
			because: "the feature flag toggle is valid and should succeed");
		ReadFeatureValueFromIsolatedHome().Should().Be(ExpectedFeatureValue,
			because: "the toggle must persist to this fixture's isolated appsettings.json and not to another fixture's home");
	}

	private bool ReadFeatureValueFromIsolatedHome() {
		string appSettingsPath = Path.Combine(_clioHome!, "appsettings.json");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(appSettingsPath));
		JsonElement features = GetPropertyIgnoreCase(document.RootElement, "Features");
		return GetPropertyIgnoreCase(features, FeatureKey).GetBoolean();
	}

	private static JsonElement GetPropertyIgnoreCase(JsonElement element, string propertyName) {
		foreach (JsonProperty property in element.EnumerateObject()) {
			if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) {
				return property.Value;
			}
		}
		throw new KeyNotFoundException($"Property '{propertyName}' was not found in JSON object.");
	}
}

/// <summary>
/// Isolated-home canary that enables the shared feature key.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExperimentalTool.ToolName)]
[Parallelizable(ParallelScope.Self)]
public sealed class IsolatedHomeFeatureFlagEnabledCanaryE2ETests : IsolatedHomeFeatureFlagCanaryBase {
	/// <inheritdoc />
	protected override bool ExpectedFeatureValue => true;
}

/// <summary>
/// Isolated-home canary that disables the shared feature key.
/// </summary>
[TestFixture]
[Category("McpE2E.NoEnvironment")]
[AllureNUnit]
[AllureFeature(ExperimentalTool.ToolName)]
[Parallelizable(ParallelScope.Self)]
public sealed class IsolatedHomeFeatureFlagDisabledCanaryE2ETests : IsolatedHomeFeatureFlagCanaryBase {
	/// <inheritdoc />
	protected override bool ExpectedFeatureValue => false;
}
