using Clio.Command;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Clio.Mcp.E2E.Support.Configuration;

/// <summary>
/// Writes the feature flags the e2e suite's own fixtures depend on into the suite-owned
/// <c>appsettings.json</c> object.
/// </summary>
/// <remarks>
/// A tool carrying <see cref="FeatureToggleAttribute"/> is not registered on the MCP surface while its
/// flag is off, and flags live in the clio home. Leaving them to the runner's machine made
/// <c>MobilePageConversionGuideSandboxE2ETests</c> run or skip according to which TeamCity agent picked up
/// the build (issue #1382). This type is deliberately separate from the fixture that calls it, and public,
/// so the merge rule below is covered by a fast unit test in <c>clio.tests</c> rather than only through a
/// full e2e bootstrap that needs a stand.
/// </remarks>
public static class SuiteFeatureFlags {

	private const string FeaturesKey = "features";

	/// <summary>
	/// Forces the flag gating <paramref name="featureGatedType"/> on in <paramref name="root"/>, carrying
	/// every other flag already present over unchanged.
	/// </summary>
	/// <param name="root">The settings object being written into the suite-owned clio home.</param>
	/// <param name="featureGatedType">A type annotated with <see cref="FeatureToggleAttribute"/>.</param>
	/// <remarks>
	/// Any differently-cased <c>features</c> key is folded into the canonical one rather than left beside
	/// it: Newtonsoft matches the property case-insensitively as a fallback, so a hand-edited
	/// <c>"Features"</c> sitting next to <c>"features"</c> could shadow the flag this writes.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown when <paramref name="featureGatedType"/> carries no <see cref="FeatureToggleAttribute"/>.
	/// </exception>
	public static void Enable(JsonObject root, Type featureGatedType) {
		ArgumentNullException.ThrowIfNull(root);
		ArgumentNullException.ThrowIfNull(featureGatedType);
		JsonObject features = [];
		foreach (string key in root.Select(property => property.Key)
			.Where(key => string.Equals(key, FeaturesKey, StringComparison.OrdinalIgnoreCase))
			.ToList()) {
			if (root[key] is JsonObject existing) {
				foreach (KeyValuePair<string, JsonNode?> flag in existing) {
					features[flag.Key] = flag.Value?.DeepClone();
				}
			}
			root.Remove(key);
		}
		features[FeatureKeyOf(featureGatedType)] = true;
		root[FeaturesKey] = features;
	}

	/// <summary>
	/// Reads the feature key that gates <paramref name="featureGatedType"/> from the type's own
	/// <see cref="FeatureToggleAttribute"/>, so the suite enables the flag the product actually reads.
	/// </summary>
	/// <param name="featureGatedType">A type annotated with <see cref="FeatureToggleAttribute"/>.</param>
	/// <returns>The feature key the annotated type is gated behind.</returns>
	/// <remarks>
	/// A literal repeated in the suite would drift silently: renaming the key in clio would leave the suite
	/// writing a flag nothing reads, and the gated tool would go back to being absent for reasons no message
	/// names.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// Thrown when <paramref name="featureGatedType"/> carries no <see cref="FeatureToggleAttribute"/>.
	/// </exception>
	public static string FeatureKeyOf(Type featureGatedType) {
		ArgumentNullException.ThrowIfNull(featureGatedType);
		return featureGatedType.GetCustomAttribute<FeatureToggleAttribute>()?.FeatureName
			?? throw new InvalidOperationException(
				$"{featureGatedType.Name} carries no [FeatureToggle], so the suite cannot enable it by attribute.");
	}
}
