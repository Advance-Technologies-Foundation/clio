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
	/// <para>
	/// Adding a name here is a deliberate cohort expansion and must come with the supervision its
	/// <see cref="McpToolExecutionMetadata"/> asks for. Everything below is
	/// <see cref="McpToolExecutionLifetime.PerCall"/> + <see cref="McpToolBudgetPolicy.ParentKillDefault"/>,
	/// which was the only combination the parent could bound at Stage 6: sticky lifetimes are story 7 and
	/// terminal-stage bounding is story 8 (<see cref="StageEightNames"/>).
	/// </para>
	/// <para>
	/// <b>A SECOND membership requirement, learned the hard way on 2026-08-19 and not derivable from the
	/// metadata: what the read goes through on the SERVER.</b> A worker authenticates on its own — the
	/// cookie container is per-process — so a read moved into a child speaks on a different Creatio
	/// session from the host-resident tools that WRITE the same object. Reads served out of Creatio's
	/// in-memory schema manager are not guaranteed to reflect a write made moments earlier on another
	/// session, and the branch's own end-to-end tests measured it: a worker <c>get-page</c> returned a
	/// body 14 s out of date, which armed a conflict baseline against a superseded generation. That is a
	/// silent-overwrite defect, not a slow read.
	/// </para>
	/// <para>
	/// So the line is drawn by the READ PATH, not by the tool:
	/// </para>
	/// <list type="bullet">
	/// <item><description>
	/// <b>Schema-designer reads are EXCLUDED</b> — <c>get-page</c>
	/// (<c>ClientUnitSchemaDesignerService/GetParentSchemas</c>), <c>get-schema</c> and
	/// <c>get-related-page-addon</c> (both through the schema designer). They are served from the
	/// server's schema cache, and their writers (<c>update-page</c>, <c>sync-pages</c>,
	/// <c>create-related-page-addon</c>) are host-resident, so read-after-write inside one agent session
	/// is exactly what an agent relies on and exactly what the boundary breaks.
	/// </description></item>
	/// <item><description>
	/// <b>Database reads STAY</b> — <c>list-pages</c> and <c>list-app-sections</c> (DataService /
	/// <c>Select</c>), <c>execute-esq</c> and <c>odata-read</c>. These go to the shared database rather
	/// than to a per-process cache, so a second session sees the same rows the first one wrote.
	/// </description></item>
	/// </list>
	/// <para>
	/// The mechanism behind the staleness is INFERRED, not measured: the plausible reading is session
	/// affinity — the session cookie pins a caller to one IIS worker process, each with its own schema
	/// cache generation, and a fresh login can land on one whose cache has not been invalidated. It is
	/// recorded as inference on purpose. The membership rule above does not depend on which cache lags,
	/// only on the fact that a schema-designer read crossed a session boundary and came back old.
	/// </para>
	/// </remarks>
	public static readonly IReadOnlyList<string> StageSixNames = [
		Tools.PageListTool.ToolName,               // list-pages    — DataService, reads the database
		Tools.ApplicationSectionGetListTool.ApplicationSectionGetListToolName, // list-app-sections — Select
		Tools.ExecuteEsqTool.ToolName,             // execute-esq   — the SQL read
		Tools.ODataReadTool.ToolName               // odata-read    — the OData read
	];

	/// <summary>
	/// The schema-designer reads that Stage 6 shipped and 2026-08-19 WITHDREW: worker-routing them breaks
	/// read-after-write for the host-resident tools that write the same schema.
	/// </summary>
	/// <remarks>
	/// Kept as a named list rather than deleted, so the exclusion is a recorded decision with a reason
	/// attached instead of an absence somebody re-fills. Re-admitting any of these requires the worker to
	/// speak on the PARENT'S Creatio session — see the membership rule on <see cref="StageSixNames"/>.
	/// </remarks>
	public static readonly IReadOnlyList<string> SchemaDesignerReadsWithheldNames = [
		Tools.PageGetTool.ToolName,                // get-page
		Tools.GetSchemaTool.ToolName,              // get-schema
		Tools.GetRelatedPageAddonTool.ToolName     // get-related-page-addon
	];

	/// <summary>
	/// The Stage 7 addition: the four long-running families whose worker is STICKY — each family's
	/// starter together with the status poller that must reach the same worker.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>A family is added WHOLE or not at all.</b> A starter without its poller would put the operation
	/// in a worker and leave the poll answering from an empty registry in another process, which is worse
	/// than not moving it; a poller without its starter would reach nothing, every time.
	/// </para>
	/// <para>
	/// <b>These names could not be here before Stage 7.</b> Each needs a worker that outlives its
	/// response, a lookup that reaches that worker WITHOUT taking an admission slot (ADR §3.2c — the
	/// alternative is hold-and-wait on the very worker being polled), a private completion signal to reap
	/// on (rule 5, because three of the four families have no terminal status), an explicit lifetime bound
	/// (T-8), and — for the configuration-build family — a reservation owned by the PARENT, since the
	/// tool-side one would live in whichever child ran the tool and exclude nothing.
	/// </para>
	/// <para>
	/// <b><c>restart-by-credentials</c> is in the list although <c>restart-status</c> cannot report it.</b>
	/// It is the same family and the same sticky worker: the readiness wait outlives the response there
	/// too, and its unreportability changes the poll story, not how the call executes. It is also the
	/// family member that most needs the private completion signal, being the one with no terminal status
	/// anywhere.
	/// </para>
	/// </remarks>
	public static readonly IReadOnlyList<string> StageSevenNames = [
		Tools.CompileCreatioTool.CompileCreatioToolName,          // compile-creatio
		Tools.CompileStatusTool.CompileStatusToolName,            // compile-status
		Tools.RestartTool.RestartByEnvironmentNameToolName,       // restart-by-environment-name
		Tools.RestartTool.RestartByCredentialsToolName,           // restart-by-credentials
		Tools.RestartStatusTool.RestartStatusToolName             // restart-status
	];

	/// <summary>
	/// Stage 7's machinery supports these two, and Stage 7 deliberately does NOT ship them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Sticky supervision was built for four long-running families, and these are two of them — the
	/// private completion signal exists precisely because neither has an operation registry to reap on.
	/// The machinery is complete and tested for them. What is withheld is MEMBERSHIP.
	/// </para>
	/// <para>
	/// <b>Why.</b> A sticky worker is still killed at its lifetime bound, and the kill-safety audit lists
	/// both of these under "unsafe to kill — a kill leaves durable damage nothing repairs":
	/// <c>install-process-builder</c> produces exactly the "installed but never compiled" state the tool
	/// exists to DETECT, whose documented recovery is a restore from backup rather than a rollback; and
	/// <c>create-app-section</c> orphans an in-flight insert and loses the "in progress, do NOT retry,
	/// poll list-app-sections" envelope that is an agent's only documented recovery path. Routing them
	/// here would convert an operation the shipped code cannot kill into one that can be killed into an
	/// unrecoverable state.
	/// </para>
	/// <para>
	/// ADR §2.5 already reserves this call: cohort expansion is "a decision to be made against this table,
	/// tool by tool — not a formality once the machinery exists". Stage 10 owns that pass, with the audit
	/// in front of it. Moving a name from here into <see cref="StageSevenNames"/> is then a deliberate,
	/// reviewable line rather than something that arrived with the plumbing.
	/// </para>
	/// </remarks>
	public static readonly IReadOnlyList<string> StageSevenSupportedButNotShippedNames = [
		Tools.InstallProcessBuilderTool.InstallProcessBuilderToolName, // install-process-builder
		Tools.ApplicationSectionCreateTool.ApplicationSectionCreateToolName // create-app-section
	];

	/// <summary>
	/// The Stage 8 addition: the two <see cref="McpToolBudgetPolicy.TerminalStage"/> tools — the whole
	/// deploy family, since the cross-field invariant pins <c>Deploy ⇒ Worker + TerminalStage</c>.
	/// </summary>
	/// <remarks>
	/// <b>These names could not be added before the protocol existed.</b> The dispatcher kills an ordinary
	/// worker at its budget unconditionally, and a deploy killed at a stopwatch can leave a half-installed
	/// environment — the one place where terminating the process is the wrong tool (ADR rule 4). They are
	/// here only because <c>McpWorkerCallDispatcher</c> now bounds them by the run's own
	/// <c>run-completed</c> stage event, by a stage-event SILENCE timer that every stage restarts, and by a
	/// post-terminal exit grace (ADR §3.3).
	/// </remarks>
	public static readonly IReadOnlyList<string> StageEightNames = [
		Tools.InstallerCommandTool.DeployCreatioToolName, // deploy-creatio
		Tools.UninstallCreatioTool.UninstallCreatioToolName // uninstall-creatio
	];

	/// <summary>
	/// The membership this build actually ships: <see cref="StageSixNames"/>, <see cref="StageSevenNames"/>
	/// and <see cref="StageEightNames"/>. They are kept as separate lists rather than merged into one so
	/// that each stage's promise stays independently readable — and independently pinnable by a test.
	/// </summary>
	public static readonly IReadOnlyList<string> ShippedNames =
		[.. StageSixNames, .. StageSevenNames, .. StageEightNames];

	private readonly IReadOnlySet<string> _names;

	/// <summary>
	/// Initializes a new instance of the <see cref="McpWorkerCohort"/> class over the shipped
	/// <see cref="ShippedNames"/>. Used by DI.
	/// </summary>
	public McpWorkerCohort()
		: this(ShippedNames) {
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
