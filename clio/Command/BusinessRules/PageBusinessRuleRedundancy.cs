using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Clio.Command.BusinessRules;

/// <summary>
/// ENG-107305: finds page business rules that add nothing to the rules already on the page, so
/// <c>create-page-business-rules</c> and <c>update-page-business-rules</c> can warn about them at save time.
/// </summary>
/// <remarks>
/// Two shapes are reported, both only for state actions (make read-only / editable, required / optional,
/// hide / show), never for <c>set-values</c> or filters:
/// <list type="bullet">
/// <item><b>subsumed</b>: another rule applies the same action to the same or more items on a strictly wider
/// condition (AND: a strict subset of the conditions; OR: a strict superset), so this one never changes the
/// outcome. AND and OR groups are not compared with each other.</item>
/// <item><b>inverse-redundant</b>: the condition is the exact negation of another rule's condition (De Morgan
/// for groups) and the action is the opposite state on the same items. A page state rule undoes itself when its
/// condition turns false, so the inverse rule only restores what the platform restores anyway. Not verified for
/// an element that is read-only, required or hidden by default; the warning says so.</item>
/// </list>
/// Disabled rules never run and are ignored. A rule of the batch replaces its stored copy (matched by name).
/// The check is advisory: it never rejects a rule.
/// </remarks>
internal static class PageBusinessRuleRedundancy {

	internal const string RuleSubsumed = "subsumed";
	internal const string RuleInverseRedundant = "inverse-redundant";

	private const string And = "AND";
	private const string Or = "OR";

	private static readonly IReadOnlyDictionary<string, string> OppositeStateActions =
		new Dictionary<string, string>(StringComparer.Ordinal) {
			["make-read-only"] = "make-editable",
			["make-editable"] = "make-read-only",
			["make-required"] = "make-optional",
			["make-optional"] = "make-required",
			["hide-element"] = "show-element",
			["show-element"] = "hide-element"
		};

	private static readonly IReadOnlyDictionary<string, string> NegatedComparisons =
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			["equal"] = "not-equal",
			["not-equal"] = "equal",
			["is-filled-in"] = "is-not-filled-in",
			["is-not-filled-in"] = "is-filled-in",
			["greater-than"] = "less-than-or-equal",
			["less-than-or-equal"] = "greater-than",
			["less-than"] = "greater-than-or-equal",
			["greater-than-or-equal"] = "less-than",
			["contain"] = "not-contain",
			["not-contain"] = "contain"
		};

	/// <summary>
	/// Returns the warnings for each incoming rule, in input order (an empty list when it is not redundant).
	/// </summary>
	/// <param name="stored">The rules already saved on the page.</param>
	/// <param name="incoming">The rules being created or updated, in batch order; a <see langword="null"/> entry
	/// gets no warnings.</param>
	public static IReadOnlyList<IReadOnlyList<string>> Analyze(IReadOnlyList<BusinessRule> stored,
		IReadOnlyList<BusinessRule?> incoming) {
		var incomingNames = new HashSet<string>(incoming
			.Where(rule => !string.IsNullOrWhiteSpace(rule?.Name))
			.Select(rule => rule!.Name!.Trim()), StringComparer.OrdinalIgnoreCase);
		List<RuleShape> others = (stored ?? [])
			.Where(rule => rule is not null
				&& (string.IsNullOrWhiteSpace(rule.Name) || !incomingNames.Contains(rule.Name.Trim())))
			.Select(RuleShape.TryCreate)
			.Where(shape => shape is not null)
			.ToList()!;
		List<RuleShape?> batch = incoming.Select(rule => rule is null ? null : RuleShape.TryCreate(rule)).ToList();
		var warnings = new List<IReadOnlyList<string>>(incoming.Count);
		for (int index = 0; index < batch.Count; index++) {
			RuleShape? shape = batch[index];
			if (shape is null) {
				warnings.Add([]);
				continue;
			}
			IEnumerable<RuleShape> otherBatchRules = batch
				.Select((other, otherIndex) => (other, otherIndex))
				.Where(pair => pair.other is not null && pair.otherIndex != index)
				.Select(pair => pair.other!);
			IEnumerable<RuleShape> earlierBatchRules = batch.Take(index).Where(other => other is not null)!;
			warnings.Add(FindWarnings(shape, others.Concat(otherBatchRules), others.Concat(earlierBatchRules!)));
		}
		return warnings;
	}

	private static List<string> FindWarnings(RuleShape rule, IEnumerable<RuleShape> subsumptionCandidates,
		IEnumerable<RuleShape> inverseCandidates) {
		var found = new List<string>();
		RuleShape? wider = subsumptionCandidates.FirstOrDefault(other => IsSubsumedBy(rule, other));
		if (wider is not null) {
			found.Add($"{RuleSubsumed} — rule '{rule.Label}' is subsumed by '{wider.Label}': that rule applies the same "
				+ "action to the same elements on a wider condition, so this rule never changes the page. Delete it, "
				+ "or merge its conditions into the wider rule.");
		}
		RuleShape? inverse = inverseCandidates.FirstOrDefault(other => IsInverseOf(rule, other));
		if (inverse is not null) {
			found.Add($"{RuleInverseRedundant} — rule '{rule.Label}' only undoes '{inverse.Label}': its condition is the "
				+ "negation of that rule's condition and its action is the opposite state. A page state rule undoes "
				+ "itself when its condition turns false, so this rule is not needed. Keep it only when the element is "
				+ "read-only, required or hidden by default (that case is not verified).");
		}
		return found;
	}

	private static bool IsSubsumedBy(RuleShape rule, RuleShape other) {
		if (!rule.Actions.All(action => other.Actions.Any(otherAction =>
				otherAction.Type == action.Type && action.Items.IsSubsetOf(otherAction.Items)))) {
			return false;
		}
		string operation = CommonOperation(rule, other);
		return operation switch {
			And => other.Conditions.IsProperSubsetOf(rule.Conditions),
			Or => rule.Conditions.IsProperSubsetOf(other.Conditions),
			_ => false
		};
	}

	private static bool IsInverseOf(RuleShape rule, RuleShape other) {
		if (!rule.Actions.All(action => other.Actions.Any(otherAction =>
				OppositeStateActions.TryGetValue(otherAction.Type, out string? opposite)
				&& opposite == action.Type
				&& action.Items.IsSubsetOf(otherAction.Items)))) {
			return false;
		}
		if (!TryNegate(other, out string negatedOperation, out HashSet<string> negatedConditions)) {
			return false;
		}
		bool operationMatches = rule.Conditions.Count == 1 || rule.Operation == negatedOperation;
		return operationMatches && rule.Conditions.SetEquals(negatedConditions);
	}

	private static string CommonOperation(RuleShape rule, RuleShape other) {
		// A one-condition group reads the same under AND and OR, so it takes the other rule's operation.
		string ruleOperation = rule.Conditions.Count == 1 ? other.Operation : rule.Operation;
		string otherOperation = other.Conditions.Count == 1 ? ruleOperation : other.Operation;
		return ruleOperation == otherOperation ? ruleOperation : string.Empty;
	}

	private static bool TryNegate(RuleShape rule, out string operation, out HashSet<string> conditions) {
		operation = rule.Operation == And ? Or : And;
		conditions = new HashSet<string>(StringComparer.Ordinal);
		foreach (ConditionKey condition in rule.ConditionKeys) {
			if (!NegatedComparisons.TryGetValue(condition.Comparison, out string? negated)) {
				return false;
			}
			conditions.Add(condition with { Comparison = negated });
		}
		return true;
	}

	private sealed record ActionShape(string Type, HashSet<string> Items);

	private sealed record ConditionKey(string Left, string Comparison, string Right) {
		public static implicit operator string(ConditionKey key) => $"{key.Left}|{key.Comparison}|{key.Right}";
	}

	private sealed record RuleShape(string Label, string Operation, IReadOnlyList<ConditionKey> ConditionKeys,
		HashSet<string> Conditions, IReadOnlyList<ActionShape> Actions) {

		public static RuleShape? TryCreate(BusinessRule rule) {
			if (rule.Enabled == false || rule.Condition?.Conditions is not { Count: > 0 } conditions
				|| rule.Actions is not { Count: > 0 } actions) {
				return null;
			}
			string operation = (rule.Condition.LogicalOperation ?? string.Empty).Trim().ToUpperInvariant();
			if (operation is not (And or Or)) {
				return null;
			}
			List<ActionShape> actionShapes = [];
			foreach (BusinessRuleAction action in actions) {
				if (action is not FieldSelectionBusinessRuleAction fieldAction
					|| !OppositeStateActions.ContainsKey(action.ActionType)) {
					return null;
				}
				actionShapes.Add(new ActionShape(action.ActionType,
					new HashSet<string>(fieldAction.FieldSelectionItems, StringComparer.Ordinal)));
			}
			List<ConditionKey> keys = conditions
				.Where(condition => condition is not null)
				.Select(condition => new ConditionKey(Describe(condition.LeftExpression),
					(condition.ComparisonType ?? string.Empty).Trim().ToLowerInvariant(),
					Describe(condition.RightExpression)))
				.ToList();
			string label = string.IsNullOrWhiteSpace(rule.Caption) ? rule.Name ?? string.Empty : rule.Caption.Trim();
			return new RuleShape(label, operation, keys,
				new HashSet<string>(keys.Select(key => (string)key), StringComparer.Ordinal), actionShapes);
		}

		private static string Describe(BusinessRuleExpression? expression) =>
			expression is null
				? string.Empty
				: string.Join("\u001f", expression.Type, expression.Path, expression.Expression, expression.SysValueName,
					expression.SysSettingName,
					expression.Value is JsonElement value ? value.GetRawText() : string.Empty);
	}
}
