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
	/// What clio adds to the server's own sentence when it relays a layout refusal.
	/// </summary>
	/// <remarks>
	/// EXACTLY ONE instruction reaches the caller, and it names the argument in the spelling clio's own surface
	/// declares. The server may not name a spelling: its wire member is <c>confirmLayoutChange</c> and the MCP
	/// argument is <c>confirm-layout-change</c>, so a message carrying both taught an agent to send the one the
	/// tool does not declare — which deserializes to null, coalesces to false, and returns the identical refusal
	/// to a user who had already agreed. The gate exists to collect that consent; dropping it silently is the
	/// one failure it cannot survive.
	/// <para>The element clause is omitted rather than left empty when the server names none, because
	/// "Affected elements: ." reads as a truncated message and sends a reader looking for what was cut off.</para>
	/// </remarks>
	/// <param name="what">What the caller re-sends — "operations" on the in-place path, "request" on the version path.</param>
	/// <returns>The sentence to append to the server's message.</returns>
	public string RelaySentence(string what) {
		string elements = Elements is { Count: > 0 }
			? $" Affected elements: {string.Join(", ", Elements.Where(name => !string.IsNullOrWhiteSpace(name)))}."
			: string.Empty;
		return $"{elements} Re-send the same {what} with confirm-layout-change once the user has agreed.";
	}

}
