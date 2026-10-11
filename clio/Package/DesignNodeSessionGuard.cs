namespace Clio.Package;

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
	/// refused before anything is uploaded. The one exception is a platform without the probe (HTTP 404), where
	/// the routing cannot be checked and the installation continues with a warning.
	/// </remarks>
	/// <param name="session">The client that carries every request of the installation.</param>
	/// <param name="environmentSettings">The environment the package is installed into.</param>
	/// <exception cref="InvalidOperationException">
	/// The environment issued <c>StickySession</c>, but the design-node probe did not confirm the design node and
	/// was not missing (any answer other than 200 with <c>isDesignNode: true</c>, or 404).
	/// </exception>
	void Prepare(ICreatioApplicationClient session, EnvironmentSettings environmentSettings);

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

	internal const string DesignProbeUnavailableMessage =
		"Web farm detected, but this Creatio version has no design-node probe (HTTP 404); "
		+ "continuing without confirming that the installation reaches the design node.";

	internal const string DesignNodeNotConfirmedMessageFormat =
		"The installation session does not reach the design node of the web farm (the design-node probe "
		+ "answered HTTP {0}). Nothing was uploaded: on a web farm packages are installed only on the design node. "
		+ "Check that the design node is running and that the gateway routes requests carrying the StickySession "
		+ "cookie to it.";

	#endregion

	#region Fields: Private

	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	/// <summary>Initializes a new instance of the <see cref="DesignNodeSessionGuard" /> class.</summary>
	/// <param name="serviceUrlBuilder">Builds the session-start and probe URLs for the target environment.</param>
	/// <param name="logger">Reports the web farm outcome.</param>
	public DesignNodeSessionGuard(IServiceUrlBuilder serviceUrlBuilder, ILogger logger) {
		serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
		logger.CheckArgumentNull(nameof(logger));
		_serviceUrlBuilder = serviceUrlBuilder;
		_logger = logger;
	}

	#endregion

	#region Methods: Private

	// The status of this request is deliberately ignored: its only purpose is to be the first authenticated
	// request of the session, which is where Creatio starts the server session and appends StickySession.
	// A login failure still throws here, exactly as it did on the first upload chunk before.
	private void StartServerSession(ICreatioApplicationClient session, EnvironmentSettings environmentSettings) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetCurrentUserInfo, environmentSettings);
		using HttpResponseMessage response = session.ExecutePostRequestAsync(url, EmptyJsonBody)
			.GetAwaiter().GetResult();
	}

	private static bool HasStickySession(ICreatioApplicationClient session) =>
		session.ExportSessionCookies()
			.Any(cookie => string.Equals(cookie.Name, StickySessionCookieName, StringComparison.Ordinal));

	private (HttpStatusCode Status, bool IsDesignNode) ProbeDesignNode(ICreatioApplicationClient session,
		EnvironmentSettings environmentSettings) {
		string url = _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.DesignReady, environmentSettings);
		using HttpResponseMessage response = session.ExecuteGetRequestAsync(url).GetAwaiter().GetResult();
		bool isDesignNode = response.StatusCode == HttpStatusCode.OK
			&& ConfirmsDesignNode(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
		return (response.StatusCode, isDesignNode);
	}

	// A 200 alone is not proof: a gateway catch-all page or a redirect that ends on a 200 page would pass too.
	// Only the probe's own answer, {"isDesignNode":true}, confirms the design node.
	private static bool ConfirmsDesignNode(string body) {
		if (string.IsNullOrWhiteSpace(body)) {
			return false;
		}
		try {
			using JsonDocument document = JsonDocument.Parse(body);
			return document.RootElement.ValueKind == JsonValueKind.Object
				&& document.RootElement.EnumerateObject().Any(property =>
					string.Equals(property.Name, IsDesignNodePropertyName, StringComparison.OrdinalIgnoreCase)
					&& property.Value.ValueKind == JsonValueKind.True);
		}
		catch (JsonException) {
			return false;
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
		(HttpStatusCode probeStatus, bool isDesignNode) = ProbeDesignNode(session, environmentSettings);
		if (isDesignNode) {
			_logger.WriteInfo(RoutedToDesignNodeMessage);
		} else if (probeStatus == HttpStatusCode.NotFound) {
			_logger.WriteWarning(DesignProbeUnavailableMessage);
		} else {
			throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
				DesignNodeNotConfirmedMessageFormat, (int)probeStatus));
		}
	}

	#endregion

}
