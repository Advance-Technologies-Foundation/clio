using System;
using Clio.Common;
using Clio.Common.RecordRights;
using CommandLine;

namespace Clio.Command.RecordRights;

[Verb("set-record-rights", HelpText = "Grant or revoke a record-level access right on a single Creatio record (destructive)")]
public class SetRecordRightsOptions : RemoteCommandOptions {

	[Option("entity", Required = true, HelpText = "Entity schema name of the target record (use with --record-id)")]
	public string Entity { get; set; }

	[Option("record-id", Required = true, HelpText = "Primary column value (record id) of the target record (use with --entity)")]
	public string RecordId { get; set; }

	[Option("grantee", Required = true, HelpText = "Grantee SysAdminUnit GUID")]
	public string Grantee { get; set; }

	[Option("operation", Required = true, HelpText = "Operation to grant or revoke: read | edit | delete")]
	public string Operation { get; set; }

	[Option("level", Required = false, Default = "granted", HelpText = "Right level for a grant: granted (default) | delegated")]
	public string Level { get; set; }

	[Option("revoke", Required = false, HelpText = "Revoke (remove) the right instead of granting it")]
	public bool Revoke { get; set; }

	[Option("confirm", Required = false, HelpText = "Confirm the destructive apply without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }
}

public class SetRecordRightsCommand : Command<SetRecordRightsOptions> {

	private readonly ICreatioRightsClient _rightsClient;
	private readonly ILogger _logger;

	public SetRecordRightsCommand(ICreatioRightsClient rightsClient, ILogger logger) {
		_rightsClient = rightsClient;
		_logger = logger;
	}

	public override int Execute(SetRecordRightsOptions options) {
		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		int operation;
		// Level is only used for a grant, so it is parsed only in the grant branch below.
		int level = RecordRightsConverter.LevelGranted;
		try {
			operation = RecordRightsConverter.ParseOperation(options.Operation);
			if (!options.Revoke) {
				level = RecordRightsConverter.ParseLevel(options.Level);
			}
		}
		catch (ArgumentException ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}

		if (!Guid.TryParse(options.Grantee, out _)) {
			_logger.WriteError("Error: --grantee must be a SysAdminUnit GUID.");
			return 1;
		}

		// The server ignores SysAdminUnitType/displayValue/Id-for-new; sent as Guid.Empty to avoid
		// null->Guid deserialization issues; the ApplyChanges response is not live-verified.
		RecordRightRow row = options.Revoke
			? new RecordRightRow {
				Id = Guid.NewGuid().ToString(),
				Operation = operation,
				RightLevel = -1,
				SysAdminUnit = new SysAdminUnitRef { Value = options.Grantee },
				SysAdminUnitType = Guid.Empty.ToString(),
				Position = -1,
				IsNew = false,
				IsDeleted = true
			}
			: new RecordRightRow {
				Id = Guid.NewGuid().ToString(),
				Operation = operation,
				RightLevel = level,
				SysAdminUnit = new SysAdminUnitRef { Value = options.Grantee },
				SysAdminUnitType = Guid.Empty.ToString(),
				Position = -1,
				IsNew = true,
				IsDeleted = false
			};

		string change = options.Revoke
			? $"Revoke '{RecordRightsConverter.OperationToName(operation)}' for {options.Grantee} on {options.Entity} '{options.RecordId}'."
			: $"Grant '{RecordRightsConverter.OperationToName(operation)}' at '{RecordRightsConverter.LevelToName(level)}' for {options.Grantee} on {options.Entity} '{options.RecordId}'.";

		ConfirmDecision decision = ConfirmApply(options, change);
		if (decision == ConfirmDecision.Cancelled) {
			return 0;
		}
		if (decision == ConfirmDecision.Refused) {
			return 1;
		}

		try {
			_rightsClient.ApplyChanges(
				new ApplyChangesRecordRef { EntitySchemaName = options.Entity, PrimaryColumnValue = options.RecordId },
				new[] { row }, requestOptions);
			_logger.WriteInfo(options.Revoke
				? $"Revoked '{RecordRightsConverter.OperationToName(operation)}' for {options.Grantee} on {options.Entity} '{options.RecordId}'."
				: $"Granted '{RecordRightsConverter.OperationToName(operation)}' at '{RecordRightsConverter.LevelToName(level)}' for {options.Grantee} on {options.Entity} '{options.RecordId}'.");
			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
	}

	// Destructive gate, mirroring other destructive clio commands: --confirm applies without a prompt; without
	// it an interactive run asks y/n, and a non-interactive run refuses (rather than silently applying).
	private ConfirmDecision ConfirmApply(SetRecordRightsOptions options, string change) {
		if (options.Confirm) {
			return ConfirmDecision.Approved;
		}
		if (Console.IsInputRedirected) {
			_logger.WriteError(
				"Error: set-record-rights is destructive and needs confirmation. Re-run with --confirm to apply "
				+ $"the change: {change}");
			return ConfirmDecision.Refused;
		}
		_logger.WriteWarning($"About to apply a record-rights change: {change}");
		_logger.WriteInfo("Apply this change? (y/n)");
		string answer = Console.ReadLine();
		if (string.IsNullOrWhiteSpace(answer) || !answer.StartsWith("y", StringComparison.CurrentCultureIgnoreCase)) {
			_logger.WriteInfo("Record-rights change cancelled.");
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
