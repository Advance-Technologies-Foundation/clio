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
/// for groups) and the action is the opposite state on the same items. Two near-opposites agents write count too:
/// <c>X not-equal v OR X is-not-filled-in</c> against <c>X equal v</c>, and <c>Flag equal false</c> against
/// <c>Flag equal true</c> (boolean constants only). A page state rule undoes itself when its
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
	private const string IsFilledIn = "is-filled-in";
	private const string IsNotFilledIn = "is-not-filled-in";

	private static readonly JsonElement TrueConstant = JsonSerializer.SerializeToElement(true);

	// The comparisons that are true on an empty value; every other value comparison is false on it.
	private static readonly HashSet<string> TrueOnEmptyComparisons = new(StringComparer.Ordinal) {
		"not-equal", "not-contain"
	};

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
			|| !TryNegate(other.InverseForm(), out string negatedOperation,
				out HashSet<ConditionKey> negatedConditions)) {
			return false;
		}
		RuleShape ruleForm = rule.InverseForm();
		return ruleForm.Operation == negatedOperation && ruleForm.Conditions.SetEquals(negatedConditions);
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

	/// <param name="RightIsConst">Whether the right side is a constant; only then can it not be empty.</param>
	private sealed record ConditionKey(string Left, string Comparison, string Right, bool RightIsConst);

	/// <param name="Conditions">The conditions as written; subsumption compares these.</param>
	/// <param name="BooleanNormalizedConditions">The same conditions with every comparison with the boolean constant
	/// <c>false</c> keyed as the opposite comparison with <c>true</c>; only <see cref="InverseForm"/> reads them.</param>
	private sealed record RuleShape(string Label, string Operation, HashSet<ConditionKey> Conditions,
		HashSet<ConditionKey> BooleanNormalizedConditions, IReadOnlyList<ActionShape> Actions) {

		/// <summary>
		/// The form the inverse check compares, with both near-opposite relaxations applied; subsumption never sees it.
		/// "Flag equal false" reads as "Flag not-equal true", so it negates "Flag equal true", and the null checks a
		/// value comparison already settles are dropped (see <see cref="WithoutImpliedNullChecks"/>).
		/// </summary>
		public RuleShape InverseForm() =>
			(this with {
				Operation = NormalizeOperation(Operation, BooleanNormalizedConditions.Count),
				Conditions = BooleanNormalizedConditions
			}).WithoutImpliedNullChecks();

		/// <summary>
		/// The same rule without the null checks a value comparison on the same attribute already settles:
		/// <c>is-not-filled-in</c> in an OR group beside a comparison that is true on an empty value
		/// (<c>not-equal</c>, <c>not-contain</c>), and <c>is-filled-in</c> in an AND group beside one that is false on
		/// it (<c>equal</c>, <c>contain</c>, the relational ones). So "X not-equal v OR X is-not-filled-in", the way
		/// agents negate "X equal v", reads as "X not-equal v". Only a comparison with a constant settles the check:
		/// another attribute, system value or setting on the right can be empty too.
		/// </summary>
		private RuleShape WithoutImpliedNullChecks() {
			bool isAnd = Operation == And;
			string impliedCheck = isAnd ? IsFilledIn : IsNotFilledIn;
			if (Conditions.Count < 2 || Conditions.All(c => c.Comparison != impliedCheck)) {
				return this;
			}
			var settledLefts = new HashSet<string>(Conditions
				.Where(c => c.RightIsConst && !BusinessRuleHelpers.IsUnaryComparisonType(c.Comparison)
					&& TrueOnEmptyComparisons.Contains(c.Comparison) != isAnd)
				.Select(c => c.Left), StringComparer.Ordinal);
			HashSet<ConditionKey> kept = Conditions
				.Where(c => c.Comparison != impliedCheck || !settledLefts.Contains(c.Left))
				.ToHashSet();
			return kept.Count == Conditions.Count
				? this
				: this with { Operation = NormalizeOperation(Operation, kept.Count), Conditions = kept };
		}

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
			List<BusinessRuleCondition> typedConditions = conditions.OfType<BusinessRuleCondition>().ToList();
			HashSet<ConditionKey> keys = typedConditions
				.Select(condition => CreateConditionKey(condition, normalizeBoolean: false))
				.ToHashSet();
			HashSet<ConditionKey> booleanNormalizedKeys = typedConditions
				.Select(condition => CreateConditionKey(condition, normalizeBoolean: true))
				.ToHashSet();
			string label = string.IsNullOrWhiteSpace(rule.Caption) ? rule.Name ?? string.Empty : rule.Caption.Trim();
			return new RuleShape(label, NormalizeOperation(operation, keys.Count), keys, booleanNormalizedKeys,
				actionShapes);
		}

		/// <summary>
		/// Builds the comparable key of a condition. With <paramref name="normalizeBoolean"/>, a comparison with the
		/// boolean constant <c>false</c> is keyed as the opposite comparison with <c>true</c>.
		/// </summary>
		private static ConditionKey CreateConditionKey(BusinessRuleCondition condition, bool normalizeBoolean) {
			string comparison = (condition.ComparisonType ?? string.Empty).Trim().ToLowerInvariant();
			BusinessRuleExpression? right = condition.RightExpression;
			bool rightIsConst = string.Equals(right?.Type, BusinessRuleConstants.ConstExpressionType,
				StringComparison.OrdinalIgnoreCase);
			if (normalizeBoolean
				&& rightIsConst
				&& BusinessRuleHelpers.IsEqualityComparisonType(comparison)
				&& right?.Value is { ValueKind: JsonValueKind.False }) {
				comparison = NegatedComparisons[comparison];
				right = right with { Value = TrueConstant };
			}
			return new ConditionKey(Describe(condition.LeftExpression), comparison, Describe(right), rightIsConst);
		}

		private static string Describe(BusinessRuleExpression? expression) =>
			expression is null
				? string.Empty
				: string.Join("\u001f", expression.Type?.ToLowerInvariant(), expression.Path, expression.Expression, expression.SysValueName,
					expression.SysSettingName,
					expression.Value is JsonElement value ? value.GetRawText() : string.Empty);
	}
}
