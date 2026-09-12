using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Clio.Command.McpServer;
using Clio.Common;
using Clio.Package;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Outcome of one dependency-candidate lookup for an entity schema the designer could not open in the
/// target package. Reporting only: this lookup never changes the environment.
/// </summary>
/// <param name="Candidates">
/// Packages that contribute <c>schemaName</c>, ranked: installed applications first, then the rest. The
/// target package itself and every package it already depends on are excluded, so every entry is something
/// the caller could actually add. Empty when nothing was found.
/// </param>
/// <param name="ApplicationCandidateCount">
/// How many leading <paramref name="Candidates"/> entries are installed applications. Zero means the ranking
/// signal was unavailable (or matched nothing), and the order carries no recommendation.
/// </param>
/// <param name="LookupSucceeded">
/// <see langword="false"/> when the candidate search itself failed, so an empty
/// <paramref name="Candidates"/> list is the absence of an answer rather than the answer "no package
/// contributes this schema". A caller that reports the two identically states a finding of fact it never
/// established (issue #722).
/// </param>
/// <param name="DependenciesKnown">
/// <see langword="false"/> when the target package's declared dependencies could not be read, so the
/// subtraction that removes already-declared packages was a no-op and <paramref name="Candidates"/> may
/// still contain them. The caller must carry that caveat into the message it surfaces, not only into a log
/// warning an MCP client never sees.
/// </param>
/// <param name="LookupFailureReason">
/// Redacted, bounded description of why the candidate search failed; <see langword="null"/> whenever
/// <paramref name="LookupSucceeded"/> is <see langword="true"/>.
/// </param>
public sealed record EntitySchemaDependencyResolution(
	IReadOnlyList<string> Candidates,
	int ApplicationCandidateCount,
	bool LookupSucceeded,
	bool DependenciesKnown,
	string? LookupFailureReason = null)
{

	/// <summary>
	/// The lookup ran and found nothing to report - a completed search with an empty answer.
	/// </summary>
	public static EntitySchemaDependencyResolution None { get; } = new([], 0, true, true);

	/// <summary>Creates the result for a candidate search that could not be completed.</summary>
	/// <param name="reason">Redacted, bounded description of the failure.</param>
	/// <returns>A resolution carrying no candidates and <c>LookupSucceeded: false</c>.</returns>
	public static EntitySchemaDependencyResolution LookupFailed(string reason) =>
		new([], 0, false, true, reason);

}

/// <summary>
/// Reports which packages could supply an entity schema the designer could not open in the target package
/// (the <c>SchemaIsNotAvailableException</c> that surfaces as an HTML error page from
/// <c>GetSchemaDesignItem</c>).
/// </summary>
/// <remarks>
/// Reporting only - nothing here writes. The predecessor of this type added the dependency itself when
/// exactly one candidate remained. That was removed once the failing body was captured from a stand: it is
/// a generic WCF "Request Error" page naming no exception, no schema and no package, so nothing in the
/// response distinguishes a missing dependency from a WAF block, a 502, or a transient server fault. A
/// write cannot be gated on evidence that does not exist, and a dependency added on a transient fault that
/// then clears looks like a success while leaving the package permanently changed. The caller gets the
/// ranked list and the exact <c>add-package-dependency</c> invocation instead.
/// </remarks>
public interface IEntitySchemaDependencyResolver
{

	/// <summary>
	/// Determines which packages could supply <paramref name="schemaName"/> to
	/// <paramref name="targetPackageName"/>.
	/// </summary>
	/// <remarks>
	/// The target package is identified by UId as well as by name because every caller is holding the UId
	/// already - it is what the designer request was scoped to. Passing only the name would make the
	/// dependency read resolve it again through the full installed-package list, one extra round-trip on a
	/// path that is already failing.
	/// </remarks>
	/// <param name="schemaName">Entity schema name that was unavailable (for example <c>Opportunity</c>).</param>
	/// <param name="targetPackageUId">Identifier of the package that is being edited.</param>
	/// <param name="targetPackageName">Package that is being edited (for example <c>Custom</c>).</param>
	/// <returns>The candidates found, and whether the searches behind them actually completed.</returns>
	EntitySchemaDependencyResolution Resolve(string schemaName, Guid targetPackageUId, string targetPackageName);

}

/// <inheritdoc cref="IEntitySchemaDependencyResolver"/>
internal sealed class EntitySchemaDependencyResolver : IEntitySchemaDependencyResolver
{

	/// <summary>Installed-application column read to rank candidates: <c>Code</c> is the root package name.</summary>
	private static readonly IReadOnlyList<SelectQueryColumnDefinition> InstalledAppColumns =
	[
		new("Code", "Code")
	];

	/// <summary>
	/// Bound on every read this class adds to a path that is already failing - the schema search, the
	/// dependency read and the installed-application read alike. They exist to enrich an error message, so a
	/// stand that accepts the connection and then stops answering must cost the caller a bounded wait, not a
	/// hung tool call: <c>ExecuteSelectQuery</c>, <c>IApplicationPackageListProvider.GetPackages</c> and
	/// <c>BasePackageOperation.SendRequest</c> all default to
	/// <see cref="System.Threading.Timeout.Infinite"/>, and a wedged read inside the enrichment would hold a
	/// long-lived MCP server tenant open indefinitely.
	/// </summary>
	private const int DiagnosticReadTimeoutMs = 30_000;

	/// <summary>Upper bound on the failure text embedded in a log warning.</summary>
	private const int MaxLoggedFailureLength = 300;

	/// <summary>
	/// Upper bound on the wait for the offloaded schema search, covering its own read bound PLUS the delay
	/// before the thread pool schedules it at all - which no per-request timeout can cap.
	/// </summary>
	/// <remarks>
	/// Settable only so a test can prove the guard without blocking for a minute; production never assigns
	/// it. Same kind of seam as <c>ReauthExecutor.LoginVersion</c>.
	/// </remarks>
	internal int ContributorsWaitTimeoutMs { get; set; } = DiagnosticReadTimeoutMs * 2;

	private readonly FindEntitySchemaCommand _findCommand;
	private readonly IPackageDependencyManager _dependencyManager;
	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	public EntitySchemaDependencyResolver(FindEntitySchemaCommand findCommand,
		IPackageDependencyManager dependencyManager, IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder, ILogger logger) {
		_findCommand = findCommand;
		_dependencyManager = dependencyManager;
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	/// <inheritdoc/>
	public EntitySchemaDependencyResolution Resolve(string schemaName, Guid targetPackageUId,
		string targetPackageName) {
		try {
			// The two independent reads run at the same time, so the diagnostic costs max(read1, read2)
			// rather than read1 + read2. On an environment that accepts the connection and then stops
			// answering that is the difference between one DiagnosticReadTimeoutMs wait and two, which is
			// what pushed this path towards the MCP client's own ceiling and turned an enriched message into
			// an opaque abort.
			//
			// The resolver stays SYNCHRONOUS and blocks once, here. Making it asynchronous would propagate
			// async through LoadSchema and the whole MCP tool -> command -> manager chain above it, for a path
			// that runs only when something has already failed; nothing in project-context.md or the analyzer
			// configuration forbids blocking, and clio already does it (HostsCommand,
			// CompileConfigurationCommand, InstallProcessBuilderCommand and ~90 other GetAwaiter().GetResult()
			// sites).
			//
			// Exactly ONE read is offloaded, for two separate reasons. First, ReadExistingDependencies handles
			// its own failures and never throws, so running it on the calling thread holds no pool thread.
			// Second, awaiting a SINGLE task with GetAwaiter().GetResult() rethrows the original exception,
			// while Task.WhenAll would not do: .Wait()/.Result on it wraps the fault in an AggregateException
			// whose text buries the reason the environment gave, and GetAwaiter().GetResult() on it surfaces
			// only the first fault - which would turn the dependency read's "degrade, do not throw" contract
			// into a thrown failure whenever it lost the race.
			//
			// Two requests are therefore in flight on the shared IApplicationClient. That is a supported
			// state: CreatioClientAdapter guards its Lazy<CreatioClient> with ExecutionAndPublication and a
			// lifetime lock, ReauthExecutor exists precisely so "a parallel burst of failing requests triggers
			// exactly one Login", and LoginDiagnostics counts RequestsInFlight with Interlocked. Two facts this
			// method does not state - that the dependency read now runs even when nothing contributes the
			// schema, and what a warning written on the offloaded thread depends on to reach an MCP response -
			// are in
			// docs/knowledge/Command/the-dependency-diagnosis-runs-two-reads-in-parallel-off-a-sync-method.md.
			Task<List<string>> contributorsTask = Task.Run(
				() => Measure(() => FindContributingPackages(schemaName, targetPackageName), "schema search"));
			(HashSet<string> existingDependencies, bool dependenciesKnown, string? dependencyFailureReason) =
				Measure(() => ReadExistingDependencies(targetPackageUId, targetPackageName), "dependency read");
			// Bounded even though the read inside is bounded: the task has to be SCHEDULED before its own
			// timeout starts running, and on a saturated thread pool the injection delay is unbounded. Twice
			// the read's own budget leaves the read itself room to answer while still capping this wait.
			// Waited through the handle rather than with Task.Wait(int) on purpose - Wait throws an
			// AggregateException for a faulted task, and this path has to surface the exception the
			// environment actually raised, which the GetAwaiter().GetResult() below does.
			if (!((IAsyncResult)contributorsTask).AsyncWaitHandle.WaitOne(ContributorsWaitTimeoutMs)) {
				throw new TimeoutException(
					"The lookup of the packages that contribute the schema did not finish within "
					+ $"{ContributorsWaitTimeoutMs} ms.");
			}
			List<string> contributors = contributorsTask.GetAwaiter().GetResult();
			if (contributors.Count == 0) {
				return EntitySchemaDependencyResolution.None;
			}
			List<string> candidates = contributors
				.Where(name => !existingDependencies.Contains(name))
				.ToList();
			if (dependenciesKnown && candidates.Count == 0) {
				// Every package that contributes the schema is already a dependency, so a missing dependency
				// is NOT what the caller is looking at. Saying nothing is the correct answer here.
				return EntitySchemaDependencyResolution.None;
			}
			// Read three keeps its original condition. Gating it on "more than one candidate" would save a
			// round-trip on the single-candidate case but silently change the surfaced text: the message
			// switches on ApplicationCandidateCount, so a lone candidate that IS an installed application is
			// reported as ", installed applications first: X" today and would become ": X". It cannot DOUBLE
			// the wait either - a stand can answer the schema search and then hang on this one, costing up to
			// one more DiagnosticReadTimeoutMs, but it is never reached unless read one already answered.
			HashSet<string> applicationPackages =
				Measure(ReadInstalledApplicationPackages, "installed applications");
			List<string> ranked = Rank(candidates, applicationPackages, out int applicationCandidateCount);
			// The dependency read's failure is reported HERE and nowhere else. It is written now that a
			// candidate list actually exists, because the warning describes that list ("may include packages
			// that are already dependencies") and because it does reach an MCP client: BaseTool sets
			// PreserveMessages and copies the captured lines into the tool result on both the success and the
			// failure path. Written where the read fails, it would reach a caller whose lookup produced no
			// list at all and describe one that was never built.
			if (!dependenciesKnown) {
				_logger.WriteWarning(
					$"Could not read the current dependencies of package '{targetPackageName}': " +
					$"{dependencyFailureReason}. " +
					"The candidate list may include packages that are already dependencies.");
			}
			return new EntitySchemaDependencyResolution(ranked, applicationCandidateCount, true,
				dependenciesKnown);
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Broad catch is intentional: FindSchemas can fail with HttpRequestException, JsonException,
			// InvalidOperationException, or ArgumentException depending on the remote state. None of these
			// should abort the caller - the enriched error message in LoadSchema takes over. The failure is
			// carried in the RESULT rather than only in this warning: the CLI prints the warning, but the
			// message LoadSchema builds is the only thing an MCP agent acts on, so a caller told just "no
			// candidates" would read a search that never ran as a finding of fact.
			string reason = DescribeFailure(ex);
			_logger.WriteWarning($"Dependency candidate lookup failed for schema '{schemaName}': {reason}");
			return EntitySchemaDependencyResolution.LookupFailed(reason);
		}
	}

	/// <summary>
	/// Runs one diagnostic read and reports how long it took, at debug verbosity.
	/// </summary>
	/// <remarks>
	/// Debug rather than info on purpose: these reads only ever run on a path that is already reporting a
	/// failure, and an extra line on the normal CLI output would change what the caller sees for a change
	/// meant to alter nothing but the timing. <c>ConsoleLogger.WriteDebug</c> drops the line unless the
	/// process was started with <c>--debug</c>.
	/// </remarks>
	/// <typeparam name="T">Result type of the read.</typeparam>
	/// <param name="read">The read to run.</param>
	/// <param name="description">Short name of the read, used in the log line.</param>
	/// <returns>Whatever <paramref name="read"/> returned.</returns>
	private T Measure<T>(Func<T> read, string description) {
		long startedAt = Stopwatch.GetTimestamp();
		try {
			return read();
		} finally {
			_logger.WriteDebug($"Dependency diagnostic {description} took "
				+ $"{Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0} ms.");
		}
	}

	/// <summary>
	/// Renders a failure for a log line and for the surfaced message: redacted and length-bounded.
	/// </summary>
	/// <remarks>
	/// Redaction is not optional here. <c>SelectQueryHelper.ExecuteSelectQuery</c> falls back to the RAW
	/// response body when the server answers <c>success:false</c> with no <c>errorInfo</c> - the shape of
	/// Creatio's JSON 401 fault envelope - so an un-redacted interpolation of the exception message would put
	/// an unbounded server body into an agent transcript, the exact leak this change removes elsewhere. The
	/// CLI path has no second redaction pass, so it has to happen here.
	/// </remarks>
	/// <param name="exception">The failure to describe.</param>
	/// <returns>The redacted, bounded text to report.</returns>
	private static string DescribeFailure(Exception exception) {
		string redacted = SensitiveErrorTextRedactor.Redact(exception.Message);
		return redacted.Length > MaxLoggedFailureLength
			? redacted[..MaxLoggedFailureLength] + "\u2026"
			: redacted;
	}

	/// <summary>
	/// Returns the distinct packages that contribute <paramref name="schemaName"/>, excluding the target
	/// package itself.
	/// </summary>
	/// <param name="schemaName">Entity schema name to look up.</param>
	/// <param name="targetPackageName">Package being edited, which is never its own dependency.</param>
	/// <returns>Contributing package names.</returns>
	private List<string> FindContributingPackages(string schemaName, string targetPackageName) {
		IReadOnlyList<EntitySchemaSearchResult> results = _findCommand.FindSchemas(
			new FindEntitySchemaOptions { SchemaName = schemaName }, DiagnosticReadTimeoutMs);
		return results
			.Where(result => !string.IsNullOrWhiteSpace(result.PackageName))
			.Select(result => result.PackageName)
			.Where(name => !string.Equals(name, targetPackageName, StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	/// <summary>
	/// Reads the dependencies <paramref name="targetPackageName"/> already declares, so they are never
	/// offered as something to add.
	/// </summary>
	/// <remarks>
	/// These are the package's DIRECT dependencies. A transitively reachable package is therefore still
	/// listed as a candidate - a false positive in a list the caller reads and chooses from, never something
	/// clio acts on by itself. Walking the whole chain would cost one GetPackageProperties request per
	/// package on a path that is already a failure path.
	/// </remarks>
	/// <param name="targetPackageUId">Identifier of the package being edited, as the caller already holds it.</param>
	/// <param name="targetPackageName">Package being edited.</param>
	/// <returns>
	/// The case-insensitive set of declared dependency names, whether the read actually succeeded, and - when
	/// it did not - the redacted reason. A failed read yields an empty set with <see langword="false"/>: the
	/// caller must not mistake that for "this package declares no dependencies". The failure is RETURNED
	/// rather than logged here, so the caller can report it only once a candidate list the warning can
	/// describe actually exists.
	/// </returns>
	private (HashSet<string> Existing, bool ReadSucceeded, string? FailureReason) ReadExistingDependencies(
		Guid targetPackageUId, string targetPackageName) {
		try {
			return (_dependencyManager
				.GetDependencies(targetPackageUId, targetPackageName, DiagnosticReadTimeoutMs)
				.ToHashSet(StringComparer.OrdinalIgnoreCase), true, null);
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Degrade to "nothing known to be a dependency": an unfiltered candidate list is still useful,
			// while failing here would suppress the whole diagnosis.
			return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), false, DescribeFailure(ex));
		}
	}

	/// <summary>
	/// Reads the root package name (<c>SysInstalledApp.Code</c>) of every installed application, used as the
	/// ranking signal.
	/// </summary>
	/// <returns>Case-insensitive set of installed application root package names; empty when the read fails.</returns>
	private HashSet<string> ReadInstalledApplicationPackages() {
		try {
			InstalledAppQueryResponse response = ExecuteSelectQuery<InstalledAppQueryResponse>(
				_applicationClient,
				_serviceUrlBuilder,
				BuildSelectQuery("SysInstalledApp", InstalledAppColumns, []),
				DiagnosticReadTimeoutMs);
			return response.Rows
				.Select(row => row.Code)
				.Where(code => !string.IsNullOrWhiteSpace(code))
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Ranking is an ordering hint; losing it must never suppress the candidate list itself.
			_logger.WriteWarning(
				"Could not read the installed applications used to rank dependency candidates: " +
				$"{DescribeFailure(ex)}. The candidates are reported in no particular order.");
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}
	}

	/// <summary>
	/// Orders candidates so installed applications come first, each group sorted by name so the reported
	/// list is stable across calls.
	/// </summary>
	/// <param name="candidates">Candidate package names.</param>
	/// <param name="applicationPackages">Case-insensitive set of installed application root package names; membership is what the ranking keys on, so the comparer must stay case-insensitive.</param>
	/// <param name="applicationCandidateCount">How many leading entries are installed applications.</param>
	/// <returns>The ranked candidate list.</returns>
	private static List<string> Rank(List<string> candidates, HashSet<string> applicationPackages,
		out int applicationCandidateCount) {
		List<string> applications = candidates
			.Where(applicationPackages.Contains)
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		List<string> others = candidates
			.Where(name => !applicationPackages.Contains(name))
			.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
			.ToList();
		applicationCandidateCount = applications.Count;
		return [.. applications, .. others];
	}

	private sealed class InstalledAppQueryResponse : SelectQueryResponseBaseDto
	{
		[System.Text.Json.Serialization.JsonPropertyName("rows")]
		public List<InstalledAppRowDto> Rows { get; set; } = [];
	}

	private sealed class InstalledAppRowDto
	{
		[System.Text.Json.Serialization.JsonPropertyName("Code")]
		public string? Code { get; set; }
	}

}
