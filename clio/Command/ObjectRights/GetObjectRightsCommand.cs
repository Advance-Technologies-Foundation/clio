using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

[Verb("get-object-rights", HelpText =
	"Read whether external (portal) users have object operation access to an object and its connected (lookup) entities")]
public class GetObjectRightsOptions : RemoteCommandOptions {

	[Option("entity-schema-name", Required = true, HelpText =
		"Root object (entity schema) name to check; its connected lookup objects are checked too")]
	public string EntitySchemaName { get; set; }
}

public class GetObjectRightsCommand : Command<GetObjectRightsOptions> {

	private readonly IObjectRightsReader _rightsReader;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public GetObjectRightsCommand(IObjectRightsReader rightsReader,
		IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_rightsReader = rightsReader;
		_columnManager = columnManager;
		_logger = logger;
	}

	public override int Execute(GetObjectRightsOptions options) {
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return 1;
		}

		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		try {
			IReadOnlyList<string> objects = ResolveObjectsToCheck(options.EntitySchemaName);
			List<string> needingGrant = new();

			_logger.WriteInfo(
				$"External-user object access for '{options.EntitySchemaName}' and its connected objects:");
			foreach (string schemaName in objects) {
				ObjectRightsInfo info = _rightsReader.GetObjectRights(schemaName, requestOptions);
				if (info.ReadError != null) {
					_logger.WriteWarning($"  {schemaName}: could not read object rights ({info.ReadError}) — skipped.");
					continue;
				}
				if (!info.Found) {
					_logger.WriteWarning($"  {schemaName}: schema not found (skipped).");
					continue;
				}
				if (!info.AdministratedByOperations) {
					_logger.WriteInfo(
						$"  {schemaName}: not administered by operation permissions — available to all, no grant needed.");
					continue;
				}
				ExternalUsersOperationRights ext = info.ExternalUsers;
				bool granted = ext is { CanRead: true, CanCreate: true, CanEdit: true };
				if (granted) {
					_logger.WriteInfo(
						$"  {schemaName}: All external users granted (read/create/edit{(ext.CanDelete ? "/delete" : "")}).");
				} else {
					needingGrant.Add(schemaName);
					_logger.WriteWarning($"  {schemaName}: MISSING external access ({DescribeRights(ext)}).");
				}
			}

			if (needingGrant.Count == 0) {
				_logger.WriteInfo(
					"All external users already have object access to every listed object. No grant needed.");
			} else {
				_logger.WriteWarning(
					$"Objects still needing a grant for All external users: {string.Join(", ", needingGrant)}. "
					+ $"Run set-object-rights --entity-schema-name {options.EntitySchemaName} to grant.");
			}
			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	// The root object plus every distinct object referenced by one of its lookup columns. Connected-object
	// enumeration is best-effort: if the schema read fails, the root is still checked (with a warning).
	private IReadOnlyList<string> ResolveObjectsToCheck(string rootSchemaName) {
		List<string> objects = new() { rootSchemaName };
		try {
			EntitySchemaPropertiesInfo schema =
				_columnManager.GetSchemaProperties(new GetEntitySchemaPropertiesOptions { SchemaName = rootSchemaName });
			// Only the object's OWN lookup columns: inherited BaseEntity audit lookups (CreatedBy/ModifiedBy →
			// Contact) are not the section object's connected objects and their targets need no portal grant.
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
				$"Could not enumerate connected objects of '{rootSchemaName}': {ex.Message}. Checking the root object only.");
		}
		return objects;
	}

	private static string DescribeRights(ExternalUsersOperationRights ext) {
		if (ext is null) {
			return "no external-users row";
		}
		string[] present = new[] {
			ext.CanRead ? "read" : null,
			ext.CanCreate ? "create" : null,
			ext.CanEdit ? "edit" : null,
			ext.CanDelete ? "delete" : null
		}.Where(flag => flag != null).ToArray();
		return present.Length == 0 ? "no operations granted" : "has only " + string.Join("/", present);
	}
}
