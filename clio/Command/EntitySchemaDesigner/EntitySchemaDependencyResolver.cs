using System;
using System.Collections.Generic;
using System.Linq;
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
	/// <param name="schemaName">Entity schema name that was unavailable (for example <c>Opportunity</c>).</param>
	/// <param name="targetPackageName">Package that is being edited (for example <c>Custom</c>).</param>
	/// <returns>The candidates found, and whether the searches behind them actually completed.</returns>
	EntitySchemaDependencyResolution Resolve(string schemaName, string targetPackageName);

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
	public EntitySchemaDependencyResolution Resolve(string schemaName, string targetPackageName) {
		try {
			List<string> contributors = FindContributingPackages(schemaName, targetPackageName);
			if (contributors.Count == 0) {
				return EntitySchemaDependencyResolution.None;
			}
			(HashSet<string> existingDependencies, bool dependenciesKnown) =
				ReadExistingDependencies(targetPackageName);
			List<string> candidates = contributors
				.Where(name => !existingDependencies.Contains(name))
				.ToList();
			if (dependenciesKnown && candidates.Count == 0) {
				// Every package that contributes the schema is already a dependency, so a missing dependency
				// is NOT what the caller is looking at. Saying nothing is the correct answer here.
				return EntitySchemaDependencyResolution.None;
			}
			HashSet<string> applicationPackages = ReadInstalledApplicationPackages();
			List<string> ranked = Rank(candidates, applicationPackages, out int applicationCandidateCount);
			return new EntitySchemaDependencyResolution(ranked, applicationCandidateCount, true,
				dependenciesKnown);
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Broad catch is intentional: FindSchemas can fail with HttpRequestException, JsonException,
			// InvalidOperationException, or ArgumentException depending on the remote state. None of these
			// should abort the caller - the enriched error message in LoadSchema takes over. The failure is
			// carried in the result, not only logged: the log warning does not reach an MCP client, so a
			// caller told only "no candidates" would read a search that never ran as a finding of fact.
			string reason = DescribeFailure(ex);
			_logger.WriteWarning($"Dependency candidate lookup failed for schema '{schemaName}': {reason}");
			return EntitySchemaDependencyResolution.LookupFailed(reason);
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
	/// <param name="targetPackageName">Package being edited.</param>
	/// <returns>
	/// The case-insensitive set of declared dependency names, and whether the read actually succeeded. A
	/// failed read yields an empty set with <see langword="false"/> - the caller must not mistake that for
	/// "this package declares no dependencies".
	/// </returns>
	private (HashSet<string> Existing, bool ReadSucceeded) ReadExistingDependencies(string targetPackageName) {
		try {
			return (_dependencyManager.GetDependencies(targetPackageName, DiagnosticReadTimeoutMs)
				.ToHashSet(StringComparer.OrdinalIgnoreCase), true);
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Degrade to "nothing known to be a dependency": an unfiltered candidate list is still useful,
			// while failing here would suppress the whole diagnosis.
			_logger.WriteWarning(
				$"Could not read the current dependencies of package '{targetPackageName}': " +
				$"{DescribeFailure(ex)}. " +
				"The candidate list may include packages that are already dependencies.");
			return (new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
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
				.ToHashSet(StringComparer.OrdinalIgnoreCase)!;
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
