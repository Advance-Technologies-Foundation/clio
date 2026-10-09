using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Clio.Package;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// MCP tool surface for polling a previously started <c>compile-creatio</c> operation, used after that
/// tool returns an in-progress notice past the MCP response deadline (ENG-91315). When this session holds no
/// record of the compile, it reads the environment's own compilation history instead (ENG-102333).
/// </summary>
[McpServerToolType]
public sealed class CompileStatusTool(ICompileOperationRegistry registry, IToolCommandResolver commandResolver) {

	/// <summary>
	/// Stable MCP tool name for compile-status.
	/// </summary>
	internal const string CompileStatusToolName = "compile-status";

	/// <summary>
	/// How many compilation-history rows a <c>not-found</c> answer lists in full.
	/// </summary>
	/// <remarks>
	/// A package or process-name compile writes about two rows; a full one writes one per project, which was dozens
	/// on a fresh 10.2 stand. Ten rows show whether rows are still arriving; <see cref="HistoryFetchCount"/> is what
	/// keeps a failure early in a long compile visible.
	/// </remarks>
	internal const int HistoryRowCount = 10;

	/// <summary>
	/// How many compilation-history rows are read, so that failed rows older than the listed ten can be reported.
	/// </summary>
	internal const int HistoryFetchCount = 100;

	/// <summary>
	/// How many failed rows older than the listed ten a <c>not-found</c> answer carries.
	/// </summary>
	internal const int OlderFailureCount = 5;

	/// <summary>
	/// How many errors of one history row a <c>not-found</c> answer carries; <c>error-count</c> says how many there were.
	/// </summary>
	internal const int ErrorsPerHistoryRow = 5;

	/// <summary>
	/// The timeout of each of the history read's two requests (the session's time-zone offset, then the rows).
	/// </summary>
	internal const int HistoryReadTimeoutMs = 10_000;

	/// <summary>
	/// The default wall-clock bound on the whole history read, login included.
	/// </summary>
	/// <remarks>
	/// The per-request timeout does not bound the read: the first request of a fresh worker logs in first, under the
	/// client's own timeout, and a lost session is logged into again and the request replayed. Right after a compile
	/// the application reloads and a request can hang for 44 s. The answer has to arrive well inside the 60 s after
	/// which a client such as Claude Code desktop gives up and restarts the MCP server, so the read is abandoned at
	/// this bound and the answer says the history could not be read.
	/// <para>
	/// 40 s, up from 25 s (ENG-102333 round 3): on a busy stand the first poll after an MCP server restart - a fresh
	/// worker, so a fresh login - often ran out at 25 s and only the poll a minute later read the history (QA). 40 s
	/// keeps the answer inside the 45 s the other long tools answer by, so it still reaches a 60 s client.
	/// </para>
	/// </remarks>
	internal static readonly TimeSpan DefaultHistoryReadBudget = TimeSpan.FromSeconds(40);

	/// <summary>
	/// What a <c>not-found</c> answer says right after it names the missing record.
	/// </summary>
	/// <remarks>
	/// <b>It must not read as "nothing ran" (ENG-102333).</b> The record lives only in this MCP server session, and
	/// only for a while after its compile ends; a client that gives up on <c>compile-creatio</c> and then restarts
	/// its MCP server (measured: Claude Code desktop 2.1.293) loses it with the server. An agent told only that
	/// nothing was recorded either guesses or compiles again, and a second compile is a second runtime reload for
	/// every user.
	/// </remarks>
	internal const string NotARunVerdict =
		" That does not mean no compile ran: a record lives only in this session, and only for a while after its "
		+ "compile ends. Do not compile again to find out.";

	/// <summary>
	/// The rest of the note of a <c>not-found</c> answer that carries the environment's compilation history.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The rows carry a time, which is what lets an agent tie them to the compile it started; the verdict
	/// <c>last-compilation-log</c> reads carries none (see <see cref="ICompilationResultReader"/>).
	/// </para>
	/// <para>
	/// <b>One row is not a finished compile.</b> A compile writes a row as each project's build ends, up to about
	/// half a minute apart, and the runtime reload lands about two minutes after the last row (see
	/// <c>CompilationCompletionDecider</c>). "Finished" is "the newest row is more than seven minutes old": the
	/// five-minute quiet window <c>CompileConfigurationCommand.QuietFallback</c> uses when it sees no reload, plus two
	/// minutes for a slower reload and for skew between this host's clock, which stamps <c>checked-utc</c>, and the
	/// environment's, which stamped the rows.
	/// </para>
	/// <para>
	/// <b>The rows cannot prove whose they are.</b> Other compiles and schema publishes write rows too, and
	/// <c>CompilationHistory</c> is an ordinary entity, so the agent is told only how to match rows by time, and to
	/// ask the user before a restart that rests on them alone - a restart reloads the application for every user. A
	/// compile that never wrote a row (it did not start, or the environment keeps no history) ends in the user's
	/// decision too, never in a compile the agent starts on its own: core-rules requires the user's confirmation
	/// before every compile.
	/// </para>
	/// <para>
	/// <b>No row since the call can mean the compile never ran (ENG-102333, QA).</b> A client that restarts this MCP
	/// server before the build request reaches the environment takes the request with it: on a busy stand the first
	/// project started 50-80 s after the call, and a server killed at about 55 s left no row at all. So the note does
	/// not call a rowless compile "still running", and it does not send the agent to <c>last-compilation-log</c> for
	/// it: that verdict carries no time, and with no row since the call it can only be an earlier compile's - the QA
	/// agent read it as a green verdict for a compile that never happened. The agent compares rows with the
	/// <c>started-utc</c> compile-creatio's in-progress answer carries; the minute it allows covers skew between this
	/// host's clock and the environment's.
	/// </para>
	/// </remarks>
	internal const string HistoryNote =
		" compilation-history lists the environment's newest compilation-history rows, newest first, and "
		+ "older-failures up to five failed rows from further back among the newest 100. A compile writes one row per "
		+ "project as that project's build ends, so one compile's rows arrive over its run, up to about half a minute "
		+ "apart (finished-utc; finished-seconds-ago counts back from checked-utc). Only rows that finished after your "
		+ "call can be your compile's: compare finished-utc with the started-utc of compile-creatio's in-progress "
		+ "answer, allowing a minute either way because the environment's clock and this host's can differ, or, "
		+ "without one, take your call to be at least as long ago as your client's timeout error says it waited, plus "
		+ "the time since. A row that finished clearly before your call is not your compile's, even one more than seven "
		+ "minutes old that looks like a finished compile. Other compiles and schema publishes write rows too, so match by "
		+ "time. Any of them with succeeded=false and errors means a build failed. Treat your compile as finished only "
		+ "when its newest row is more than seven minutes old: until then more rows can follow, and the application "
		+ "reloads about two minutes after the last one. These rows cannot prove which compile wrote them, so ask the "
		+ "user before a restart that rests on them alone. If no row was written since your call, the compile has "
		+ "either not finished its first project yet or never started: a request still on its way when this MCP server "
		+ "restarted was lost with it. Call compile-status again in a minute or two. If a package or process-name "
		+ "compile has had ten minutes, or a full one about 20, and still no row was written since your call, treat it "
		+ "as not run and ask the user before compiling again. Do not read " + LastCompilationLogTool.ToolName
		+ " for it: with no row since your call it can only return an earlier compile's verdict.";

	/// <summary>
	/// The short form of the history rule, the one every tool surface that summarizes it reuses.
	/// </summary>
	/// <remarks>
	/// <see cref="HistoryNote"/>, which every not-found answer carries, is the full rule. The summary keeps both of its
	/// exits - the user's confirmation before a restart that rests on rows alone, and a stop with the user when no row
	/// ever comes - because an agent may act on a description without ever reading the note.
	/// </remarks>
	internal const string HistoryRuleSummary =
		"only rows that finished after your call (the started-utc of compile-creatio's in-progress answer) can be your "
		+ "compile's, and "
		+ "other compiles write rows too; it has finished only once its newest row is over seven minutes old, and a "
		+ "restart resting on those rows alone needs the user's confirmation; none yet means it has not finished a "
		+ "project yet or never started, and if none appears within ten minutes (about 20 for a full compile), treat it "
		+ "as not run and ask the user before compiling again - " + LastCompilationLogTool.ToolName + " could then only "
		+ "show an earlier compile's verdict.";

	/// <summary>
	/// The rest of the note of a <c>not-found</c> answer whose history could not be read.
	/// </summary>
	/// <remarks>
	/// The read fails the same way whether the environment is reloading after a compile, which passes, or cannot
	/// serve the history at all (no read rights, wrong stored credentials), which does not; the answer cannot tell
	/// them apart. So the note says to ask again once or twice and then stop. <c>last-compilation-log</c> stays the
	/// fallback, with the timing rule its undated verdict needs: a timing rule, not a ban, because a restart the
	/// workflow needs afterwards (a package compile's activation) is still owed.
	/// </remarks>
	internal const string HistoryUnavailableNote =
		" The environment's compilation history could not be read (see compilation-history-error), so this answer "
		+ "cannot say whether that compile finished. Right after a compile the application reloads and briefly stops "
		+ "answering, so call compile-status again in a minute; if the next poll or two still cannot read it, stop "
		+ "polling. " + LastCompilationLogTool.ToolName + " (through clio-run) reads the environment's latest FINISHED "
		+ "compile and carries no time, so until a compile you started has had time to finish (a package or "
		+ "process-name compile about ten minutes, a full one about 20) it can return an earlier compile's verdict: "
		+ "do not rely on it, or restart on it, before then. Even after that it cannot show that your compile ran: a "
		+ "compile whose request was lost when this MCP server restarted never ran, and the verdict is then an earlier "
		+ "compile's. Tell the user it is unconfirmed, and ask before compiling again or restarting on it.";

	/// <summary>
	/// The rest of the note of a <c>not-found</c> answer whose environment-name does not resolve.
	/// </summary>
	/// <remarks>
	/// <c>last-compilation-log</c> is not offered here: it resolves the same name and fails the same way.
	/// </remarks>
	internal const string UnresolvedEnvironmentNote =
		" Neither this answer nor last-compilation-log can read that environment until environment-name names one "
		+ "this MCP server can reach (see compilation-history-error).";

	/// <summary>
	/// The <c>compilation-history-error</c> of a <c>not-found</c> answer whose history read failed or ran out of time.
	/// </summary>
	/// <remarks>
	/// clio's own sentence: whatever the environment answered is server-authored text and never reaches this field.
	/// </remarks>
	internal const string HistoryUnreadableError =
		"The environment's compilation history could not be read: the environment did not answer in time, or did not "
		+ "answer with compilation-history rows and the session's time-zone offset.";

	/// <summary>
	/// The <c>compilation-history-error</c> of a <c>not-found</c> answer whose environment-name does not resolve.
	/// </summary>
	/// <remarks>
	/// A caller's mistake, not a transient failure: telling the agent to ask again in a minute would start a polling
	/// loop that can never end.
	/// </remarks>
	internal const string HistoryUnresolvedError =
		"environment-name does not name an environment this MCP server can reach, so its compilation history cannot be "
		+ "read; asking again will not help. Check the name with list-environments.";

	// An operation id compile-creatio issues is a GUID; anything else is named by description only, so a not-found
	// answer never repeats arbitrary caller text back. \z, not $: $ also matches before a trailing newline.
	private static readonly Regex EchoableOperationId = new(@"^[0-9a-fA-F-]{1,64}\z", RegexOptions.CultureInvariant,
		TimeSpan.FromMilliseconds(100));

	// The project names the platform writes: dot-separated identifiers ending in .csproj, such as
	// Terrasoft.Configuration.Dev.csproj. Anything else is server-authored text of unknown origin - CompilationHistory
	// is an ordinary entity, and a hyphenated sentence would pass a looser pattern - so it is fenced before it reaches
	// an agent. The length cap is the one validated identifiers get elsewhere.
	private static readonly Regex PlatformProjectName = new(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+){0,6}\.csproj\z",
		RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

	private const int MaxPlatformProjectNameLength = 64;

	/// <summary>
	/// The wall-clock bound on the whole history read; a seam for tests, <see cref="DefaultHistoryReadBudget"/>
	/// otherwise.
	/// </summary>
	internal TimeSpan HistoryReadBudget { get; set; } = DefaultHistoryReadBudget;

	/// <summary>
	/// The first sentence of a <c>not-found</c> answer: which record this session does not hold.
	/// </summary>
	/// <param name="operationId">The operation-id the caller asked about, or <see langword="null"/> for the latest.</param>
	/// <returns>The sentence.</returns>
	/// <remarks>
	/// A lookup by operation-id names the id: answering it with "no record of a compile-creatio operation for this
	/// environment" contradicts a session that does hold another record for that environment (ENG-102333, QA).
	/// </remarks>
	internal static string NoRecordSentence(string operationId) {
		if (string.IsNullOrWhiteSpace(operationId)) {
			return "This MCP server session holds no record of a compile-creatio operation for this environment.";
		}
		string trimmed = operationId.Trim();
		return IsMatch(EchoableOperationId, trimmed)
			? $"This MCP server session holds no record of operation-id '{trimmed}' for this environment."
			: "This MCP server session holds no record of the operation-id you passed for this environment.";
	}

	/// <summary>
	/// Returns the tracked status of a compile-creatio operation, or, when this session holds no record of it, the
	/// environment's newest compilation-history rows.
	/// </summary>
	[McpServerTool(Name = CompileStatusToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.Sticky,
		OperationFamily = McpToolOperationFamily.ConfigurationBuild,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillExtended,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.ConfigurationBuild)]
	[Description("Returns the status of the most recent compile-creatio operation tracked for an environment, or of a specific operation-id from a compile-creatio in-progress response. Use this after compile-creatio returns an in-progress note, AND after your MCP client stopped waiting for compile-creatio (for example 'Request timed out'): the compile keeps running and is tracked here. Do not re-run compile-creatio just to check. A not-found answer means this MCP server session holds no record, not that nothing ran: it then lists the environment's newest compilation-history rows with the time each finished (UTC, and seconds before checked-utc): " + HistoryRuleSummary)]
	public CompileStatusResponse GetStatus(
		[Description("Status query parameters")] [Required] CompileStatusArgs args) {
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return new CompileStatusResponse(false, "invalid-request",
				Note: "environment-name is required and cannot be empty.");
		}

		string callerTenantKey = commandResolver.GetTenantKey(new EnvironmentOptions { Environment = args.EnvironmentName });
		CompileOperationRecord record = string.IsNullOrWhiteSpace(args.OperationId)
			? registry.GetLatest(callerTenantKey)
			: registry.GetById(args.OperationId.Trim());

		// Scope operation-id lookups to the caller's tenant: on a shared MCP HTTP server a caller who obtains
		// (or guesses) another session's global operation id must not read its environment/package/exit-code/
		// message-tail. GetLatest is already tenant-keyed, so this only tightens the GetById path.
		if (record is not null && !string.Equals(record.TenantKey, callerTenantKey, StringComparison.Ordinal)) {
			record = null;
		}

		if (record is null) {
			return BuildNotFound(args, callerTenantKey);
		}

		return new CompileStatusResponse(
			true,
			record.Status.ToString().ToLowerInvariant(),
			record.OperationId,
			record.EnvironmentName,
			record.PackageName,
			record.StartedUtc,
			record.FinishedUtc,
			record.ExitCode,
			record.MessageTail,
			ProcessName: record.ProcessName);
	}

	private CompileStatusResponse BuildNotFound(CompileStatusArgs args, string callerTenantKey) {
		string noRecord = NoRecordSentence(args.OperationId) + NotARunVerdict;
		(CompilationHistoryReading reading, string error) = TryReadHistory(args.EnvironmentName, callerTenantKey);
		if (reading is null) {
			string unavailable = error == HistoryUnresolvedError ? UnresolvedEnvironmentNote : HistoryUnavailableNote;
			return new CompileStatusResponse(true, "not-found", EnvironmentName: args.EnvironmentName,
				Note: noRecord + unavailable, CompilationHistoryError: error);
		}
		List<CompilationHistoryRow> listed = reading.Rows.Take(HistoryRowCount).ToList();
		return new CompileStatusResponse(true, "not-found", EnvironmentName: args.EnvironmentName,
			Note: noRecord + HistoryNote, CheckedUtc: reading.CheckedUtc,
			CompilationHistory: listed.Select(row => ToHistoryEntry(row, reading.CheckedUtc)).ToList(),
			OlderFailures: reading.Rows.Skip(HistoryRowCount).Where(row => !row.Succeeded).Take(OlderFailureCount)
				.Select(row => ToHistoryEntry(row, reading.CheckedUtc)).ToList());
	}

	private (CompilationHistoryReading Reading, string Error) TryReadHistory(string environmentName, string tenantKey) {
		// Pinned like the restart readiness wait: on a shared server a concurrent call for another tenant must not
		// evict and dispose this environment's container while its client is mid-request. The pin is released when
		// the read ends, which can be after this answer when the read is abandoned at its bound.
		McpToolExecutionLock.MarkInUse(tenantKey);
		ICompilationHistoryReader reader;
		try {
			reader = commandResolver.Resolve<ICompilationHistoryReader>(new EnvironmentOptions { Environment = environmentName });
		} catch (EnvironmentResolutionException) {
			McpToolExecutionLock.MarkSessionContainerAvailable(tenantKey);
			return (null, HistoryUnresolvedError);
		} catch (Exception) {
			McpToolExecutionLock.MarkSessionContainerAvailable(tenantKey);
			return (null, HistoryUnreadableError);
		}
		Task<CompilationHistoryReading> read = Task.Run(() => reader?.ReadLatest(HistoryFetchCount, HistoryReadTimeoutMs));
		read.ContinueWith(finished => {
			// Observed so an abandoned read that fails later cannot surface as an unobserved task exception.
			_ = finished.Exception;
			McpToolExecutionLock.MarkSessionContainerAvailable(tenantKey);
		}, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
		try {
			// Deliberately broad: an unreachable host, a login page, a failed query and a read past its bound all mean
			// the same thing here - no history to show - and the answer is still a valid not-found that says so.
			return read.Wait(HistoryReadBudget) && read.Result is not null
				? (read.Result, null)
				: (null, HistoryUnreadableError);
		} catch (AggregateException) {
			return (null, HistoryUnreadableError);
		}
	}

	private static CompileHistoryEntry ToHistoryEntry(CompilationHistoryRow row, DateTime checkedUtc) {
		List<PackageBuildDiagnostic> errors = row.Diagnostics.Where(diagnostic => !diagnostic.IsWarning).ToList();
		return new CompileHistoryEntry(
			RenderProjectName(row.ProjectName),
			row.FinishedUtc,
			// Two clocks: the environment's wrote the row and this host's read it, so a small skew must not read as
			// a row from the future.
			Math.Max(0L, (long)(checkedUtc - row.FinishedUtc).TotalSeconds),
			row.DurationSeconds,
			row.Succeeded,
			errors.Count,
			errors.Take(ErrorsPerHistoryRow).Select(RenderError).ToList());
	}

	private static string RenderProjectName(string projectName) =>
		projectName is null
		|| (projectName.Length <= MaxPlatformProjectNameLength && IsMatch(PlatformProjectName, projectName))
			? projectName
			: UntrustedText.Fenced(projectName);

	// Both patterns are anchored and linear, so their timeout fires only on a starved thread; that must not turn a
	// valid answer into a tool error, and the safe reading of "could not tell" is "not a match".
	private static bool IsMatch(Regex pattern, string text) {
		try {
			return pattern.IsMatch(text);
		} catch (RegexMatchTimeoutException) {
			return false;
		}
	}

	// The compiler's text is authored by whoever wrote the code on the environment, so it is fenced as data; the
	// file is cut to its NAME, because the environment's directory layout is not the agent's business.
	private static string RenderError(PackageBuildDiagnostic diagnostic) =>
		UntrustedText.Fenced(diagnostic.Format(static code => code, FileNameOnly));

	// Not Path.GetFileName: the path is the ENVIRONMENT's, so a Windows path must lose its directories on a Linux host.
	private static string FileNameOnly(string path) =>
		string.IsNullOrWhiteSpace(path) ? path : path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

}

/// <summary>
/// MCP arguments for the compile-status tool.
/// </summary>
public sealed record CompileStatusArgs(

	[property: JsonPropertyName("environment-name")]
	[Description(McpToolDescriptions.EnvironmentName)]
	[Required]
	string EnvironmentName,

	[property: JsonPropertyName("operation-id")]
	[Description("Optional operation id from a compile-creatio in-progress response. When omitted, returns the most recently started operation for this environment.")]
	string? OperationId = null);

/// <summary>
/// Response payload for the compile-status tool.
/// </summary>
public sealed record CompileStatusResponse(

	[property: JsonPropertyName("success")]
	[Description("False only for an invalid request (e.g. empty environment-name); true whenever the lookup itself completed, including a not-found result.")]
	bool Success,

	[property: JsonPropertyName("status")]
	[Description("One of: running, succeeded, failed, not-found, invalid-request.")]
	string Status,

	[property: JsonPropertyName("operation-id")]
	string OperationId = null,

	[property: JsonPropertyName("environment-name")]
	string EnvironmentName = null,

	[property: JsonPropertyName("package-name")]
	[Description("The single package compiled, or null for a full compilation - or for a process-name compile, which sets process-name instead.")]
	string PackageName = null,

	[property: JsonPropertyName("started-utc")]
	DateTime? StartedUtc = null,

	[property: JsonPropertyName("finished-utc")]
	DateTime? FinishedUtc = null,

	[property: JsonPropertyName("exit-code")]
	int? ExitCode = null,

	[property: JsonPropertyName("message-tail")]
	[Description("The trailing lines of compile output captured when the operation finished; empty while running.")]
	IReadOnlyList<string> MessageTail = null,

	[property: JsonPropertyName("note")]
	string Note = null,

	[property: JsonPropertyName("process-name")]
	[Description("The business process whose package a process-name compile compiled; null otherwise.")]
	string ProcessName = null,

	[property: JsonPropertyName("checked-utc")]
	[Description("On a not-found answer: when the compilation history was read, by this clio host's clock (UTC).")]
	DateTime? CheckedUtc = null,

	[property: JsonPropertyName("compilation-history")]
	[Description("On a not-found answer: the environment's newest compilation-history rows, newest first - one row per project a compile built, written when that project's build ended. Only rows that finished after you called compile-creatio (the started-utc of its in-progress answer) can be that compile's; other compiles and schema publishes write rows too.")]
	IReadOnlyList<CompileHistoryEntry> CompilationHistory = null,

	[property: JsonPropertyName("compilation-history-error")]
	[Description("On a not-found answer: why the compilation history could not be read; absent when it was.")]
	string CompilationHistoryError = null,

	[property: JsonPropertyName("older-failures")]
	[Description("On a not-found answer: failed rows among the environment's newest 100 compilation-history rows that are older than those in compilation-history, newest first - a full compile writes dozens of rows, and a failure early in it must not hide behind the projects built after it.")]
	IReadOnlyList<CompileHistoryEntry> OlderFailures = null);

/// <summary>
/// One compilation-history row in a <c>compile-status</c> not-found answer.
/// </summary>
public sealed record CompileHistoryEntry(

	[property: JsonPropertyName("project-name")]
	[Description("The project this row is for, for example Terrasoft.Configuration.Dev.csproj; a value that is not a project file name is fenced as text the environment authored.")]
	string ProjectName,

	[property: JsonPropertyName("finished-utc")]
	[Description("When the environment wrote the row, which is when that project's build ended (UTC).")]
	DateTime FinishedUtc,

	[property: JsonPropertyName("finished-seconds-ago")]
	[Description("How long before checked-utc the row was written, in seconds. checked-utc is this clio host's clock and finished-utc the environment's, so a skew between the two shifts it.")]
	long FinishedSecondsAgo,

	[property: JsonPropertyName("duration-seconds")]
	[Description("How long that project's build took.")]
	int DurationSeconds,

	[property: JsonPropertyName("succeeded")]
	[Description("The row's own result. False with errors is a failed build; false without errors failed for another reason, which last-compilation-log (through clio-run) reports.")]
	bool Succeeded,

	[property: JsonPropertyName("error-count")]
	[Description("How many compiler errors (not warnings) the row carries.")]
	int ErrorCount,

	[property: JsonPropertyName("errors")]
	[Description("The first compiler errors of the row, each fenced as text the environment authored.")]
	IReadOnlyList<string> Errors);
