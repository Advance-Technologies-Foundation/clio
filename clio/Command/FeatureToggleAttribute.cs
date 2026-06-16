using System;

namespace Clio.Command;

/// <summary>
/// Marks a command (or any type) as being gated behind a named feature flag.
/// </summary>
/// <remarks>
/// <para>
/// The supplied <see cref="FeatureName"/> is a feature <b>key</b>, not a verb. It is deliberately
/// decoupled from the CLI verb so that a single key can gate multiple commands at once and so the
/// gate survives command renames (the verb may change while the feature key stays stable).
/// </para>
/// <para>
/// A type that is <b>not</b> annotated with this attribute is always enabled. A type that <b>is</b>
/// annotated is enabled only when the corresponding feature flag is turned on in clio settings.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FeatureToggleAttribute : Attribute
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FeatureToggleAttribute"/> class.
	/// </summary>
	/// <param name="featureName">
	/// The feature key that gates the annotated type. Must be a non-empty, non-whitespace value.
	/// </param>
	/// <exception cref="ArgumentException">
	/// Thrown when <paramref name="featureName"/> is <c>null</c>, empty, or whitespace.
	/// </exception>
	public FeatureToggleAttribute(string featureName) {
		if (string.IsNullOrWhiteSpace(featureName)) {
			throw new ArgumentException("Feature name must be a non-empty value.", nameof(featureName));
		}
		FeatureName = featureName;
	}

	/// <summary>
	/// Gets the feature key that gates the annotated type.
	/// </summary>
	public string FeatureName { get; }
}
