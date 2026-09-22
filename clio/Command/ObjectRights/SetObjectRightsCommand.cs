using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

[Verb("set-object-rights", HelpText =
	"Grant or revoke object operation permissions (read/create/edit/delete) for a role on an object (destructive)")]
public class SetObjectRightsOptions : RemoteCommandOptions {

	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name whose operation permissions are changed")]
	public string EntitySchemaName { get; set; }

	[Option("grantee", Required = true, HelpText =
		"SysAdminUnit id (organizational/functional role or user) to grant/revoke. Names are not unique — pass the id. "
		+ "Portal audience: All external users = 720b771c-e7a7-4f31-9cfb-52cd21c3739f")]
	public string Grantee { get; set; }

	[Option("operations", Required = false, HelpText =
		"Comma-separated operations: read,create,edit,delete. Default: read,create,edit (delete not granted by default).")]
	public string Operations { get; set; }

	[Option("revoke", Required = false, HelpText =
		"Revoke the operations instead of granting. A role left with no operations is removed.")]
	public bool Revoke { get; set; }

	[Option("disable-operation-permissions", Required = false, HelpText =
		"Allow a revoke to remove the object's LAST rights row, which turns the object's operation permissions "
		+ "OFF and makes it available to ALL internal users. Without this such a revoke is refused.")]
	public bool DisableOperationPermissions { get; set; }

	[Option("include-connected", Required = false, HelpText =
		"Also apply to every object referenced by the root object's own lookup columns (portal-section convenience)")]
	public bool IncludeConnected { get; set; }

	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }
}

public class SetObjectRightsCommand : Command<SetObjectRightsOptions> {

	private readonly IObjectRightsWriter _rightsWriter;
	private readonly IConnectedObjectsResolver _connectedObjects;
	private readonly ILogger _logger;

	public SetObjectRightsCommand(IObjectRightsWriter rightsWriter,
		IConnectedObjectsResolver connectedObjects, ILogger logger) {
		_rightsWriter = rightsWriter;
		_connectedObjects = connectedObjects;
		_logger = logger;
	}

	public override int Execute(SetObjectRightsOptions options) {
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return 1;
		}
		if (!Guid.TryParse(options.Grantee, out Guid grantee) || grantee == Guid.Empty) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit id (GUID).");
			return 1;
		}
		if (!TryParseOperations(options.Operations, out IReadOnlyCollection<ObjectOperation> operations, out string opError)) {
			_logger.WriteError(opError);
			return 1;
		}

		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		// Resolve the full target set BEFORE confirming, so the destructive prompt names every object that will
		// be written (with --include-connected the fan-out can span several permission objects).
		IReadOnlyList<string> objects =
			_connectedObjects.Resolve(options.EntitySchemaName, options.IncludeConnected, "Changing");
		string verb = options.Revoke ? "Revoke" : "Grant";
		string opList = string.Join("/", operations.Select(op => op.ToString().ToLowerInvariant()));
		string targets = objects.Count == 1
			? $"'{objects[0]}'"
			: $"{objects.Count} objects ({string.Join(", ", objects)})";
		string change = $"{verb} object operations [{opList}] for grantee {grantee} on {targets}.";
		if (options.Revoke && options.DisableOperationPermissions) {
			// The operator approves the access WIDENING here, not just the revoke — so the prompt has to say it.
			change += " Any object left with no rights rows has its operation permissions turned OFF"
				+ " and becomes available to ALL internal users.";
		}

		ConfirmDecision decision = ConfirmApply(options, change);
		if (decision == ConfirmDecision.Cancelled) {
			return 0;
		}
		if (decision == ConfirmDecision.Refused) {
			return 1;
		}

		try {
			bool anyFailure = false;
			foreach (string schemaName in objects) {
				ObjectRightsChange result = _rightsWriter.SetObjectRights(
					schemaName, grantee, operations, options.Revoke, options.DisableOperationPermissions, requestOptions);
				if (result.Error != null) {
					anyFailure = true;
					_logger.WriteError($"  {schemaName}: {result.Error}");
				} else if (!result.Found) {
					_logger.WriteWarning($"  {schemaName}: schema not found (skipped).");
				} else if (result.RefusedLastRowRemoval) {
					// Nothing was written, so this is not a success: the revoke the operator asked for did not happen.
					anyFailure = true;
					_logger.WriteError(
						$"  {schemaName}: grantee {grantee} holds the object's LAST rights row. Removing it would turn "
						+ $"operation permissions OFF and make '{schemaName}' available to ALL internal users. "
						+ "Nothing was changed — re-run with --disable-operation-permissions if that is what you want.");
				} else if (result.Changed) {
					string widened = result.OperationPermissionsDisabled
						? " Operation permissions are now OFF on this object — it is available to ALL internal users."
						: "";
					_logger.WriteInfo(
						$"  {schemaName}: {(options.Revoke ? "revoked" : "granted")} [{opList}] for grantee {grantee}.{widened}");
				} else {
					_logger.WriteInfo($"  {schemaName}: already in the requested state (no change).");
				}
			}
			return anyFailure ? 1 : 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	private static bool TryParseOperations(string raw, out IReadOnlyCollection<ObjectOperation> operations,
		out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			// Least-privilege default: read/create/edit (the access a role needs to work with an object, and
			// what the portal "make available" flow grants). delete is NOT granted by default — pass it in
			// --operations explicitly. This matches the read/create/edit "has access" check in get-object-rights.
			operations = new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit };
			return true;
		}
		List<ObjectOperation> parsed = new();
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			switch (token.ToLowerInvariant()) {
				case "read": parsed.Add(ObjectOperation.Read); break;
				case "create": case "append": parsed.Add(ObjectOperation.Create); break;
				case "edit": case "write": parsed.Add(ObjectOperation.Edit); break;
				case "delete": parsed.Add(ObjectOperation.Delete); break;
				default:
					operations = Array.Empty<ObjectOperation>();
					error = $"Error: unknown operation '{token}'. Use read,create,edit,delete.";
					return false;
			}
		}
		operations = parsed.Distinct().ToArray();
		return true;
	}

	// Destructive gate, mirroring other destructive clio commands: --confirm applies without a prompt; without
	// it an interactive run asks y/n, and a non-interactive run refuses (rather than silently applying).
	private ConfirmDecision ConfirmApply(SetObjectRightsOptions options, string change) {
		if (options.Confirm) {
			return ConfirmDecision.Approved;
		}
		if (Console.IsInputRedirected) {
			_logger.WriteError(
				"Error: set-object-rights is destructive and needs confirmation. Re-run with --confirm to apply "
				+ $"the change: {change}");
			return ConfirmDecision.Refused;
		}
		_logger.WriteWarning($"About to change object permissions: {change}");
		_logger.WriteInfo("Apply this change? (y/n)");
		string answer = Console.ReadLine();
		if (string.IsNullOrWhiteSpace(answer) || !answer.StartsWith("y", StringComparison.CurrentCultureIgnoreCase)) {
			_logger.WriteInfo("Object-permissions change cancelled.");
			return ConfirmDecision.Cancelled;
		}
		return ConfirmDecision.Approved;
	}

	private enum ConfirmDecision {
		Approved,
		Cancelled,
		Refused
	}
}
