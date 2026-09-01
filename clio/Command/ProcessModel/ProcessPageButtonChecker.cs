using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.McpServer.Tools;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Refuses a <c>preconfiguredPage</c> block that names a completing button the page does not have.
/// </summary>
/// <remarks>
/// <para>This check lives in clio and CANNOT live in the package, for the same reason
/// <c>get-process-page-facts</c> exists at all: a Freedom UI page is assembled from its template chain by the
/// CLIENT, so the server never sees the merged view config and cannot enumerate the page's buttons even in
/// principle. clio already merges that chain to build the page bundle, so the answer is already in hand here.</para>
/// <para>Worth the round trip because of how the failure presents without it: a button name that exists nowhere
/// on the page is stored as the tag <c>{name}_clicked</c>, the process builds green, saves green, and then the
/// step waits forever at run time — the runtime matches the pressed button against that tag and nothing ever
/// raises it. No build-time check sees it, and an agent that invents a plausible name instead of reading the
/// facts is the ordinary case, not the exotic one.</para>
/// <para>Deliberately silent when the facts cannot be read: an unknown page, an unreachable environment or a
/// Classic page are all refused downstream with a message about THAT, and turning them into a button complaint
/// here would replace a precise diagnosis with a worse one.</para>
/// </remarks>
public interface IProcessPageButtonChecker {

	/// <summary>
	/// Checks every <c>preconfiguredPage</c> block's button names against the page they name.
	/// </summary>
	/// <param name="environmentName">The environment whose pages the names are checked against.</param>
	/// <param name="payload">A create descriptor or a modify operations array.</param>
	ProcessPageButtonCheckResult CheckButtons(string environmentName, JsonNode payload);

}

/// <summary>
/// The outcome of a button check: at most one refusal, plus any names that exist on the page but are not
/// completing candidates.
/// </summary>
/// <param name="Error">The refusal, or <c>null</c> when nothing must be stopped.</param>
/// <param name="Warnings">Names present on the page but outside the candidate set — reported, never refused.</param>
public sealed record ProcessPageButtonCheckResult(string Error, IReadOnlyList<string> Warnings) {

	/// <summary>Nothing to say about this payload.</summary>
	public static readonly ProcessPageButtonCheckResult Clean = new(null, []);

}

/// <inheritdoc />
public sealed class ProcessPageButtonChecker(IToolCommandResolver commandResolver) : IProcessPageButtonChecker {

	/// <inheritdoc />
	public ProcessPageButtonCheckResult CheckButtons(string environmentName, JsonNode payload) {
		if (payload is null || string.IsNullOrWhiteSpace(environmentName)) {
			return ProcessPageButtonCheckResult.Clean;
		}
		List<string> warnings = [];
		// One read per distinct page, not per button: a process routinely shows the same page from several steps.
		Dictionary<string, PageButtons> buttonsByPage = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonObject block in FindPreconfiguredPageBlocks(payload)) {
			string pageName = (block["page"]?.GetValue<string>() ?? string.Empty).Trim();
			if (pageName.Length == 0) {
				// A modify that changes only the recommendation carries no page; the buttons it does not send
				// are the ones already stored, which were checked when they were set.
				continue;
			}
			List<string> named = ReadButtonNames(block["buttons"] as JsonArray);
			if (named.Count == 0) {
				continue;
			}
			if (!buttonsByPage.TryGetValue(pageName, out PageButtons pageButtons)) {
				pageButtons = ReadPageButtons(environmentName, pageName);
				buttonsByPage[pageName] = pageButtons;
			}
			if (pageButtons is null) {
				continue;
			}
			foreach (string name in named) {
				// Ordinal on both tests: the name is stored verbatim and the run time matches the tag composed
				// from it, so a case-only difference is a button the page cannot raise.
				if (!pageButtons.All.Contains(name, StringComparer.Ordinal)) {
					// Absent from the page ENTIRELY. This one is always broken — no handler can ever raise it —
					// so it is refused rather than reported.
					return new ProcessPageButtonCheckResult(
						BuildRefusal(pageName, name, pageButtons.All), warnings);
				}
				if (!pageButtons.Candidates.Contains(name, StringComparer.Ordinal)) {
					// Present, but outside the candidate set. NOT refused: the candidate rule admits a button
					// whose handler issues a completing request or declares none, and a custom button that
					// finishes the step in its own code is legitimate — the caller may know something the rule
					// does not. Reported so the choice is deliberate rather than accidental.
					warnings.Add($"Button '{name}' exists on page '{pageName}' but is not among its "
						+ "completing-button candidates — its handler issues a request that does not complete the "
						+ "page. That is correct for a custom button which finishes the step in its own code; if "
						+ "it does not, the step waits after the button is pressed.");
				}
			}
		}
		return new ProcessPageButtonCheckResult(null, warnings);
	}

	/// <summary>A page's buttons in the two sets a name has to be tested against.</summary>
	private sealed record PageButtons(IReadOnlyCollection<string> All, IReadOnlyCollection<string> Candidates);

	private PageButtons ReadPageButtons(string environmentName, string pageName) {
		ProcessPageFactsOptions options = new() { SchemaName = pageName, Environment = environmentName };
		try {
			ProcessPageFactsCommand command = commandResolver.Resolve<ProcessPageFactsCommand>(options);
			if (!command.TryGetFacts(options, out ProcessPageFactsResponse response,
						out List<ProcessPageButton> allButtons)
					|| allButtons is null || response?.CompletingButtonCandidates is null) {
				return null;
			}
			return new PageButtons(Names(allButtons), Names(response.CompletingButtonCandidates));
		} catch (Exception) {
			// Reading the facts is a courtesy, never a new failure mode: whatever stopped it also stops the
			// build itself moments later, with the diagnosis that belongs to it.
			return null;
		}
	}

	private static List<string> Names(IEnumerable<ProcessPageButton> buttons) =>
		buttons.Select(button => button.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();

	private static List<string> ReadButtonNames(JsonArray buttons) {
		List<string> names = [];
		if (buttons is null) {
			return names;
		}
		foreach (JsonNode button in buttons) {
			string name = (button as JsonObject)?["name"]?.GetValue<string>();
			if (!string.IsNullOrWhiteSpace(name)) {
				names.Add(name.Trim());
			}
		}
		return names;
	}

	/// <summary>Every <c>preconfiguredPage</c> object anywhere in the payload.</summary>
	/// <remarks>
	/// Walked rather than addressed by path on purpose: the block sits at <c>elements[].preconfiguredPage</c> on a
	/// build, at <c>element.preconfiguredPage</c> under <c>addElement</c> and at
	/// <c>elementUpdate.preconfiguredPage</c> under <c>setElement</c>. One walk covers all three and does not
	/// acquire a fourth blind spot when an operation is added.
	/// </remarks>
	private static IEnumerable<JsonObject> FindPreconfiguredPageBlocks(JsonNode node) {
		switch (node) {
			case JsonObject obj:
				foreach (KeyValuePair<string, JsonNode> property in obj) {
					if (property.Value is JsonObject block
							&& string.Equals(property.Key, "preconfiguredPage", StringComparison.Ordinal)) {
						yield return block;
					}
					foreach (JsonObject nested in FindPreconfiguredPageBlocks(property.Value)) {
						yield return nested;
					}
				}
				break;
			case JsonArray array:
				foreach (JsonNode item in array) {
					foreach (JsonObject nested in FindPreconfiguredPageBlocks(item)) {
						yield return nested;
					}
				}
				break;
		}
	}

	private static string BuildRefusal(string pageName, string unknown,
			IReadOnlyCollection<string> allButtons) {
		string available = allButtons.Count == 0
			? "that page reports no buttons at all"
			: "the page carries: " + string.Join(", ", allButtons.Select(name => $"'{name}'"));
		return $"Page '{pageName}' has no button named '{unknown}'. A completing button is stored as the tag "
			+ "'<name>_clicked' and the run time matches the pressed button against it, so a name the page does "
			+ "not carry builds and saves green and then leaves the step waiting forever. Read the page's "
			+ $"candidates with get-process-page-facts and pass them unchanged ({available}).";
	}

}
