using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Package;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Automatically resolves missing package dependencies when the entity schema designer cannot access
/// a schema in the target package (the <c>SchemaIsNotAvailableException</c> that surfaces as an HTML
/// error page from <c>GetSchemaDesignItem</c>).
/// </summary>
public interface IEntitySchemaDependencyResolver
{

	/// <summary>
	/// Detects and adds missing package dependencies so the target package can access the requested schema.
	/// Mirrors the auto-dependency behavior of the Creatio <c>PackageElementDependencyApplier</c> that runs
	/// inside <c>SaveSchema</c> but is absent from the <c>GetSchemaDesignItem</c> code path.
	/// </summary>
	/// <param name="schemaName">Entity schema name that was unavailable (for example <c>Opportunity</c>).</param>
	/// <param name="targetPackageName">Package that is being edited (for example <c>Custom</c>).</param>
	/// <returns><c>true</c> when at least one dependency was added and the caller should retry.</returns>
	bool TryAutoResolve(string schemaName, string targetPackageName);

}

/// <inheritdoc cref="IEntitySchemaDependencyResolver"/>
internal sealed class EntitySchemaDependencyResolver : IEntitySchemaDependencyResolver
{

	private readonly FindEntitySchemaCommand _findCommand;
	private readonly IPackageDependencyManager _dependencyManager;
	private readonly ILogger _logger;

	public EntitySchemaDependencyResolver(FindEntitySchemaCommand findCommand,
		IPackageDependencyManager dependencyManager, ILogger logger) {
		_findCommand = findCommand;
		_dependencyManager = dependencyManager;
		_logger = logger;
	}

	/// <inheritdoc/>
	public bool TryAutoResolve(string schemaName, string targetPackageName) {
		try {
			IReadOnlyList<EntitySchemaSearchResult> results = _findCommand.FindSchemas(
				new FindEntitySchemaOptions { SchemaName = schemaName });
			List<string> candidatePackages = results
				.Where(result => !string.IsNullOrWhiteSpace(result.PackageName))
				.Where(result => !string.Equals(result.PackageName, targetPackageName,
					StringComparison.OrdinalIgnoreCase))
				.Select(result => result.PackageName)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			if (candidatePackages.Count == 0) {
				return false;
			}
			if (candidatePackages.Count > 1) {
				_logger.WriteWarning(
					$"Schema '{schemaName}' exists in multiple packages ({string.Join(", ", candidatePackages)}). " +
					$"Auto-resolution refused — add the correct dependency to '{targetPackageName}' manually.");
				return false;
			}
			_logger.WriteInfo(
				$"Schema '{schemaName}' is not available in package '{targetPackageName}'. " +
				$"Auto-adding dependency: {candidatePackages[0]}");
			_dependencyManager.AddDependencies(targetPackageName,
				candidatePackages.Select(name => new PackageDependencySpec(name)).ToList());
			return true;
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Broad catch is intentional: FindSchemas and AddDependencies can fail with
			// HttpRequestException, JsonException, InvalidOperationException, or ArgumentException
			// depending on the remote state. None of these should abort the caller — the enriched
			// error message in LoadSchema takes over when TryAutoResolve returns false.
			_logger.WriteWarning($"Auto-dependency resolution failed for schema '{schemaName}': {ex.Message}");
			return false;
		}
	}

}
