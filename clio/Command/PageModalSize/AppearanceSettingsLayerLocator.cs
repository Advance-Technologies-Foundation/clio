using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.AddonSchemaDesigner;
using Clio.Common;

namespace Clio.Command.PageModalSize;

/// <summary>The package whose <c>AppearanceSettings</c> layer sets a modal page's size.</summary>
/// <param name="PackageName">The package, or <c>null</c> when it could not be told apart.</param>
/// <param name="CandidatePackageNames">The top layers' packages left when no single one could be told apart; empty
/// otherwise.</param>
/// <param name="Warning">Why the package could not be determined, or <c>null</c>.</param>
public sealed record AppearanceSettingsLayerSource(
	string PackageName,
	IReadOnlyList<string> CandidatePackageNames,
	string Warning);

/// <summary>
/// Finds which package's <c>AppearanceSettings</c> add-on layer a modal page's size comes from, from the persisted
/// layers (<c>SysSchema</c>, <c>AddonSchemaManager</c>) and the package dependencies.
/// </summary>
public interface IAppearanceSettingsLayerLocator {
	/// <summary>
	/// Returns the package of the topmost layer that sets <paramref name="size"/>: among the layers the scope holds,
	/// the one no other layer's package depends on; when several are parallel, the one whose own view holds the size.
	/// The layers and the package dependencies are read once per instance, until <see cref="Forget"/>. Never throws: a failed lookup is reported
	/// in <see cref="AppearanceSettingsLayerSource.Warning"/>.
	/// </summary>
	/// <param name="pageSchemaName">The modal page schema name.</param>
	/// <param name="pageSchemaUId">The page schema UId, the add-on's parent.</param>
	/// <param name="visibleFromPackageName">Only layers this package sees (itself and its dependencies); <c>null</c>
	/// for every layer of the environment, which is what the window opens with.</param>
	/// <param name="size">The raw <c>modalSettings.size</c> the scope shows; <c>null</c> or 0 for no size.</param>
	AppearanceSettingsLayerSource FindSource(string pageSchemaName, Guid pageSchemaUId, string visibleFromPackageName,
		int? size);

	/// <summary>
	/// Returns whether <paramref name="packageName"/> sees <paramref name="layerPackageName"/>'s layer: it is that
	/// package or depends on it, directly or not. <c>false</c> when the dependencies could not be read.
	/// </summary>
	/// <param name="pageSchemaName">The modal page schema name.</param>
	/// <param name="packageName">The package that looks.</param>
	/// <param name="layerPackageName">The package holding the layer.</param>
	bool Sees(string pageSchemaName, string packageName, string layerPackageName);

	/// <summary>
	/// Drops the layers and package dependencies read so far, so the next call reads them again. Call it after a
	/// write that may have created a layer.
	/// </summary>
	void Forget();
}

/// <inheritdoc cref="IAppearanceSettingsLayerLocator" />
internal sealed class AppearanceSettingsLayerLocator(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IAddonSchemaDesignerClient addonSchemaDesignerClient)
	: IAppearanceSettingsLayerLocator {

	private sealed record Layer(string PackageName, string PackageUId);

	private sealed record Environment(IReadOnlyList<Layer> Layers, ILookup<string, string> DependsOn);

	private readonly Dictionary<string, Environment> _environments = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HashSet<string>> _reach = new(StringComparer.OrdinalIgnoreCase);

	public AppearanceSettingsLayerSource FindSource(string pageSchemaName, Guid pageSchemaUId,
		string visibleFromPackageName, int? size) {
		string unknown = $"The package the size of '{pageSchemaName}' comes from could not be determined";
		try {
			(Environment environment, string error) = ReadEnvironment(pageSchemaName);
			if (error is not null) {
				return Unknown($"{unknown}: {error}");
			}
			List<Layer> tops = SelectTopLayers(environment, visibleFromPackageName);
			return tops.Count switch {
				0 => Unknown($"{unknown}: no AppearanceSettings layer was found."),
				1 => new AppearanceSettingsLayerSource(tops[0].PackageName, [], null),
				_ => PickAmongParallel(tops, pageSchemaUId, size, unknown)
			};
		} catch (Exception exception) {
			// The source only decorates a read or a refusal, so no failure of this lookup may fail the caller.
			return Unknown($"{unknown}: {exception.Message}");
		}
	}

	public bool Sees(string pageSchemaName, string packageName, string layerPackageName) {
		try {
			(Environment environment, string error) = ReadEnvironment(pageSchemaName);
			return error is null && Reach(packageName, environment.DependsOn).Contains(layerPackageName);
		} catch (Exception) {
			return false;
		}
	}

	public void Forget() {
		_environments.Clear();
		_reach.Clear();
	}

	private static AppearanceSettingsLayerSource Unknown(string warning) => new(null, [], warning);

	private (Environment environment, string error) ReadEnvironment(string pageSchemaName) {
		if (_environments.TryGetValue(pageSchemaName, out Environment cached)) {
			return (cached, null);
		}
		var (layers, layersError) = PageSchemaMetadataHelper.QueryAddonSchemaLayers(applicationClient,
			serviceUrlBuilder, pageSchemaName + ModalSizeMetadata.AddonName);
		if (layersError is not null) {
			return (null, layersError);
		}
		var (dependencies, dependenciesError) =
			PageSchemaMetadataHelper.QueryPackageDependencies(applicationClient, serviceUrlBuilder);
		if (dependenciesError is not null) {
			return (null, dependenciesError);
		}
		Environment environment = new(
			layers.Select(layer => new Layer(layer.packageName, layer.packageUId)).ToList(),
			dependencies.ToLookup(dependency => dependency.packageName, dependency => dependency.dependsOnPackageName,
				StringComparer.OrdinalIgnoreCase));
		_environments[pageSchemaName] = environment;
		return (environment, null);
	}

	// The layers in scope that no other layer's package depends on, directly or not.
	private List<Layer> SelectTopLayers(Environment environment, string visibleFromPackageName) {
		HashSet<string> visible = visibleFromPackageName is null
			? null
			: Reach(visibleFromPackageName, environment.DependsOn);
		List<Layer> scoped = environment.Layers
			.Where(layer => visible is null || visible.Contains(layer.PackageName))
			.ToList();
		return scoped
			.Where(layer => !scoped.Any(other => IsAbove(other, layer, environment.DependsOn)))
			.ToList();
	}

	private bool IsAbove(Layer other, Layer layer, ILookup<string, string> dependsOn) =>
		!string.Equals(other.PackageName, layer.PackageName, StringComparison.OrdinalIgnoreCase)
		&& Reach(other.PackageName, dependsOn).Contains(layer.PackageName);

	// Parallel top layers: the one whose own view holds the size is the one that sets it.
	private AppearanceSettingsLayerSource PickAmongParallel(List<Layer> tops, Guid pageSchemaUId, int? size,
		string unknown) {
		List<string> matching = tops
			.Where(layer => ModalSizeMetadata.ToStoredSize(ReadOwnSize(layer.PackageUId, pageSchemaUId))
				== ModalSizeMetadata.ToStoredSize(size))
			.Select(layer => layer.PackageName)
			.ToList();
		if (matching.Count == 1) {
			return new AppearanceSettingsLayerSource(matching[0], [], null);
		}
		List<string> candidates = (matching.Count > 0 ? matching : tops.Select(layer => layer.PackageName))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		string names = string.Join(", ", candidates.Select(name => $"'{name}'"));
		return new AppearanceSettingsLayerSource(null, candidates,
			$"{unknown}: parallel packages {names} have a layer each.");
	}

	// The package and every package it depends on, directly or not.
	private HashSet<string> Reach(string packageName, ILookup<string, string> dependsOn) {
		if (_reach.TryGetValue(packageName, out HashSet<string> cached)) {
			return cached;
		}
		HashSet<string> reached = new(StringComparer.OrdinalIgnoreCase) { packageName };
		Queue<string> pending = new([packageName]);
		while (pending.Count > 0) {
			foreach (string dependency in dependsOn[pending.Dequeue()].Where(name => !reached.Contains(name))) {
				reached.Add(dependency);
				pending.Enqueue(dependency);
			}
		}
		_reach[packageName] = reached;
		return reached;
	}

	// The size as the layer's own package sees it: useFullHierarchy=false reads the package's hierarchy only.
	private int? ReadOwnSize(string packageUId, Guid pageSchemaUId) {
		if (!Guid.TryParse(packageUId, out Guid packageId)) {
			return null;
		}
		AddonSchemaDto schema = addonSchemaDesignerClient.GetSchema(
			ModalSizeMetadata.BuildGetRequest(pageSchemaUId, packageId));
		return schema is not null && ModalSizeMetadata.TryParse(schema.MetaData, out JsonObject metadata)
			? ModalSizeMetadata.ReadRawSize(metadata)
			: null;
	}
}
