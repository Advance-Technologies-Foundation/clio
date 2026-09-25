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

	[Option("connected-operations", Required = false, HelpText =
		"Operations applied to the CONNECTED lookup objects with --include-connected. Default: read — picking a "
		+ "lookup value only needs read, so create/edit are not fanned out to shared dictionaries unless passed here.")]
	public string ConnectedOperations { get; set; }

	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }
}

public class SetObjectRightsCommand : Command<SetObjectRightsOptions> {

	private readonly IObjectRightsWriter _rightsWriter;
	private readonly IConnectedObjectsResolver _connectedObjects;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	public SetObjectRightsCommand(IObjectRightsWriter rightsWriter,
		IConnectedObjectsResolver connectedObjects, IInteractiveConsole console, ILogger logger) {
		_rightsWriter = rightsWriter;
		_connectedObjects = connectedObjects;
		_console = console;
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
		if (!TryParseOperations(options.Operations, DefaultOperations, out IReadOnlyCollection<ObjectOperation> operations,
				out string opError)) {
			_logger.WriteError(opError);
			return 1;
		}
		if (!TryParseOperations(options.ConnectedOperations, DefaultConnectedOperations,
				out IReadOnlyCollection<ObjectOperation> connectedOperations, out string connectedError)) {
			_logger.WriteError(connectedError.Replace("Error: ", "Error: --connected-operations: "));
			return 1;
		}

		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		// A revoke fans out to the connected lookups only when the caller names the operations to take away there.
		// The read-only default is a least-privilege default for a GRANT; applied to a revoke it would strip READ
		// from shared dictionaries (Contact, Account, Currency) that other sections of the same audience still need.
		bool explicitConnected = !string.IsNullOrWhiteSpace(options.ConnectedOperations);
		bool fanOut = options.IncludeConnected && (!options.Revoke || explicitConnected);
		if (options.IncludeConnected && !fanOut) {
			_logger.WriteWarning(
				"--include-connected with --revoke leaves the connected objects untouched unless "
				+ "--connected-operations names what to revoke there; only the root object is changed.");
		}

		// Resolve the full target set BEFORE confirming, so the destructive prompt names every object that will
		// be written (with --include-connected the fan-out can span several permission objects).
		ConnectedObjectsResolution resolution = _connectedObjects.Resolve(options.EntitySchemaName, fanOut);
		if (resolution.EnumerationError is not null) {
			// The caller asked for the root AND its lookups. Writing the root alone would report success while the
			// lookups stay unreachable, so nothing is written.
			_logger.WriteError(
				$"Error: could not enumerate the connected objects of '{options.EntitySchemaName}' "
				+ $"({resolution.EnumerationError}). Nothing was changed — re-run, or drop --include-connected "
				+ "to change the root object only.");
			return 1;
		}
		foreach (string excluded in resolution.Excluded) {
			_logger.WriteWarning(
				$"  {excluded}: security/system object — not included in the fan-out. Pass it as "
				+ "--entity-schema-name to change it explicitly.");
		}
		IReadOnlyList<string> objects = resolution.Objects;
		string verb = options.Revoke ? "Revoke" : "Grant";
		string opList = FormatOperations(operations);
		string connectedOpList = FormatOperations(connectedOperations);
		string change = $"{verb} object operations [{opList}] for grantee {grantee} on '{objects[0]}'";
		if (objects.Count > 1) {
			change += $", and [{connectedOpList}] on {objects.Count - 1} connected object(s) "
				+ $"({string.Join(", ", objects.Skip(1))})";
		}
		change += ".";
		if (options.Revoke && options.DisableOperationPermissions) {
			// The operator approves the access WIDENING here, not just the revoke — so the prompt has to say it.
			change += $" If '{objects[0]}' is left with no rights rows, its operation permissions are turned OFF"
				+ " and it becomes available to ALL internal users (connected objects are never turned off).";
		}
		if (!options.Revoke) {
			// The mirror-image transition: granting to an object not administered yet NARROWS it for everyone else.
			change += " An object that does not use operation permissions yet has them turned ON, after which only"
				+ " the listed roles can reach it.";
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
			for (int index = 0; index < objects.Count; index++) {
				string schemaName = objects[index];
				bool isRoot = index == 0;
				IReadOnlyCollection<ObjectOperation> objectOperations = isRoot ? operations : connectedOperations;
				string objectOpList = isRoot ? opList : connectedOpList;
				// Turning operation permissions OFF is only ever approved for the object the caller named: a shared
				// lookup made available to all internal users as a side effect would be a silent widening.
				ObjectRightsChange result = _rightsWriter.SetObjectRights(
					schemaName, grantee, objectOperations, options.Revoke,
					isRoot && options.DisableOperationPermissions, requestOptions);
				if (result.Error != null) {
					anyFailure = true;
					_logger.WriteError($"  {schemaName}: {result.Error}");
				} else if (!result.Found && isRoot) {
					// The object the caller NAMED does not exist (typically a typo): nothing was written, so this is
					// neither applied, a no-op nor cancelled — it must not report success.
					anyFailure = true;
					_logger.WriteError($"  {schemaName}: schema not found — nothing was changed. Check the object name.");
				} else if (!result.Found) {
					_logger.WriteWarning($"  {schemaName}: schema not found (skipped).");
				} else if (result.RefusedLastRowRemoval) {
					// Nothing was written, so this is not a success: the revoke the operator asked for did not happen.
					anyFailure = true;
					_logger.WriteError(
						$"  {schemaName}: grantee {grantee} holds the object's LAST rights row. Removing it would turn "
						+ $"operation permissions OFF and make '{schemaName}' available to ALL internal users. "
						+ (isRoot
							? "Nothing was changed — re-run with --disable-operation-permissions if that is what you want."
							: "Nothing was changed — a connected object is never turned off by a fan-out; name it as "
								+ "--entity-schema-name to do that explicitly."));
				} else if (result.Changed) {
					string transition = result.OperationPermissionsDisabled
						? " Operation permissions are now OFF on this object — it is available to ALL internal users."
						: result.OperationPermissionsEnabled
							? " Operation permissions were turned ON for this object — only the listed roles can reach it now."
							: "";
					_logger.WriteInfo(
						$"  {schemaName}: {(options.Revoke ? "revoked" : "granted")} [{objectOpList}] for grantee {grantee}.{transition}");
				} else {
					_logger.WriteInfo($"  {schemaName}: already in the requested state (no change).");
				}
				if (isRoot && anyFailure && objects.Count > 1) {
					// The root change did not happen, so changing its lookups would leave a half-applied state.
					_logger.WriteWarning(
						$"  The root object was not changed, so the {objects.Count - 1} connected object(s) were not attempted.");
					break;
				}
			}
			return anyFailure ? 1 : 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	// Least-privilege default for the ROOT object: read/create/edit (the access a role needs to work with an
	// object). delete is NOT granted by default — pass it in --operations explicitly. This matches the
	// read/create/edit "has access" check in get-object-rights.
	private static readonly ObjectOperation[] DefaultOperations =
		{ ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit };

	// Least-privilege default for CONNECTED lookup objects: read only. A role picks a lookup value with read;
	// fanning create/edit out to every shared dictionary (status/type lookups, Currency, Contact, Account) would
	// hand an untrusted audience such as the portal write access it never needed.
	private static readonly ObjectOperation[] DefaultConnectedOperations = { ObjectOperation.Read };

	private static string FormatOperations(IEnumerable<ObjectOperation> operations) =>
		string.Join("/", operations.Select(op => op.ToString().ToLowerInvariant()));

	private static bool TryParseOperations(string raw, IReadOnlyCollection<ObjectOperation> defaults,
		out IReadOnlyCollection<ObjectOperation> operations, out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			operations = defaults;
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
		if (parsed.Count == 0) {
			// Only separators (for example ","): an empty set would write an empty row on a grant, and could turn
			// operation permissions ON for an object while granting nothing.
			operations = Array.Empty<ObjectOperation>();
			error = "Error: no operation given. Use read,create,edit,delete.";
			return false;
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
		if (!_console.IsInteractive) {
			_logger.WriteError(
				"Error: set-object-rights is destructive and needs confirmation. Re-run with --confirm to apply "
				+ $"the change: {change}");
			return ConfirmDecision.Refused;
		}
		_logger.WriteWarning($"About to change object permissions: {change}");
		if (!_console.Prompt("Apply this change?")) {
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
