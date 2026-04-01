using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Clio.Common;
namespace Clio.Command;

public interface IEnvironmentRuntimeDetectionService {
	bool Detect(EnvironmentSettings environmentSettings);
}

internal sealed class EnvironmentRuntimeDetectionService(
	IApplicationClientFactory applicationClientFactory,
	IHttpClientFactory httpClientFactory)
	: IEnvironmentRuntimeDetectionService {
	private const int ProbeTimeoutMs = 10000;
	private static readonly JsonSerializerOptions JsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	public bool Detect(EnvironmentSettings environmentSettings) {
		ArgumentNullException.ThrowIfNull(environmentSettings);
		if (string.IsNullOrWhiteSpace(environmentSettings.Uri)) {
			throw new InvalidOperationException(
				"Cannot auto-detect the Creatio runtime because the environment URI is missing. Rerun reg-web-app with --IsNetCore true or --IsNetCore false.");
		}
		bool canAuthenticate = HasAuthentication(environmentSettings);
		RuntimeProbeResult netCoreProbe = Probe(environmentSettings, true, canAuthenticate);
		RuntimeProbeResult netFrameworkProbe = Probe(environmentSettings, false, canAuthenticate);
		if (!canAuthenticate) {
			return ResolveWithoutAuthentication(environmentSettings.Uri, netCoreProbe, netFrameworkProbe);
		}
		int successfulServiceProbeCount = (netCoreProbe.ServiceProbe.Succeeded ? 1 : 0)
			+ (netFrameworkProbe.ServiceProbe.Succeeded ? 1 : 0);

		return successfulServiceProbeCount switch {
			1 when netCoreProbe.ServiceProbe.Succeeded => true,
			1 => false,
			0 => throw new InvalidOperationException(BuildFailureMessage(environmentSettings.Uri, netCoreProbe, netFrameworkProbe)),
			_ => ResolveAmbiguousServiceSuccess(netCoreProbe, netFrameworkProbe)
		};
	}

	private static bool HasAuthentication(EnvironmentSettings environmentSettings) =>
		(!string.IsNullOrWhiteSpace(environmentSettings.Login)
			&& !string.IsNullOrWhiteSpace(environmentSettings.Password))
		|| (!string.IsNullOrWhiteSpace(environmentSettings.ClientId)
			&& !string.IsNullOrWhiteSpace(environmentSettings.ClientSecret)
			&& !string.IsNullOrWhiteSpace(environmentSettings.AuthAppUri));

	private static bool ResolveWithoutAuthentication(
		string baseUri,
		RuntimeProbeResult netCoreProbe,
		RuntimeProbeResult netFrameworkProbe) {
		int successfulHealthProbeCount = (netCoreProbe.HealthProbe.Succeeded ? 1 : 0)
			+ (netFrameworkProbe.HealthProbe.Succeeded ? 1 : 0);
		if (successfulHealthProbeCount == 1) {
			return netCoreProbe.HealthProbe.Succeeded;
		}
		int successfulUiMarkerProbeCount = (netCoreProbe.UiMarkerProbe.Succeeded ? 1 : 0)
			+ (netFrameworkProbe.UiMarkerProbe.Succeeded ? 1 : 0);
		return successfulUiMarkerProbeCount switch {
			1 when netCoreProbe.UiMarkerProbe.Succeeded => true,
			1 => false,
			_ => throw new InvalidOperationException(BuildUnauthenticatedFailureMessage(baseUri, netCoreProbe, netFrameworkProbe))
		};
	}

	private static bool ResolveAmbiguousServiceSuccess(
		RuntimeProbeResult netCoreProbe,
		RuntimeProbeResult netFrameworkProbe) {
		int successfulUiMarkerProbeCount = (netCoreProbe.UiMarkerProbe.Succeeded ? 1 : 0)
			+ (netFrameworkProbe.UiMarkerProbe.Succeeded ? 1 : 0);

		return successfulUiMarkerProbeCount switch {
			1 when netCoreProbe.UiMarkerProbe.Succeeded => true,
			1 => false,
			_ => throw new InvalidOperationException(BuildAmbiguousMessage(netCoreProbe, netFrameworkProbe))
		};
	}

	private RuntimeProbeResult Probe(EnvironmentSettings baseSettings, bool isNetCore, bool canAuthenticate) {
		EnvironmentSettings probeSettings = Clone(baseSettings, isNetCore);
		string healthUrl = BuildHealthUrl(probeSettings.Uri!, isNetCore);
		string uiMarkerUrl = BuildUiMarkerUrl(probeSettings.Uri!, isNetCore);
		string serviceUrl = new ServiceUrlBuilder(probeSettings).Build(ServiceUrlBuilder.KnownRoute.Select);
		ProbeAttempt healthProbe = ExecuteHttpGetProbe(healthUrl);
		ProbeAttempt serviceProbe = canAuthenticate
			? ExecuteProbe(
				() => ExecuteAuthenticatedServiceProbe(
					applicationClientFactory.CreateEnvironmentClient(probeSettings),
					serviceUrl),
				ValidateSelectQueryResponse)
			: new ProbeAttempt(false, "skipped because credentials are missing");
		ProbeAttempt uiMarkerProbe = ExecuteHttpGetProbe(uiMarkerUrl);
		return new RuntimeProbeResult(isNetCore, healthUrl, healthProbe, serviceUrl, serviceProbe, uiMarkerUrl, uiMarkerProbe);
	}

	private static EnvironmentSettings Clone(EnvironmentSettings source, bool isNetCore) =>
		new() {
			Uri = source.Uri?.TrimEnd('/'),
			Login = source.Login,
			Password = source.Password,
			ClientId = source.ClientId,
			ClientSecret = source.ClientSecret,
			AuthAppUri = source.AuthAppUri,
			IsNetCore = isNetCore
		};

	private static string BuildHealthUrl(string baseUri, bool isNetCore) =>
		$"{baseUri.TrimEnd('/')}{(isNetCore ? "/api/HealthCheck/Ping" : "/0/api/HealthCheck/Ping")}";

	private static string BuildUiMarkerUrl(string baseUri, bool isNetCore) =>
		$"{baseUri.TrimEnd('/')}{(isNetCore ? "/Login/Login.html" : "/0/Login/NuiLogin.aspx")}";

	private static ProbeAttempt ExecuteProbe(Func<string> request, Action<string>? validateResponse = null) {
		try {
			string response = request();
			validateResponse?.Invoke(response);
			return new ProbeAttempt(true, null);
		} catch (Exception exception) {
			return new ProbeAttempt(false, exception.GetBaseException().Message);
		}
	}

	private ProbeAttempt ExecuteHttpGetProbe(string url) {
		try {
			using HttpClient client = httpClientFactory.CreateClient(nameof(EnvironmentRuntimeDetectionService));
			client.Timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs);
			using HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
			if (!response.IsSuccessStatusCode) {
				return new ProbeAttempt(false,
					$"The remote server returned an error: ({(int)response.StatusCode}) {response.ReasonPhrase}.");
			}

			return new ProbeAttempt(true, null);
		} catch (Exception exception) {
			return new ProbeAttempt(false, exception.GetBaseException().Message);
		}
	}

	private string ExecuteAuthenticatedServiceProbe(IApplicationClient client, string serviceUrl) {
		return client.ExecutePostRequest(serviceUrl, BuildSelectQueryBody(), ProbeTimeoutMs, 1, 1);
	}

	private static void ValidateSelectQueryResponse(string responseBody) {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			throw new InvalidOperationException("SelectQuery returned an empty response.");
		}

		SelectQueryProbeResponse response = JsonSerializer.Deserialize<SelectQueryProbeResponse>(responseBody, JsonOptions)
			?? throw new InvalidOperationException("SelectQuery returned an empty response.");
		if (!response.Success) {
			throw new InvalidOperationException(response.ErrorInfo?.Message ?? "SelectQuery failed.");
		}
	}

	private static string BuildSelectQueryBody() {
		object query = new {
			rootSchemaName = "SysAdminUnit",
			operationType = 0,
			allColumns = false,
			isDistinct = false,
			ignoreDisplayValues = false,
			rowCount = 1,
			rowsOffset = -1,
			isPageable = false,
			conditionalValues = (object)null,
			isHierarchical = false,
			hierarchicalMaxDepth = 0,
			hierarchicalColumnFiltersValue = new {
				filterType = 6,
				isEnabled = true,
				items = new { },
				logicalOperation = 0,
				trimDateTimeParameterToDate = false
			},
			hierarchicalColumnName = (string)null,
			hierarchicalColumnValue = (object)null,
			hierarchicalFullDataLoad = false,
			useLocalization = true,
			useRecordDeactivation = false,
			columns = new {
				items = new {
					Id = new {
						expression = new {
							expressionType = 0,
							columnPath = "Id"
						},
						orderDirection = 0,
						orderPosition = -1,
						isVisible = true
					}
				}
			},
			filters = new {
				filterType = 6,
				isEnabled = true,
				trimDateTimeParameterToDate = false,
				logicalOperation = 0,
				items = new { }
			},
			__type = "Terrasoft.Nui.ServiceModel.DataContract.SelectQuery",
			queryKind = 0,
			serverESQCacheParameters = new {
				cacheLevel = 0,
				cacheGroup = string.Empty,
				cacheItemName = string.Empty
			},
			queryOptimize = false,
			useMetrics = false,
			querySource = 0
		};
		return JsonSerializer.Serialize(query);
	}

	private static string BuildFailureMessage(string baseUri, RuntimeProbeResult netCoreProbe, RuntimeProbeResult netFrameworkProbe) =>
		TryBuildReachabilityFailureMessage(baseUri, netCoreProbe, netFrameworkProbe, out string message)
			? message
			: $"Unable to auto-detect the Creatio runtime. {BuildProbeSummary(netCoreProbe, netFrameworkProbe)} Rerun reg-web-app with --IsNetCore true or --IsNetCore false to override detection.";

	private static string BuildUnauthenticatedFailureMessage(
		string baseUri,
		RuntimeProbeResult netCoreProbe,
		RuntimeProbeResult netFrameworkProbe) =>
		TryBuildReachabilityFailureMessage(baseUri, netCoreProbe, netFrameworkProbe, out string message)
			? message
			: $"Unable to auto-detect the Creatio runtime because no credentials were provided and the unauthenticated probes were inconclusive. {BuildProbeSummary(netCoreProbe, netFrameworkProbe)} Provide -l/-p or OAuth settings to enable the authenticated SelectQuery probe, or rerun reg-web-app with --IsNetCore true or --IsNetCore false.";

	private static string BuildAmbiguousMessage(RuntimeProbeResult netCoreProbe, RuntimeProbeResult netFrameworkProbe) =>
		$"Unable to auto-detect the Creatio runtime because both .NET Core / NET8 and .NET Framework service probes succeeded. {BuildProbeSummary(netCoreProbe, netFrameworkProbe)} Rerun reg-web-app with --IsNetCore true or --IsNetCore false to override detection.";

	private static bool TryBuildReachabilityFailureMessage(
		string baseUri,
		RuntimeProbeResult netCoreProbe,
		RuntimeProbeResult netFrameworkProbe,
		out string message) {
		ProbeAttempt[] directAttempts = [
			netCoreProbe.HealthProbe,
			netCoreProbe.UiMarkerProbe,
			netFrameworkProbe.HealthProbe,
			netFrameworkProbe.UiMarkerProbe
		];
		if (directAttempts.Any(attempt => attempt.Succeeded)
			|| directAttempts.Any(attempt => !IsReachabilityFailure(attempt.ErrorMessage))) {
			message = string.Empty;
			return false;
		}
		string authority = Uri.TryCreate(baseUri, UriKind.Absolute, out Uri? uri)
			? uri.Authority
			: baseUri;
		message =
			$"Unable to auto-detect the Creatio runtime because the host '{authority}' could not be reached from this machine. Verify the URL, DNS/VPN connectivity, and that the site is accessible, then rerun reg-web-app. {BuildProbeSummary(netCoreProbe, netFrameworkProbe)}";
		return true;
	}

	private static bool IsReachabilityFailure(string? errorMessage) {
		if (string.IsNullOrWhiteSpace(errorMessage)) {
			return false;
		}
		return errorMessage.Contains("could not resolve host", StringComparison.OrdinalIgnoreCase)
			|| errorMessage.Contains("nodename nor servname", StringComparison.OrdinalIgnoreCase)
			|| errorMessage.Contains("name or service not known", StringComparison.OrdinalIgnoreCase)
			|| errorMessage.Contains("no such host is known", StringComparison.OrdinalIgnoreCase)
			|| errorMessage.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
			|| errorMessage.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
			|| errorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase)
			|| errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase);
	}

	private static string BuildProbeSummary(RuntimeProbeResult netCoreProbe, RuntimeProbeResult netFrameworkProbe) =>
		$".NET Core / NET8: health {netCoreProbe.HealthUrl} => {Describe(netCoreProbe.HealthProbe)}, service {netCoreProbe.ServiceUrl} => {Describe(netCoreProbe.ServiceProbe)}, UI marker {netCoreProbe.UiMarkerUrl} => {Describe(netCoreProbe.UiMarkerProbe)}. .NET Framework: health {netFrameworkProbe.HealthUrl} => {Describe(netFrameworkProbe.HealthProbe)}, service {netFrameworkProbe.ServiceUrl} => {Describe(netFrameworkProbe.ServiceProbe)}, UI marker {netFrameworkProbe.UiMarkerUrl} => {Describe(netFrameworkProbe.UiMarkerProbe)}.";

	private static string Describe(ProbeAttempt attempt) => attempt.Succeeded ? "success" : attempt.ErrorMessage ?? "failed";

	private sealed record RuntimeProbeResult(
		bool IsNetCore,
		string HealthUrl,
		ProbeAttempt HealthProbe,
		string ServiceUrl,
		ProbeAttempt ServiceProbe,
		string UiMarkerUrl,
		ProbeAttempt UiMarkerProbe);

	private sealed record ProbeAttempt(bool Succeeded, string? ErrorMessage);

	private sealed class SelectQueryProbeResponse {
		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("errorInfo")]
		public SelectQueryProbeErrorInfo? ErrorInfo { get; set; }
	}

	private sealed class SelectQueryProbeErrorInfo {
		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}
}
