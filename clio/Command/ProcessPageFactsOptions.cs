namespace Clio.Command;

using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

[Verb("get-process-page-facts", Aliases = ["page-facts"],
	HelpText = "Read the facts a Pre-configured page process element needs about a Freedom UI page")]
public class ProcessPageFactsOptions : EnvironmentOptions {
	[Option("schema-name", Required = true, HelpText = "Freedom UI page schema name, e.g. 'UsrMyApp_FormPage'")]
	public string SchemaName { get; set; }

	[Option("culture", Required = false,
		HelpText = "Culture used to resolve resource-backed button captions (default en-US)")]
	public string Culture { get; set; }
}

/// <summary>
/// Reports the facts a Pre-configured page process element needs about a Freedom UI page: which buttons can
/// complete the page, and which page-scoped entity data sources it has.
/// <para>Why this exists in clio rather than in the CrtProcessBuilder package: both answers are only knowable from
/// the MERGED page, because a page inherits buttons from its template chain. The platform performs that merge
/// client-side in the process designer, and exposes no server-side merged-view API — whereas clio already merges
/// the chain to produce a page bundle. So the package writes the process element, and clio supplies the page facts
/// it cannot see. See <see cref="ProcessPageFactsProjection"/> for the selection rules and where they come from.
/// </para>
/// </summary>
public class ProcessPageFactsCommand : Command<ProcessPageFactsOptions> {

	#region Fields: Private

	private readonly PageGetCommand _pageGetCommand;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public ProcessPageFactsCommand(PageGetCommand pageGetCommand, ILogger logger) {
		_pageGetCommand = pageGetCommand;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	/// <summary>
	/// Reads the page and projects its process-page facts. Returns <c>false</c> with a populated
	/// <paramref name="response"/> error rather than throwing, so the MCP surface can report a failure as data.
	/// </summary>
	public bool TryGetFacts(ProcessPageFactsOptions options, out ProcessPageFactsResponse response) {
		if (string.IsNullOrWhiteSpace(options.SchemaName)) {
			response = new ProcessPageFactsResponse { Success = false, Error = "schema-name is required." };
			return false;
		}
		PageGetOptions pageOptions = new() {
			SchemaName = options.SchemaName,
			Environment = options.Environment,
			Uri = options.Uri,
			Login = options.Login,
			Password = options.Password
		};
		if (!_pageGetCommand.TryGetPage(pageOptions, out PageGetResponse page) || page?.Bundle is null) {
			response = new ProcessPageFactsResponse {
				Success = false,
				SchemaName = options.SchemaName,
				Error = page?.Error ?? $"Page '{options.SchemaName}' could not be read."
			};
			return false;
		}
		// The page must be a Freedom UI one: a Classic page has no merged view config to read buttons from, and the
		// process element completes it through its own page-designer buttons instead. Saying so is more useful than
		// reporting an empty candidate list, which reads as "this page has no buttons".
		if (page.Page is not null
			&& !string.Equals(page.Page.SchemaType, PageSchemaType.Web.ToLabel(), StringComparison.Ordinal)) {
			response = new ProcessPageFactsResponse {
				Success = false,
				SchemaName = options.SchemaName,
				Error = $"Page '{options.SchemaName}' is not a Freedom UI web page (schema type "
					+ $"'{page.Page.SchemaType}'), so it has no completing-button candidates to report."
			};
			return false;
		}
		// The bundle model serializes with System.Text.Json; the projection reads Newtonsoft nodes so it can be
		// tested against the exact JSON an agent sees in bundle.json. One round-trip is cheaper than two parsers.
		JObject bundle = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(page.Bundle));
		(List<ProcessPageButton> buttons, List<ProcessPageDataSource> dataSources) =
			ProcessPageFactsProjection.Project(bundle, options.Culture);
		response = new ProcessPageFactsResponse {
			Success = true,
			SchemaName = page.Page?.SchemaName ?? options.SchemaName,
			CompletingButtonCandidates = buttons.Where(ProcessPageFactsProjection.IsCompletingCandidate).ToList(),
			DataSources = dataSources
		};
		return true;
	}

	/// <inheritdoc />
	public override int Execute(ProcessPageFactsOptions options) {
		bool success = TryGetFacts(options, out ProcessPageFactsResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response, Formatting.Indented));
		return success ? 0 : 1;
	}

	#endregion

}
