using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

[Verb("get-object-rights", HelpText =
	"Read object operation permissions (read/create/edit/delete per role) for an object and, optionally, its connected objects")]
public class GetObjectRightsOptions : RemoteCommandOptions {

	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name to read")]
	public string EntitySchemaName { get; set; }

	[Option("grantee", Required = false, HelpText =
		"Optional SysAdminUnit id to filter to one role (e.g. All external users = 720b771c-e7a7-4f31-9cfb-52cd21c3739f). "
		+ "When omitted, every role's rights are reported.")]
	public string Grantee { get; set; }

	[Option("include-connected", Required = false, HelpText =
		"Also read every object referenced by the root object's own lookup columns (portal-section convenience)")]
	public bool IncludeConnected { get; set; }
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
		Guid? granteeFilter = null;
		if (!string.IsNullOrWhiteSpace(options.Grantee)) {
			if (!Guid.TryParse(options.Grantee, out Guid parsed) || parsed == Guid.Empty) {
				_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
				return 1;
			}
			granteeFilter = parsed;
		}

		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		try {
			IReadOnlyList<string> objects = ResolveObjectsToCheck(options.EntitySchemaName, options.IncludeConnected);
			List<string> granteeMissing = new();

			_logger.WriteInfo(
				$"Object operation permissions for '{options.EntitySchemaName}'"
				+ (options.IncludeConnected ? " and its connected objects" : "")
				+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})") + ":");

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
						$"  {schemaName}: not administered by operation permissions — available to all.");
					continue;
				}
				ReportObject(schemaName, info, granteeFilter, granteeMissing);
			}

			if (granteeFilter is not null) {
				if (granteeMissing.Count == 0) {
					_logger.WriteInfo($"Grantee {granteeFilter} already has read/create/edit on every listed object.");
				} else {
					_logger.WriteWarning(
						$"Objects where grantee {granteeFilter} lacks read/create/edit: {string.Join(", ", granteeMissing)}.");
				}
			}
			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	private void ReportObject(string schemaName, ObjectRightsInfo info, Guid? granteeFilter, List<string> granteeMissing) {
		IEnumerable<RoleOperationRights> roles = info.Roles;
		if (granteeFilter is not null) {
			roles = roles.Where(role => role.GranteeId == granteeFilter.Value);
			RoleOperationRights row = roles.FirstOrDefault();
			if (row is null) {
				granteeMissing.Add(schemaName);
				_logger.WriteWarning($"  {schemaName}: grantee {granteeFilter} has NO object operations granted.");
				return;
			}
			if (!(row.CanRead && row.CanCreate && row.CanEdit)) {
				granteeMissing.Add(schemaName);
			}
			_logger.WriteInfo($"  {schemaName}: {Describe(row)}.");
			return;
		}
		if (!info.Roles.Any()) {
			_logger.WriteInfo($"  {schemaName}: administered by operations, no role grants.");
			return;
		}
		_logger.WriteInfo($"  {schemaName}:");
		foreach (RoleOperationRights role in info.Roles) {
			_logger.WriteInfo($"    {Describe(role)}");
		}
	}

	private static string Describe(RoleOperationRights role) {
		string[] ops = new[] {
			role.CanRead ? "read" : null,
			role.CanCreate ? "create" : null,
			role.CanEdit ? "edit" : null,
			role.CanDelete ? "delete" : null
		}.Where(op => op != null).ToArray();
		string granted = ops.Length == 0 ? "no operations" : string.Join("/", ops);
		return $"{role.GranteeName} ({role.GranteeId}): {granted}";
	}

	// The root object plus (when requested) every distinct object referenced by one of its OWN lookup columns.
	// Connected-object enumeration is best-effort: if the schema read fails, only the root is checked.
	private IReadOnlyList<string> ResolveObjectsToCheck(string rootSchemaName, bool includeConnected) {
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
				$"Could not enumerate connected objects of '{rootSchemaName}': {ex.Message}. Checking the root object only.");
		}
		return objects;
	}
}
