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
/// The predicate is deliberately narrow. It must NOT retry:
/// <list type="bullet">
/// <item><description>a SUCCESSFUL answer whose large payload happens to embed one of the marker
/// phrases — a failure signal (a transport-level error, or the tools' own <c>success:false</c> shape)
/// is required before any marker is even considered, because <c>create-app</c> is not idempotent and
/// replaying a successful create would be wrong;</description></item>
/// <item><description>a failed assertion on returned data (a wrong value, a missing field) — that is a
/// real defect and retrying it would hide it;</description></item>
/// <item><description><c>success:false</c> carrying a business-rule message (validation, a duplicate
/// name, a missing dependency) — that is a real, repeatable outcome, not a platform hiccup;</description></item>
/// <item><description><c>error-class=contention</c> — contention has its own dedicated handling
/// elsewhere in the harness and is not one of the three platform conditions this gate exists for;</description></item>
/// <item><description>a create that already happened — <c>ApplicationCreateService</c>'s "created but
/// its metadata could not be loaded" failure names a real side effect on the platform, so retrying it
/// would replay the same application name/code against an application that already exists.</description></item>
/// </list>
/// Only the three exact signatures documented on <see cref="IsKnownTransientPlatformCondition"/> match,
/// so anything else — including the cases above — falls straight through as a real result.
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
	/// Hard upper bound for the whole retry loop, regardless of attempt count. Kept genuinely above the
	/// ~120s retry window (see <see cref="MaxAttempts"/>) so the attempt cap — not this deadline — is the
	/// one that normally ends the loop; the deadline exists only to bound a pathological case where
	/// individual attempts themselves run long.
	/// </summary>
	private static readonly TimeSpan OverallDeadline = TimeSpan.FromMinutes(3);

	/// <summary>
	/// Decides whether a FAILED MCP tool answer is a KNOWN transient platform condition worth retrying, by
	/// first requiring a failure signal (<see cref="HasFailureSignal"/>) — otherwise a successful answer
	/// whose large payload happens to embed one of the marker phrases would be retried, and <c>create-app</c>
	/// is not idempotent — then looking for one of three exact signatures in the call's serialized
	/// structured content and text content: <see cref="ODataRebuildMarker"/>, <see cref="LoginRejectionMarker"/>,
	/// or either of <see cref="HtmlPageInsteadOfJsonMarker"/> / <see cref="RedirectedToLoginPageMarker"/>.
	/// <see cref="ApplicationAlreadyCreatedMarker"/> is excluded before the marker match: it means the
	/// create already happened, and retrying would replay the same application name/code. Pure and
	/// stand-free, so it is unit-tested directly (<c>TransientPlatformConditionRetryGateTests</c>).
	/// </summary>
	/// <param name="callResult">The tool call result to inspect, or <see langword="null"/>.</param>
	/// <returns><c>true</c> when one of the known transient signatures is present on a failed answer; otherwise <c>false</c>.</returns>
	internal static bool IsKnownTransientPlatformCondition(CallToolResult? callResult) {
		string text = DescribePayload(callResult);
		if (string.IsNullOrEmpty(text)) {
			return false;
		}

		string normalized = NormalizeEscapedQuotes(text);

		if (normalized.Contains(ApplicationAlreadyCreatedMarker, StringComparison.Ordinal)) {
			return false;
		}

		if (!HasFailureSignal(callResult, normalized)) {
			return false;
		}

		return normalized.Contains(ODataRebuildMarker, StringComparison.Ordinal)
			|| normalized.Contains(LoginRejectionMarker, StringComparison.Ordinal)
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
		DescribePayload(callResult).Contains(LoginRejectionMarker, StringComparison.Ordinal);

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
