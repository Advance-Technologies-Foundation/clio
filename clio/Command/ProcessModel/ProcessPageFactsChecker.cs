using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Clio.Command.McpServer.Tools;

namespace Clio.Command.ProcessModel;

/// <summary>
/// Refuses a <c>preconfiguredPage</c> block that names a completing button or a data source the referenced page
/// does not have.
/// </summary>
/// <remarks>
/// <para>This check lives in clio and CANNOT live in the package, for the same reason
/// <c>get-process-page-facts</c> exists at all: a Freedom UI page is assembled from its template chain by the
/// CLIENT, so the server never sees the merged view config and cannot enumerate the page's buttons or data
/// sources even in principle. clio already merges that chain to build the page bundle, so the answer is already
/// in hand here.</para>
/// <para>Worth the round trip because of how both failures present without it — identically, and in the worst
/// possible way. A button name that exists nowhere on the page is stored as the tag <c>{name}_clicked</c>; a data
/// source the page does not declare is stored as the parameter tag
/// <c>DataSource:{name}:PrimaryColumnValue</c>. Either way the process builds green, saves green, reads back with
/// <c>inSync: true</c>, and then the step waits forever at run time: the button is never matched, or the
/// completion handler cannot resolve the data source and gives up before signalling the engine. Nothing is
/// logged where a caller would see it and the page simply stays open. An agent that invents a plausible name
/// instead of reading the facts is the ordinary case, not the exotic one.</para>
/// <para>Deliberately silent when the facts cannot be read: an unknown page, an unreachable environment or a
/// Classic page are all refused downstream with a message about THAT, and turning them into a button or
/// data-source complaint here would replace a precise diagnosis with a worse one.</para>
/// </remarks>
public interface IProcessPageFactsChecker {

	/// <summary>
	/// Checks every <c>preconfiguredPage</c> block's button and data-source names against the page they name.
	/// </summary>
	/// <param name="environmentName">The environment whose pages the names are checked against.</param>
	/// <param name="payload">A create descriptor or a modify operations array.</param>
	ProcessPageCheckResult CheckPreconfiguredPages(string environmentName, JsonNode payload);

}

/// <summary>
/// The outcome of a page-facts check: at most one refusal, plus any names that exist on the page but sit outside
/// the set the designer would offer.
/// </summary>
/// <param name="Error">The refusal, or <c>null</c> when nothing must be stopped.</param>
/// <param name="Warnings">Names present on the page but outside the offered set — reported, never refused.</param>
public sealed record ProcessPageCheckResult(string Error, IReadOnlyList<string> Warnings) {

	/// <summary>Nothing to say about this payload.</summary>
	public static readonly ProcessPageCheckResult Clean = new(null, []);

}

/// <inheritdoc />
public sealed class ProcessPageFactsChecker(IToolCommandResolver commandResolver) : IProcessPageFactsChecker {

	/// <inheritdoc />
	public ProcessPageCheckResult CheckPreconfiguredPages(string environmentName, JsonNode payload) {
		if (payload is null || string.IsNullOrWhiteSpace(environmentName)) {
			return ProcessPageCheckResult.Clean;
		}
		List<string> warnings = [];
		// One read per distinct page, not per name: a process routinely shows the same page from several steps.
		Dictionary<string, PageFacts> factsByPage = new(StringComparer.OrdinalIgnoreCase);
		foreach (JsonObject block in FindPreconfiguredPageBlocks(payload)) {
			string pageName = (block["page"]?.GetValue<string>() ?? string.Empty).Trim();
			if (pageName.Length == 0) {
				// A modify that changes only the recommendation carries no page; the buttons and data sources it
				// does not send are the ones already stored, which were checked when they were set.
				continue;
			}
			List<string> namedButtons = ReadNames(block["buttons"] as JsonArray);
			List<NamedDataSource> namedDataSources = ReadDataSources(block["dataSources"] as JsonArray);
			if (namedButtons.Count == 0 && namedDataSources.Count == 0) {
				continue;
			}
			if (!factsByPage.TryGetValue(pageName, out PageFacts facts)) {
				facts = ReadPageFacts(environmentName, pageName);
				factsByPage[pageName] = facts;
			}
			if (facts is null) {
				continue;
			}
			string error = CheckButtonNames(pageName, namedButtons, facts, warnings)
				?? CheckDataSourceNames(pageName, namedDataSources, facts, warnings);
			if (error is not null) {
				return new ProcessPageCheckResult(error, warnings);
			}
		}
		return new ProcessPageCheckResult(null, warnings);
	}

	#region Methods: Private

	/// <summary>
	/// The refusal for the first button name the page does not carry, or <c>null</c>. Names that are present but
	/// not candidates are appended to <paramref name="warnings"/>.
	/// </summary>
	private static string CheckButtonNames(string pageName, List<string> names, PageFacts facts,
			List<string> warnings) {
		foreach (string name in names) {
			// Ordinal on both tests: the name is stored verbatim and the run time matches the tag composed from
			// it, so a case-only difference is a button the page cannot raise.
			if (!facts.AllButtons.Contains(name, StringComparer.Ordinal)) {
				// Absent from the page ENTIRELY. This one is always broken — no handler can ever raise it — so it
				// is refused rather than reported.
				return BuildButtonRefusal(pageName, name, facts.AllButtons);
			}
			if (!facts.ButtonCandidates.Contains(name, StringComparer.Ordinal)) {
				// Present, but outside the candidate set. NOT refused: the candidate rule admits a button whose
				// handler issues a completing request or declares none, and a custom button that finishes the
				// step in its own code is legitimate — the caller may know something the rule does not. Reported
				// so the choice is deliberate rather than accidental.
				warnings.Add($"Button '{name}' exists on page '{pageName}' but is not among its "
					+ "completing-button candidates — its handler issues a request that does not complete the "
					+ "page. That is correct for a custom button which finishes the step in its own code; if "
					+ "it does not, the step waits after the button is pressed.");
			}
		}
		return null;
	}

	/// <summary>
	/// The refusal for the first data source the page does not declare, or <c>null</c>. Names present on the page
	/// but outside the offered set, and entity mismatches, are appended to <paramref name="warnings"/>.
	/// </summary>
	/// <remarks>
	/// Split refuse-versus-warn exactly like the buttons, and for a measured reason rather than symmetry. The run
	/// time resolves the stored name against the page view model's whole data-source map, so a name declared
	/// anywhere on the page resolves; a name declared nowhere resolves to nothing and the completion dies there.
	/// The facts, by contrast, report only the page-scoped entity sources — so the two sets differ, and refusing
	/// on the narrower one would block a caller who deliberately named a source the card does not offer.
	/// </remarks>
	private static string CheckDataSourceNames(string pageName, List<NamedDataSource> named, PageFacts facts,
			List<string> warnings) {
		// Unknown means "unreadable", not "none": null would make every name below look invented.
		if (facts.AllDataSourceNames is null) {
			return null;
		}
		foreach (NamedDataSource dataSource in named) {
			if (!facts.AllDataSourceNames.Contains(dataSource.Name, StringComparer.Ordinal)) {
				return BuildDataSourceRefusal(pageName, dataSource.Name, facts.OfferedDataSources.Keys);
			}
			if (!facts.OfferedDataSources.TryGetValue(dataSource.Name, out string pageEntity)) {
				warnings.Add($"Data source '{dataSource.Name}' is declared on page '{pageName}' but is not one of "
					+ "its page-scoped entity data sources — those are the ones the element's card offers, and the "
					+ "only ones that yield an element parameter the page fills. The step can still complete; the "
					+ "parameter generated for this source may never receive a value.");
				continue;
			}
			if (!string.IsNullOrWhiteSpace(dataSource.EntitySchemaName)
					&& !string.Equals(dataSource.EntitySchemaName, pageEntity, StringComparison.Ordinal)) {
				// Not a refusal: the run time reads the VALUE from the page's source either way, so the step
				// still completes. The entity only types the generated parameter — and typing it to the wrong
				// entity resolves the wrong primary column, so the value silently fails to arrive.
				warnings.Add($"Data source '{dataSource.Name}' on page '{pageName}' is over '{pageEntity}', not "
					+ $"'{dataSource.EntitySchemaName}'. The generated parameter is typed from the name you pass, "
					+ "so the wrong entity resolves the wrong primary column and the record id never reaches the "
					+ "process. Pass the entity get-process-page-facts reports.");
			}
		}
		return null;
	}

	/// <summary>A page's names in the sets a descriptor entry has to be tested against.</summary>
	/// <param name="AllButtons">Every button on the page, candidate or not.</param>
	/// <param name="ButtonCandidates">The buttons that may complete the page.</param>
	/// <param name="AllDataSourceNames">Every data source the page declares; <c>null</c> when unreadable.</param>
	/// <param name="OfferedDataSources">Page-scoped entity sources, name to entity schema name.</param>
	private sealed record PageFacts(
		IReadOnlyCollection<string> AllButtons,
		IReadOnlyCollection<string> ButtonCandidates,
		IReadOnlyCollection<string> AllDataSourceNames,
		IReadOnlyDictionary<string, string> OfferedDataSources);

	/// <summary>A data source as the descriptor names it.</summary>
	private sealed record NamedDataSource(string Name, string EntitySchemaName);

	private PageFacts ReadPageFacts(string environmentName, string pageName) {
		ProcessPageFactsOptions options = new() { SchemaName = pageName, Environment = environmentName };
		try {
			ProcessPageFactsCommand command = commandResolver.Resolve<ProcessPageFactsCommand>(options);
			if (!command.TryGetFacts(options, out ProcessPageFactsResponse response,
						out List<ProcessPageButton> allButtons, out List<string> allDataSourceNames)
					|| allButtons is null || response?.CompletingButtonCandidates is null) {
				return null;
			}
			Dictionary<string, string> offered = new(StringComparer.Ordinal);
			foreach (ProcessPageDataSource dataSource in response.DataSources ?? []) {
				if (!string.IsNullOrWhiteSpace(dataSource?.Name)) {
					offered[dataSource.Name] = dataSource.EntitySchemaName;
				}
			}
			return new PageFacts(Names(allButtons), Names(response.CompletingButtonCandidates),
				allDataSourceNames, offered);
		} catch (Exception) {
			// Reading the facts is a courtesy, never a new failure mode: whatever stopped it also stops the
			// build itself moments later, with the diagnosis that belongs to it.
			return null;
		}
	}

	private static List<string> Names(IEnumerable<ProcessPageButton> buttons) =>
		buttons.Select(button => button.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();

	private static List<string> ReadNames(JsonArray items) {
		List<string> names = [];
		if (items is null) {
			return names;
		}
		foreach (JsonNode item in items) {
			string name = (item as JsonObject)?["name"]?.GetValue<string>();
			if (!string.IsNullOrWhiteSpace(name)) {
				names.Add(name.Trim());
			}
		}
		return names;
	}

	private static List<NamedDataSource> ReadDataSources(JsonArray dataSources) {
		List<NamedDataSource> named = [];
		if (dataSources is null) {
			return named;
		}
		foreach (JsonNode item in dataSources) {
			if (item is not JsonObject dataSource) {
				continue;
			}
			string name = dataSource["name"]?.GetValue<string>();
			if (string.IsNullOrWhiteSpace(name)) {
				continue;
			}
			named.Add(new NamedDataSource(name.Trim(),
				dataSource["entitySchemaName"]?.GetValue<string>()?.Trim()));
		}
		return named;
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

	private static string BuildButtonRefusal(string pageName, string unknown,
			IReadOnlyCollection<string> allButtons) {
		string available = allButtons.Count == 0
			? "that page reports no buttons at all"
			: "the page carries: " + string.Join(", ", allButtons.Select(name => $"'{name}'"));
		return $"Page '{pageName}' has no button named '{unknown}'. A completing button is stored as the tag "
			+ "'<name>_clicked' and the run time matches the pressed button against it, so a name the page does "
			+ "not carry builds and saves green and then leaves the step waiting forever. Read the page's "
			+ $"candidates with get-process-page-facts and pass them unchanged ({available}).";
	}

	private static string BuildDataSourceRefusal(string pageName, string unknown,
			IEnumerable<string> offeredNames) {
		List<string> offered = offeredNames.ToList();
		string available = offered.Count == 0
			? "that page has no page-scoped entity data source, so pass no 'dataSources' at all"
			: "the page offers: " + string.Join(", ", offered.Select(name => $"'{name}'"));
		return $"Page '{pageName}' has no data source named '{unknown}'. The element stores it as the parameter "
			+ "tag 'DataSource:<name>:PrimaryColumnValue', and when the completing button is pressed the run time "
			+ "resolves that name against the page's own data sources to read the record id back. A name the page "
			+ "does not declare resolves to nothing and the completion is abandoned there — silently: the page "
			+ "stays open with no error and no validation message, and the process instance never leaves "
			+ $"'Running'. Read the page's data sources with get-process-page-facts ({available}).";
	}

	#endregion

}
