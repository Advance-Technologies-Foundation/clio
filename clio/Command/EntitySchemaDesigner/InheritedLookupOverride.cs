using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Redirects a lookup column that a schema inherits from its parent to another reference schema. The column
/// stays in <c>InheritedColumns</c>; only its <c>ReferenceSchema</c> changes, so no own column is created.
/// </summary>
internal interface IInheritedLookupOverride {
	/// <summary>
	/// Validates the override preconditions and sets the reference schema of the inherited column in place.
	/// </summary>
	/// <param name="schema">Design schema whose <c>InheritedColumns</c> entry is changed. Its <c>Package</c> and
	/// <c>ParentSchema</c> must be populated.</param>
	/// <param name="columnName">Name of the inherited lookup column.</param>
	/// <param name="referenceSchemaName">Name of the schema the column must reference.</param>
	/// <param name="referenceSchema">The requested schema as listed by the non-virtual reference-schema lookup,
	/// or <see langword="null"/> when that lookup does not list it.</param>
	/// <param name="options">Remote environment options.</param>
	/// <returns>The changed inherited column.</returns>
	/// <exception cref="EntitySchemaDesignerException">A precondition does not hold; nothing is changed.</exception>
	EntitySchemaColumnDto Apply(EntityDesignSchemaDto schema, string columnName, string referenceSchemaName,
		ManagerItemDto? referenceSchema, RemoteCommandOptions options);

	/// <summary>
	/// Confirms that the reloaded schema keeps the column inherited and referencing the requested schema.
	/// </summary>
	/// <param name="reloadedSchema">Design schema read back after the save.</param>
	/// <param name="columnName">Name of the inherited lookup column.</param>
	/// <param name="referenceSchemaName">Name of the schema the column must reference.</param>
	/// <exception cref="EntitySchemaDesignerException">The override was not persisted.</exception>
	void VerifyPersisted(EntityDesignSchemaDto reloadedSchema, string columnName, string referenceSchemaName);
}

internal sealed class InheritedLookupOverride(IRemoteEntitySchemaDesignerClient designerClient,
	IRetryDelay retryDelay) : IInheritedLookupOverride {

	/// <summary>Upper bound on the parent hops followed from the requested schema.</summary>
	internal const int MaxHierarchyDepth = 32;

	/// <summary>Number of reads of the requested schema before its hierarchy is reported unreadable.</summary>
	internal const int TargetReadAttemptCount = 5;

	/// <summary>Wait between reads of the requested schema.</summary>
	internal static readonly TimeSpan TargetReadRetryInterval = TimeSpan.FromSeconds(3);

	/// <inheritdoc />
	public EntitySchemaColumnDto Apply(EntityDesignSchemaDto schema, string columnName, string referenceSchemaName,
		ManagerItemDto? referenceSchema, RemoteCommandOptions options) {
		ArgumentNullException.ThrowIfNull(schema);
		EntitySchemaColumnDto column = RequireInheritedLookup(schema, columnName);
		EnsureParentAppliesOverride(schema, columnName);
		ManagerItemDto target = referenceSchema ?? throw CreateMissingTargetException(schema, columnName,
			referenceSchemaName, options);
		EntityDesignSchemaDto parentReference = ReadParentReference(schema, columnName, options);
		HierarchyWalk walk = WalkHierarchy(target.UId, parentReference.UId, options);
		if (!walk.Reached) {
			throw new EntitySchemaDesignerException(
				$"Reference schema '{target.Name}' does not inherit from '{parentReference.Name}', the schema "
				+ $"column '{columnName}' references in parent schema '{schema.ParentSchema.Name}'. An override must "
				+ $"reference '{parentReference.Name}' or a schema that inherits from it. The hierarchy of "
				+ $"'{target.Name}' was read up to '{walk.LastSchemaName}'.");
		}
		column.ReferenceSchema = new EntityDesignSchemaDto {
			UId = target.UId,
			Name = target.Name,
			Caption = [EntitySchemaDesignerSupport.CreateLocalizableString(target.Caption)]
		};
		return column;
	}

	/// <inheritdoc />
	public void VerifyPersisted(EntityDesignSchemaDto reloadedSchema, string columnName, string referenceSchemaName) {
		ArgumentNullException.ThrowIfNull(reloadedSchema);
		if (EntitySchemaDesignerSupport.FindOwnColumn(reloadedSchema, columnName) != null) {
			throw new EntitySchemaDesignerException(
				$"Column '{columnName}' was saved as an own column of schema '{reloadedSchema.Name}' instead of "
				+ "overriding the inherited column.");
		}
		EntitySchemaColumnDto inheritedColumn = EntitySchemaDesignerSupport.FindInheritedColumn(reloadedSchema, columnName)
			?? throw new EntitySchemaDesignerException(
				$"Inherited column '{columnName}' could not be reloaded for schema '{reloadedSchema.Name}'.");
		string persistedReference = inheritedColumn.ReferenceSchema?.Name;
		if (!string.Equals(persistedReference, referenceSchemaName, StringComparison.OrdinalIgnoreCase)) {
			throw new EntitySchemaDesignerException(
				$"Reference override of inherited column '{columnName}' was not persisted for schema "
				+ $"'{reloadedSchema.Name}': expected '{referenceSchemaName}', the server returned "
				+ $"'{persistedReference ?? "<none>"}'.");
		}
	}

	private static EntitySchemaColumnDto RequireInheritedLookup(EntityDesignSchemaDto schema, string columnName) {
		EntitySchemaColumnDto? column = EntitySchemaDesignerSupport.FindInheritedColumn(schema, columnName);
		if (column == null) {
			bool isOwnColumn = EntitySchemaDesignerSupport.FindOwnColumn(schema, columnName) != null;
			throw new EntitySchemaDesignerException(isOwnColumn
				? EntitySchemaDesignerSupport.BuildOverrideOfOwnColumnMessage(schema.Name, columnName)
				: EntitySchemaDesignerSupport.BuildOverrideOfMissingColumnMessage(schema.Name, schema.ParentSchema?.Name,
					columnName));
		}
		if (!column.IsLookupType()) {
			throw new EntitySchemaDesignerException(
				$"Inherited column '{columnName}' is of type "
				+ $"'{EntitySchemaDesignerSupport.GetFriendlyTypeName(column.DataValueType)}'; only the reference "
				+ "schema of an inherited Lookup column can be overridden.");
		}
		return column;
	}

	// The platform applies a reference change on an inherited column only when the parent is virtual and the
	// schema does not replace its parent; otherwise it drops the change and still reports success.
	private static void EnsureParentAppliesOverride(EntityDesignSchemaDto schema, string columnName) {
		if (schema.ExtendParent) {
			throw new EntitySchemaDesignerException(
				$"Schema '{schema.Name}' replaces its parent, and Creatio does not apply a reference override of "
				+ $"inherited column '{columnName}' on a replacement schema.");
		}
		if (schema.ParentSchema == null) {
			throw new EntitySchemaDesignerException(
				$"Schema '{schema.Name}' has no parent schema; an override of column '{columnName}' requires "
				+ "parent-schema-name.");
		}
		if (!schema.ParentSchema.IsVirtual) {
			throw new EntitySchemaDesignerException(
				$"Parent schema '{schema.ParentSchema.Name}' of schema '{schema.Name}' is not virtual, "
				+ $"and Creatio applies a reference override of inherited column '{columnName}' only when the parent "
				+ "is virtual (for example BaseItemInFolder).");
		}
	}

	private EntitySchemaDesignerException CreateMissingTargetException(EntityDesignSchemaDto schema,
		string columnName, string referenceSchemaName, RemoteCommandOptions options) {
		AvailableEntitySchemasResponse withVirtual = designerClient.GetAvailableReferenceSchemas(
			new GetAvailableSchemasRequestDto {
				PackageUId = schema.Package?.UId ?? Guid.Empty,
				UseFullHierarchy = false,
				AllowVirtual = true
			},
			options);
		bool isVirtual = (withVirtual.Items ?? []).Any(item =>
			string.Equals(item.Name, referenceSchemaName, StringComparison.OrdinalIgnoreCase));
		return new EntitySchemaDesignerException(isVirtual
			? $"Reference schema '{referenceSchemaName}' is virtual; inherited column '{columnName}' can be "
				+ "overridden only with a non-virtual schema."
			: $"Reference schema '{referenceSchemaName}' was not found for the override of inherited column "
				+ $"'{columnName}'.");
	}

	// Reads the parent schema as seen from the schema's package, so a reference the parent itself redirects
	// is the one the override is validated against.
	private EntityDesignSchemaDto ReadParentReference(EntityDesignSchemaDto schema, string columnName,
		RemoteCommandOptions options) {
		string parentSchemaName = schema.ParentSchema.Name;
		EntityDesignSchemaDto? parentSchema = designerClient.GetSchemaDesignItem(new GetSchemaDesignItemRequestDto {
			Name = parentSchemaName,
			PackageUId = schema.Package?.UId ?? Guid.Empty,
			UseFullHierarchy = true,
			Cultures = [EntitySchemaDesignerSupport.DefaultCultureName]
		}, options)?.Schema;
		EntitySchemaColumnDto? parentColumn = parentSchema == null
			? null
			: EntitySchemaDesignerSupport.FindOwnColumn(parentSchema, columnName)
				?? EntitySchemaDesignerSupport.FindInheritedColumn(parentSchema, columnName);
		EntityDesignSchemaDto? parentReference = parentColumn?.ReferenceSchema;
		if (parentReference == null || parentReference.UId == Guid.Empty) {
			throw new EntitySchemaDesignerException(
				$"Column '{columnName}' of parent schema '{parentSchemaName}' has no reference schema to override.");
		}
		return parentReference;
	}

	private readonly record struct HierarchyWalk(bool Reached, string LastSchemaName);

	private HierarchyWalk WalkHierarchy(Guid schemaUId, Guid ancestorUId, RemoteCommandOptions options) {
		HashSet<Guid> visited = [];
		Guid current = schemaUId;
		string lastSchemaName = schemaUId.ToString();
		for (int depth = 0; depth <= MaxHierarchyDepth && current != Guid.Empty && visited.Add(current); depth++) {
			if (current == ancestorUId) {
				return new HierarchyWalk(true, lastSchemaName);
			}
			RuntimeEntitySchemaDto? runtimeSchema = ReadRuntimeSchema(current, isTarget: depth == 0, options);
			if (runtimeSchema == null) {
				break;
			}
			lastSchemaName = string.IsNullOrWhiteSpace(runtimeSchema.Name) ? current.ToString() : runtimeSchema.Name;
			current = runtimeSchema.ParentUId;
		}
		return new HierarchyWalk(false, lastSchemaName);
	}

	// The root entity schema reports a parent the runtime service cannot resolve; the service answers that
	// with an unsuccessful response, which ends the hierarchy above the target. The requested schema is read
	// again on that answer, because a schema published moments ago reads as missing until the schema manager
	// finishes refreshing it; when every read fails, its hierarchy could not be read. Transport and markup
	// failures (InvalidOperationException subclasses) are not an answer and propagate.
	private RuntimeEntitySchemaDto? ReadRuntimeSchema(Guid schemaUId, bool isTarget, RemoteCommandOptions options) {
		int attemptCount = isTarget ? TargetReadAttemptCount : 1;
		InvalidOperationException? lastAnswer = null;
		for (int attempt = 1; attempt <= attemptCount; attempt++) {
			if (attempt > 1) {
				retryDelay.Wait(TargetReadRetryInterval);
			}
			try {
				RuntimeEntitySchemaResponse response = designerClient.GetRuntimeEntitySchema(schemaUId, options);
				if (response?.Success == true && response.Schema != null) {
					return response.Schema;
				}
				lastAnswer = null;
			} catch (InvalidOperationException exception) when (exception.GetType() == typeof(InvalidOperationException)) {
				lastAnswer = exception;
			}
		}
		if (!isTarget) {
			return null;
		}
		string message = BuildUnreadableHierarchyMessage(schemaUId);
		throw lastAnswer == null
			? new EntitySchemaDesignerException(message)
			: new EntitySchemaDesignerException(message, lastAnswer);
	}

	private static string BuildUnreadableHierarchyMessage(Guid schemaUId) {
		return $"The parent hierarchy of schema '{schemaUId}' could not be read, so the override target could not be "
			+ "validated.";
	}
}
