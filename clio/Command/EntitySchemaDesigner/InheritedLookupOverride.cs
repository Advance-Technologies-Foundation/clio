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

internal sealed class InheritedLookupOverride(IRemoteEntitySchemaDesignerClient designerClient)
	: IInheritedLookupOverride {

	/// <summary>Upper bound on the parent hops followed from the requested schema.</summary>
	internal const int MaxHierarchyDepth = 32;

	/// <summary>
	/// Builds the refusal for a new column whose name the schema already inherits, naming the parent and the
	/// override form that changes an inherited lookup instead.
	/// </summary>
	/// <param name="schema">Design schema that inherits the column.</param>
	/// <param name="columnName">Requested column name.</param>
	/// <returns>The caller-facing message.</returns>
	internal static string BuildInheritedNameConflictMessage(EntityDesignSchemaDto schema, string columnName) {
		string parentName = string.IsNullOrWhiteSpace(schema.ParentSchema?.Name)
			? "its parent schema"
			: $"parent schema '{schema.ParentSchema.Name}'";
		return $"Column '{columnName}' is inherited by schema '{schema.Name}' from {parentName}, so it cannot be "
			+ "added as a new column. To point an inherited lookup at another schema, override it instead: in "
			+ $"create-entity-schema send {{\"name\":\"{columnName}\",\"override\":true,"
			+ "\"reference-schema-name\":\"<Schema>\"}; on an existing schema use the modify action with "
			+ "reference-schema-name. Otherwise choose a different column name.";
	}

	/// <summary>Returns whether <paramref name="columnName"/> is one of the inherited columns of the schema.</summary>
	internal static bool IsInheritedColumn(EntityDesignSchemaDto schema, string columnName) {
		return FindInheritedColumn(schema, columnName) != null;
	}

	/// <inheritdoc />
	public EntitySchemaColumnDto Apply(EntityDesignSchemaDto schema, string columnName, string referenceSchemaName,
		ManagerItemDto? referenceSchema, RemoteCommandOptions options) {
		ArgumentNullException.ThrowIfNull(schema);
		EntitySchemaColumnDto column = RequireInheritedLookup(schema, columnName);
		EnsureParentAppliesOverride(schema, columnName);
		ManagerItemDto target = referenceSchema ?? throw CreateMissingTargetException(schema, columnName,
			referenceSchemaName, options);
		Guid currentReferenceUId = column.ReferenceSchema?.UId ?? Guid.Empty;
		if (currentReferenceUId == Guid.Empty) {
			throw new EntitySchemaDesignerException(
				$"Inherited column '{columnName}' has no reference schema to override.");
		}
		if (!InheritsFrom(target.UId, currentReferenceUId, options)) {
			throw new EntitySchemaDesignerException(
				$"Reference schema '{target.Name}' does not inherit from '{column.ReferenceSchema.Name}', the schema "
				+ $"inherited column '{columnName}' currently references. An override must reference "
				+ $"'{column.ReferenceSchema.Name}' or a schema that inherits from it.");
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
		bool ownColumnExists = (reloadedSchema.Columns ?? []).Any(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
		if (ownColumnExists) {
			throw new EntitySchemaDesignerException(
				$"Column '{columnName}' was saved as an own column of schema '{reloadedSchema.Name}' instead of "
				+ "overriding the inherited column.");
		}
		EntitySchemaColumnDto inheritedColumn = FindInheritedColumn(reloadedSchema, columnName)
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

	private static EntitySchemaColumnDto? FindInheritedColumn(EntityDesignSchemaDto schema, string columnName) {
		return (schema.InheritedColumns ?? []).FirstOrDefault(column =>
			string.Equals(column.Name, columnName, StringComparison.OrdinalIgnoreCase));
	}

	private static EntitySchemaColumnDto RequireInheritedLookup(EntityDesignSchemaDto schema, string columnName) {
		EntitySchemaColumnDto? column = FindInheritedColumn(schema, columnName);
		if (column == null) {
			bool isOwnColumn = (schema.Columns ?? []).Any(item =>
				string.Equals(item.Name, columnName, StringComparison.OrdinalIgnoreCase));
			throw new EntitySchemaDesignerException(isOwnColumn
				? $"Column '{columnName}' is an own column of schema '{schema.Name}', not an inherited one; an "
					+ "override applies only to a column inherited from the parent schema."
				: $"Parent schema '{schema.ParentSchema?.Name ?? "<none>"}' of schema '{schema.Name}' has no column "
					+ $"'{columnName}' to override.");
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
		if (schema.ParentSchema?.IsVirtual != true) {
			throw new EntitySchemaDesignerException(
				$"Parent schema '{schema.ParentSchema?.Name ?? "<none>"}' of schema '{schema.Name}' is not virtual, "
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

	private bool InheritsFrom(Guid schemaUId, Guid ancestorUId, RemoteCommandOptions options) {
		HashSet<Guid> visited = [];
		Guid current = schemaUId;
		for (int depth = 0; depth <= MaxHierarchyDepth && current != Guid.Empty && visited.Add(current); depth++) {
			if (current == ancestorUId) {
				return true;
			}
			current = ReadParentUId(current, isTarget: depth == 0, options);
		}
		return false;
	}

	// The root entity schema reports a parent the runtime service cannot resolve; the service answers that
	// with an unsuccessful response, which ends the hierarchy. Transport and markup failures are not an
	// answer and propagate.
	private Guid ReadParentUId(Guid schemaUId, bool isTarget, RemoteCommandOptions options) {
		try {
			RuntimeEntitySchemaResponse response = designerClient.GetRuntimeEntitySchema(schemaUId, options);
			if (response?.Success == true && response.Schema != null) {
				return response.Schema.ParentUId;
			}
		} catch (InvalidOperationException exception) when (exception.GetType() == typeof(InvalidOperationException)
			&& !isTarget) {
			return Guid.Empty;
		}
		if (!isTarget) {
			return Guid.Empty;
		}
		throw new EntitySchemaDesignerException(
			$"The parent hierarchy of schema '{schemaUId}' could not be read, so the override target could not be "
			+ "validated.");
	}
}
