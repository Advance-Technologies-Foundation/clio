using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;

namespace Clio.Command.ObjectRights;

/// <summary>
/// The set of objects an object-rights operation targets. <see cref="Objects"/> always starts with the root
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
/// Resolves the set of objects an object-rights operation targets: the root object plus, when requested,
/// every distinct object referenced by the root's OWN lookup columns (inherited BaseEntity audit lookups
/// such as CreatedBy/ModifiedBy are excluded). Shared by get-object-rights and set-object-rights.
/// </summary>
public interface IConnectedObjectsResolver {
	/// <summary>
	/// Returns the root object and, when <paramref name="includeConnected"/> is true, its own connected lookup
	/// objects. Security and system objects (the role/user directory, schema and package metadata, rights
	/// tables) are never fanned out to: they are returned in <see cref="ConnectedObjectsResolution.Excluded"/>
	/// and can only be targeted by naming them as the root. A failed schema read is reported in
	/// <see cref="ConnectedObjectsResolution.EnumerationError"/> and never throws.
	/// </summary>
	ConnectedObjectsResolution Resolve(string rootSchemaName, bool includeConnected);
}

/// <inheritdoc />
public class ConnectedObjectsResolver : IConnectedObjectsResolver {

	// A fan-out grant goes to a whole audience at once (typically All external users), and on MCP nobody sees
	// the target list before it is written. These objects expose the role/user directory, security
	// configuration or platform metadata. Granting them as a SIDE EFFECT of a portal-section grant would make
	// that data readable through DataService wherever record permissions do not also protect it.
	private static readonly string[] ExcludedPrefixes = { "SysAdmin", "SysUser", "SysSchema", "SysPackage", "SysSettings" };

	private static readonly string[] ExcludedSuffixes = { "Right", "Rights" };

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
		catch (Exception ex) {
			return new ConnectedObjectsResolution(objects, Array.Empty<string>(), ex.Message);
		}
		List<string> excluded = connected.Where(IsSecurityOrSystemObject).ToList();
		objects.AddRange(connected.Where(name => !IsSecurityOrSystemObject(name)));
		return new ConnectedObjectsResolution(objects, excluded);
	}

	private static bool IsSecurityOrSystemObject(string schemaName) =>
		ExcludedPrefixes.Any(prefix => schemaName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
		|| ExcludedSuffixes.Any(suffix => schemaName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
