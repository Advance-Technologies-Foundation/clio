using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using Microsoft.Extensions.Logging;

namespace Clio.Command.EntitySchemaDesigner;

/// <summary>
/// Resolves the connected Creatio user's profile culture from the standard
/// <c>ApplicationInfoService.svc/GetApplicationInfo</c> endpoint, so caption/label culture for
/// created entities is the language set in the logged-in user's profile rather than the host
/// machine locale. Resolution uses only <see cref="IApplicationClient"/> (no cliogate, no raw
/// <c>HttpClient</c>) and never throws into the creation path — a missing/invalid/unreachable
/// culture is reported as <see cref="CultureResolution.Failed(string)"/>.
/// </summary>
public interface ICurrentUserCultureResolver
{
	/// <summary>
	/// Resolves and validates <c>sysValues.userCulture.displayValue</c> from
	/// <c>GetApplicationInfo</c>. Returns a <see cref="CultureResolution"/>; never throws for a
	/// missing/invalid culture or an unreachable/unauthorized endpoint. Results are served from a
	/// shared per-environment cache when available.
	/// </summary>
	Task<CultureResolution> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ICurrentUserCultureResolver"/> backed by a specific environment's
/// <see cref="IApplicationClient"/> and <see cref="EnvironmentSettings"/>, sharing the singleton
/// <see cref="ICurrentUserCultureCache"/>. Mirrors the platform-version resolver precedent:
/// the synchronous <see cref="IApplicationClient.ExecutePostRequest(string,string)"/> is offloaded
/// via <see cref="Task.Run(System.Action,CancellationToken)"/> so the long-lived MCP host loop is
/// never blocked.
/// </summary>
public sealed class CurrentUserCultureResolver : ICurrentUserCultureResolver
{
	/// <summary>
	/// Standard Creatio service that returns the logged-in user's profile culture without requiring
	/// cliogate — only an authenticated session. Returns
	/// <c>{ applicationInfo: { sysValues: { userCulture: { displayValue: "en-US" } } } }</c>.
	/// </summary>
	internal const string GetApplicationInfoServicePath = CreatioServicePaths.GetApplicationInfo;

	internal const string ReasonUserCultureMissing = "userCulture-missing";
	internal const string ReasonUserCultureInvalid = "userCulture-invalid";
	internal const string ReasonUnauthorized = "unauthorized";
	internal const string ReasonUnreachable = "unreachable";
	internal const string ReasonNoActiveEnvironment = "no-active-environment";

	private readonly IApplicationClient _applicationClient;
	private readonly EnvironmentSettings _environmentSettings;
	private readonly IServiceUrlBuilderFactory _serviceUrlBuilderFactory;
	private readonly ICurrentUserCultureCache _cache;
	private readonly ILogger<CurrentUserCultureResolver> _logger;

	/// <summary>Initializes the resolver for one environment.</summary>
	public CurrentUserCultureResolver(
		IApplicationClient applicationClient,
		EnvironmentSettings environmentSettings,
		IServiceUrlBuilderFactory serviceUrlBuilderFactory,
		ICurrentUserCultureCache cache,
		ILogger<CurrentUserCultureResolver> logger)
	{
		_applicationClient = applicationClient ?? throw new ArgumentNullException(nameof(applicationClient));
		_environmentSettings = environmentSettings ?? throw new ArgumentNullException(nameof(environmentSettings));
		_serviceUrlBuilderFactory = serviceUrlBuilderFactory ?? throw new ArgumentNullException(nameof(serviceUrlBuilderFactory));
		_cache = cache ?? throw new ArgumentNullException(nameof(cache));
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <inheritdoc />
	public async Task<CultureResolution> ResolveAsync(CancellationToken cancellationToken = default)
	{
		string environmentKey = _environmentSettings.Uri;
		if (string.IsNullOrWhiteSpace(environmentKey))
		{
			return CultureResolution.Failed(ReasonNoActiveEnvironment);
		}

		if (_cache.TryGet(environmentKey, out CultureResolution? cached))
		{
			return cached;
		}

		CultureResolution resolution = await ProbeAsync(environmentKey, cancellationToken).ConfigureAwait(false);

		// Cache only successful resolutions: a transient unreachable/unauthorized failure should be
		// retried on the next call rather than pinned for the whole TTL window.
		if (resolution.Success)
		{
			_cache.Set(environmentKey, resolution);
		}

		return resolution;
	}

	private async Task<CultureResolution> ProbeAsync(string environmentKey, CancellationToken cancellationToken)
	{
		// Route through IServiceUrlBuilder so .NET Framework deployments (IsNetCore=false) receive
		// the /0/... alias they need; hand-rolling the URL would 404 on those environments.
		IServiceUrlBuilder serviceUrlBuilder = _serviceUrlBuilderFactory.Create(_environmentSettings);
		string url = serviceUrlBuilder.Build(GetApplicationInfoServicePath);

		string? rawResponse;
		try
		{
			// ExecutePostRequest is synchronous; offload so the MCP host loop is not blocked.
			// The service takes an empty JSON body.
			rawResponse = await Task.Run(
				() => _applicationClient.ExecutePostRequest(url, "{}"),
				cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			string reason = IsUnauthorized(ex) ? ReasonUnauthorized : ReasonUnreachable;
			_logger.LogInformation(ex,
				"user-culture probe-failed env={Env} reason={Reason} error={Error}",
				environmentKey, reason, ex.Message);
			return CultureResolution.Failed(reason);
		}

		return ParseAndValidate(rawResponse, environmentKey);
	}

	private CultureResolution ParseAndValidate(string? rawJson, string environmentKey)
	{
		string? rawCulture = TryExtractUserCultureDisplayValue(rawJson);
		if (string.IsNullOrWhiteSpace(rawCulture))
		{
			// userCulture absent/empty: do NOT substitute primaryCulture (the system default) — the
			// profile culture is specifically the logged-in user's, so this is a resolution failure.
			_logger.LogInformation(
				"user-culture source=failed reason={Reason} env={Env}", ReasonUserCultureMissing, environmentKey);
			return CultureResolution.Failed(ReasonUserCultureMissing);
		}

		try
		{
			CultureInfo culture = CultureInfo.GetCultureInfo(rawCulture.Trim());
			_logger.LogInformation(
				"user-culture source=environment env={Env} culture={Culture}", environmentKey, culture.Name);
			return CultureResolution.Resolved(culture.Name);
		}
		catch (CultureNotFoundException ex)
		{
			_logger.LogInformation(ex,
				"user-culture source=failed reason={Reason} raw={Raw} env={Env}",
				ReasonUserCultureInvalid, rawCulture, environmentKey);
			return CultureResolution.Failed(ReasonUserCultureInvalid);
		}
	}

	/// <summary>
	/// Extracts <c>applicationInfo.sysValues.userCulture.displayValue</c> (the BCP-47 code, e.g.
	/// <c>en-US</c>) from the <c>GetApplicationInfo</c> response. Reads only the culture object's
	/// <c>displayValue</c> — never <c>primaryLanguage</c> (a human label) or <c>primaryCulture</c>
	/// (the system culture). Returns <c>null</c> on any missing node or non-string value.
	/// </summary>
	private static string? TryExtractUserCultureDisplayValue(string? rawJson)
	{
		if (string.IsNullOrWhiteSpace(rawJson))
		{
			return null;
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(rawJson);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object
				|| !root.TryGetProperty("applicationInfo", out JsonElement appInfo)
				|| appInfo.ValueKind != JsonValueKind.Object
				|| !appInfo.TryGetProperty("sysValues", out JsonElement sysValues)
				|| sysValues.ValueKind != JsonValueKind.Object
				|| !sysValues.TryGetProperty("userCulture", out JsonElement userCulture)
				|| userCulture.ValueKind != JsonValueKind.Object
				|| !userCulture.TryGetProperty("displayValue", out JsonElement displayValue)
				|| displayValue.ValueKind != JsonValueKind.String)
			{
				return null;
			}

			return displayValue.GetString();
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static bool IsUnauthorized(Exception exception)
	{
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			string message = current.Message;
			if (message.Contains("401", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("403", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
				|| message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
