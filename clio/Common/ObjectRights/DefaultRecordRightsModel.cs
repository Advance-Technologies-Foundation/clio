using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>A record operation a default record rule grants (a column of the "Use record permissions" grid).</summary>
public enum RecordOperation {
	Read,
	Edit,
	Delete
}

/// <summary>
/// The level of one operation in a default record rule, as the platform stores it. Any other stored value is invalid
/// (the server keeps it without a check) and is refused before a save would send it back.
/// </summary>
public enum RecordRightLevel {
	/// <summary>Not set: the rule gives no right for the operation.</summary>
	NotSet = 0,
	/// <summary>Granted.</summary>
	Granted = 1,
	/// <summary>Granted with the right to delegate it.</summary>
	Delegated = 2
}

/// <summary>
/// The one spelling of each record operation and level, for the output and for <c>--operations</c> / <c>--level</c>.
/// </summary>
public static class RecordRightNames {

	/// <summary>Every operation, in grid order: read, edit, delete.</summary>
	public static IReadOnlyList<RecordOperation> AllOperations { get; } =
		new[] { RecordOperation.Read, RecordOperation.Edit, RecordOperation.Delete };

	/// <summary>The accepted operation names, comma-separated in grid order: <c>read,edit,delete</c>.</summary>
	public static string AcceptedOperations { get; } = string.Join(",", AllOperations.Select(Of));

	/// <summary>The accepted level names: <c>granted</c>, <c>delegated</c>.</summary>
	public const string AcceptedLevels = "granted, delegated";

	/// <summary>Reads one operation name, in any case.</summary>
	/// <param name="name">The name, e.g. <c>read</c>.</param>
	/// <param name="operation">The operation, when the name is one of <see cref="AllOperations"/>.</param>
	/// <returns><see langword="true"/> when the name is an operation's.</returns>
	public static bool TryParseOperation(string name, out RecordOperation operation) {
		RecordOperation[] match = AllOperations
			.Where(candidate => string.Equals(Of(candidate), name, StringComparison.OrdinalIgnoreCase))
			.Take(1)
			.ToArray();
		operation = match.FirstOrDefault();
		return match.Length == 1;
	}

	/// <summary>Reads a level a caller may grant — <c>granted</c> or <c>delegated</c> — in any case.</summary>
	/// <param name="name">The name.</param>
	/// <param name="level">The level, when the name is one of them.</param>
	/// <returns><see langword="true"/> for <c>granted</c> or <c>delegated</c>.</returns>
	public static bool TryParseGrantLevel(string name, out RecordRightLevel level) {
		level = RecordRightLevel.Granted;
		if (string.Equals(name, "granted", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		if (string.Equals(name, "delegated", StringComparison.OrdinalIgnoreCase)) {
			level = RecordRightLevel.Delegated;
			return true;
		}
		return false;
	}

	/// <summary>The name of <paramref name="operation"/>: read, edit or delete.</summary>
	/// <param name="operation">The operation.</param>
	/// <returns>The operation's name.</returns>
	public static string Of(RecordOperation operation) => operation switch {
		RecordOperation.Read => "read",
		RecordOperation.Edit => "edit",
		RecordOperation.Delete => "delete",
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};

	/// <summary>The name of <paramref name="level"/>: <c>-</c>, <c>granted</c>, <c>delegated</c>, or the stored number.</summary>
	/// <param name="level">The level.</param>
	/// <returns>The level's name.</returns>
	public static string Of(RecordRightLevel level) => level switch {
		RecordRightLevel.NotSet => "-",
		RecordRightLevel.Granted => "granted",
		RecordRightLevel.Delegated => "delegated",
		_ => $"invalid level {(int)level}"
	};
}

/// <summary>
/// One default record rule of an object: records created by members of the author role get the levels of read, edit
/// and delete for the grantee role. A rule is identified by its (author, grantee) pair; rules have no order, and the
/// rights of several rules add up.
/// </summary>
/// <param name="AuthorId">The SysAdminUnit id of the author role or user.</param>
/// <param name="AuthorName">The author's name, as the service returned it.</param>
/// <param name="GranteeId">The SysAdminUnit id of the grantee role or user.</param>
/// <param name="GranteeName">The grantee's name, as the service returned it.</param>
/// <param name="Read">The read level.</param>
/// <param name="Edit">The edit level.</param>
/// <param name="Delete">The delete level.</param>
/// <param name="DoNotApplyForManager">"Do not apply for manager": the grantee role's managers do not inherit the
/// rule.</param>
public sealed record DefaultRecordRule(
	Guid AuthorId,
	string AuthorName,
	Guid GranteeId,
	string GranteeName,
	RecordRightLevel Read,
	RecordRightLevel Edit,
	RecordRightLevel Delete,
	bool DoNotApplyForManager) {

	/// <summary>The rule gives no right at all; the server drops such a rule on save.</summary>
	public bool IsEmpty => Read == RecordRightLevel.NotSet && Edit == RecordRightLevel.NotSet
		&& Delete == RecordRightLevel.NotSet;

	/// <summary>A level of the rule is outside the three the platform defines.</summary>
	public bool HasInvalidLevel => !IsValid(Read) || !IsValid(Edit) || !IsValid(Delete);

	/// <summary>The level of <paramref name="operation"/>.</summary>
	/// <param name="operation">The operation.</param>
	/// <returns>Its level.</returns>
	public RecordRightLevel LevelOf(RecordOperation operation) => operation switch {
		RecordOperation.Read => Read,
		RecordOperation.Edit => Edit,
		RecordOperation.Delete => Delete,
		_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
	};

	/// <summary>The same rule with every operation in <paramref name="operations"/> set to <paramref name="level"/>.</summary>
	/// <param name="operations">The operations to set.</param>
	/// <param name="level">The level to set them to.</param>
	/// <returns>The changed rule.</returns>
	public DefaultRecordRule With(IEnumerable<RecordOperation> operations, RecordRightLevel level) {
		DefaultRecordRule rule = this;
		foreach (RecordOperation operation in operations) {
			rule = operation switch {
				RecordOperation.Read => rule with { Read = level },
				RecordOperation.Edit => rule with { Edit = level },
				RecordOperation.Delete => rule with { Delete = level },
				_ => throw new ArgumentOutOfRangeException(nameof(operations), operation, null)
			};
		}
		return rule;
	}

	/// <summary>Whether <paramref name="other"/> is a rule for the same (author, grantee) pair.</summary>
	/// <param name="other">The rule to compare with.</param>
	/// <returns><see langword="true"/> for the same pair.</returns>
	public bool SamePairAs(DefaultRecordRule other) =>
		other is not null && AuthorId == other.AuthorId && GranteeId == other.GranteeId;

	/// <summary>Whether <paramref name="other"/> is the same rule: the same pair, levels and flag. Names are not compared.</summary>
	/// <param name="other">The rule to compare with.</param>
	/// <returns><see langword="true"/> when nothing the platform stores differs.</returns>
	public bool SameRuleAs(DefaultRecordRule other) =>
		SamePairAs(other) && Read == other.Read && Edit == other.Edit && Delete == other.Delete
		&& DoNotApplyForManager == other.DoNotApplyForManager;

	private static bool IsValid(RecordRightLevel level) =>
		level is RecordRightLevel.NotSet or RecordRightLevel.Granted or RecordRightLevel.Delegated;
}

/// <summary>
/// The record-permissions state of one object: whether "Use record permissions" is on, and its default record rules.
/// The rules are kept while the switch is off and come back into effect when it is turned on again.
/// </summary>
/// <param name="AdministratedByRecords">Whether "Use record permissions" is on.</param>
/// <param name="Rules">The default record rules, in the order the service returned them (the order has no meaning).</param>
public sealed record DefaultRecordRightsState(bool AdministratedByRecords, IReadOnlyList<DefaultRecordRule> Rules) {

	/// <summary>The rule for the (<paramref name="author"/>, <paramref name="grantee"/>) pair, or <see langword="null"/>.</summary>
	/// <param name="author">The author's SysAdminUnit id.</param>
	/// <param name="grantee">The grantee's SysAdminUnit id.</param>
	/// <returns>The first rule of the pair, or <see langword="null"/>.</returns>
	public DefaultRecordRule RuleFor(Guid author, Guid grantee) =>
		Rules.FirstOrDefault(rule => rule.AuthorId == author && rule.GranteeId == grantee);

	/// <summary>Whether the rules differ from <paramref name="other"/>'s, compared as a set keyed by (author, grantee).</summary>
	/// <param name="other">The state to compare with.</param>
	/// <returns><see langword="true"/> when a rule was added, removed or changed.</returns>
	public bool RulesDifferFrom(DefaultRecordRightsState other) => DiffRules(other).Count > 0;

	/// <summary>
	/// The differences between this state and <paramref name="other"/>: the switch, and the rules compared as a set keyed
	/// by (author, grantee), every stored field included. Rendered for output, one line per difference.
	/// </summary>
	/// <param name="other">The state to compare with, for example the object read back after a save.</param>
	/// <returns>The differences; empty when the states match.</returns>
	public IReadOnlyList<string> Diff(DefaultRecordRightsState other) {
		ArgumentNullException.ThrowIfNull(other);
		List<string> differences = new();
		if (AdministratedByRecords != other.AdministratedByRecords) {
			differences.Add($"record permissions are {DefaultRecordRightsFormat.Switch(other)}, expected "
				+ DefaultRecordRightsFormat.Switch(this));
		}
		differences.AddRange(DiffRules(other));
		return differences;
	}

	private List<string> DiffRules(DefaultRecordRightsState other) {
		ArgumentNullException.ThrowIfNull(other);
		List<string> differences = new();
		List<DefaultRecordRule> extra = other.Rules.ToList();
		foreach (DefaultRecordRule expected in Rules) {
			DefaultRecordRule actual = extra.FirstOrDefault(rule => rule.SamePairAs(expected));
			if (actual is null) {
				differences.Add($"rule {DefaultRecordRightsFormat.Rule(expected)} is missing");
				continue;
			}
			extra.Remove(actual);
			if (!actual.SameRuleAs(expected)) {
				differences.Add($"rule {DefaultRecordRightsFormat.Rule(actual)}, expected "
					+ DefaultRecordRightsFormat.Levels(expected));
			}
		}
		differences.AddRange(extra.Select(rule => $"rule {DefaultRecordRightsFormat.Rule(rule)} is not expected"));
		return differences;
	}
}

/// <summary>How the record layer is rendered, the same way in every command and in the read-back.</summary>
public static class DefaultRecordRightsFormat {

	/// <summary>The state of the "Use record permissions" switch: <c>ON</c> or <c>OFF</c>.</summary>
	/// <param name="state">The object's record state.</param>
	/// <returns>The switch text.</returns>
	public static string Switch(DefaultRecordRightsState state) => state.AdministratedByRecords ? "ON" : "OFF";

	/// <summary>
	/// One rule: <c>All employees → Sales: read granted, edit delegated, delete -</c>, plus <c>do not apply for manager</c>
	/// when set. With the ids when asked for.
	/// </summary>
	/// <param name="rule">The rule.</param>
	/// <param name="withIds">Also show the SysAdminUnit ids.</param>
	/// <returns>The display-safe rule.</returns>
	public static string Rule(DefaultRecordRule rule, bool withIds = false) =>
		$"{Name(rule.AuthorName, rule.AuthorId, withIds)} → {Name(rule.GranteeName, rule.GranteeId, withIds)}: "
		+ Levels(rule);

	/// <summary>The levels and the manager flag of a rule: <c>read granted, edit -, delete -</c>.</summary>
	/// <param name="rule">The rule.</param>
	/// <returns>The levels text.</returns>
	public static string Levels(DefaultRecordRule rule) =>
		string.Join(", ", RecordRightNames.AllOperations.Select(operation =>
			$"{RecordRightNames.Of(operation)} {RecordRightNames.Of(rule.LevelOf(operation))}"))
		+ (rule.DoNotApplyForManager ? ", do not apply for manager" : "");

	/// <summary>Rules separated by <c>"; "</c>, or <c>"none"</c>.</summary>
	/// <param name="rules">The rules.</param>
	/// <returns>The rules text.</returns>
	public static string Rules(IEnumerable<DefaultRecordRule> rules) {
		string[] formatted = rules.Select(rule => Rule(rule)).ToArray();
		return formatted.Length == 0 ? "none" : string.Join("; ", formatted);
	}

	/// <summary>The operations a request names, in grid order: <c>read/edit</c>.</summary>
	/// <param name="operations">The operations.</param>
	/// <returns>The operations text.</returns>
	public static string Operations(IEnumerable<RecordOperation> operations) =>
		string.Join("/", operations.Select(RecordRightNames.Of));

	private static string Name(string name, Guid id, bool withId) =>
		ObjectRightsSupport.Display(string.IsNullOrEmpty(name) ? id.ToString() : name) + (withId ? $" ({id})" : "");
}
