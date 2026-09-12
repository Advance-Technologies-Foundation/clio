using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command;

/// <summary>
/// The <see cref="IFeatureToggleService"/> an MCP worker child runs on: an immutable map handed down by the
/// parent at spawn, with no settings repository behind it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What the immutability buys.</b> A worker resolves the same feature values the parent resolved, for the
/// worker's whole life. Reading <c>appsettings.json</c> instead would let a mid-session
/// <c>clio experimental --name … --enable</c> put a tool in the worker that the parent does not advertise, or
/// remove one the parent does — and because MCP primitive registration is fixed before the child can receive a
/// message, that disagreement surfaces as an unroutable call rather than as an error.
/// </para>
/// <para>
/// The map covers the WHOLE feature map, not the subset that gates MCP primitives: a worker also dispatches
/// CLI verbs through <c>clio-run</c>, and the same service backs the command-line parser's feature gate.
/// </para>
/// <para>
/// An unknown key reads as disabled, matching <c>ISettingsRepository.IsFeatureEnabled</c> for an absent flag.
/// </para>
/// </remarks>
public sealed class FrozenFeatureToggleService : IFeatureToggleService
{
	private readonly IReadOnlyDictionary<string, bool> _features;

	/// <summary>
	/// Initializes a new instance of the <see cref="FrozenFeatureToggleService"/> class.
	/// </summary>
	/// <param name="features">
	/// The frozen feature map. Copied into a case-insensitive dictionary because the settings repository
	/// compares feature keys case-insensitively — an ordinal lookup here would read a case-differing name as
	/// absent while the parent read it as enabled.
	/// </param>
	public FrozenFeatureToggleService(IReadOnlyDictionary<string, bool> features) {
		ArgumentNullException.ThrowIfNull(features);
		Dictionary<string, bool> copy = new(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, bool> feature in features.Where(f => !string.IsNullOrWhiteSpace(f.Key))) {
			copy[feature.Key] = feature.Value;
		}
		_features = copy;
	}

	/// <inheritdoc/>
	public bool IsEnabled(Type type) => FeatureToggleReflection.IsEnabled(type, IsFeatureEnabled);

	/// <inheritdoc/>
	public bool IsFeatureEnabled(string featureName) =>
		!string.IsNullOrWhiteSpace(featureName)
		&& _features.TryGetValue(featureName, out bool enabled)
		&& enabled;

	/// <inheritdoc/>
	public IReadOnlyList<FeatureToggleInfo> GetCatalog(IEnumerable<Type> types) =>
		FeatureToggleReflection.BuildCatalog(types, IsFeatureEnabled);
}
