using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;

namespace Clio.Command.ObjectRights;

/// <summary>
/// Resolves the set of objects an object-rights operation targets: the root object plus, when requested,
/// every distinct object referenced by the root's OWN lookup columns (inherited BaseEntity audit lookups
/// such as CreatedBy/ModifiedBy are excluded). Best-effort — a failed schema read falls back to the root
/// object alone with a warning. Shared by get-object-rights and set-object-rights.
/// </summary>
public interface IConnectedObjectsResolver {
	/// <summary>
	/// Returns the root object, and (when <paramref name="includeConnected"/> is true) its own connected
	/// lookup objects. <paramref name="actionVerb"/> is the word used in the fallback warning ("Checking",
	/// "Changing").
	/// </summary>
	IReadOnlyList<string> Resolve(string rootSchemaName, bool includeConnected, string actionVerb);
}

/// <inheritdoc />
public class ConnectedObjectsResolver : IConnectedObjectsResolver {

	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public ConnectedObjectsResolver(IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_columnManager = columnManager;
		_logger = logger;
	}

	public IReadOnlyList<string> Resolve(string rootSchemaName, bool includeConnected, string actionVerb) {
		List<string> objects = new() { rootSchemaName };
		if (!includeConnected) {
			return objects;
		}
		try {
			EntitySchemaPropertiesInfo schema =
				_columnManager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions { SchemaName = rootSchemaName });
			IEnumerable<string> connected = (schema.Columns ?? Array.Empty<EntitySchemaPropertyColumnInfo>())
				.Where(column => string.Equals(column.Source, "own", StringComparison.OrdinalIgnoreCase))
				.Select(column => column.ReferenceSchemaName)
				.Where(name => !string.IsNullOrWhiteSpace(name)
					&& !string.Equals(name, rootSchemaName, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
			objects.AddRange(connected);
		}
		catch (Exception ex) {
			_logger.WriteWarning(
				$"Could not enumerate connected objects of '{rootSchemaName}': {ex.Message}. {actionVerb} the root object only.");
		}
		return objects;
	}
}
