using System;
using System.Collections.Generic;
using System.Linq;
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
	private readonly IConnectedObjectsResolver _connectedObjects;
	private readonly ILogger _logger;

	public GetObjectRightsCommand(IObjectRightsReader rightsReader,
		IConnectedObjectsResolver connectedObjects, ILogger logger) {
		_rightsReader = rightsReader;
		_connectedObjects = connectedObjects;
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
			ConnectedObjectsResolution resolution =
				_connectedObjects.Resolve(options.EntitySchemaName, options.IncludeConnected);
			List<string> granteeMissing = new();
			List<string> granteeNotAdministered = new();
			int skipped = 0;
			int read = 0;
			bool rootFailed = false;

			_logger.WriteInfo(
				$"Object operation permissions for '{options.EntitySchemaName}'"
				+ (options.IncludeConnected ? " and its connected objects" : "")
				+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})") + ":");
			if (resolution.EnumerationError is not null) {
				_logger.WriteWarning(
					$"  Could not enumerate the connected objects of '{options.EntitySchemaName}' "
					+ $"({resolution.EnumerationError}) — they are UNVERIFIED; only the root object was read.");
			}
			foreach (string excluded in resolution.Excluded) {
				_logger.WriteWarning(
					$"  {excluded}: security/system object — not part of the connected check. Pass it as "
					+ "--entity-schema-name to read it.");
			}

			for (int index = 0; index < resolution.Objects.Count; index++) {
				string schemaName = resolution.Objects[index];
				bool isRoot = index == 0;
				ObjectRightsInfo info = _rightsReader.GetObjectRights(schemaName, requestOptions);
				if (info.ReadError != null || !info.Found) {
					string reason = info.ReadError != null
						? $"could not read object rights ({info.ReadError})"
						: "schema not found";
					if (isRoot) {
						// The object the caller NAMED could not be read: the check did not happen, so it must not
						// report success (set-object-rights answers the same failure with exit 1).
						rootFailed = true;
						_logger.WriteError($"  {schemaName}: {reason}.");
					} else {
						skipped++;
						_logger.WriteWarning($"  {schemaName}: {reason} — skipped.");
					}
					continue;
				}
				read++;
				if (!info.AdministratedByOperations) {
					// Not administered = reachable by every INTERNAL user, not by everyone: external/portal users are
					// deny-by-default and reach an object only through an explicit grant. The tool cannot tell whether
					// the grantee is internal or external, so such an object is never counted as covered (no all-clear
					// that would let an agent skip a portal grant), but it is reported apart from "cannot read": for an
					// internal role it IS reachable.
					if (granteeFilter is not null) {
						granteeNotAdministered.Add(schemaName);
						_logger.WriteWarning(
							$"  {schemaName}: not administered by operation permissions — available to all INTERNAL users "
							+ $"only; grantee {granteeFilter} has no explicit grant (external users are deny-by-default).");
					} else {
						_logger.WriteInfo(
							$"  {schemaName}: not administered by operation permissions — available to all internal users "
							+ "(external users still need an explicit grant).");
					}
					continue;
				}
				ReportObject(schemaName, info, granteeFilter, granteeMissing);
			}

			if (granteeFilter is not null) {
				ReportGranteeSummary(granteeFilter.Value, granteeMissing, granteeNotAdministered, read, skipped,
					resolution.EnumerationError is not null);
			}
			return rootFailed ? 1 : 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	// The coverage bar is READ on every object: that is what makes a record and its lookup values visible to the
	// role, and it is what set-object-rights grants on connected lookups by default. Demanding create/edit here
	// would list every read-only connected lookup as "missing" and push the caller to widen them to write access.
	// The operations each object DOES hold are printed per object above.
	private void ReportGranteeSummary(Guid grantee, List<string> granteeMissing,
		List<string> granteeNotAdministered, int read, int skipped, bool enumerationFailed) {
		List<string> unverified = new();
		if (skipped > 0) {
			unverified.Add($"{skipped} object(s) could not be read");
		}
		if (enumerationFailed) {
			unverified.Add("the connected objects could not be enumerated");
		}
		string unverifiedNote = unverified.Count == 0 ? "" : $" Could not verify: {string.Join("; ", unverified)}.";
		if (granteeMissing.Count > 0 || granteeNotAdministered.Count > 0) {
			if (granteeMissing.Count > 0) {
				_logger.WriteWarning(
					$"Objects grantee {grantee} cannot read: {string.Join(", ", granteeMissing)}.{unverifiedNote}");
			}
			if (granteeNotAdministered.Count > 0) {
				_logger.WriteWarning(
					$"Objects with no explicit grant for grantee {grantee} (not administered by operation permissions — "
					+ "reachable only if the role is internal; external users need a grant): "
					+ $"{string.Join(", ", granteeNotAdministered)}."
					+ (granteeMissing.Count > 0 ? "" : unverifiedNote));
			}
			return;
		}
		if (read == 0) {
			// Nothing was read, so any coverage sentence would be vacuously true.
			_logger.WriteWarning($"Could not verify grantee {grantee}: no object could be read.{unverifiedNote}");
			return;
		}
		// Never report a clean all-clear when something was not verified — an unread object is unknown, not covered.
		if (unverified.Count == 0) {
			_logger.WriteInfo($"Grantee {grantee} can read every listed object.");
		} else {
			_logger.WriteWarning($"Grantee {grantee} can read the {read} object(s) that could be read.{unverifiedNote}");
		}
	}

	private void ReportObject(string schemaName, ObjectRightsInfo info, Guid? granteeFilter, List<string> granteeMissing) {
		if (granteeFilter is not null) {
			RoleOperationRights row = info.Roles.FirstOrDefault(role => role.GranteeId == granteeFilter.Value);
			if (row is null) {
				granteeMissing.Add(schemaName);
				_logger.WriteWarning($"  {schemaName}: grantee {granteeFilter} has NO object operations granted.");
				return;
			}
			if (!row.CanRead) {
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
}
