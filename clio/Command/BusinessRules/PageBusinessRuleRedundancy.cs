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
/// Disabled rules never run and are ignored; an incoming rule that omits <c>enabled</c> keeps its stored value.
/// A rule of the batch replaces its stored copy (matched by name). The check is advisory: it never rejects a rule.
/// </remarks>
internal static class PageBusinessRuleRedundancy {

	internal const string RuleSubsumed = "subsumed";
	internal const string RuleInverseRedundant = "inverse-redundant";

	private const string And = "AND";
	private const string Or = "OR";

	private static readonly IReadOnlyDictionary<string, string> OppositeStateActions = BuildOppositeStateActions(
		(new MakeReadOnlyBusinessRuleAction().ActionType, new MakeEditableBusinessRuleAction().ActionType),
		(new MakeRequiredBusinessRuleAction().ActionType, new MakeOptionalBusinessRuleAction().ActionType),
		(new HideElementBusinessRuleAction().ActionType, new ShowElementBusinessRuleAction().ActionType));

	// Pairs every comparison with its negation; the names are the keys of SupportedComparisonTypeValues.
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
	/// gets no warnings and is not compared.</param>
	public static IReadOnlyList<IReadOnlyList<string>> Analyze(IReadOnlyList<BusinessRule> stored,
		IReadOnlyList<BusinessRule?> incoming) {
		Dictionary<string, BusinessRule> storedByName = stored
			.Where(rule => !string.IsNullOrWhiteSpace(rule?.Name))
			.GroupBy(rule => rule.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
		var incomingNames = new HashSet<string>(incoming
			.Where(rule => !string.IsNullOrWhiteSpace(rule?.Name))
			.Select(rule => rule!.Name!.Trim()), StringComparer.OrdinalIgnoreCase);
		List<RuleShape> others = stored
			.Where(rule => rule is not null
				&& (string.IsNullOrWhiteSpace(rule.Name) || !incomingNames.Contains(rule.Name.Trim())))
			.Select(RuleShape.TryCreate)
			.OfType<RuleShape>()
			.ToList();
		List<RuleShape?> batch = incoming
			.Select(rule => rule is null ? null : RuleShape.TryCreate(WithStoredEnabled(rule, storedByName)))
			.ToList();
		var warnings = new List<IReadOnlyList<string>>(incoming.Count);
		for (int index = 0; index < batch.Count; index++) {
			if (batch[index] is not { } shape) {
				warnings.Add([]);
				continue;
			}
			int current = index;
			IEnumerable<RuleShape> otherBatchRules = batch.Where((_, otherIndex) => otherIndex != current).OfType<RuleShape>();
			IEnumerable<RuleShape> earlierBatchRules = batch.Take(current).OfType<RuleShape>();
			warnings.Add(FindWarnings(shape, others.Concat(otherBatchRules), others.Concat(earlierBatchRules)));
		}
		return warnings;
	}

	private static BusinessRule WithStoredEnabled(BusinessRule rule, IReadOnlyDictionary<string, BusinessRule> storedByName) =>
		rule.Enabled is null && !string.IsNullOrWhiteSpace(rule.Name)
			&& storedByName.TryGetValue(rule.Name.Trim(), out BusinessRule? storedCopy)
			? rule with { Enabled = storedCopy.Enabled }
			: rule;

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
		if (!CoversActions(rule, other, otherType => otherType)) {
			return false;
		}
		// The one-condition side takes the other side's operation: "A" is narrower than "A OR B" too.
		string operation = rule.Conditions.Count == 1 ? other.Operation : rule.Operation;
		if (other.Conditions.Count > 1 && other.Operation != operation) {
			return false;
		}
		return operation == And
			? other.Conditions.IsProperSubsetOf(rule.Conditions)
			: rule.Conditions.IsProperSubsetOf(other.Conditions);
	}

	private static bool IsInverseOf(RuleShape rule, RuleShape other) {
		if (!CoversActions(rule, other, otherType => OppositeStateActions[otherType])
			|| !TryNegate(other, out string negatedOperation, out HashSet<ConditionKey> negatedConditions)) {
			return false;
		}
		return rule.Operation == negatedOperation && rule.Conditions.SetEquals(negatedConditions);
	}

	/// <summary>
	/// Whether every action of <paramref name="rule"/> matches an action of <paramref name="other"/> (its type
	/// mapped by <paramref name="mapOtherType"/>) on the same or more items.
	/// </summary>
	private static bool CoversActions(RuleShape rule, RuleShape other, Func<string, string> mapOtherType) =>
		rule.Actions.All(action => other.Actions.Any(otherAction =>
			mapOtherType(otherAction.Type) == action.Type && action.Items.IsSubsetOf(otherAction.Items)));

	private static bool TryNegate(RuleShape rule, out string operation, out HashSet<ConditionKey> conditions) {
		conditions = [];
		foreach (ConditionKey condition in rule.Conditions) {
			if (!NegatedComparisons.TryGetValue(condition.Comparison, out string? negated)) {
				operation = string.Empty;
				return false;
			}
			conditions.Add(condition with { Comparison = negated });
		}
		operation = NormalizeOperation(rule.Operation == And ? Or : And, conditions.Count);
		return true;
	}

	// A one-condition group reads the same under AND and OR, so it is always stored as AND.
	private static string NormalizeOperation(string operation, int conditionCount) =>
		conditionCount == 1 ? And : operation;

	private static IReadOnlyDictionary<string, string> BuildOppositeStateActions(
		params (string First, string Second)[] pairs) {
		var map = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach ((string first, string second) in pairs) {
			map[first] = second;
			map[second] = first;
		}
		return map;
	}

	private sealed record ActionShape(string Type, HashSet<string> Items);

	private sealed record ConditionKey(string Left, string Comparison, string Right);

	private sealed record RuleShape(string Label, string Operation, HashSet<ConditionKey> Conditions,
		IReadOnlyList<ActionShape> Actions) {

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
			HashSet<ConditionKey> keys = conditions
				.OfType<BusinessRuleCondition>()
				.Select(condition => new ConditionKey(Describe(condition.LeftExpression),
					(condition.ComparisonType ?? string.Empty).Trim().ToLowerInvariant(),
					Describe(condition.RightExpression)))
				.ToHashSet();
			string label = string.IsNullOrWhiteSpace(rule.Caption) ? rule.Name ?? string.Empty : rule.Caption.Trim();
			return new RuleShape(label, NormalizeOperation(operation, keys.Count), keys, actionShapes);
		}

		private static string Describe(BusinessRuleExpression? expression) =>
			expression is null
				? string.Empty
				: string.Join("\u001f", expression.Type, expression.Path, expression.Expression, expression.SysValueName,
					expression.SysSettingName,
					expression.Value is JsonElement value ? value.GetRawText() : string.Empty);
	}
}
