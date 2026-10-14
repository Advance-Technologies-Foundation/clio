namespace Clio.Package;

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Clio.Common;

/// <summary>
/// Makes the session of one package installation reach the design node of a Creatio web farm before any
/// package data is sent.
/// </summary>
/// <remarks>
/// A web farm gateway (for example the Creatio K8s Control Plane runtime gateway) routes a request to the design
/// node only when it carries the <c>StickySession</c> cookie, and Creatio issues that cookie when the server
/// session starts: in the response to the first authenticated request after login, never in the login response.
/// Package installation state (upload, temp archive, installation log) is kept per node, so an installation whose
/// first upload chunk is sent without the cookie lands on a common node and is split across nodes.
/// </remarks>
public interface IDesignNodeSessionGuard {

	/// <summary>
	/// Starts the server session on <paramref name="session"/> and, when the environment is a web farm that issued
	/// <c>StickySession</c>, confirms that the session is routed to the design node.
	/// </summary>
	/// <remarks>
	/// On a web farm only the design node may change during an installation; the common nodes receive the result
	/// through publish and rollout. An installation whose session is not confirmed on the design node is therefore
	/// refused before anything is uploaded, whatever the probe answered instead: a common node's own answer, a
	/// missing probe (HTTP 404) or any other status. Only the gateway's own answer that the design node is down for
	/// the moment (HTTP 502, 503 or 504 without the probe's body, for example while the design node restarts after
	/// the previous installation) is waited out first, for a bounded time. A session-start request that does not
	/// succeed is reported as a warning and does not stop the installation by itself.
	/// </remarks>
	/// <param name="session">The client that carries every request of the installation.</param>
	/// <param name="environmentSettings">The environment the package is installed into.</param>
	/// <exception cref="InvalidOperationException">
	/// The environment issued <c>StickySession</c>, but the design-node probe did not answer 200 with
	/// <c>isDesignNode: true</c>, or kept answering that the design node is unavailable until the wait ran out.
	/// </exception>
	void Prepare(ICreatioApplicationClient session, EnvironmentSettings environmentSettings);

	/// <summary>
	/// On a web farm, waits after the installation restarted the application until the design node has gone down
	/// and answers again, so the next installation does not start on a design node that is about to stop.
	/// </summary>
	/// <remarks>
	/// <c>RestartApp</c> answers before the design node stops, and the node disappears from the gateway a few
	/// seconds later. The wait is bounded; a design node that does not come back in time is reported as a warning,
	/// because the package itself is installed. Outside a web farm (no <c>StickySession</c>) it returns at once.
	/// </remarks>
	/// <param name="session">The installation session the restart was sent through.</param>
	/// <param name="environmentSettings">The environment that was restarted.</param>
	void WaitForRestartedDesignNode(ICreatioApplicationClient session, EnvironmentSettings environmentSettings);

}

/// <inheritdoc cref="IDesignNodeSessionGuard" />
public class DesignNodeSessionGuard : IDesignNodeSessionGuard {

	#region Constants: Private

	private const string StickySessionCookieName = "StickySession";
	private const string IsDesignNodePropertyName = "isDesignNode";
	private const string EmptyJsonBody = "{}";

	#endregion

	#region Constants: Internal

	internal const string RoutedToDesignNodeMessage =
		"Web farm detected: the installation session is routed to the design node.";

	internal const string SessionStartFailedMessageFormat =
		"The request that starts the installation session answered HTTP {0}. If this environment is a web farm, "
		+ "the installation may not reach its design node.";

	internal const string WaitingForDesignNodeMessageFormat =
		"The design node of the web farm is not available (the design-node probe {0}), for example "
		+ "because it is restarting after a previous installation. Waiting up to {1} minutes for it before "
		+ "uploading anything.";

	internal const string StillWaitingForDesignNodeMessageFormat =
		"Still waiting for the design node of the web farm ({0} of {1} minutes).";

	internal const string WaitingForRestartMessageFormat =
		"Waiting for the design node of the web farm to come back after the restart (up to {0} minutes).";

	internal const string BackAfterRestartMessage = "The design node of the web farm is back after the restart.";

	internal const string NotBackAfterRestartMessageFormat =
		"The design node of the web farm did not answer within {0} minutes after the restart. The package is "
		+ "installed; the next installation on this web farm waits for the design node or stops before uploading.";

	internal const string DesignNodeNotConfirmedMessageFormat =
		"The installation session does not reach the design node of the web farm (the design-node probe "
		+ "{0}). Nothing was uploaded: on a web farm packages are installed only on the design node. "
		+ "Check that the design node is running and that the gateway routes requests carrying the StickySession "
		+ "cookie to it.";

	#endregion

	#region Fields: Private

	// A restarting design pod of a Control Plane runtime was back within two minutes on a local stand. Five
	// minutes leave room for a slower production pod without letting a design node that is really down hold a CI
	// job for long; a longer restart is still covered across a pipeline, because the next command's Prepare
	// waits again before it uploads.
	private static readonly TimeSpan DesignNodeWaitBudget = TimeSpan.FromMinutes(5);
	private static readonly TimeSpan DesignNodePollInterval = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan DesignNodeWaitReportInterval = TimeSpan.FromMinutes(1);

	// On a local Control Plane runtime the design node left the gateway 15-20 seconds after RestartApp answered.
	private static readonly TimeSpan RestartShutdownWindow = TimeSpan.FromSeconds(60);

	// A probe is a tiny anonymous GET; the client's default of 100 seconds would let one hung probe outlast the
	// polling it is part of.
	private const int ProbeTimeoutMilliseconds = 15_000;

	// The status of a probe that got no HTTP answer at all.
	private const HttpStatusCode ProbeFailedStatus = 0;

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;
	private readonly TimeProvider _timeProvider;
	private readonly ICancellableDelay _delay;

	#endregion

	#region Constructors: Public

	/// <summary>Initializes a new instance of the <see cref="DesignNodeSessionGuard" /> class.</summary>
	/// <param name="serviceUrlBuilder">Builds the session-start and probe URLs for the target environment.</param>
	/// <param name="logger">Reports the web farm outcome.</param>
	/// <param name="timeProvider">Measures how long the guard has waited for an unavailable design node.</param>
	/// <param name="delay">Spaces the design-node probes while the guard waits.</param>
	public DesignNodeSessionGuard(IServiceUrlBuilder serviceUrlBuilder, ILogger logger, TimeProvider timeProvider,
		ICancellableDelay delay) {
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		logger.CheckArgumentNull(nameof(logger));
		timeProvider.CheckArgumentNull(nameof(timeProvider));
		delay.CheckArgumentNull(nameof(delay));
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
		_timeProvider = timeProvider;
		_delay = delay;
	}

	#endregion

	#region Methods: Private

	// The request exists to be the first authenticated request of the session, which is where Creatio starts the
	// server session and appends StickySession. A login failure still throws here, exactly as it did on the first
	// upload chunk before. Any other unsuccessful answer is reported, because without a started session a web
	// farm issues no StickySession and the guard cannot tell it from a single node.
	private void StartServerSession(ICreatioApplicationClient session, EnvironmentSettings environmentSettings) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetCurrentUserInfo, environmentSettings);
		using HttpResponseMessage response = session.ExecutePostRequestAsync(url, EmptyJsonBody)
			.GetAwaiter().GetResult();
		if (!response.IsSuccessStatusCode) {
			_logger.WriteWarning(string.Format(CultureInfo.InvariantCulture, SessionStartFailedMessageFormat,
				(int)response.StatusCode));
		}
	}

	// What a gateway answers itself while the design backend is down: no live server (503), a refused or reset
	// connection to it (502) or no answer in time (504). A Creatio node answers the probe with its own body - a
	// common node with 503 {"isDesignNode":false} - so a 503 that carries that body means the session reached a
	// node that is not the design node, and waiting would not change where it is routed.
	private static bool MeansDesignNodeIsUnavailable(ProbeAnswer answer) =>
		!answer.IsProbeBody
		&& answer.Status is ProbeFailedStatus or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway
			or HttpStatusCode.GatewayTimeout;

	// Prepare takes no cancellation token - the installation it precedes cannot be cancelled either - so the wait
	// is bounded by its budget alone, and the last pause is cut to what is left of it.
	private ProbeAnswer WaitForDesignNode(ICreatioApplicationClient session, EnvironmentSettings environmentSettings) {
		ProbeAnswer answer = TryProbeDesignNode(session, environmentSettings);
		if (answer.IsDesignNode || !MeansDesignNodeIsUnavailable(answer)) {
			return answer;
		}
		int budgetMinutes = (int)DesignNodeWaitBudget.TotalMinutes;
		_logger.WriteInfo(string.Format(CultureInfo.InvariantCulture, WaitingForDesignNodeMessageFormat,
			DescribeAnswer(answer), budgetMinutes));
		long startedAt = _timeProvider.GetTimestamp();
		TimeSpan nextReportAt = DesignNodeWaitReportInterval;
		TimeSpan waited = TimeSpan.Zero;
		while (!answer.IsDesignNode && MeansDesignNodeIsUnavailable(answer) && waited < DesignNodeWaitBudget) {
			TimeSpan remaining = DesignNodeWaitBudget - waited;
			Pause(remaining < DesignNodePollInterval ? remaining : DesignNodePollInterval);
			waited = _timeProvider.GetElapsedTime(startedAt);
			if (waited >= nextReportAt) {
				_logger.WriteInfo(string.Format(CultureInfo.InvariantCulture, StillWaitingForDesignNodeMessageFormat,
					(int)waited.TotalMinutes, budgetMinutes));
				nextReportAt += DesignNodeWaitReportInterval;
			}
			answer = TryProbeDesignNode(session, environmentSettings);
		}
		return answer;
	}

	private static bool HasStickySession(ICreatioApplicationClient session) =>
		session.ExportSessionCookies()
			.Any(cookie => string.Equals(cookie.Name, StickySessionCookieName, StringComparison.Ordinal));

	private static string DescribeAnswer(ProbeAnswer answer) =>
		answer.Status == ProbeFailedStatus
			? "got no answer"
			: string.Format(CultureInfo.InvariantCulture, "answered HTTP {0}", (int)answer.Status);

	private void Pause(TimeSpan duration) => _delay.WaitOrCancelled(duration, CancellationToken.None);

	// A probe can also fail outright: a connection reset by a stopping node, or a timeout on a hung or starting
	// one (neither wait has a caller token, so a cancellation is always the probe timeout). The session-start
	// request has just reached the same gateway, so such a failure points at the design backend and counts as
	// one more "not available yet"; its status stays 0, which nothing accepts as a confirmation.
	private ProbeAnswer TryProbeDesignNode(ICreatioApplicationClient session,
		EnvironmentSettings environmentSettings) {
		try {
			return ProbeDesignNode(session, environmentSettings);
		}
		catch (HttpRequestException) {
			return default;
		}
		catch (OperationCanceledException) {
			return default;
		}
	}

	private ProbeAnswer ProbeDesignNode(ICreatioApplicationClient session, EnvironmentSettings environmentSettings) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DesignReady, environmentSettings);
		using HttpResponseMessage response = session.ExecuteGetRequestAsync(url, ProbeTimeoutMilliseconds)
			.GetAwaiter().GetResult();
		bool? isDesignNodeInBody = ReadIsDesignNode(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
		return new ProbeAnswer(response.StatusCode, isDesignNodeInBody.HasValue,
			response.StatusCode == HttpStatusCode.OK && isDesignNodeInBody == true);
	}

	// A 200 alone is not proof: a gateway catch-all page or a redirect that ends on a 200 page would pass too.
	// Only the probe's own body, {"isDesignNode":true}, confirms the design node. Returns null for any body that is
	// not the probe's answer (a gateway page, an empty body).
	private static bool? ReadIsDesignNode(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return null;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			if (document.RootElement.ValueKind != JsonValueKind.Object) {
				return null;
			}
			foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
				if (string.Equals(property.Name, IsDesignNodePropertyName, StringComparison.OrdinalIgnoreCase)
					&& property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) {
					return property.Value.ValueKind == JsonValueKind.True;
				}
			}
			return null;
		}
		catch (JsonException) {
			return null;
		}
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public void Prepare(ICreatioApplicationClient session, EnvironmentSettings environmentSettings) {
		session.CheckArgumentNull(nameof(session));
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		StartServerSession(session, environmentSettings);
		if (!HasStickySession(session)) {
			return;
		}
		ProbeAnswer answer = WaitForDesignNode(session, environmentSettings);
		if (!answer.IsDesignNode) {
			throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
				DesignNodeNotConfirmedMessageFormat, DescribeAnswer(answer)));
		}
		_logger.WriteInfo(RoutedToDesignNodeMessage);
	}

	/// <inheritdoc />
	public void WaitForRestartedDesignNode(ICreatioApplicationClient session,
		EnvironmentSettings environmentSettings) {
		session.CheckArgumentNull(nameof(session));
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		if (!HasStickySession(session)) {
			return;
		}
		int budgetMinutes = (int)DesignNodeWaitBudget.TotalMinutes;
		_logger.WriteInfo(string.Format(CultureInfo.InvariantCulture, WaitingForRestartMessageFormat, budgetMinutes));
		long startedAt = _timeProvider.GetTimestamp();
		// The node keeps answering for a few seconds after RestartApp; wait until it has stopped answering as the
		// design node - two answers in a row, so one connection reset while it drains is not taken for the stop - or
		// until the shutdown window passes (a restart too quick to observe).
		int notConfirmedInARow = 0;
		while (notConfirmedInARow < 2 && _timeProvider.GetElapsedTime(startedAt) < RestartShutdownWindow) {
			Pause(DesignNodePollInterval);
			notConfirmedInARow = TryProbeDesignNode(session, environmentSettings).IsDesignNode
				? 0
				: notConfirmedInARow + 1;
		}
		// Any answer other than the probe's confirmation means the node is still coming back. Every probe follows a
		// pause, the last one included, so a node that comes back at the very end of the budget is still seen.
		TimeSpan nextReportAt = DesignNodeWaitReportInterval;
		TimeSpan waited = _timeProvider.GetElapsedTime(startedAt);
		while (true) {
			TimeSpan remaining = DesignNodeWaitBudget - waited;
			Pause(remaining < DesignNodePollInterval ? remaining : DesignNodePollInterval);
			waited = _timeProvider.GetElapsedTime(startedAt);
			if (waited >= nextReportAt) {
				_logger.WriteInfo(string.Format(CultureInfo.InvariantCulture, StillWaitingForDesignNodeMessageFormat,
					(int)waited.TotalMinutes, budgetMinutes));
				nextReportAt += DesignNodeWaitReportInterval;
			}
			if (TryProbeDesignNode(session, environmentSettings).IsDesignNode) {
				_logger.WriteInfo(BackAfterRestartMessage);
				return;
			}
			if (waited >= DesignNodeWaitBudget) {
				break;
			}
		}
		_logger.WriteWarning(string.Format(CultureInfo.InvariantCulture, NotBackAfterRestartMessageFormat,
			budgetMinutes));
	}

	#endregion

	#region Nested types: Private

	// IsProbeBody: the body is the probe's own JSON answer (any node of Creatio), not a gateway page.
	private readonly record struct ProbeAnswer(HttpStatusCode Status, bool IsProbeBody, bool IsDesignNode);

	#endregion

}
