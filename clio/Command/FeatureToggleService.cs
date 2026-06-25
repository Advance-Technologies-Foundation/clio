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
	public bool IsEnabled(Type type) {
		if (type is null) {
			return false;
		}
		FeatureToggleAttribute attribute = type.GetCustomAttribute<FeatureToggleAttribute>(inherit: false);
		if (attribute is null) {
			return true;
		}
		return _settingsRepository.IsFeatureEnabled(attribute.FeatureName);
	}

	/// <inheritdoc/>
	public bool IsFeatureEnabled(string featureName) {
		return _settingsRepository.IsFeatureEnabled(featureName);
	}

	/// <inheritdoc/>
	public IReadOnlyList<FeatureToggleInfo> GetCatalog(IEnumerable<Type> types) {
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
			catalog.Add(new FeatureToggleInfo(
				attribute.FeatureName,
				_settingsRepository.IsFeatureEnabled(attribute.FeatureName)));
		}
		return catalog;
	}
}
