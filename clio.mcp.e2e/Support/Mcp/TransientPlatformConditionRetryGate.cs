using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Protocol;

namespace Clio.Mcp.E2E.Support.Mcp;

/// <summary>
/// Retries an MCP tool call when its answer carries a KNOWN transient platform condition instead of a
/// real test result (issue #1381, part 2).
/// </summary>
/// <remarks>
/// A handful of the sandbox fixtures observed flaky failures that are not bugs in the tool under test:
/// the platform itself answered with one of a small, fixed set of "come back later / try again" shapes
/// while it settled. This gate re-invokes the caller-supplied call while (and only while) the answer
/// matches <see cref="IsKnownTransientPlatformCondition"/>, bounded by <see cref="MaxAttempts"/> and a
/// hard <see cref="OverallDeadline"/>, exactly the way <see cref="DataForgeReadinessGate"/> bounds its
/// own readiness poll — a fixed attempt cap, a fixed inter-attempt delay, and a wall-clock ceiling on
/// top of both so a misbehaving stand can never hang the suite.
/// <para>
/// The predicate is deliberately narrow, and the cases it must NOT retry fall into two groups that are
/// kept apart on purpose — the earlier revision of this list did not, which hid a real gap.
/// </para>
/// <para>
/// ENFORCED by an explicit exclusion, checked before any marker match, because for these a marker CAN
/// legitimately appear in the same payload and a retry would still be wrong:
/// <list type="bullet">
/// <item><description>a SUCCESSFUL answer whose large payload happens to embed one of the marker
/// phrases — a failure signal (a transport-level error, or the tools' own <c>success:false</c> shape)
/// is required before any marker is even considered, because <c>create-app</c> is not idempotent and
/// replaying a successful create would be wrong (<see cref="HasFailureSignal"/>);</description></item>
/// <item><description>a create that already happened — <c>ApplicationCreateService</c>'s "created but
/// its metadata could not be loaded" failure names a real side effect on the platform, so retrying it
/// would replay the same application name/code against an application that already exists
/// (<see cref="ApplicationAlreadyCreatedMarker"/>);</description></item>
/// <item><description>a create whose POST timed out — <c>ApplicationCreateService.PollApplicationInfo</c>
/// is reached from <c>catch (Exception exception) when (IsTimeout(exception))</c>, so the request may
/// well have landed server-side; that is the whole reason the poll exists. Its failure prefix is the
/// sibling of the already-created one and carries the same last-load error, which is exactly where a
/// marker turns up (<see cref="ApplicationCreateTimeoutMarker"/>);</description></item>
/// <item><description>a <c>create-app-section</c> whose insert already landed or may still be landing —
/// any <c>section-created</c> value other than <c>false</c>. The section insert carries a
/// client-generated id, so a tool-level retry inserts a SECOND section rather than recovering the first
/// (<see cref="SectionAlreadyCreatedMarkers"/>). The sibling "insert landed, readback failed" shape needs
/// no entry here: <c>ApplicationSectionCreateCommand</c> reports it as "Section 'X' was created but its
/// metadata could not be loaded…", which the pre-existing <see cref="ApplicationAlreadyCreatedMarker"/>
/// already matches verbatim;</description></item>
/// <item><description><c>error-class=contention</c> — contention has its own dedicated handling
/// elsewhere in the harness and is not one of the three platform conditions this gate exists for. Matched
/// on the serialized envelope field rather than inferred from marker absence
/// (<see cref="ContentionErrorClassMarker"/>).</description></item>
/// </list>
/// </para>
/// <para>
/// NOT enforced, and deliberately so — these carry no signature in the envelope, so they fall through
/// only because none of the three transient markers is present:
/// <list type="bullet">
/// <item><description>a failed assertion on returned data (a wrong value, a missing field) — there is
/// nothing in the payload to key an exclusion on, and none of the three markers describes it;</description></item>
/// <item><description><c>success:false</c> carrying a business-rule message (validation, a duplicate
/// name, a missing dependency). The wording of those rejections comes from the platform, not from clio,
/// so there is no stable literal to exclude on. A business-rule failure whose payload ALSO embeds a
/// transient marker is therefore still retried; the cost is the wasted retry window, not a wrong side
/// effect, because a genuine duplicate create is rejected outright rather than succeeding twice.</description></item>
/// </list>
/// </para>
/// </remarks>
internal static class TransientPlatformConditionRetryGate {
	/// <summary>
	/// The platform's OData rebuild window: <c>create-entity-schema</c> (and similar schema-publishing
	/// calls) start an asynchronous, global OData rebuild that outlives the call, and a concurrent
	/// request against the same stand can observe "Creatio is currently rebuilding the OData library"
	/// instead of its own result while that rebuild is in flight (see
	/// <c>clio.mcp.e2e/DataBindingDbColorSchemaE2ETests.cs</c>).
	/// </summary>
	internal const string ODataRebuildMarker = "rebuilding the OData library";

	/// <summary>
	/// The prefix <see cref="LoginDiagnostics"/> puts on the message it decorates when
	/// <c>Creatio.Client.CreatioClient.Login()</c> rejects a login attempt (<c>"Unauthorized " + userName
	/// + " for " + AppUrl</c>). Reused verbatim from <see cref="LoginDiagnostics.LoginRejectionMessagePrefix"/>
	/// rather than retyped, so the two call sites can never drift apart.
	/// </summary>
	internal const string LoginRejectionMarker = LoginDiagnostics.LoginRejectionMessagePrefix;

	/// <summary>
	/// The segment that separates the user name from the URL in the same server message
	/// (<c>"Unauthorized " + userName + " for " + AppUrl</c>), required to appear AFTER
	/// <see cref="LoginRejectionMarker"/> before a payload counts as a login rejection.
	/// </summary>
	/// <remarks>
	/// <see cref="LoginDiagnostics"/> itself qualifies the prefix with <c>StartsWith</c> on the exception
	/// message (<c>clio/Common/LoginDiagnostics.cs:159</c>). This gate can only see a serialized payload,
	/// not the exception, so it cannot anchor at position 0 — but a bare <c>Contains("Unauthorized ")</c>
	/// over <c>StructuredContent + Content</c> concatenated is far looser than the real signature: any 401
	/// text, permission-denied message, log line or URL embedding the word anywhere in a failed payload
	/// would be classified both as transient AND as a login rejection, and the login-rejection branch
	/// replaces the fixed <see cref="RetryDelay"/> with a full session restart — so a false positive costs
	/// up to <c>MaxAttempts - 1</c> back-to-back <c>McpServerSession.StartAsync</c> calls with no wait
	/// between them. Requiring the companion segment restores the shape of the actual message.
	/// </remarks>
	internal const string LoginRejectionSubjectSeparator = " for ";

	/// <summary>
	/// The verbatim wording <c>ServiceResponseJsonGuard</c> uses when a Creatio service answered with an
	/// HTML page instead of JSON — most often because the request was redirected to a login page (see
	/// <c>clio/Package/ServiceResponseJsonGuard.cs:110-119</c>, whose message documents that the caller
	/// should retry).
	/// </summary>
	internal const string HtmlPageInsteadOfJsonMarker = "returned an HTML page instead of JSON";

	/// <summary>
	/// The companion wording from the same <c>ServiceResponseJsonGuard</c> message, naming the most
	/// likely cause of the HTML body.
	/// </summary>
	internal const string RedirectedToLoginPageMarker = "redirected to a login page";

	/// <summary>
	/// The exact wording <see cref="Clio.Command.ApplicationCreateService"/> puts on its failure message
	/// when <c>create-app</c> already created the application row before the metadata read-back failed
	/// (<c>clio/Command/ApplicationCreateService.cs:532</c>, <c>LoadCreatedApplication</c>): <c>"Application
	/// '&lt;code&gt;' was created but its metadata could not be loaded ..."</c>, followed by the last load
	/// error, which can itself embed <see cref="ODataRebuildMarker"/>. Retrying that call would resubmit
	/// the same application name and code against an application that already exists, hitting a real
	/// "already exists" business error instead of the OData-rebuild window it looks like — so this
	/// signature is excluded from the transient match even when a marker also appears in the same payload.
	/// </summary>
	internal const string ApplicationAlreadyCreatedMarker = "was created but its metadata could not be loaded";

	/// <summary>
	/// The sibling failure prefix from the same <c>LoadApplicationInfoWithRetry</c> helper, used by
	/// <see cref="Clio.Command.ApplicationCreateService"/>'s timeout-recovery poll
	/// (<c>clio/Command/ApplicationCreateService.cs:546</c>, <c>PollApplicationInfo</c>): <c>"CreateApp
	/// request timed out and application '&lt;code&gt;' could not be loaded ..."</c>, likewise followed by
	/// the last load error.
	/// </summary>
	/// <remarks>
	/// This one matters MORE than <see cref="ApplicationAlreadyCreatedMarker"/>, not less. It is reached
	/// only from <c>catch (Exception exception) when (IsTimeout(exception))</c> around the <c>CreateApp</c>
	/// POST (<c>clio/Command/ApplicationCreateService.cs:173</c>), so the request may already have landed
	/// server-side — that possibility is the entire reason the poll exists. The load path underneath it is
	/// <c>SelectQuery</c> + <c>ServiceResponseJsonGuard.Deserialize</c>, which is precisely the code that
	/// emits <see cref="HtmlPageInsteadOfJsonMarker"/> / <see cref="RedirectedToLoginPageMarker"/> and
	/// which fails during an OData rebuild. So the natural sequence is: POST times out under rebuild load →
	/// the application probably exists → the appended last-load error carries a marker → without this
	/// exclusion the gate retries → a duplicate <c>create-app</c> with the same name and code. The retried
	/// case and the excluded case are the same physical scenario wearing two different prefixes.
	/// <para>
	/// Matched on the invariant leading fragment only, since the application code is interpolated into the
	/// middle of the message.
	/// </para>
	/// </remarks>
	internal const string ApplicationCreateTimeoutMarker = "CreateApp request timed out and application";

	/// <summary>
	/// The serialized <c>error-class</c> envelope field carrying the contention classification
	/// (<c>ApplicationSectionCreateFailureClass.Contention.ToWireValue()</c> is <c>"contention"</c>, surfaced
	/// through the <c>[property: JsonPropertyName("error-class")]</c> member of the application tool
	/// responses). Checked EXPLICITLY rather than left to fall through on marker absence: a contention
	/// rejection raised while the stand is also rebuilding its OData library carries both signals in one
	/// payload, and the documented contract is that contention is never retried here.
	/// </summary>
	internal const string ContentionErrorClassMarker = "\"error-class\":\"contention\"";

	/// <summary>
	/// The serialized <c>section-created</c> envelope field values that say the section INSERT already
	/// landed, or may still be landing, server-side — every value EXCEPT the one that proves it did not:
	/// <list type="bullet">
	/// <item><description><c>in-progress</c> — the MCP response deadline fired while the backend kept
	/// creating (<c>ApplicationToolSupport.CreateSectionInProgressResponse</c>);</description></item>
	/// <item><description><c>unknown</c> — verification itself failed, so the insert MAY have landed
	/// (<c>ApplicationSectionCreateException.SectionCreated == null</c>, mapped at
	/// <c>ApplicationToolSupport.CreateSectionContextErrorResponse</c>). Latent today, because none of the
	/// messages that carry it also carries a transient marker — but that is a property of the current
	/// wording, not a guarantee, and one reworded message would turn it into a duplicate insert;</description></item>
	/// <item><description><c>true</c> — DEFENSIVE ONLY. No current call site emits it: the one path that
	/// could, <c>RecoverFromInsertTimeout</c>, returns early on a visible section and so reaches
	/// <c>BuildTimeoutFailure</c> with <c>false</c> or <c>null</c> exclusively. Excluded anyway because
	/// the value's own meaning is "the row is there".</description></item>
	/// </list>
	/// <c>false</c> is deliberately NOT here: it is the verified-absent outcome, and excluding it would
	/// disable the gate for the section-create failures that are actually safe to repeat.
	/// </summary>
	/// <remarks>
	/// These are the <c>create-app-section</c> analogue of <see cref="ApplicationCreateTimeoutMarker"/> and
	/// <see cref="ApplicationAlreadyCreatedMarker"/>, and they matter for the same reason: the section
	/// insert carries a client-generated id and <c>TryVerifySectionExists</c> matches on THAT id, so a
	/// retry at the tool-call level generates a NEW id and inserts a SECOND section with the same caption
	/// rather than recovering the first. The in-progress envelope says so in its own retry guidance, in
	/// those words: "Do NOT retry create-app-section (a retry would create a duplicate section)". Enforced
	/// explicitly rather than left to fall through on marker absence, because the in-progress answer is
	/// produced by a deadline that a stand under OData-rebuild load is exactly what causes — so the
	/// transient marker and this field can and do arrive in one payload.
	/// </remarks>
	internal static readonly string[] SectionAlreadyCreatedMarkers = [
		"\"section-created\":\"in-progress\"",
		"\"section-created\":\"unknown\"",
		"\"section-created\":\"true\""
	];

	/// <summary>
	/// The failure-shape marker the MCP tools use in their JSON envelope (<c>{"success":false,"error":...}</c>).
	/// Matched against the NORMALIZED payload (see <see cref="NormalizeEscapedQuotes"/>) because the
	/// payload text is JSON-inside-JSON: the tool's own JSON body is itself carried as a string inside the
	/// outer <see cref="CallToolResult"/> serialization, so its quotes can arrive escaped as <c>\"</c>.
	/// </summary>
	private const string SuccessFalseMarker = "\"success\":false";

	/// <summary>
	/// Bounded TOTAL attempt count, initial call included: the loop makes the initial call, then up to
	/// <c>MaxAttempts - 1</c> retries, so at most <see cref="MaxAttempts"/> calls happen in all.
	/// </summary>
	/// <remarks>
	/// The retry window this produces — <c>(MaxAttempts - 1) * RetryDelay</c> — is deliberately sized to
	/// match the ~120s window the two other consumers of the SAME OData-rebuild condition converged on
	/// independently: <c>DataBindingDbFixtureBase.WaitUntilSchemaIsQueryableAsync</c> (24 attempts × 5s)
	/// and <c>ApplicationToolE2ETests.CanonicalMainEntityReadbackAttempts</c> (40 attempts × 3s). With
	/// <see cref="RetryDelay"/> at 15s, 8 retries reach the same ~120s, hence 9.
	/// </remarks>
	private const int MaxAttempts = 9;

	/// <summary>Fixed delay between attempts that are not re-authenticated (the OData/HTML-page cases).</summary>
	private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

	/// <summary>
	/// Hard upper bound for the whole retry loop, regardless of attempt count. It is WALL-CLOCK and counts
	/// the attempts themselves, not just the waits, so which of the two bounds ends the loop depends on how
	/// long one attempt takes: for a fast call the attempt cap ends it after the ~120s of waiting, while
	/// for a ~95s <c>create-app-section</c> the deadline is reached after only one or two extra full
	/// attempts. Callers sizing their own CancellationTokenSource should budget against THIS number.
	/// </summary>
	private static readonly TimeSpan OverallDeadline = TimeSpan.FromMinutes(3);

	/// <summary>
	/// Decides whether a FAILED MCP tool answer is a KNOWN transient platform condition worth retrying, by
	/// first requiring a failure signal (<see cref="HasFailureSignal"/>) — otherwise a successful answer
	/// whose large payload happens to embed one of the marker phrases would be retried, and <c>create-app</c>
	/// is not idempotent — then looking for one of three exact signatures in the call's serialized
	/// structured content and text content: <see cref="ODataRebuildMarker"/>, <see cref="LoginRejectionMarker"/>,
	/// or either of <see cref="HtmlPageInsteadOfJsonMarker"/> / <see cref="RedirectedToLoginPageMarker"/>.
	/// The login rejection additionally requires its companion segment
	/// (<see cref="LoginRejectionSubjectSeparator"/>), so the bare word does not classify any 401 text as a
	/// rejected login. <see cref="IsExcludedRealOutcome"/> runs before the marker match, so the two
	/// create-may-already-have-happened prefixes and an explicit <c>error-class=contention</c> never match
	/// even when a marker appears in the same payload. Pure and stand-free, so it is unit-tested directly
	/// (<c>TransientPlatformConditionRetryGateTests</c>).
	/// </summary>
	/// <param name="callResult">The tool call result to inspect, or <see langword="null"/>.</param>
	/// <returns><c>true</c> when one of the known transient signatures is present on a failed answer; otherwise <c>false</c>.</returns>
	internal static bool IsKnownTransientPlatformCondition(CallToolResult? callResult) {
		string text = DescribePayload(callResult);
		if (string.IsNullOrEmpty(text)) {
			return false;
		}

		string normalized = NormalizeEscapedQuotes(text);

		if (IsExcludedRealOutcome(normalized)) {
			return false;
		}

		if (!HasFailureSignal(callResult, normalized)) {
			return false;
		}

		return normalized.Contains(ODataRebuildMarker, StringComparison.Ordinal)
			|| HasLoginRejectionSignature(normalized)
			|| normalized.Contains(HtmlPageInsteadOfJsonMarker, StringComparison.Ordinal)
			|| normalized.Contains(RedirectedToLoginPageMarker, StringComparison.Ordinal);
	}

	/// <summary>
	/// Decides, from the same serialized payload <see cref="IsKnownTransientPlatformCondition"/> inspects,
	/// whether the transient condition is specifically the login rejection — the one case that calls for
	/// re-establishing the session rather than simply waiting and repeating the call.
	/// </summary>
	/// <param name="callResult">The tool call result to inspect, or <see langword="null"/>.</param>
	/// <returns><c>true</c> when the login-rejection signature is present; otherwise <c>false</c>.</returns>
	internal static bool IsLoginRejection(CallToolResult? callResult) =>
		HasLoginRejectionSignature(NormalizeEscapedQuotes(DescribePayload(callResult)));

	/// <summary>
	/// The login-rejection signature: <see cref="LoginRejectionMarker"/> followed — later in the same
	/// payload — by <see cref="LoginRejectionSubjectSeparator"/>, matching the shape of the message
	/// <c>Creatio.Client.CreatioClient.Login()</c> actually produces rather than the bare prefix on its own.
	/// </summary>
	/// <param name="normalizedPayload">The payload with escaped quotes normalized back to plain quotes.</param>
	/// <returns><c>true</c> when the login-rejection signature is present.</returns>
	private static bool HasLoginRejectionSignature(string normalizedPayload) {
		int prefixIndex = normalizedPayload.IndexOf(LoginRejectionMarker, StringComparison.Ordinal);
		return prefixIndex >= 0
			&& normalizedPayload.IndexOf(
				LoginRejectionSubjectSeparator,
				prefixIndex + LoginRejectionMarker.Length,
				StringComparison.Ordinal) >= 0;
	}

	/// <summary>
	/// Re-invokes <paramref name="invokeAsync"/> while its answer matches
	/// <see cref="IsKnownTransientPlatformCondition"/>, up to <see cref="MaxAttempts"/> attempts bounded
	/// by <see cref="OverallDeadline"/>, and returns the last answer once attempts run out — the caller's
	/// own assertions still decide pass/fail, exactly as <see cref="DataForgeReadinessGate"/> leaves the
	/// ready/not-ready decision to its caller.
	/// </summary>
	/// <param name="invokeAsync">Makes one attempt at the tool call.</param>
	/// <param name="reauthenticateAsync">
	/// Invoked instead of the fixed <see cref="RetryDelay"/> when the failed answer is specifically the
	/// login rejection (<see cref="IsLoginRejection"/>) — re-establishing the session (a fresh login, or a
	/// fresh MCP session, however the caller's harness layer does it) rather than blindly repeating a call
	/// that will fail the same way against the same stale session. May be <see langword="null"/> when the
	/// caller has no re-authentication seam available; the gate then falls back to the fixed delay for
	/// every matched condition, including the login rejection.
	/// </param>
	/// <param name="cancellationToken">Cancels the whole retry loop.</param>
	/// <returns>The last <see cref="CallToolResult"/> observed, whether or not it still matches a known transient condition.</returns>
	internal static async Task<CallToolResult> InvokeWithRetryAsync(
		Func<CancellationToken, Task<CallToolResult>> invokeAsync,
		Func<CancellationToken, Task>? reauthenticateAsync,
		CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(invokeAsync);

		System.Diagnostics.Stopwatch elapsedTimer = System.Diagnostics.Stopwatch.StartNew();
		CallToolResult last = await invokeAsync(cancellationToken);
		for (int attempt = 1; attempt < MaxAttempts; attempt++) {
			cancellationToken.ThrowIfCancellationRequested();
			if (!IsKnownTransientPlatformCondition(last)) {
				return last;
			}
			if (OverallDeadlineReached(elapsedTimer.Elapsed)) {
				break;
			}

			// One line per retry, on purpose. A retry that silently succeeds turns a REAL intermittent
			// regression wearing one of the transient shapes into a slow pass, and the concurrency test's
			// overlap degrades into a staggered one without any assertion noticing. Both are visible in the
			// CI log as a rising retry count; neither is visible without this line.
			TestContext.Out.WriteLine(
				$"[transient-retry] attempt {attempt} of {MaxAttempts - 1}: matched '{DescribeMatchedMarker(last)}' "
				+ $"after {elapsedTimer.Elapsed.TotalSeconds:F0}s; "
				+ (IsLoginRejection(last) && reauthenticateAsync is not null
					? "re-authenticating before the next attempt."
					: $"waiting {RetryDelay.TotalSeconds:F0}s before the next attempt."));
			if (IsLoginRejection(last) && reauthenticateAsync is not null) {
				await reauthenticateAsync(cancellationToken);
			} else {
				await Task.Delay(RetryDelay, cancellationToken);
			}

			last = await invokeAsync(cancellationToken);
		}

		return last;
	}

	private static bool OverallDeadlineReached(TimeSpan elapsed) => elapsed >= OverallDeadline;

	/// <summary>
	/// Names which of the transient signatures matched, for the retry log line. Reports the SAME order
	/// <see cref="IsKnownTransientPlatformCondition"/> evaluates, so the name always identifies the
	/// signature that actually caused the retry rather than the first one that happens to be present.
	/// </summary>
	private static string DescribeMatchedMarker(CallToolResult? callResult) {
		string normalized = NormalizeEscapedQuotes(DescribePayload(callResult));
		if (normalized.Contains(ODataRebuildMarker, StringComparison.Ordinal)) {
			return ODataRebuildMarker;
		}

		if (HasLoginRejectionSignature(normalized)) {
			return "login rejection";
		}

		if (normalized.Contains(HtmlPageInsteadOfJsonMarker, StringComparison.Ordinal)) {
			return HtmlPageInsteadOfJsonMarker;
		}

		return normalized.Contains(RedirectedToLoginPageMarker, StringComparison.Ordinal)
			? RedirectedToLoginPageMarker
			: "unclassified";
	}

	/// <summary>
	/// The exclusions that are enforced explicitly, checked BEFORE any marker match because for each of
	/// them a transient marker can legitimately appear in the very same payload:
	/// <see cref="ApplicationAlreadyCreatedMarker"/> and <see cref="ApplicationCreateTimeoutMarker"/> (the
	/// two <c>LoadApplicationInfoWithRetry</c> prefixes — the create may already have taken effect, so a
	/// retry would replay the same application name/code), and <see cref="ContentionErrorClassMarker"/>
	/// (handled elsewhere in the harness by contract).
	/// </summary>
	/// <param name="normalizedPayload">The payload with escaped quotes normalized back to plain quotes.</param>
	/// <returns><c>true</c> when the answer names a real outcome that must never be retried.</returns>
	private static bool IsExcludedRealOutcome(string normalizedPayload) =>
		normalizedPayload.Contains(ApplicationAlreadyCreatedMarker, StringComparison.Ordinal)
		|| normalizedPayload.Contains(ApplicationCreateTimeoutMarker, StringComparison.Ordinal)
		|| normalizedPayload.Contains(ContentionErrorClassMarker, StringComparison.Ordinal)
		|| Array.Exists(SectionAlreadyCreatedMarkers,
			marker => normalizedPayload.Contains(marker, StringComparison.Ordinal));

	/// <summary>
	/// A failure signal is required before any marker match counts as a known transient platform
	/// condition: either the MCP transport itself flagged the call as an error
	/// (<c>callResult.IsError == true</c>), or the payload carries the tools' own
	/// <c>{"success":false,...}</c> failure shape (checked against <paramref name="normalizedPayload"/>,
	/// which has already had escaped quotes normalized back to plain quotes).
	/// </summary>
	private static bool HasFailureSignal(CallToolResult? callResult, string normalizedPayload) =>
		callResult?.IsError == true
		|| normalizedPayload.Contains(SuccessFalseMarker, StringComparison.Ordinal);

	/// <summary>
	/// Un-escapes a quote character back to <c>"</c> so a failure-shape marker is found whether the
	/// tool's JSON body was serialized once (plain quotes) or embedded as a JSON string inside the outer
	/// <see cref="CallToolResult"/> serialization — the payload is JSON-inside-JSON, and the outer
	/// serialization step re-encodes those quotes. Two encodings are normalized because callers must not
	/// assume one over the other: the conventional backslash escape (<c>\"</c>), and the Unicode escape
	/// <c>System.Text.Json</c>'s default (HTML-safe) encoder actually emits for a quote nested inside
	/// another string (<c>\u0022</c>) — observed directly from <see cref="DescribePayload"/>'s own output.
	/// </summary>
	private static string NormalizeEscapedQuotes(string text) =>
		text.Replace("\\u0022", "\"", StringComparison.OrdinalIgnoreCase)
			.Replace("\\\"", "\"", StringComparison.Ordinal);

	private static string DescribePayload(CallToolResult? callResult) {
		if (callResult is null) {
			return string.Empty;
		}

		// Same shape DataForgeReadinessGate's diagnostics use: serialize both the structured content and
		// the raw content so the markers are found regardless of which channel the tool used to carry them.
		string structured = callResult.StructuredContent is null
			? string.Empty
			: JsonSerializer.Serialize(callResult.StructuredContent);
		string content = JsonSerializer.Serialize(callResult.Content ?? []);
		return structured + content;
	}
}
