using System;
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
		try {
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ProcessBuilderPing);
			string response = _applicationClient.ExecutePostRequest(
				url, "{}", ProbeTimeoutMs, ProbeAttempts, ProbeDelaySec);
			using JsonDocument document = JsonDocument.Parse(response);
			if (!document.RootElement.TryGetProperty("PingResult", out JsonElement result)
				|| !result.TryGetProperty("success", out JsonElement success)
				|| success.ValueKind != JsonValueKind.True) {
				// Parsed, but not our envelope: a proxy, a login redirect or another responder. Not evidence.
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

}
