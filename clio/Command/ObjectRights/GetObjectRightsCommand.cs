using System;
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
		"Optional SysAdminUnit id (role or user) to filter to one role. When omitted, every role's rights are reported.")]
	public string Grantee { get; set; }

	[Option("include-connected", Required = false, HelpText =
		"Also read every object referenced by the root object's own lookup columns")]
	public bool IncludeConnected { get; set; }
}

/// <summary>
/// Reports the facts of the object-permissions layer — per object, which operations each role (or the one
/// grantee) holds, or that the object is not administered by operation permissions, or that it could not be
/// read. It deliberately draws no coverage verdict: what those facts mean for a particular audience (for
/// example the portal) is owned by the guidance for that scenario, not by this general tool.
/// </summary>
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
			bool rootFailed = false;

			_logger.WriteInfo(
				$"Object operation permissions for '{options.EntitySchemaName}'"
				+ (options.IncludeConnected ? " and its connected objects" : "")
				+ (granteeFilter is null ? "" : $" (grantee {granteeFilter})") + ":");
			if (resolution.EnumerationError is not null) {
				_logger.WriteWarning(
					$"  Could not enumerate the connected objects of '{options.EntitySchemaName}' "
					+ $"({resolution.EnumerationError}); only the root object was read.");
			}
			foreach (string excluded in resolution.Excluded) {
				_logger.WriteWarning(
					$"  {excluded}: security/system object — not read as a connected object. Pass it as "
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
						// The object the caller NAMED could not be read, so the read did not happen: fail, as
						// set-object-rights does for the same case.
						rootFailed = true;
						_logger.WriteError($"  {schemaName}: {reason}.");
					} else {
						_logger.WriteWarning($"  {schemaName}: {reason}.");
					}
					continue;
				}
				if (!info.AdministratedByOperations) {
					_logger.WriteInfo(
						$"  {schemaName}: not administered by operation permissions — available to all internal users; "
						+ "external users reach it only through an explicit grant.");
					continue;
				}
				ReportObject(schemaName, info, granteeFilter);
			}
			return rootFailed ? 1 : 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	private void ReportObject(string schemaName, ObjectRightsInfo info, Guid? granteeFilter) {
		if (granteeFilter is not null) {
			RoleOperationRights row = info.Roles.FirstOrDefault(role => role.GranteeId == granteeFilter.Value);
			_logger.WriteInfo(row is null
				? $"  {schemaName}: grantee {granteeFilter} has NO object operations granted."
				: $"  {schemaName}: {Describe(row)}.");
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
