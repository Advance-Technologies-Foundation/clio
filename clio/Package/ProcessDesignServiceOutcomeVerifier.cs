using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Clio.Common;

namespace Clio.Package;

/// <summary>
/// Verifies a bundled package's outcome by asking <c>ProcessDesignService.Ping</c> whether the package's own
/// code is serving on the target.
/// </summary>
/// <remarks>
/// The name says what it USES, while <see cref="IPackageInstallOutcomeVerifier"/> says what it ANSWERS.
/// <para>
/// The operation it calls is UNGATED on the package side, which is what makes this check answer the install
/// question and only the install question. An earlier version probed <c>ListUserTasks</c> instead — a real,
/// gated operation — and that conflated two verdicts: "the build did not take" and "you may not design
/// processes". The second is not this command's business. A caller lacking the right finds out at its next
/// call, from the guard's own message, which names the right; a caller whose build failed finds out here.
/// </para>
/// <para>
/// Two of the three negative causes are distinguished, and the third deliberately is not: a body that parses
/// but is not this package's envelope produces its own diagnosis (something else is serving that route), a
/// transport failure is logged with its status, and "nothing answered on a route that should exist" is left to
/// the caller's message because that is the case the caller can act on.
/// </para>
/// <para>
/// What this therefore does NOT establish: that the caller can USE the package, and that the package's
/// dependencies resolve at runtime. The second is bounded — <c>ErrorOr</c> and <c>ATF.Repository</c> are
/// compile references, so a successful build on the target already implies they are present.
/// </para>
/// </remarks>
public class ProcessDesignServiceOutcomeVerifier : IPackageInstallOutcomeVerifier {

	#region Constants: Private

	/// <summary>
	/// Per-request budget for a probe, in milliseconds.
	/// </summary>
	/// <remarks>
	/// <see cref="IApplicationClient.ExecutePostRequest"/> defaults to <see cref="Timeout.Infinite"/>, which
	/// is wrong for the one call that decides the install command's exit code: an instance that accepts the
	/// connection right after its restart but stalls behind the configuration-build lock would hang the CLI
	/// with no output and no way out but Ctrl+C. Every probe in <see cref="IServerReadinessWaiter"/> is
	/// bounded for exactly this reason; the final probe must not be the only unbounded call in the flow.
	/// <c>Ping</c> returns a constant — it opens no scope, touches no database, and answers in milliseconds.
	/// </remarks>
	private const int ProbeTimeoutMs = 15_000;

	/// <summary>
	/// Attempts per verification, retried because the readiness gate the caller waits on is weaker than the
	/// question asked here.
	/// </summary>
	/// <remarks>
	/// A readiness wait proves the host answers <c>/api/HealthCheck/Ping</c>, which a still-draining worker
	/// or one whose configuration workspace has not finished loading can also do. A single probe therefore
	/// risks reporting "the environment did not compile the package" about an environment that answers
	/// correctly a few seconds later. Three attempts, because this probe alone decides the exit code.
	/// <para>
	/// Bounds what this actually buys: the retry lives inside <see cref="IApplicationClient"/> and re-issues on
	/// a TRANSPORT failure, so it covers a refused connection or a timeout during warm-up. A response that
	/// arrives and parses — including a 200 from a proxy — is evaluated exactly once, by design: re-asking a
	/// responder that answered would return the same answer three times.
	/// </para>
	/// </remarks>
	private const int ProbeAttempts = 3;

	/// <summary>Delay between probe attempts, in seconds.</summary>
	private const int ProbeDelaySec = 5;

	#endregion

	#region Fields: Private

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>
	/// Initializes a new instance of the <see cref="ProcessDesignServiceOutcomeVerifier"/> class.
	/// </summary>
	/// <param name="applicationClient">Client used to call the service on the target environment.</param>
	/// <param name="serviceUrlBuilder">Builder for the <c>ProcessDesignService</c> route.</param>
	/// <param name="logger">Logger used to report why a probe failed.</param>
	public ProcessDesignServiceOutcomeVerifier(
		IApplicationClient applicationClient,
		IServiceUrlBuilder serviceUrlBuilder,
		ILogger logger) {
		applicationClient.CheckArgumentNull(nameof(applicationClient));
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		logger.CheckArgumentNull(nameof(logger));
		_applicationClient = applicationClient;
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	/// <remarks>
	/// The response is parsed rather than pattern-matched, because the interesting failure is an HTML error
	/// page from IIS when the route does not resolve — that fails <see cref="JsonDocument.Parse"/> and is
	/// correctly reported as "nothing is serving", whereas a substring search over it could accidentally match.
	/// Route resolution is what makes the no-assembly case decidable at all: Creatio registers services by
	/// reflecting over LOADED types, so with no compiled assembly there is no type, no route, and nothing that
	/// can answer.
	/// <para>
	/// The envelope is checked, not merely the HTTP status: a proxy, a login redirect or a 404 body that happens
	/// to be JSON must not read as evidence. <c>success</c> must be present and <see langword="true"/>.
	/// </para>
	/// </remarks>
	public bool IsPackageOperational(string packageName, out string diagnosis) {
		diagnosis = null;
		string url = null;
		try {
			url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ProcessBuilderPing);
			string response = _applicationClient.ExecutePostRequest(
				url, "{}", ProbeTimeoutMs, ProbeAttempts, ProbeDelaySec);
			// BEFORE parsing, because this is the one non-JSON answer that has a specific cause and a specific
			// remedy. The non-generic ExecutePostRequest returns the login page VERBATIM when automatic
			// re-authentication fails (only the generic overload turns that into an exception), so without this
			// check the HTML fails JsonDocument.Parse, lands in the catch with no diagnosis, and the caller's
			// fallback message blames the configuration build and sends the operator to the build log — for a
			// credentials problem. That is exactly the conflation the remarks above forbid.
			if (ReauthExecutor.IsSessionExpiredResponse(response)) {
				diagnosis =
					$"{packageName} was installed, but {url} answered with a login page: the session expired and "
					+ "automatic re-authentication did not restore it. The configuration build is NOT implicated "
					+ "and the package's state is UNKNOWN — check the environment's credentials, then verify "
					+ "with 'clio call-service --service-path rest/ProcessDesignService/Ping -m POST -b {} "
					+ "-e <environment>'.";
				return false;
			}
			using JsonDocument document = JsonDocument.Parse(response);
			// Both branches below are failures, but they send the reader to DIFFERENT places, which is the whole
			// reason this method reports a diagnosis rather than letting the caller's generic message stand:
			// that message blames the configuration build, and neither of these is a build problem.
			if (!document.RootElement.TryGetProperty("PingResult", out JsonElement result)) {
				diagnosis =
					$"{packageName} was installed, but {url} answered with a JSON body carrying no PingResult, "
					+ "so something other than this package is serving that route — a reverse proxy, an "
					+ "authenticating gateway or an expired-session redirect. The configuration build is NOT "
					+ "implicated; check what sits in front of the environment. First 200 characters of the "
					+ $"answer: {Truncate(response, 200)}";
				return false;
			}
			if (!result.TryGetProperty("success", out JsonElement success)
				|| success.ValueKind != JsonValueKind.True) {
				diagnosis =
					$"{packageName}'s Ping route answered, but not with success — so the package is serving and "
					+ "is reporting a problem, which the shipped build cannot do (its Ping returns a constant). "
					+ "Either the serving build is not the one clio ships, or something is answering in this "
					+ $"package's envelope. First 200 characters of the answer: {Truncate(response, 200)}";
				return false;
			}
			return true;
		} catch (Exception e) {
			// WriteError, not WriteInfo: this line carries the WebException status / HTTP code, i.e. the only
			// statement of WHY the probe failed. The caller writes the summary at error level, so logging the
			// cause below it hid the useful half from anyone filtering on errors.
			_logger.WriteError($"ProcessDesignService did not answer: {e.GetReadableMessageException()}");
			return false;
		}
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Shortens an unexpected response for inclusion in a diagnosis.
	/// </summary>
	/// <remarks>
	/// Delegates the actual work to <see cref="TextUtilities.SanitizeForDisplay"/> rather than repeating it.
	/// This method used to carry its own copy of the same control-character strip and truncation, which left
	/// three caps for one class of text in one feature and, worse, meant a future hardening of the shared
	/// helper would silently miss this call site — the one that quotes a body from an UNKNOWN responder.
	/// Control characters are the security-relevant half: the value goes straight into a log line and, on the
	/// MCP path, an agent's context, so CR/LF or ANSI escapes in it could forge or overwrite lines around it.
	/// <para>
	/// What is NOT delegated is the empty case. The helper returns empty input unchanged, which would render as
	/// nothing at all in the middle of a sentence; <c>(empty)</c> says that the responder answered and said
	/// nothing, which is a different fact from a short answer. Note it is not reachable today: both call sites
	/// sit after a successful <c>JsonDocument.Parse</c>, which rejects null, empty and whitespace-only input
	/// into the catch-all above. Kept as defence for a future caller that quotes a body before parsing it.
	/// </para>
	/// </remarks>
	private static string Truncate(string response, int max) =>
		string.IsNullOrEmpty(response)
			? "(empty)"
			: TextUtilities.SanitizeForDisplay(response, max);

	#endregion

}
