using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// Shared skip-gate for MCP E2E fixtures whose tools are behind a <c>[FeatureToggle]</c>. The clio MCP
/// server does not advertise a gated feature's tools while its flag is off (the default until the feature
/// ships), so a fixture calls <see cref="SkipIfFeatureDisabled"/> to <see cref="Assert.Ignore(string)"/>
/// rather than fail when the feature is disabled. Enable the feature
/// (<c>clio experimental --name &lt;key&gt; --enable</c>) on the stand's clio settings to run those fixtures.
/// Fixtures call <see cref="SkipIfFeatureDisabled"/> with their feature key (for example <c>theming</c> or
/// <c>process-designer</c>), so the feature-state resolution lives in one place.
/// </summary>
internal static class FeatureE2EGate {

	/// <summary>
	/// Skips the calling test when <paramref name="featureKey"/> is disabled in the appsettings the
	/// configured clio MCP server process loads. Call FIRST in a fixture's arrange (after
	/// <c>ClioProcessPath</c> is set).
	/// </summary>
	/// <param name="settings">The E2E settings whose clio process + environment locate the appsettings.</param>
	/// <param name="featureKey">The feature-toggle key to check (for example <c>theming</c>).</param>
	public static void SkipIfFeatureDisabled(McpE2ESettings settings, string featureKey) {
		if (!IsFeatureEnabled(settings, featureKey)) {
			Assert.Ignore(
				$"The '{featureKey}' feature is disabled, so its MCP tools are not advertised. Enable it "
				+ $"(clio experimental --name {featureKey} --enable) to run these MCP E2E tests.");
		}
	}

	// Reads the `features` map from the clio appsettings the MCP server process will load (the same file
	// McpFeatureToggleFilter reads to decide which tools to register) and reports whether the feature is on.
	private static bool IsFeatureEnabled(McpE2ESettings settings, string featureKey) {
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
			JProperty flag = features.Properties()
				.FirstOrDefault(property => string.Equals(property.Name, featureKey, StringComparison.OrdinalIgnoreCase));
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
