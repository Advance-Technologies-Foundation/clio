using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// Skip-gate for the watch-compilation MCP E2E fixtures. The tool is
/// <c>[FeatureToggle("watch-compilation")]</c>-gated, so the clio MCP server does NOT advertise it
/// while the feature is off — which is the default. These fixtures <see cref="Assert.Ignore(string)"/>
/// (skip) rather than fail when the feature is disabled, exactly like the process-designer gate.
/// Enable the feature (<c>clio experimental --name watch-compilation --enable</c>) on the stand's
/// clio settings to run them.
/// </summary>
internal static class WatchCompilationE2EGate {
	private const string FeatureKey = "watch-compilation";

	/// <summary>
	/// NUnit category carried by every watch-compilation E2E fixture, so CI lanes that must not even
	/// discover these tests can exclude them at the runner level:
	/// <c>dotnet test --filter "TestCategory!=McpE2E.WatchCompilation"</c>.
	/// </summary>
	public const string CategoryName = "McpE2E.WatchCompilation";

	/// <summary>
	/// Skips the calling test when <c>watch-compilation</c> is disabled in the appsettings the
	/// configured clio MCP server process loads. Call FIRST in a fixture's arrange (after
	/// <c>ClioProcessPath</c> is set).
	/// </summary>
	public static void SkipIfFeatureDisabled(McpE2ESettings settings) {
		if (!IsFeatureEnabled(settings)) {
			Assert.Ignore(
				$"The '{FeatureKey}' feature is disabled, so its MCP tool is not advertised. Enable it "
				+ $"(clio experimental --name {FeatureKey} --enable) to run the watch-compilation MCP E2E tests.");
		}
	}

	// Reads the `features` map from the clio appsettings the MCP server process will load (the same file
	// McpFeatureToggleFilter reads to decide which tools to register) and reports whether the feature is on.
	private static bool IsFeatureEnabled(McpE2ESettings settings) {
		try {
			string appSettingsPath = TemporaryClioSettingsOverride.GetClioAppSettingsPath(
				settings.ClioProcessPath, settings.ProcessEnvironmentVariables);
			if (!File.Exists(appSettingsPath)) {
				return false;
			}
			if (JObject.Parse(File.ReadAllText(appSettingsPath))["features"] is not JObject features) {
				return false;
			}
			// Feature keys compare case-insensitively (the IFeatureToggleService contract).
			JProperty? flag = features.Properties()
				.FirstOrDefault(property => string.Equals(property.Name, FeatureKey, StringComparison.OrdinalIgnoreCase));
			return flag is not null && flag.Value.Type == JTokenType.Boolean && flag.Value.Value<bool>();
		}
		catch (Exception exception) when (
			exception is InvalidOperationException or IOException or Newtonsoft.Json.JsonException) {
			// If the feature state cannot be resolved (settings path/read/parse failure), treat it as
			// disabled and skip — the gate must never itself fail the test.
			return false;
		}
	}
}
