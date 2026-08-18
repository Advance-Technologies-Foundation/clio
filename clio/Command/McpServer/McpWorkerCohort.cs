using System;
using System.Collections.Generic;
using System.Linq;

namespace Clio.Command.McpServer;

/// <summary>
/// Which worker-classified tools actually execute in a worker process right now.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a cohort exists at all, when the ADR says membership is metadata.</b> It is metadata, and this
/// does not contradict that: 153 of the 189 tools already declare
/// <see cref="McpToolExecutionLocation.Worker"/>, because Stage 1 classified every tool with the location
/// it will EVENTUALLY have. Routing on the declaration alone would therefore move the whole catalog in one
/// step — including the destructive, deploy and sticky families whose supervision is Stage 7 and Stage 8
/// work that does not exist yet. The declaration says "this tool belongs in a worker"; the cohort says
/// "and the machinery it needs is already built". Stage 10 expands the cohort until it covers every
/// worker-classified tool, and then this type can be deleted.
/// </para>
/// <para>
/// <b>Still not a feature toggle</b> (ADR §5): the shipped membership is compile-time data with no runtime
/// switch, nothing reads <c>IFeatureToggleService</c>, and there is no <c>features</c> entry that turns it
/// off. The branch's own unit and end-to-end runs exercise the real worker path for every shipped member,
/// which is the property a default-off toggle would have destroyed. It is an INTERFACE only because ADR §5
/// also says cohort membership must be "substitutable in DI for tests" — TC-E-603's in-process arm and the
/// three-site agreement test both need to state a membership rather than inherit one.
/// </para>
/// </remarks>
public interface IMcpWorkerCohort {

	/// <summary>Gets the canonical names of the tools that route to a worker.</summary>
	IReadOnlySet<string> Names { get; }

	/// <summary>
	/// Determines whether a canonical tool name is in the cohort.
	/// </summary>
	/// <param name="routingKey">
	/// The canonical tool name, ALREADY unwrapped from <c>clio-run</c> and canonicalised from any
	/// deprecated alias (ADR rule 7). Passing a raw caller-supplied name would make a cohort tool reached
	/// through the executor or an alias read as a non-member.
	/// </param>
	/// <returns><see langword="true"/> when the tool is a cohort member.</returns>
	bool Contains(string routingKey);
}

/// <inheritdoc cref="IMcpWorkerCohort"/>
public sealed class McpWorkerCohort : IMcpWorkerCohort {

	/// <summary>
	/// The Stage 6 cohort: the retry-safe stdio reads named in story 6 and ADR §5 — precisely the commands
	/// agents were forced off MCP onto the CLI in
	/// <see href="https://github.com/Creatio-Platform/creatio-ai-app-development-toolkit/pull/93"/>, which
	/// is what makes them the ones whose repair is worth proving.
	/// </summary>
	/// <remarks>
	/// Adding a name here is a deliberate cohort expansion and must come with the supervision its
	/// <see cref="McpToolExecutionMetadata"/> asks for. Everything below is
	/// <see cref="McpToolExecutionLifetime.PerCall"/> + <see cref="McpToolBudgetPolicy.ParentKillDefault"/>,
	/// which is the only combination the parent can bound today: sticky lifetimes are story 7 and
	/// terminal-stage bounding is story 8.
	/// </remarks>
	public static readonly IReadOnlyList<string> StageSixNames = [
		Tools.PageGetTool.ToolName,                // get-page
		Tools.PageListTool.ToolName,               // list-pages
		Tools.ApplicationSectionGetListTool.ApplicationSectionGetListToolName, // list-app-sections
		Tools.GetSchemaTool.ToolName,              // get-schema
		Tools.GetRelatedPageAddonTool.ToolName,    // get-related-page-addon
		Tools.ExecuteEsqTool.ToolName,             // execute-esq  — the SQL read
		Tools.ODataReadTool.ToolName               // odata-read   — the OData read
	];

	private readonly IReadOnlySet<string> _names;

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerCohort"/> class over the shipped
	/// <see cref="StageSixNames"/>. Used by DI.
	/// </summary>
	public McpWorkerCohort()
		: this(StageSixNames) {
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerCohort"/> class over an explicit membership,
	/// so a test can state which tools route without changing the shipped cohort.
	/// </summary>
	/// <remarks>
	/// INTERNAL on purpose. The container picks the public constructor with the most parameters it can
	/// satisfy, and it satisfies <c>IEnumerable&lt;string&gt;</c> with an EMPTY sequence for any unregistered
	/// element type — so a public overload here would silently hand production an empty cohort and every
	/// tool would quietly stay in-process. Keeping it internal makes the parameterless constructor the only
	/// one DI can see.
	/// </remarks>
	/// <param name="names">The canonical tool names that route to a worker.</param>
	/// <exception cref="ArgumentNullException"><paramref name="names"/> is <see langword="null"/>.</exception>
	internal McpWorkerCohort(IEnumerable<string> names) {
		ArgumentNullException.ThrowIfNull(names);
		_names = names
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => name.Trim())
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
	}

	/// <inheritdoc/>
	public IReadOnlySet<string> Names => _names;

	/// <inheritdoc/>
	public bool Contains(string routingKey) =>
		!string.IsNullOrWhiteSpace(routingKey) && _names.Contains(routingKey.Trim());
}
