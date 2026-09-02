namespace Clio.Command;

/// <summary>
/// The <c>viewConfigDiff</c> operation verbs, exactly as the platform differ spells them.
/// </summary>
/// <remarks>
/// One source for the five strings, so a differ change does not have to be hunted across
/// <see cref="PageBodyMerger"/> and <see cref="PageInertOperationDetector"/>.
/// <para>
/// Only the NAMES belong here. The rules built on them deliberately differ per consumer and must not be
/// unified: the differ's GROUPING splits only <c>remove</c> on a <c>properties</c> array, while the
/// merger's identity discriminator covers <c>remove</c> AND <c>set</c>, because <c>Set</c> calls
/// <c>Remove</c> and its apply path branches on that array too. Collapsing the two would reintroduce the
/// <c>set</c> identity collision the discriminator exists to prevent.
/// </para>
/// <para>
/// Compared verbatim and case-sensitively wherever they are used. The differ switches on the raw verb
/// with an empty <c>default</c> arm, so a mis-cased spelling lands in no group and is discarded whole —
/// case-folding any comparison against these would report behaviour the platform does not have.
/// </para>
/// </remarks>
internal static class PageViewConfigDiffVerbs {

	/// <summary>Patches an existing element's properties.</summary>
	public const string Merge = "merge";

	/// <summary>Replaces an element wholesale; applied after every other group.</summary>
	public const string Set = "set";

	/// <summary>Adds a new element.</summary>
	public const string Insert = "insert";

	/// <summary>Relocates an existing element.</summary>
	public const string Move = "move";

	/// <summary>Deletes an element, or strips named properties when it carries a <c>properties</c> array.</summary>
	public const string Remove = "remove";

}
