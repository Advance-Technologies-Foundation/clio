using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Clio.Command.ProcessModel;

/// <summary>
/// The <c>layoutChange</c> block a CrtProcessBuilder write path returns when it refuses an edit that would
/// re-draw someone's diagram.
/// </summary>
/// <remarks>
/// One owner, because it is ONE server-owned wire contract: it was declared privately inside each of the two
/// commands that read it, and the two copies had already drifted in the sentence they compose from it before
/// either shipped.
/// <para>Absent on a CrtProcessBuilder that does not gate layout changes, which reads as "no question was
/// asked" and is exactly right there.</para>
/// </remarks>
public sealed class LayoutChangeRelay {

	/// <summary>Which of the two conditions the server measured.</summary>
	[JsonPropertyName("reason")]
	public string? Reason { get; set; }

	/// <summary>The sentence to show the person whose diagram it is.</summary>
	[JsonPropertyName("summary")]
	public string? Summary { get; set; }

	/// <summary>The elements that move, narrowed by the server to what a reader would look for.</summary>
	[JsonPropertyName("elements")]
	public List<string>? Elements { get; set; }

	/// <summary>
	/// The elements the report is about, as a sentence, or nothing at all when the server named none.
	/// </summary>
	/// <remarks>
	/// Omitted rather than left empty, because "Affected elements: ." reads as a truncated message and sends a
	/// reader looking for what was cut off.
	/// </remarks>
	/// <returns>The clause to append, already separated from what precedes it.</returns>
	public string ElementsClause() =>
		Elements is { Count: > 0 }
			? $" Affected elements: {string.Join(", ", Elements.Where(name => !string.IsNullOrWhiteSpace(name)))}."
			: string.Empty;

	/// <summary>
	/// What clio adds to the server's own sentence when it relays an in-place layout refusal: the elements, and
	/// the two ways forward in the order they should be offered.
	/// </summary>
	/// <remarks>
	/// The new-version route is named FIRST because it is the one that costs the user nothing, and because a
	/// bare "re-send with confirm-layout-change" is an instruction an agent can carry out without ever asking
	/// anybody — which is the failure this gate exists to prevent, arriving by a different door.
	/// <para>The flag is named in the spelling clio's own surface declares, and in exactly one place. The server
	/// may not name a spelling: its wire member is <c>confirmLayoutChange</c> and the MCP argument is
	/// <c>confirm-layout-change</c>, so a message carrying both taught an agent to send the one the tool does
	/// not declare — which deserializes to null, coalesces to false, and returns the identical refusal to a user
	/// who had already agreed.</para>
	/// </remarks>
	/// <returns>The sentence to append to the server's message.</returns>
	public string RelaySentence() =>
		ElementsClause()
		+ " ASK THE USER which they want, and send nothing until they answer: the same operations to "
		+ "modify-business-process-as-new-version, which leaves this process and its diagram untouched and "
		+ "creates an INACTIVE version they can look at first, or the same operations here again with "
		+ "confirm-layout-change, which re-draws this process in place.";

}
