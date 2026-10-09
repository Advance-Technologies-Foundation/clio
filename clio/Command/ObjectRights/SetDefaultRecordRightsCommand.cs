using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using Clio.Common.ObjectRights;
using CommandLine;

namespace Clio.Command.ObjectRights;

/// <summary>
/// Options of <c>set-default-record-rights</c>: change one default record rule and/or the "Use record permissions"
/// switch of one object.
/// </summary>
[Verb("set-default-record-rights", HelpText =
	"Turn record permissions on/off for one object and grant or revoke one of its default record rules (author -> "
	+ "grantee: read/edit/delete) (destructive)")]
public class SetDefaultRecordRightsOptions : RemoteCommandOptions {

	/// <summary>The object (entity schema) whose record layer changes.</summary>
	[Option("entity-schema-name", Required = true, HelpText =
		"Object (entity schema) name whose record permissions are changed. One object per call.")]
	public string EntitySchemaName { get; set; }

	/// <summary>The SysAdminUnit id of the rule's author role or user.</summary>
	[Option("author", Required = false, HelpText =
		"SysAdminUnit id (role or user) of the rule's author: the rule applies to records created by its members. Given "
		+ "together with --grantee and --operations, or not at all (a switch-only call).")]
	public string Author { get; set; }

	/// <summary>The SysAdminUnit id of the rule's grantee role or user.</summary>
	[Option("grantee", Required = false, HelpText =
		"SysAdminUnit id (role or user) that gets the rights on those records. Given together with --author and "
		+ "--operations.")]
	public string Grantee { get; set; }

	/// <summary>Comma-separated operations to grant or revoke. Required for a rule change: none is granted by default.</summary>
	[Option("operations", Required = false, HelpText =
		"Comma-separated operations to grant or revoke: read,edit,delete. Required for a rule change - no operation is "
		+ "granted by default.")]
	public string Operations { get; set; }

	/// <summary>The level a grant sets: granted (default) or delegated.</summary>
	[Option("level", Required = false, HelpText =
		"Level a grant sets for the named operations: granted (default) or delegated (granted with the right to "
		+ "delegate). A grant can lower delegated to granted; the output shows every level before and after.")]
	public string Level { get; set; }

	/// <summary>The rule's "Do not apply for manager" flag; omitted: kept, or false for a new rule.</summary>
	[Option("do-not-apply-for-manager", Required = false, HelpText =
		"true/false: set the rule's 'Do not apply for manager' flag (managers of the grantee role do not inherit the "
		+ "rule). Omitted: an existing rule keeps its flag, a new rule gets false. Grant only.")]
	public bool? DoNotApplyForManager { get; set; }

	/// <summary>Revoke the operations instead of granting them.</summary>
	[Option("revoke", Required = false, HelpText =
		"Set the operations named in --operations to 'not set'. A rule left with no right is removed. Allowed while "
		+ "record permissions are off (it cleans a stored rule before an enable brings it into effect).")]
	public bool Revoke { get; set; }

	/// <summary>Turn the object's record permissions on.</summary>
	[Option("enable-record-permissions", Required = false, HelpText =
		"Turn the object's record permissions ON. Required for a grant on an object whose record permissions are off. "
		+ "With no rule, every user sees only the records they create. On a first enable existing records get no "
		+ "record rights until apply-default-record-rights runs; on a re-enable they get back the rights they had.")]
	public bool EnableRecordPermissions { get; set; }

	/// <summary>Turn the object's record permissions off.</summary>
	[Option("disable-record-permissions", Required = false, HelpText =
		"Turn the object's record permissions OFF: every user with read operation rights reaches every record. Rules "
		+ "and record rights are kept. A call of its own: no --author, --grantee, --operations or --revoke.")]
	public bool DisableRecordPermissions { get; set; }

	/// <summary>Apply the change without a prompt.</summary>
	[Option("confirm", Required = false, HelpText =
		"Confirm the destructive change without a prompt (required in non-interactive runs)")]
	public bool Confirm { get; set; }

	/// <summary>Write nothing; show what the call would change.</summary>
	[Option("preview", Required = false, HelpText =
		"Write nothing: show what the call would change and whether it would be refused")]
	public bool Preview { get; set; }

	/// <summary>
	/// The time the whole call may take, for a caller whose answer is bounded by a deadline (MCP); not a CLI option.
	/// <see langword="null"/>: no limit beyond each request's timeout.
	/// </summary>
	internal TimeSpan? CallBudget { get; set; }
}

/// <summary>
/// <c>set-default-record-rights</c>: changes one default record rule and/or the "Use record permissions" switch of ONE
/// object, like the "Use record permissions" page of the Object permissions designer. The flow is read → plan →
/// policy → confirm → save → read back; the output and the exit code come from the plan and the read-back. Destructive
/// and confirm-gated. It never applies the rules to existing records: that is <c>apply-default-record-rights</c>.
/// </summary>
public class SetDefaultRecordRightsCommand : Command<SetDefaultRecordRightsOptions> {

	private const string CommandName = "set-default-record-rights";

	private readonly IObjectRightsReader _reader;
	private readonly IDefaultRecordRightsWriter _writer;
	private readonly IDefaultRecordRightsPlanner _planner;
	private readonly IGranteeLookup _unitLookup;
	private readonly IObjectRecordCounter _recordCounter;
	private readonly IInteractiveConsole _console;
	private readonly ILogger _logger;

	/// <summary>Creates the command.</summary>
	public SetDefaultRecordRightsCommand(IObjectRightsReader reader, IDefaultRecordRightsWriter writer,
		IDefaultRecordRightsPlanner planner, IGranteeLookup unitLookup, IObjectRecordCounter recordCounter,
		IInteractiveConsole console, ILogger logger) {
		_reader = reader;
		_writer = writer;
		_planner = planner;
		_unitLookup = unitLookup;
		_recordCounter = recordCounter;
		_console = console;
		_logger = logger;
	}

	/// <inheritdoc />
	public override int Execute(SetDefaultRecordRightsOptions options) {
		if (!TryParseInputs(options, out string schemaName, out RuleInput rule)) {
			return 1;
		}
		CreatioRequestOptions requestOptions = ObjectRightsCommandInput.RequestOptions(options, options.CallBudget);
		DefaultRecordRuleChange ruleChange = null;
		if (rule is not null) {
			if (!ObjectRightsCommandInput.TryResolveUnitName(_unitLookup, rule.Author, "author", requestOptions, _logger,
					out string authorName)
				|| !ObjectRightsCommandInput.TryResolveUnitName(_unitLookup, rule.Grantee, "grantee", requestOptions,
					_logger, out string granteeName)) {
				return 1;
			}
			ruleChange = new DefaultRecordRuleChange(rule.Author, authorName, rule.Grantee, granteeName, rule.Operations,
				rule.Level, options.DoNotApplyForManager, options.Revoke);
		}
		ObjectRightsInfo before = _reader.GetObjectRights(schemaName, requestOptions);
		if (!before.IsRead) {
			_logger.WriteError($"Error: '{schemaName}': {before.FailureReason}. Nothing was changed.");
			return 1;
		}
		DefaultRecordRightsChangeRequest request = new(ruleChange, options.EnableRecordPermissions,
			options.DisableRecordPermissions);
		DefaultRecordRightsPlan plan = _planner.Plan(before.RecordState, request);
		Change change = new(schemaName, request, plan);
		if (plan.Refused) {
			_logger.WriteError($"Error: {RefusalMessage(change)} Nothing was changed.");
			return 1;
		}
		if (!plan.Changes) {
			_logger.WriteInfo($"'{schemaName}': {NoChangeReason(change)} (no change).");
			return 0;
		}
		List<string> facts = DescribePlan(change).ToList();
		// The whole planned state, so the operator approves the switch and every rule the save sends, not only the
		// one rule the call names.
		facts.Add($"Planned: record permissions {DefaultRecordRightsFormat.Switch(plan.After)}; rules: "
			+ $"{DefaultRecordRightsFormat.Rules(plan.After.Rules)}.");
		// The count reads the whole table, so it is taken only when someone will see it: in the preview, in the prompt,
		// or in the result of a confirmed call — never for a run that is about to be refused for want of a --confirm.
		// While the switch stays off there is nothing to apply (apply is refused), so neither the count nor the hint.
		bool countShown = options.Preview || options.Confirm || _console.IsInteractive;
		if ((plan.Enables || plan.ChangesRules) && plan.After.AdministratedByRecords && countShown) {
			facts.Add($"'{schemaName}' has {ObjectRightsCommandInput.DescribeRecordCount(_recordCounter, schemaName,
				requestOptions, out _)}. Applying the rules to existing records is a separate, heavy step "
				+ "(apply-default-record-rights): ask the user whether and when to run it.");
		}
		if (options.Preview) {
			_logger.WriteInfo($"PREVIEW — nothing was changed. {change.Summary}");
			WriteFacts(facts);
			return 0;
		}
		switch (ObjectRightsCommandInput.Confirm(options.Confirm, _console, _logger, CommandName, "record permissions",
					change.Summary, facts, "Record-permissions change cancelled.")) {
			case ConfirmDecision.Cancelled:
				return 0;
			case ConfirmDecision.Refused:
				return 1;
		}
		if (!ObjectRightsCommandInput.HasTimeToSave(requestOptions, schemaName, _logger)) {
			return 1;
		}
		ObjectRightsSaveResult save = _writer.Save(before.Snapshot, plan.After, requestOptions);
		// After a failed save the read-back is one attempt as well, so on MCP the answer still arrives within the budget.
		ObjectRightsInfo actual = _reader.GetObjectRights(schemaName,
			save.Succeeded ? requestOptions : requestOptions with { MaxAttempts = 1 });
		return save.Succeeded ? ReportSaved(change, facts, actual) : ReportFailedSave(change, facts, save, actual);
	}

	// The parsed rule arguments of a rule change.
	private sealed record RuleInput(Guid Author, Guid Grantee, IReadOnlyCollection<RecordOperation> Operations,
		RecordRightLevel Level);

	// Everything the output of one call is rendered from.
	private sealed record Change(string SchemaName, DefaultRecordRightsChangeRequest Request,
		DefaultRecordRightsPlan Plan) {

		public DefaultRecordRuleChange Rule => Request.Rule;

		public string Pair => Rule is null
			? null
			: $"{Label(Rule.AuthorName, Rule.Author)} → {Label(Rule.GranteeName, Rule.Grantee)}";

		public string Summary {
			get {
				List<string> parts = new();
				if (Request.EnableRecordPermissions) {
					parts.Add("turn record permissions ON");
				}
				if (Rule is not null) {
					parts.Add(Rule.Revoke
						? $"revoke [{DefaultRecordRightsFormat.Operations(Rule.Operations)}] of rule {Pair}"
						: $"grant [{DefaultRecordRightsFormat.Operations(Rule.Operations)}] "
							+ $"{RecordRightNames.Of(Rule.Level)} by rule {Pair}"
							+ (Rule.DoNotApplyForManager is { } flag ? $", do not apply for manager: {Lower(flag)}" : ""));
				}
				if (Request.DisableRecordPermissions) {
					parts.Add("turn record permissions OFF");
				}
				return $"On '{SchemaName}': {string.Join("; ", parts)}.";
			}
		}

		private static string Label(string name, Guid id) => $"'{ObjectRightsSupport.Display(name)}' ({id})";
	}

	private bool TryParseInputs(SetDefaultRecordRightsOptions options, out string schemaName, out RuleInput rule) {
		rule = null;
		if (!ObjectRightsCommandInput.TryReadSchemaName(options.EntitySchemaName, _logger, out schemaName)) {
			return false;
		}
		if (options.Preview && options.Confirm) {
			_logger.WriteError("Error: --preview writes nothing, so it cannot be combined with --confirm.");
			return false;
		}
		if (options.EnableRecordPermissions && options.DisableRecordPermissions) {
			_logger.WriteError("Error: --enable-record-permissions and --disable-record-permissions contradict each other; "
				+ "pass one of them.");
			return false;
		}
		bool anyRuleArgument = options.Author is not null || options.Grantee is not null || options.Operations is not null;
		if (!anyRuleArgument) {
			return TryParseSwitchOnly(options);
		}
		// A rule is the (author, grantee) pair plus the operations: all three, or the call names no rule at all.
		if (options.Author is null || options.Grantee is null || options.Operations is null) {
			_logger.WriteError("Error: a rule change needs --author, --grantee and --operations together (the rule is the "
				+ "author → grantee pair, and no operation is granted by default). To change only the switch, pass none of "
				+ "them and --enable-record-permissions or --disable-record-permissions.");
			return false;
		}
		if (!TryParseUnit(options.Author, "--author", out Guid author)
			|| !TryParseUnit(options.Grantee, "--grantee", out Guid grantee)) {
			return false;
		}
		if (!TryParseOperations(options.Operations, out IReadOnlyCollection<RecordOperation> operations)) {
			return false;
		}
		// The switch and the rules change separately (as ENG-99741 D9): a disable is a call of its own, and a revoke never
		// changes the switch. Only a grant may carry the enable.
		if (options.DisableRecordPermissions) {
			_logger.WriteError("Error: --disable-record-permissions is a call of its own: pass only --entity-schema-name "
				+ "and the flag. It keeps every rule as it is.");
			return false;
		}
		if (options.Revoke && options.EnableRecordPermissions) {
			_logger.WriteError("Error: a revoke never changes the switch. Turn record permissions on in a call of its own "
				+ "(--enable-record-permissions), or with a grant.");
			return false;
		}
		if (options.Revoke && (options.Level is not null || options.DoNotApplyForManager is not null)) {
			_logger.WriteError("Error: --level and --do-not-apply-for-manager apply to a grant; a revoke sets the named "
				+ "operations to 'not set'.");
			return false;
		}
		RecordRightLevel level = RecordRightLevel.Granted;
		if (options.Level is not null && !RecordRightNames.TryParseGrantLevel(options.Level.Trim(), out level)) {
			_logger.WriteError($"Error: --level: unknown level '{ObjectRightsSupport.Display(options.Level)}'. Use "
				+ $"{RecordRightNames.AcceptedLevels}.");
			return false;
		}
		rule = new RuleInput(author, grantee, operations, level);
		return true;
	}

	private bool TryParseSwitchOnly(SetDefaultRecordRightsOptions options) {
		if (options.Revoke || options.Level is not null || options.DoNotApplyForManager is not null) {
			_logger.WriteError("Error: --revoke, --level and --do-not-apply-for-manager need a rule: pass --author, "
				+ "--grantee and --operations.");
			return false;
		}
		if (!options.EnableRecordPermissions && !options.DisableRecordPermissions) {
			_logger.WriteError("Error: nothing to change: pass a rule (--author, --grantee, --operations) and/or "
				+ "--enable-record-permissions or --disable-record-permissions.");
			return false;
		}
		return true;
	}

	private bool TryParseUnit(string raw, string option, out Guid id) {
		if (Guid.TryParse(raw, out id) && id != Guid.Empty) {
			return true;
		}
		_logger.WriteError($"Error: {option} must be a SysAdminUnit id (GUID).");
		return false;
	}

	private bool TryParseOperations(string raw, out IReadOnlyCollection<RecordOperation> operations) {
		List<RecordOperation> parsed = new();
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
			if (!RecordRightNames.TryParseOperation(token, out RecordOperation operation)) {
				operations = Array.Empty<RecordOperation>();
				_logger.WriteError($"Error: --operations: unknown operation '{ObjectRightsSupport.Display(token)}'. Use "
					+ $"{RecordRightNames.AcceptedOperations}.");
				return false;
			}
			parsed.Add(operation);
		}
		if (parsed.Count == 0) {
			// Given but empty ("", " ", ","): the value the approval shows names no operation.
			operations = Array.Empty<RecordOperation>();
			_logger.WriteError($"Error: --operations: no operation given. Use {RecordRightNames.AcceptedOperations}.");
			return false;
		}
		operations = parsed.Distinct().OrderBy(operation => operation).ToArray();
		return true;
	}

	private static string RefusalMessage(Change change) {
		string schema = change.SchemaName;
		DefaultRecordRightsPlan plan = change.Plan;
		return plan.Refusal switch {
			DefaultRecordRightsRefusal.EnableNotRequested =>
				$"record permissions on '{schema}' are OFF, so this rule would be stored but give nobody anything. Re-run "
				+ "with --enable-record-permissions to turn them ON"
				+ (plan.StoredRulesComingIntoEffect.Count > 0
					? $"; these stored rules would then come into effect too: "
						+ $"{DefaultRecordRightsFormat.Rules(plan.StoredRulesComingIntoEffect)}"
					: "")
				+ ".",
			DefaultRecordRightsRefusal.DuplicatePairs =>
				$"the stored rules of '{schema}' have more than one rule for one author → grantee pair "
				+ $"({DefaultRecordRightsFormat.Rules(plan.ProblemRules)}). The save replaces the whole rule list and the "
				+ "server keeps only the last rule of a pair, so the tool does not save it. Remove the duplicates in the "
				+ "Object permissions designer, then re-run.",
			DefaultRecordRightsRefusal.InvalidStoredLevel =>
				$"stored rules of '{schema}' have a level that is not 'not set', 'granted' or 'delegated' "
				+ $"({DefaultRecordRightsFormat.Rules(plan.ProblemRules)}). The save would send them back as they are, so "
				+ "the tool does not save. Fix them in the Object permissions designer, then re-run.",
			_ => $"the change on '{schema}' is refused ({plan.Refusal})."
		};
	}

	private static string NoChangeReason(Change change) {
		List<string> reasons = new();
		DefaultRecordRightsChangeRequest request = change.Request;
		DefaultRecordRightsPlan plan = change.Plan;
		if (request.EnableRecordPermissions || request.DisableRecordPermissions) {
			reasons.Add($"record permissions are already {DefaultRecordRightsFormat.Switch(plan.Before)}");
		}
		if (change.Rule is not null) {
			reasons.Add(plan.RuleBefore is null
				? $"there is no rule {change.Pair}, so there is nothing to revoke"
				: $"the rule {change.Pair} already has {DefaultRecordRightsFormat.Levels(plan.RuleBefore)}");
		}
		return string.Join(", and ", reasons);
	}

	// The facts the operator approves: what turns on or off, how the rule changes, and when the change takes effect.
	// Facts only — who can reach which record is not computed.
	private static IEnumerable<string> DescribePlan(Change change) {
		DefaultRecordRightsPlan plan = change.Plan;
		string schema = change.SchemaName;
		if (plan.Enables) {
			yield return $"Record permissions on '{schema}' are turned ON: from then on a record is reachable only through "
				+ "its record rights.";
			yield return plan.StoredRulesComingIntoEffect.Count == 0
				? $"'{schema}' has NO default record rules, so the built-in default applies: every user sees only the "
					+ "records they create (and their managers and holders of 'view any data' see them too)."
				: "Rules that apply to records created from now on: "
					+ $"{DefaultRecordRightsFormat.Rules(plan.StoredRulesComingIntoEffect)}.";
			// The platform keeps the records' rights while the switch is off, so a re-enable brings them back; only a
			// record that never got rights (a first enable, or one created while off) has none. Which case it is is not
			// read: the sentence states both.
			yield return "Existing records keep the record rights they have: a record that had record rights before "
				+ "record permissions were turned off gets them back. A record created while they were off — and every "
				+ "record on a first enable — has none until apply-default-record-rights runs for "
				+ $"'{schema}': it is reachable only by its owner/author, their managers and holders of 'view any data'.";
		}
		if (change.Rule is not null) {
			yield return DescribeRule(change);
			if (plan.ChangesRules && !plan.After.AdministratedByRecords) {
				yield return $"Record permissions on '{schema}' are OFF, so the stored rule changes but access does not "
					+ "change now; the rules come into effect when record permissions are turned on.";
			} else if (plan.ChangesRules && !plan.Enables) {
				yield return "The change applies to records created from now on; existing records keep their record rights "
					+ "until apply-default-record-rights runs.";
			}
		}
		if (plan.Disables) {
			yield return $"Record permissions on '{schema}' are turned OFF: every user with read operation rights reaches "
				+ "every record. The rules and the records' rights are kept and come back into effect when record "
				+ "permissions are turned on again; a record created while they are off gets no record rights until "
				+ "apply-default-record-rights runs.";
		}
	}

	private static string DescribeRule(Change change) {
		DefaultRecordRule before = change.Plan.RuleBefore;
		DefaultRecordRule after = change.Plan.RuleAfter;
		if (before is null && after is null) {
			return $"There is no rule {change.Pair}; no rule changes.";
		}
		if (before is null) {
			return $"A rule is added: {change.Pair}: {DefaultRecordRightsFormat.Levels(after)}.";
		}
		if (after is null) {
			return $"The rule {change.Pair} is removed: it has no right left.";
		}
		if (before.SameRuleAs(after)) {
			return $"The rule {change.Pair} is unchanged ({DefaultRecordRightsFormat.Levels(before)}).";
		}
		IEnumerable<string> levels = RecordRightNames.AllOperations
			.Where(operation => before.LevelOf(operation) != after.LevelOf(operation))
			.Select(operation => $"{RecordRightNames.Of(operation)} {RecordRightNames.Of(before.LevelOf(operation))} → "
				+ RecordRightNames.Of(after.LevelOf(operation)));
		if (before.DoNotApplyForManager != after.DoNotApplyForManager) {
			levels = levels.Append($"do not apply for manager {Lower(before.DoNotApplyForManager)} → "
				+ Lower(after.DoNotApplyForManager));
		}
		return $"The rule {change.Pair} changes: {string.Join(", ", levels)}. It now has "
			+ $"{DefaultRecordRightsFormat.Levels(after)}.";
	}

	// The save succeeded; the read-back is compared with the plan — the switch and EVERY rule — so a change that did
	// not land, or a rule the save lost, is never reported as done.
	private int ReportSaved(Change change, IReadOnlyList<string> facts, ObjectRightsInfo actual) {
		string schema = change.SchemaName;
		if (!actual.IsRead) {
			_logger.WriteError($"Error: '{schema}': saved, but NOT verified — on the read-back {actual.FailureReason}. "
				+ "Check it with get-object-rights before retrying: the plan was:");
			WriteFacts(facts);
			return 1;
		}
		IReadOnlyList<string> differences = change.Plan.After.Diff(actual.RecordState);
		if (differences.Count > 0) {
			_logger.WriteError($"Error: '{schema}': saved, but NOT verified — the object read back does not match the "
				+ $"plan: {string.Join("; ", differences)}. Check it with get-object-rights.");
			return 1;
		}
		ReportDone(change, facts, actual.RecordState);
		return 0;
	}

	// A save that reported an error may still have been committed, so the object is read back before the call is
	// reported as failed; a save that got no answer can even land AFTER the read-back.
	private int ReportFailedSave(Change change, IReadOnlyList<string> facts, ObjectRightsSaveResult save,
		ObjectRightsInfo actual) {
		string schema = change.SchemaName;
		string failure = save.OutcomeUnknown
			? $"the save got no answer ({save.Error}) and may still be applied"
			: $"the save failed ({save.Error})";
		string recheck = save.OutcomeUnknown
			? " Re-read the object with get-object-rights before retrying or reporting a failure."
			: " Read the object with get-object-rights before retrying.";
		if (!actual.IsRead) {
			_logger.WriteError($"Error: '{schema}': {failure}, and on the read-back {actual.FailureReason}.{recheck}");
			return 1;
		}
		if (change.Plan.After.Diff(actual.RecordState).Count > 0) {
			_logger.WriteError($"Error: '{schema}': {failure}. The object now: record permissions "
				+ $"{DefaultRecordRightsFormat.Switch(actual.RecordState)}; rules "
				+ $"{DefaultRecordRightsFormat.Rules(actual.RecordState.Rules)}.{(save.OutcomeUnknown ? recheck : "")}");
			return 1;
		}
		_logger.WriteWarning($"'{schema}': the save reported an error ({save.Error}), but the object read back matches "
			+ "the plan.");
		ReportDone(change, facts, actual.RecordState);
		return 0;
	}

	private void ReportDone(Change change, IReadOnlyList<string> facts, DefaultRecordRightsState state) {
		_logger.WriteInfo($"Done. {change.Summary}");
		WriteFacts(facts);
		_logger.WriteInfo($"  Record permissions now: {DefaultRecordRightsFormat.Switch(state)}; rules: "
			+ $"{DefaultRecordRightsFormat.Rules(state.Rules)}.");
	}

	private void WriteFacts(IEnumerable<string> facts) {
		foreach (string fact in facts) {
			_logger.WriteInfo($"  {fact}");
		}
	}

	private static string Lower(bool value) => value ? "true" : "false";
}
