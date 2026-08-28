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
/// The one page-reading operation <see cref="ProcessPageFactsCommand"/> needs, extracted as a seam so the command
/// is testable: <see cref="PageGetCommand"/> is concrete and pulls six collaborators, which made the two guards
/// that decide "facts or refusal" the only code on this surface with zero unit coverage.
/// </summary>
public interface IProcessPageReader {
	/// <inheritdoc cref="PageGetCommand.TryGetPage"/>
	bool TryGetPage(PageGetOptions options, out PageGetResponse response);
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

	private readonly IProcessPageReader _pageReader;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public ProcessPageFactsCommand(IProcessPageReader pageReader, ILogger logger) {
		_pageReader = pageReader;
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
		if (!_pageReader.TryGetPage(pageOptions, out PageGetResponse page) || page?.Bundle is null) {
			response = new ProcessPageFactsResponse {
				Success = false,
				SchemaName = options.SchemaName,
				Error = page?.Error ?? $"Page '{options.SchemaName}' could not be read."
			};
			return false;
		}
		// The page must be a Freedom UI web page: a Classic page has no merged view config to read buttons from,
		// and the process element completes it through its own page-designer buttons instead. Saying so is more
		// useful than reporting an empty candidate list, which reads as "this page has no buttons".
		// The numeric schema type LABELS everything that is not positively web/mobile — a Classic page, but ALSO a
		// platform that simply omitted the value — as "unknown", and the raw numeric tells those apart: present but
		// non-web is a positive identification and refuses outright, while body inference runs only when the value
		// is genuinely absent — refusing on "the platform did not tell us" would turn one absent field into a
		// refusal for every page on that environment.
		PageSchemaType resolvedType = ResolvePageType(page);
		if (resolvedType != PageSchemaType.Web) {
			string reason = resolvedType == PageSchemaType.Mobile
				? "it is a MOBILE page"
				: "it could not be positively identified as one — a Classic UI page reads back this way";
			response = new ProcessPageFactsResponse {
				Success = false,
				SchemaName = options.SchemaName,
				Error = $"Page '{options.SchemaName}' is not a Freedom UI web page ({reason}), so it has no "
					+ "completing-button candidates to report. A Classic UI page completes through its own "
					+ "page-designer buttons instead."
			};
			return false;
		}
		// The bundle model serializes with System.Text.Json; the projection reads Newtonsoft nodes so it can be
		// tested against the exact JSON an agent sees in bundle.json. One round-trip is cheaper than two parsers.
		// Guarded: TryGetPage catches its own failures, but the serialize/parse/project chain below runs OUTSIDE
		// that boundary, and an exception escaping here would leave the MCP surface with a raw, unredacted error.
		List<ProcessPageButton> buttons;
		List<ProcessPageDataSource> dataSources;
		try {
			JObject bundle = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(page.Bundle));
			(buttons, dataSources) = ProcessPageFactsProjection.Project(bundle, options.Culture);
		} catch (Exception projectionError) {
			response = new ProcessPageFactsResponse {
				Success = false,
				SchemaName = options.SchemaName,
				Error = $"Page '{options.SchemaName}' was read but its merged bundle could not be projected: "
					+ $"{projectionError.Message}"
			};
			return false;
		}
		List<ProcessPageButton> candidates =
			buttons.Where(ProcessPageFactsProjection.IsCompletingCandidate).ToList();
		response = new ProcessPageFactsResponse {
			Success = true,
			SchemaName = page.Page?.SchemaName ?? options.SchemaName,
			CompletingButtonCandidates = candidates,
			DataSources = dataSources,
			// An empty candidate list on a page that PASSED the web-page guard is ambiguous — the page may
			// genuinely have no buttons, or the merged bundle's shape may not be one the projection recognises —
			// and silence here reads as the first. Said explicitly, because a Pre-configured page element built
			// with no completing button can never finish at run time.
			Warnings = candidates.Count > 0
				? null
				: new List<string> {
					$"No completing-button candidates were found on '{options.SchemaName}'. Either the page "
					+ "genuinely has no buttons, or the merged bundle's shape was not recognised — verify in the "
					+ "page designer before building a Pre-configured page element on it, because an element "
					+ "without a completing button can never finish at run time."
				}
		};
		return true;
	}

	/// <summary>
	/// Resolves the page's UI generation from the label, then the raw numeric type, then — only when the numeric
	/// is absent — body inference: the LABEL maps everything but web/mobile to Unknown, a Classic page and a
	/// missing value alike, and it is the raw <see cref="PageMetadataInfo.SchemaTypeValue"/> that tells those
	/// apart.
	/// <para>The order of evidence matters, and both shortcuts were measured wrong on a live stand. A PRESENT
	/// numeric type that is neither web nor mobile is a POSITIVE identification — a Classic page, a module — and
	/// is refused without looking at the body. The body is consulted only when the numeric is genuinely absent,
	/// and even then a shape test is not enough: a Classic body is an AMD <c>define(...)</c> module too, and the
	/// <c>viewConfigDiff</c> marker alone is not proof either, because <c>get-page</c> SYNTHESIZES a marker-bearing
	/// empty body whenever the page has no editable schema — clio would be trusting evidence it planted itself
	/// (measured: <c>ProcessModuleV2</c>, numeric type present, synthesized body, marker and all).</para>
	/// </summary>
	private static PageSchemaType ResolvePageType(PageGetResponse page) {
		string label = page.Page?.SchemaType;
		if (string.Equals(label, PageSchemaType.Web.ToLabel(), StringComparison.Ordinal)) {
			return PageSchemaType.Web;
		}
		if (string.Equals(label, PageSchemaType.Mobile.ToLabel(), StringComparison.Ordinal)) {
			return PageSchemaType.Mobile;
		}
		if (page.Page?.SchemaTypeValue is not null) {
			// The platform DID say what this is, and it is neither web nor mobile.
			return PageSchemaType.Unknown;
		}
		string body = page.Raw?.Body;
		if (PageSchemaTypeExtensions.FromBody(body) == PageSchemaType.Mobile) {
			return PageSchemaType.Mobile;
		}
		bool hasFreedomMarkers = body?.Contains("viewConfigDiff", StringComparison.Ordinal) == true;
		return hasFreedomMarkers ? PageSchemaType.Web : PageSchemaType.Unknown;
	}

	/// <inheritdoc />
	public override int Execute(ProcessPageFactsOptions options) {
		bool success = TryGetFacts(options, out ProcessPageFactsResponse response);
		_logger.WriteInfo(JsonConvert.SerializeObject(response, Formatting.Indented));
		return success ? 0 : 1;
	}

	#endregion

}
