using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command;
using Clio.Command.EntitySchemaDesigner;
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
		"Comma-separated operations: read,create,edit,delete. Default: all four.")]
	public string Operations { get; set; }

	[Option("revoke", Required = false, HelpText =
		"Revoke the operations instead of granting. A role left with no operations is removed.")]
	public bool Revoke { get; set; }

	[Option("include-connected", Required = false, HelpText =
		"Also apply to every object referenced by the root object's own lookup columns (portal-section convenience)")]
	public bool IncludeConnected { get; set; }

	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }
}

public class SetObjectRightsCommand : Command<SetObjectRightsOptions> {

	private readonly IObjectRightsWriter _rightsWriter;
	private readonly IRemoteEntitySchemaColumnManager _columnManager;
	private readonly ILogger _logger;

	public SetObjectRightsCommand(IObjectRightsWriter rightsWriter,
		IRemoteEntitySchemaColumnManager columnManager, ILogger logger) {
		_rightsWriter = rightsWriter;
		_columnManager = columnManager;
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

		string verb = options.Revoke ? "Revoke" : "Grant";
		string opList = string.Join("/", operations.Select(op => op.ToString().ToLowerInvariant()));
		string scope = options.IncludeConnected ? " and its connected objects" : "";
		string change = $"{verb} object operations [{opList}] for grantee {grantee} on '{options.EntitySchemaName}'{scope}.";

		ConfirmDecision decision = ConfirmApply(options, change);
		if (decision == ConfirmDecision.Cancelled) {
			return 0;
		}
		if (decision == ConfirmDecision.Refused) {
			return 1;
		}

		try {
			IReadOnlyList<string> objects = ResolveObjectsToChange(options.EntitySchemaName, options.IncludeConnected);
			bool anyFailure = false;
			foreach (string schemaName in objects) {
				ObjectRightsChange result = _rightsWriter.SetObjectRights(
					schemaName, grantee, operations, options.Revoke, requestOptions);
				if (result.Error != null) {
					anyFailure = true;
					_logger.WriteError($"  {schemaName}: {result.Error}");
				} else if (!result.Found) {
					_logger.WriteWarning($"  {schemaName}: schema not found (skipped).");
				} else if (result.Changed) {
					_logger.WriteInfo($"  {schemaName}: {(options.Revoke ? "revoked" : "granted")} [{opList}] for grantee {grantee}.");
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

	// The root object, plus (when requested) every distinct object referenced by one of its OWN lookup columns.
	// Connected-object enumeration is best-effort: if the schema read fails, only the root is changed.
	private IReadOnlyList<string> ResolveObjectsToChange(string rootSchemaName, bool includeConnected) {
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
				$"Could not enumerate connected objects of '{rootSchemaName}': {ex.Message}. Changing the root object only.");
		}
		return objects;
	}

	private static bool TryParseOperations(string raw, out IReadOnlyCollection<ObjectOperation> operations,
		out string error) {
		error = null;
		if (string.IsNullOrWhiteSpace(raw)) {
			operations = new[] { ObjectOperation.Read, ObjectOperation.Create, ObjectOperation.Edit, ObjectOperation.Delete };
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
