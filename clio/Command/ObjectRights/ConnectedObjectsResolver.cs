using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common.ObjectRights;

namespace Clio.Command.ObjectRights;

/// <summary>
/// The objects get-object-rights reads. <see cref="Objects"/> always starts with the root
/// object. <see cref="Excluded"/> lists connected objects that were deliberately left out because they are
/// security or system objects (see <see cref="IConnectedObjectsResolver"/>). <see cref="EnumerationError"/> is
/// set when the connected objects were requested but could not be enumerated. <see cref="Objects"/> then holds
/// the root only, and the caller must treat the connected set as UNKNOWN, not empty.
/// </summary>
public sealed record ConnectedObjectsResolution(
	IReadOnlyList<string> Objects,
	IReadOnlyList<string> Excluded,
	string EnumerationError = null);

/// <summary>
/// Resolves the objects get-object-rights reads: the root object plus, when requested, every distinct object
/// referenced by the root's OWN lookup columns (inherited BaseEntity audit lookups such as CreatedBy/ModifiedBy
/// are excluded). Security and system objects (<see cref="ObjectRightsSupport.IsSecurityOrSystemObject"/>) are
/// never read as connected objects.
/// </summary>
public interface IConnectedObjectsResolver {
	/// <summary>
	/// Returns the root object and, when <paramref name="includeConnected"/> is true, its own connected lookup
	/// objects. Security and system objects (the role/user directory, schema and package metadata, rights
	/// tables) are never read as connected objects: they are returned in <see cref="ConnectedObjectsResolution.Excluded"/>
	/// and are read only when named as the root. A failed schema read is reported in
	/// <see cref="ConnectedObjectsResolution.EnumerationError"/> and never throws.
	/// </summary>
	ConnectedObjectsResolution Resolve(string rootSchemaName, bool includeConnected);
}

/// <inheritdoc />
public class ConnectedObjectsResolver : IConnectedObjectsResolver {

	private readonly IRemoteEntitySchemaColumnManager _columnManager;

	public ConnectedObjectsResolver(IRemoteEntitySchemaColumnManager columnManager) {
		_columnManager = columnManager;
	}

	public ConnectedObjectsResolution Resolve(string rootSchemaName, bool includeConnected) {
		List<string> objects = new() { rootSchemaName };
		if (!includeConnected) {
			return new ConnectedObjectsResolution(objects, Array.Empty<string>());
		}
		List<string> connected;
		try {
			EntitySchemaPropertiesInfo schema =
				_columnManager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions { SchemaName = rootSchemaName });
			connected = (schema.Columns ?? Array.Empty<EntitySchemaPropertyColumnInfo>())
				.Where(column => string.Equals(column.Source, "own", StringComparison.OrdinalIgnoreCase))
				.Select(column => column.ReferenceSchemaName)
				.Where(name => !string.IsNullOrWhiteSpace(name)
					&& !string.Equals(name, rootSchemaName, StringComparison.OrdinalIgnoreCase))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
		catch (Exception ex) when (ObjectRightsSupport.IsServiceFailure(ex)) {
			return new ConnectedObjectsResolution(objects, Array.Empty<string>(), ObjectRightsSupport.DisplayError(ex.Message));
		}
		List<string> excluded = connected.Where(ObjectRightsSupport.IsSecurityOrSystemObject).ToList();
		objects.AddRange(connected.Where(name => !ObjectRightsSupport.IsSecurityOrSystemObject(name)));
		return new ConnectedObjectsResolution(objects, excluded);
	}
}
