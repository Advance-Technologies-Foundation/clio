using System;
using System.Text.Json;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.AddonSchemaDesigner;

/// <summary>
/// The object (entity schema) an add-on attaches to, resolved in the context of the package whose add-on layer
/// is read or written.
/// </summary>
/// <param name="PackageUId">The package UId as SysPackage returned it.</param>
/// <param name="PackageId">The package UId as a GUID.</param>
/// <param name="EntitySchema">The object as the entity schema designer returned it for that package.</param>
internal sealed record EntityAddonTarget(string PackageUId, Guid PackageId, EntityDesignSchemaDto EntitySchema);

/// <summary>
/// Resolves the object (entity schema) an add-on attaches to through <c>EntitySchemaManager</c>
/// (<c>RelatedPage</c>, <c>MobileRelatedPage</c>, <c>EntityConnections</c>), so the package lookup and the object
/// lookup do not drift between those add-ons.
/// </summary>
internal interface IEntityAddonTargetResolver {

	/// <summary>
	/// Resolves the package by name and the object through the entity schema designer — the same source the
	/// Business Rule add-on path uses — so the add-on request carries the object's real <c>UId</c> and, for a
	/// replacing/derived schema, its parent schema <c>UId</c>.
	/// </summary>
	/// <param name="packageName">The package whose add-on layer is read or written.</param>
	/// <param name="entitySchemaName">The object (entity schema) name.</param>
	/// <returns>The resolved package and object.</returns>
	/// <exception cref="InvalidOperationException">The package or the object cannot be resolved.</exception>
	EntityAddonTarget Resolve(string packageName, string entitySchemaName);
}

/// <inheritdoc cref="IEntityAddonTargetResolver" />
/// <remarks>
/// Also hosts the pure helpers shared by the same add-ons (<see cref="BuildGetRequest"/>,
/// <see cref="TryReadTargetSchemaUId"/>), so the add-on request shape does not drift between them.
/// </remarks>
internal sealed class EntityAddonTargetResolver(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder,
	IRemoteEntitySchemaDesignerClient entitySchemaDesignerClient)
	: IEntityAddonTargetResolver {

	private const string EntitySchemaManagerName = "EntitySchemaManager";

	/// <inheritdoc />
	public EntityAddonTarget Resolve(string packageName, string entitySchemaName) {
		(string packageUId, string packageError) = PageSchemaMetadataHelper.QueryPackageUId(
			applicationClient, serviceUrlBuilder, packageName);
		if (packageError != null) {
			throw new InvalidOperationException(packageError);
		}
		if (!Guid.TryParse(packageUId, out Guid packageId)) {
			throw new InvalidOperationException(
				$"Resolved package '{packageName}' UId '{packageUId}' is not a valid GUID.");
		}
		// Use the throwing GetSchemaDesignItem (like the business-rule path), NOT TryGetSchemaDesignItem: the
		// Try variant returns null on an HTML / auth-redirect / server-error page, which would be misreported as
		// "object not found" below. GetSchemaDesignItem surfaces those transport/server faults with a clear error.
		// Note: it returns Clio.Command.EntitySchemaDesigner.DesignerResponse, which collides by simple name with
		// Clio.Command.DesignerResponse — let the compiler infer the type.
		var response = entitySchemaDesignerClient.GetSchemaDesignItem(
			new GetSchemaDesignItemRequestDto {
				Name = entitySchemaName.Trim(),
				PackageUId = packageId,
				UseFullHierarchy = true,
				Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
			},
			new RemoteCommandOptions());
		EntityDesignSchemaDto entitySchema = response?.Schema
			?? throw new InvalidOperationException(
				$"Object (entity schema) '{entitySchemaName}' not found in package '{packageName}'. The object must "
				+ "be visible from that package — if it lives elsewhere, add a package dependency.");
		return new EntityAddonTarget(packageUId, packageId, entitySchema);
	}

	/// <summary>
	/// Builds the add-on <c>GetSchema</c> request for the resolved object. Mirrors the Interface Designer / Business
	/// Rule add-on path: a replacing or derived object reports the parent it extends; a plain object reports none
	/// (<c>Guid.Empty</c>).
	/// </summary>
	internal static AddonGetRequestDto BuildGetRequest(
		EntityDesignSchemaDto entitySchema, Guid packageId, string addonName) =>
		new() {
			AddonName = addonName,
			TargetSchemaUId = entitySchema.UId,
			TargetParentSchemaUId = entitySchema.ParentSchema?.UId ?? Guid.Empty,
			TargetPackageUId = packageId,
			TargetSchemaManagerName = EntitySchemaManagerName,
			UseFullHierarchy = true
		};

	/// <summary>
	/// Reads the persisted object identity the add-on service resolved (<c>targetSchemaUId</c>). The designer
	/// <c>UId</c> may identify an unsaved replacement, so it is never used as the object identity.
	/// </summary>
	/// <returns>The target schema UId, or <c>null</c> when the response carries no usable one.</returns>
	internal static Guid? TryReadTargetSchemaUId(AddonSchemaDto schema) {
		if (schema?.AdditionalData == null) {
			return null;
		}
		foreach (var field in schema.AdditionalData) {
			if (string.Equals(field.Key, "targetSchemaUId", StringComparison.OrdinalIgnoreCase)
				&& field.Value.ValueKind == JsonValueKind.String
				&& Guid.TryParse(field.Value.GetString(), out Guid targetUId)
				&& targetUId != Guid.Empty) {
				return targetUId;
			}
		}
		return null;
	}
}
