using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Common.ObjectRights;

/// <summary>The one default record rule a <c>set-default-record-rights</c> call changes.</summary>
/// <param name="Author">The SysAdminUnit id of the author role or user.</param>
/// <param name="AuthorName">The author's name, used for a rule the plan adds.</param>
/// <param name="Grantee">The SysAdminUnit id of the grantee role or user.</param>
/// <param name="GranteeName">The grantee's name, used for a rule the plan adds.</param>
/// <param name="Operations">The operations to grant, or to revoke when <paramref name="Revoke"/> is set.</param>
/// <param name="Level">The level a grant sets: <see cref="RecordRightLevel.Granted"/> or
/// <see cref="RecordRightLevel.Delegated"/>. Ignored by a revoke.</param>
/// <param name="DoNotApplyForManager">A grant sets the rule's "Do not apply for manager" flag to this value;
/// <see langword="null"/> keeps an existing rule's flag and gives a new rule <see langword="false"/>.</param>
/// <param name="Revoke">Set the operations to "not set" instead of granting them.</param>
public sealed record DefaultRecordRuleChange(
	Guid Author,
	string AuthorName,
	Guid Grantee,
	string GranteeName,
	IReadOnlyCollection<RecordOperation> Operations,
	RecordRightLevel Level,
	bool? DoNotApplyForManager,
	bool Revoke);

/// <summary>What one <c>set-default-record-rights</c> call asks for, on ONE object: one rule and/or the switch.</summary>
/// <param name="Rule">The rule change; <see langword="null"/> for a switch-only call.</param>
/// <param name="EnableRecordPermissions">Turn "Use record permissions" ON.</param>
/// <param name="DisableRecordPermissions">Turn "Use record permissions" OFF.</param>
public sealed record DefaultRecordRightsChangeRequest(
	DefaultRecordRuleChange Rule,
	bool EnableRecordPermissions,
	bool DisableRecordPermissions);

/// <summary>Why a planned record-permissions change is refused. A refused plan writes nothing.</summary>
public enum DefaultRecordRightsRefusal {
	/// <summary>The plan is allowed.</summary>
	None,
	/// <summary>
	/// A grant on an object whose record permissions are OFF, without <c>--enable-record-permissions</c>: the rule would
	/// be stored but give nobody anything, and the stored rules would all come into effect with a later enable.
	/// </summary>
	EnableNotRequested,
	/// <summary>
	/// The stored rules have two or more rules for one (author, grantee) pair. The save replaces the whole list and the
	/// server keeps only the last rule of a pair, so sending the list back would lose rights silently.
	/// </summary>
	DuplicatePairs,
	/// <summary>A stored rule has a level outside not set / granted / delegated, which the save would send back as is.</summary>
	InvalidStoredLevel
}

/// <summary>
/// The planned change for one object's record layer: the state before and after, and the facts a caller needs to see
/// before approving it. Output, exit code and the dry run are rendered from this plan.
/// </summary>
/// <param name="Before">The record state as read.</param>
/// <param name="After">The record state the save would leave. Equal to <paramref name="Before"/> when refused.</param>
/// <param name="Refusal">Why the plan is refused; <see cref="DefaultRecordRightsRefusal.None"/> when allowed.</param>
/// <param name="RuleBefore">The target rule as read; <see langword="null"/> when the pair has none or the call is
/// switch-only.</param>
/// <param name="RuleAfter">The target rule as planned; <see langword="null"/> when the pair ends with no rule (a revoke
/// that empties it removes it) or the call is switch-only. On a refusal, the rule the refused change would have
/// written.</param>
/// <param name="StoredRulesComingIntoEffect">On enable: the rules that start to apply. On a refusal of a grant on an
/// object that is OFF: the stored rules that an enable would bring into effect.</param>
/// <param name="ProblemRules">On <see cref="DefaultRecordRightsRefusal.DuplicatePairs"/> or
/// <see cref="DefaultRecordRightsRefusal.InvalidStoredLevel"/>: the stored rules that block the save.</param>
public sealed record DefaultRecordRightsPlan(
	DefaultRecordRightsState Before,
	DefaultRecordRightsState After,
	DefaultRecordRightsRefusal Refusal,
	DefaultRecordRule RuleBefore,
	DefaultRecordRule RuleAfter,
	IReadOnlyList<DefaultRecordRule> StoredRulesComingIntoEffect,
	IReadOnlyList<DefaultRecordRule> ProblemRules) {

	/// <summary>The plan is refused and writes nothing.</summary>
	public bool Refused => Refusal != DefaultRecordRightsRefusal.None;

	/// <summary>The plan is allowed and the save would change something.</summary>
	public bool Changes => !Refused && Before.Diff(After).Count > 0;

	/// <summary>The plan turns record permissions ON.</summary>
	public bool Enables => !Refused && !Before.AdministratedByRecords && After.AdministratedByRecords;

	/// <summary>The plan turns record permissions OFF.</summary>
	public bool Disables => !Refused && Before.AdministratedByRecords && !After.AdministratedByRecords;

	/// <summary>The plan changes the rule list (adds, changes or removes the target rule).</summary>
	public bool ChangesRules => !Refused && Before.RulesDifferFrom(After);
}

/// <summary>
/// Computes the change one <c>set-default-record-rights</c> call makes to one object's record layer, without any I/O.
/// The rule list is changed for the one (author, grantee) pair the request names; every other rule stays as read; the
/// switch changes only with its flag.
/// </summary>
public interface IDefaultRecordRightsPlanner {
	/// <summary>Plans <paramref name="request"/> against the object's current record state.</summary>
	/// <param name="before">The record state as read.</param>
	/// <param name="request">The change the call asks for.</param>
	/// <returns>The plan, allowed or refused.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="before"/> or <paramref name="request"/> is null.</exception>
	/// <exception cref="ArgumentException">The request names neither a rule nor a switch flag, names both flags, a rule
	/// with the disable, a revoke with the enable, or a rule with no operation. The command refuses those shapes before
	/// any read.</exception>
	DefaultRecordRightsPlan Plan(DefaultRecordRightsState before, DefaultRecordRightsChangeRequest request);
}

/// <inheritdoc />
public sealed class DefaultRecordRightsPlanner : IDefaultRecordRightsPlanner {

	/// <inheritdoc />
	public DefaultRecordRightsPlan Plan(DefaultRecordRightsState before, DefaultRecordRightsChangeRequest request) {
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(request);
		Validate(request);
		DefaultRecordRuleChange change = request.Rule;
		DefaultRecordRule ruleBefore = change is null ? null : before.RuleFor(change.Author, change.Grantee);
		DefaultRecordRule ruleAfter = change is null ? null : Apply(change, ruleBefore);
		List<DefaultRecordRule> rules = Replace(before.Rules, ruleBefore, ruleAfter);
		bool switchAfter = request.EnableRecordPermissions
			|| (!request.DisableRecordPermissions && before.AdministratedByRecords);
		DefaultRecordRightsState after = new(switchAfter, rules);
		bool grants = change is { Revoke: false };

		// THE POLICY, in the order the refusals are reported. A refusal writes nothing.
		if (grants && !switchAfter) {
			return Refuse(before, DefaultRecordRightsRefusal.EnableNotRequested, ruleBefore, ruleAfter,
				storedRules: before.Rules);
		}
		// The checks of the stored list apply only when the save sends the list: a call that leaves the rules alone
		// sends them as null, so a bad stored rule is never re-sent by it.
		if (before.RulesDifferFrom(after)) {
			DefaultRecordRule[] duplicates = before.Rules
				.GroupBy(rule => (rule.AuthorId, rule.GranteeId))
				.Where(group => group.Count() > 1)
				.SelectMany(group => group)
				.ToArray();
			if (duplicates.Length > 0) {
				return Refuse(before, DefaultRecordRightsRefusal.DuplicatePairs, ruleBefore, ruleAfter,
					problemRules: duplicates);
			}
			DefaultRecordRule[] invalid = before.Rules.Where(rule => rule.HasInvalidLevel).ToArray();
			if (invalid.Length > 0) {
				return Refuse(before, DefaultRecordRightsRefusal.InvalidStoredLevel, ruleBefore, ruleAfter,
					problemRules: invalid);
			}
		}
		bool enables = !before.AdministratedByRecords && switchAfter;
		return new DefaultRecordRightsPlan(before, after, DefaultRecordRightsRefusal.None, ruleBefore, ruleAfter,
			enables ? after.Rules : Array.Empty<DefaultRecordRule>(), Array.Empty<DefaultRecordRule>());
	}

	private static void Validate(DefaultRecordRightsChangeRequest request) {
		if (request.EnableRecordPermissions && request.DisableRecordPermissions) {
			throw new ArgumentException("The request both enables and disables record permissions.", nameof(request));
		}
		if (request.Rule is null && !request.EnableRecordPermissions && !request.DisableRecordPermissions) {
			throw new ArgumentException("The request names neither a rule nor a switch flag.", nameof(request));
		}
		// The switch and the rules change separately: a disable names no rule, and a revoke names no switch flag.
		if (request.Rule is not null && request.DisableRecordPermissions) {
			throw new ArgumentException("A disable is a call of its own; it names no rule.", nameof(request));
		}
		if (request.Rule is { Revoke: true } && request.EnableRecordPermissions) {
			throw new ArgumentException("A revoke never changes the switch.", nameof(request));
		}
		// A rule change names its operations (nothing is granted by default).
		if (request.Rule is { Operations: null or { Count: 0 } }) {
			throw new ArgumentException("The rule change names no operation.", nameof(request));
		}
	}

	// The target rule after the change, or null when the pair ends with no right: the server drops an all-"not set" rule
	// on save, so the plan removes it explicitly and the output can say so.
	private static DefaultRecordRule Apply(DefaultRecordRuleChange change, DefaultRecordRule ruleBefore) {
		if (change.Revoke) {
			DefaultRecordRule revoked = ruleBefore?.With(change.Operations, RecordRightLevel.NotSet);
			return revoked is null || revoked.IsEmpty ? null : revoked;
		}
		DefaultRecordRule granted = (ruleBefore ?? new DefaultRecordRule(change.Author, change.AuthorName, change.Grantee,
				change.GranteeName, RecordRightLevel.NotSet, RecordRightLevel.NotSet, RecordRightLevel.NotSet, false))
			.With(change.Operations, change.Level);
		return change.DoNotApplyForManager is { } flag ? granted with { DoNotApplyForManager = flag } : granted;
	}

	// Every other rule stays exactly as read and in place; the target rule is replaced, removed or appended.
	private static List<DefaultRecordRule> Replace(IReadOnlyList<DefaultRecordRule> rules, DefaultRecordRule ruleBefore,
		DefaultRecordRule ruleAfter) {
		List<DefaultRecordRule> result = rules.ToList();
		int index = ruleBefore is null ? -1 : result.IndexOf(ruleBefore);
		if (index >= 0 && ruleAfter is null) {
			result.RemoveAt(index);
		} else if (index >= 0) {
			result[index] = ruleAfter;
		} else if (ruleAfter is not null) {
			result.Add(ruleAfter);
		}
		return result;
	}

	private static DefaultRecordRightsPlan Refuse(DefaultRecordRightsState before, DefaultRecordRightsRefusal refusal,
		DefaultRecordRule ruleBefore, DefaultRecordRule ruleAfter, IReadOnlyList<DefaultRecordRule> storedRules = null,
		IReadOnlyList<DefaultRecordRule> problemRules = null) =>
		new(before, before, refusal, ruleBefore, ruleAfter, storedRules ?? Array.Empty<DefaultRecordRule>(),
			problemRules ?? Array.Empty<DefaultRecordRule>());
}
