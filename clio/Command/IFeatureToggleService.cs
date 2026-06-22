using System;
using System.Collections.Generic;

namespace Clio.Command;

/// <summary>
/// Describes a single feature flag and its current enabled state, for catalog/list scenarios.
/// </summary>
/// <param name="FeatureName">The feature key.</param>
/// <param name="Enabled">Whether the feature is currently enabled.</param>
public record FeatureToggleInfo(string FeatureName, bool Enabled);

/// <summary>
/// Answers whether a type is gated behind a feature flag and whether that flag is enabled.
/// </summary>
public interface IFeatureToggleService
{
	/// <summary>
	/// Determines whether the supplied type is currently enabled.
	/// </summary>
	/// <param name="type">The type to inspect for a <see cref="FeatureToggleAttribute"/>.</param>
	/// <returns>
	/// <c>true</c> when the type carries no <see cref="FeatureToggleAttribute"/> (always available),
	/// or when it does and the associated feature flag is enabled; otherwise <c>false</c>.
	/// </returns>
	bool IsEnabled(Type type);

	/// <summary>
	/// Determines whether the named feature flag is enabled.
	/// </summary>
	/// <param name="featureName">The feature key.</param>
	/// <returns><c>true</c> when the flag is enabled; otherwise <c>false</c>.</returns>
	bool IsFeatureEnabled(string featureName);

	/// <summary>
	/// Builds a catalog of all feature-gated types, deduplicated by feature name.
	/// </summary>
	/// <param name="types">The candidate types to inspect.</param>
	/// <returns>
	/// One <see cref="FeatureToggleInfo"/> per distinct feature key found among the supplied types
	/// that carry a <see cref="FeatureToggleAttribute"/>. Types without the attribute are ignored.
	/// </returns>
	IReadOnlyList<FeatureToggleInfo> GetCatalog(IEnumerable<Type> types);
}
