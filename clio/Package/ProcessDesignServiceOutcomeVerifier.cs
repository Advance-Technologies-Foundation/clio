using System;
using System.Text.Json;
using System.Threading;
using Clio.Common;

namespace Clio.Package;

/// <summary>
/// Verifies a bundled package's outcome by asking <c>ProcessDesignService</c> — the service the
/// <c>CrtProcessBuilder</c> package serves — whether it answers on the target.
/// </summary>
/// <remarks>
/// The name says what it USES, while <see cref="IPackageInstallOutcomeVerifier"/> says what it ANSWERS.
/// <para>
/// One real weakness, worth naming rather than hiding: it cannot tell WHICH build answered, so on an upgrade
/// a still-serving old assembly passes. That is the half a platform-generic signal (the installation log plus
/// the <c>ConfActivityLog</c> compilation record) would add — see the interface.
/// </para>
/// <para>
/// What is NOT a weakness, though it reads like one: <c>ListUserTasks</c> is gated on
/// <c>CanManageProcessDesign</c> plus a General user inside the package, so a caller lacking that right fails
/// this check even though the archive installed. That is the correct answer to the question this verifier
/// exists to answer — not "did the archive install" but "is the capability usable". The identity that
/// installs is the identity that will use it: clio carries one credential per environment, and an agent
/// installing the package mid-task cannot complete that task without the right either. Reporting success
/// would send it on to a process-designer call that fails with a raw service rejection, i.e. the same
/// verdict from a place that carries no diagnosis. So the <c>errorMessage</c> branch below does not paper
/// over the failure — it explains it, says a re-install cannot fix it, and names the right to grant.
/// <para>
/// The corollary for any package-agnostic replacement: a build-log signal answers "did it compile", which is
/// NOT this question. It would report success to a caller that still cannot use the feature. The two
/// checks are complements, not substitutes.
/// </para>
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
	/// bounded for exactly this reason; the final probe must not be the only unbounded call in the flow. A
	/// serving <c>ListUserTasks</c> answers in well under a second — it reads a task catalogue, nothing more.
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
	/// correctly reported as "not answering", whereas a substring search over it could accidentally match.
	/// </remarks>
	public bool IsPackageOperational(string packageName, out string diagnosis) {
		diagnosis = null;
		try {
			string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ListUserTasks);
			string response = _applicationClient.ExecutePostRequest(
				url, "{}", ProbeTimeoutMs, ProbeAttempts, ProbeDelaySec);
			using JsonDocument document = JsonDocument.Parse(response);
			if (!document.RootElement.TryGetProperty("ListUserTasksResult", out JsonElement result)) {
				return false;
			}
			if (result.TryGetProperty("success", out JsonElement success)
				&& success.ValueKind == JsonValueKind.True) {
				return true;
			}
			// A PARSEABLE envelope saying success:false proves the assembly exists and is serving — the
			// failure is INSIDE it, and errorMessage is the only field that says what. Discarding it and
			// letting the caller print "the environment did not compile the package" sends the reader to a
			// build log that is clean. The likeliest cause is authorization: the package returns the
			// process-design guard's rejection this way, and installing a package does not grant
			// CanManageProcessDesign.
			if (result.TryGetProperty("errorMessage", out JsonElement message)
				&& message.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(message.GetString())) {
				diagnosis =
					$"{packageName} was installed and ProcessDesignService is responding, but it rejected the " +
					$"check: {message.GetString()}. The package compiled — this is not a build failure, so " +
					"re-installing will NOT help and only costs another configuration build. If the message is " +
					"about permissions, note that ListUserTasks requires the CanManageProcessDesign operation " +
					"and a General (non-portal) user, which installing a package does not grant — grant those, " +
					"then verify with 'clio call-service --service-path " +
					"rest/ProcessDesignService/ListUserTasks -m POST -b {} -e <environment>'.";
			}
			return false;
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
