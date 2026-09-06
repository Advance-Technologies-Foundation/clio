using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Command.McpServer;
using Clio.Common;
using Clio.Package;
using static Clio.Package.SelectQueryHelper;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Outcome of one dependency-resolution attempt for an entity schema the designer could not open in the
/// target package.
/// </summary>
/// <param name="DependencyAdded">
/// <see langword="true"/> only when a dependency was actually added and the caller should retry the load.
/// </param>
/// <param name="Candidates">
/// Packages that contribute <c>schemaName</c>, ranked: installed applications first, then the rest. The
/// target package itself and every package it already depends on are excluded, so every entry is something
/// the caller could actually add. Empty when nothing could be determined.
/// </param>
/// <param name="ApplicationCandidateCount">
/// How many leading <paramref name="Candidates"/> entries are installed applications. Zero means the ranking
/// signal was unavailable (or matched nothing), and the order carries no recommendation.
/// </param>
public sealed record EntitySchemaDependencyResolution(
	bool DependencyAdded,
	IReadOnlyList<string> Candidates,
	int ApplicationCandidateCount)
{

	/// <summary>Result carrying no candidates and no change - the shape every failure path returns.</summary>
	public static EntitySchemaDependencyResolution None { get; } = new(false, [], 0);

}

/// <summary>
/// Resolves the package dependency an entity schema designer request needs when it cannot open a schema in
/// the target package (the <c>SchemaIsNotAvailableException</c> that surfaces as an HTML error page from
/// <c>GetSchemaDesignItem</c>).
/// </summary>
public interface IEntitySchemaDependencyResolver
{

	/// <summary>
	/// Determines which packages could supply <paramref name="schemaName"/> to
	/// <paramref name="targetPackageName"/>, and - only when exactly one candidate remains and
	/// <paramref name="allowAutoAdd"/> is set - adds it.
	/// <para>
	/// Mirrors the auto-dependency behavior of the Creatio <c>PackageElementDependencyApplier</c> that runs
	/// inside <c>SaveSchema</c> but is absent from the <c>GetSchemaDesignItem</c> code path.
	/// </para>
	/// </summary>
	/// <param name="schemaName">Entity schema name that was unavailable (for example <c>Opportunity</c>).</param>
	/// <param name="targetPackageName">Package that is being edited (for example <c>Custom</c>).</param>
	/// <param name="allowAutoAdd">
	/// Whether this call is allowed to write. Read paths pass <see langword="false"/> and still receive the
	/// ranked candidate list, which is the only thing that makes their error message actionable.
	/// </param>
	/// <returns>The candidates found and whether a dependency was added.</returns>
	EntitySchemaDependencyResolution Resolve(string schemaName, string targetPackageName, bool allowAutoAdd);

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
	/// Bound on the two reads this class adds to a path that is already failing. They exist to enrich an
	/// error message, so a stand that stops answering must cost the caller a bounded wait, not a hung tool
	/// call - <c>ExecuteSelectQuery</c> defaults to <see cref="System.Threading.Timeout.Infinite"/>.
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
	public EntitySchemaDependencyResolution Resolve(string schemaName, string targetPackageName,
		bool allowAutoAdd) {
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
			// A single remaining candidate is the ONLY case with one right answer, so it is the only case that
			// writes. Ranking narrows a longer list but never resolves it: on a measured stand, intersecting
			// the Lead candidates with the installed applications still leaves three, and adding the wrong one
			// is a real change to the package. Everything above one is reported, not applied.
			//
			// dependenciesKnown gates the write as well: when the dependency read failed, the subtraction
			// above was a no-op, so "exactly one remains" would be true of an UNFILTERED list - the safety
			// condition would look satisfied without ever having been evaluated. Reporting stays on in that
			// case; only the write is withheld.
			if (allowAutoAdd && dependenciesKnown && ranked.Count == 1
				&& TryAddDependency(schemaName, targetPackageName, ranked[0])) {
				return new EntitySchemaDependencyResolution(true, ranked, applicationCandidateCount);
			}
			return new EntitySchemaDependencyResolution(false, ranked, applicationCandidateCount);
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Broad catch is intentional: FindSchemas, the dependency reads and AddDependencies can fail with
			// HttpRequestException, JsonException, InvalidOperationException, or ArgumentException depending on
			// the remote state. None of these should abort the caller - the enriched error message in
			// LoadSchema takes over when no candidate is returned.
			_logger.WriteWarning(
				$"Dependency candidate lookup failed for schema '{schemaName}': {DescribeFailure(ex)}");
			return EntitySchemaDependencyResolution.None;
		}
	}

	/// <summary>
	/// Adds one dependency, reporting failure instead of propagating it so the candidate list survives a
	/// refused write.
	/// </summary>
	/// <remarks>
	/// The catch is scoped to the write rather than left to the caller's outer catch: a refused
	/// <c>SavePackageProperties</c> would otherwise discard the candidates that had already been computed,
	/// and the caller would report "clio found no package…" - the opposite of what happened.
	/// </remarks>
	/// <param name="schemaName">Schema being made reachable, used in the progress message.</param>
	/// <param name="targetPackageName">Package whose dependency list is extended.</param>
	/// <param name="dependencyName">The single candidate to add.</param>
	/// <returns><see langword="true"/> when the dependency was added.</returns>
	private bool TryAddDependency(string schemaName, string targetPackageName, string dependencyName) {
		try {
			_logger.WriteInfo(
				$"Schema '{schemaName}' is not available in package '{targetPackageName}'. " +
				"Exactly one package contributes it and is not already a dependency - " +
				$"auto-adding dependency: {dependencyName}");
			_dependencyManager.AddDependencies(targetPackageName, [new PackageDependencySpec(dependencyName)]);
			return true;
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			_logger.WriteWarning(
				$"Could not add dependency '{dependencyName}' to package '{targetPackageName}': " +
				$"{DescribeFailure(ex)}");
			return false;
		}
	}

	/// <summary>
	/// Renders a failure for a log line: redacted and length-bounded.
	/// </summary>
	/// <remarks>
	/// Redaction is not optional here. <c>SelectQueryHelper.ExecuteSelectQuery</c> falls back to the RAW
	/// response body when the server answers <c>success:false</c> with no <c>errorInfo</c> - the shape of
	/// Creatio's JSON 401 fault envelope - so an un-redacted interpolation of the exception message would put
	/// an unbounded server body into an agent transcript, the exact leak this change removes elsewhere. The
	/// CLI path has no second redaction pass, so it has to happen here.
	/// </remarks>
	/// <param name="exception">The failure to describe.</param>
	/// <returns>The redacted, bounded text to log.</returns>
	private static string DescribeFailure(Exception exception) {
		string redacted = SensitiveErrorTextRedactor.Redact(exception.Message);
		return redacted.Length > MaxLoggedFailureLength
			? redacted[..MaxLoggedFailureLength] + "…"
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
			new FindEntitySchemaOptions { SchemaName = schemaName });
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
	/// listed as a candidate - that is a false positive in the list, not a wrong write: the auto-add path
	/// only fires when one candidate remains, and adding an already-reachable package is idempotent at the
	/// platform level. Walking the whole chain would cost one GetPackageProperties request per package on a
	/// path that is already a failure path.
	/// </remarks>
	/// <param name="targetPackageName">Package being edited.</param>
	/// <returns>
	/// The case-insensitive set of declared dependency names, and whether the read actually succeeded. A
	/// failed read yields an empty set with <see langword="false"/> - the caller must not mistake that for
	/// "this package declares no dependencies".
	/// </returns>
	private (HashSet<string> Existing, bool ReadSucceeded) ReadExistingDependencies(string targetPackageName) {
		try {
			return (_dependencyManager.GetDependencies(targetPackageName)
				.ToHashSet(StringComparer.OrdinalIgnoreCase), true);
		} catch (Exception ex) when (ex is not OutOfMemoryException) {
			// Degrade to "nothing known to be a dependency": an unfiltered candidate list is still useful,
			// while failing here would suppress the whole diagnosis.
			_logger.WriteWarning(
				$"Could not read the current dependencies of package '{targetPackageName}': " +
				$"{DescribeFailure(ex)}. " +
				"The candidate list may include packages that are already dependencies, and no dependency " +
				"will be added automatically.");
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
