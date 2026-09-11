using System;
using System.Collections.Generic;
using System.Reflection;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <inheritdoc cref="IFeatureToggleService"/>
public class FeatureToggleService : IFeatureToggleService
{
	private readonly ISettingsRepository _settingsRepository;

	/// <summary>
	/// Initializes a new instance of the <see cref="FeatureToggleService"/> class.
	/// </summary>
	/// <param name="settingsRepository">The settings repository backing the feature flags.</param>
	public FeatureToggleService(ISettingsRepository settingsRepository) {
		_settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
	}

	/// <inheritdoc/>
	public bool IsEnabled(Type type) => FeatureToggleReflection.IsEnabled(type, IsFeatureEnabled);

	/// <inheritdoc/>
	public bool IsFeatureEnabled(string featureName) {
		return _settingsRepository.IsFeatureEnabled(featureName);
	}

	/// <inheritdoc/>
	public IReadOnlyList<FeatureToggleInfo> GetCatalog(IEnumerable<Type> types) =>
		FeatureToggleReflection.BuildCatalog(types, IsFeatureEnabled);
}

/// <summary>
/// The attribute-reading half of <see cref="IFeatureToggleService"/>, shared verbatim by every
/// implementation so that a live-settings service and a frozen one can never disagree about what a type's
/// <see cref="FeatureToggleAttribute"/> means — only about whether the named flag is on.
/// </summary>
internal static class FeatureToggleReflection {

	/// <summary>
	/// Determines whether a type is currently enabled: types without a <see cref="FeatureToggleAttribute"/>
	/// are always enabled, a gated type follows its flag.
	/// </summary>
	/// <param name="type">The candidate type; <see langword="null"/> is never enabled.</param>
	/// <param name="isFeatureEnabled">Resolves a feature key to its current state.</param>
	/// <returns>Whether the type is enabled.</returns>
	internal static bool IsEnabled(Type type, Func<string, bool> isFeatureEnabled) {
		if (type is null) {
			return false;
		}
		FeatureToggleAttribute attribute = type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false);
		if (attribute is null) {
			return true;
		}
		return isFeatureEnabled(attribute.FeatureName);
	}

	/// <summary>
	/// Builds one <see cref="FeatureToggleInfo"/> per distinct feature key found among the supplied types.
	/// </summary>
	/// <param name="types">The candidate types; entries without the attribute are ignored.</param>
	/// <param name="isFeatureEnabled">Resolves a feature key to its current state.</param>
	/// <returns>The deduplicated catalog.</returns>
	internal static IReadOnlyList<FeatureToggleInfo> BuildCatalog(
		IEnumerable<Type> types,
		Func<string, bool> isFeatureEnabled) {
		List<FeatureToggleInfo> catalog = [];
		if (types is null) {
			return catalog;
		}
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		foreach (Type type in types) {
			if (type is null) {
				continue;
			}
			FeatureToggleAttribute attribute = type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false);
			if (attribute is null) {
				continue;
			}
			if (!seen.Add(attribute.FeatureName)) {
				continue;
			}
			catalog.Add(new FeatureToggleInfo(attribute.FeatureName, isFeatureEnabled(attribute.FeatureName)));
		}
		return catalog;
	}
}
