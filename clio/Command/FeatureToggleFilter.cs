using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command;

/// <summary>
/// Pure helper that narrows a set of command option types down to the subset whose feature
/// flags are currently enabled.
/// </summary>
/// <remarks>
/// This is the single shared seam used by both the CLI parser (so disabled-feature verbs are
/// unparseable) and the help renderer (so disabled-feature verbs are absent from help output).
/// Both consume the same <see cref="IFeatureToggleService.IsEnabled(Type)"/> predicate so the two
/// surfaces cannot drift: a type without a <see cref="FeatureToggleAttribute"/> is always kept,
/// while a gated type is kept only when its flag is on.
/// </remarks>
public static class FeatureToggleFilter
{
	/// <summary>
	/// Returns the subset of <paramref name="types"/> that are currently enabled according to
	/// <paramref name="featureToggleService"/>.
	/// </summary>
	/// <param name="types">The candidate command option types.</param>
	/// <param name="featureToggleService">The service that decides whether a type is enabled.</param>
	/// <returns>
	/// The enabled subset, preserving the input order. Types lacking a
	/// <see cref="FeatureToggleAttribute"/> are always included; gated types are included only when
	/// their feature flag is on.
	/// </returns>
	/// <exception cref="ArgumentNullException">
	/// Thrown when <paramref name="types"/> or <paramref name="featureToggleService"/> is <c>null</c>.
	/// </exception>
	public static Type[] GetEnabled(IEnumerable<Type> types, IFeatureToggleService featureToggleService) {
		ArgumentNullException.ThrowIfNull(types);
		ArgumentNullException.ThrowIfNull(featureToggleService);
		return types.Where(featureToggleService.IsEnabled).ToArray();
	}
}
