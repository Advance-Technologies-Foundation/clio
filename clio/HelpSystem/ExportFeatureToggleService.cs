using System;
using System.Collections.Generic;
using Clio.Command;

namespace Clio.Help;

/// <summary>
/// Deterministic <see cref="IFeatureToggleService"/> used only when generating the committed public
/// documentation artifacts (<c>Commands.md</c>, <c>help/en/*</c>, <c>Wiki/WikiAnchors.txt</c>,
/// per-command <c>docs/commands/*.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// Doc generation must be reproducible: the artifacts checked into the repository must not depend on
/// whichever feature flags happen to be enabled in the local <c>appsettings.json</c> of the person
/// (or CI agent) who runs <c>clio</c> to regenerate them. Using the live settings-backed service
/// here would make committed docs machine-dependent.
/// </para>
/// <para>
/// This service therefore applies a fixed baseline: a type without a
/// <see cref="FeatureToggleAttribute"/> is always advertised, while any gated type is treated as
/// <b>off</b>. The net effect is that experimental, feature-gated commands are never advertised in
/// the committed public docs, exactly mirroring a fresh install with no flags enabled.
/// </para>
/// </remarks>
internal sealed class ExportFeatureToggleService : IFeatureToggleService {
	/// <inheritdoc/>
	public bool IsEnabled(Type type) =>
		type is not null && !Attribute.IsDefined(type, typeof(FeatureToggleAttribute), inherit: false);

	/// <inheritdoc/>
	public bool IsFeatureEnabled(string featureName) => false;

	/// <inheritdoc/>
	public IReadOnlyList<FeatureToggleInfo> GetCatalog(IEnumerable<Type> types) => [];
}
