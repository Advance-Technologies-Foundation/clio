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
	/// <param name="rootSchemaName">The normalized name of the object the caller named.</param>
	/// <param name="includeConnected">Also enumerate the object's own lookup objects.</param>
	/// <param name="readTimeoutMilliseconds">The timeout of the schema read; <see langword="null"/> leaves it unbounded.
	/// The caller passes its own request timeout, so the enumeration honours it like every other read.</param>
	/// <returns>The objects to read, the security and system objects left out, and any enumeration failure.</returns>
	ConnectedObjectsResolution Resolve(string rootSchemaName, bool includeConnected, int? readTimeoutMilliseconds = null);
}

/// <inheritdoc />
public class ConnectedObjectsResolver : IConnectedObjectsResolver {

	private readonly IRemoteEntitySchemaColumnManager _columnManager;

	public ConnectedObjectsResolver(IRemoteEntitySchemaColumnManager columnManager) {
		_columnManager = columnManager;
	}

	public ConnectedObjectsResolution Resolve(string rootSchemaName, bool includeConnected,
		int? readTimeoutMilliseconds = null) {
		List<string> objects = new() { rootSchemaName };
		if (!includeConnected) {
			return new ConnectedObjectsResolution(objects, Array.Empty<string>());
		}
		EntitySchemaPropertiesInfo schema;
		// Only the service call is guarded: a failure in the code that works on its result is a bug, not a service failure.
		// The column manager rethrows some faults — a bare transport or parse fault, a schema it cannot find — as
		// EntitySchemaDesignerException, and lets the rest through as they are (the client's AggregateException, a login
		// rejection). Either way the connected set is unknown: an enumeration failure, and the root is still read on its own.
		try {
			schema = _columnManager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions {
				SchemaName = rootSchemaName, RuntimeReadTimeoutMilliseconds = readTimeoutMilliseconds
			});
		}
		catch (Exception ex) when (ex is EntitySchemaDesignerException || ObjectRightsSupport.IsServiceFailure(ex)) {
			return new ConnectedObjectsResolution(objects, Array.Empty<string>(), ObjectRightsSupport.DisplayFailure(ex));
		}
		// A referenced name is normalized like a caller's name before the security gate sees it: the gate matches the
		// name as a string, while SQL Server ignores trailing spaces and would still find the table. A name that is not
		// a schema identifier is not an object that can be read.
		List<string> connected = (schema.Columns ?? Array.Empty<EntitySchemaPropertyColumnInfo>())
			.Where(column => string.Equals(column.Source, "own", StringComparison.OrdinalIgnoreCase))
			.Select(column => ObjectRightsSupport.TryNormalizeSchemaName(column.ReferenceSchemaName, out string name)
				? name
				: null)
			.Where(name => name is not null
				&& !string.Equals(name, rootSchemaName, StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		List<string> excluded = connected.Where(ObjectRightsSupport.IsSecurityOrSystemObject).ToList();
		objects.AddRange(connected.Where(name => !ObjectRightsSupport.IsSecurityOrSystemObject(name)));
		return new ConnectedObjectsResolution(objects, excluded);
	}
}
