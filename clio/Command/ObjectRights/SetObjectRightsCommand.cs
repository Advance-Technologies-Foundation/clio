using System;
using Clio.Common;
using CommandLine;

namespace Clio.Command.ObjectRights;

[Verb("set-object-rights", HelpText = "Grant operation and record permissions to an object and its connected (lookup) entities (destructive)")]
public class SetObjectRightsOptions : RemoteCommandOptions {

	[Option("entity-schema-name", Required = true, HelpText = "Root object (entity schema) name whose access is granted; connected lookup objects are resolved server-side")]
	public string EntitySchemaName { get; set; }

	[Option("confirm", Required = false, HelpText = "Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }
}

public class SetObjectRightsCommand : Command<SetObjectRightsOptions> {

	private readonly ISectionServiceClient _sectionServiceClient;
	private readonly ILogger _logger;

	public SetObjectRightsCommand(ISectionServiceClient sectionServiceClient, ILogger logger) {
		_sectionServiceClient = sectionServiceClient;
		_logger = logger;
	}

	public override int Execute(SetObjectRightsOptions options) {
		if (string.IsNullOrWhiteSpace(options.EntitySchemaName)) {
			_logger.WriteError("Error: --entity-schema-name is required.");
			return 1;
		}

		CreatioRequestOptions requestOptions = new() {
			TimeOut = options.TimeOut, MaxAttempts = options.MaxAttempts, RetryDelay = options.RetryDelay
		};

		string change = $"Grant operation and record permissions for '{options.EntitySchemaName}' and its connected objects.";

		ConfirmDecision decision = ConfirmApply(options, change);
		if (decision == ConfirmDecision.Cancelled) {
			return 0;
		}
		if (decision == ConfirmDecision.Refused) {
			return 1;
		}

		try {
			_sectionServiceClient.SetConnectedEntitiesAdministratedByEntity(options.EntitySchemaName, requestOptions);
			_logger.WriteInfo(
				$"Granted operation and record permissions for '{options.EntitySchemaName}' and its connected objects.");
			return 0;
		}
		catch (Exception ex) {
			_logger.WriteError($"Error: {ex.Message}");
			return 1;
		}
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
