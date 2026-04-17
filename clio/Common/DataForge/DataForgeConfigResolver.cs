using System;
using System.Linq;
using System.Text.Json;

namespace Clio.Common.DataForge;

public interface IDataForgeConfigResolver {
	DataForgeResolvedConfig Resolve(DataForgeConfigRequest request);
}

public sealed class DataForgeConfigResolver(
	EnvironmentSettings environmentSettings,
	ISysSettingsManager sysSettingsManager,
	IDataForgeSysSettingDirectReader directReader)
	: IDataForgeConfigResolver {
	private const string DataForgeServiceUrlCode = "DataForgeServiceUrl";
	private const string DefaultOAuthScope = "use_enrichment";
	private const int DefaultTimeoutMs = 30_000;
	private const int DefaultSimilarTablesLimit = 50;
	private const int DefaultLookupLimit = 5;
	private const int DefaultRelationshipsLimit = 5;

	public DataForgeResolvedConfig Resolve(DataForgeConfigRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		string serviceUrl = ResolveServiceUrl(request);

		int timeoutMs = request.TimeoutMs ?? GetIntSysSetting("DataForgeServiceQueryTimeout", DefaultTimeoutMs);
		int similarTablesLimit =
			request.SimilarTablesLimit ?? GetIntSysSetting("DataForgeSimilarTablesResultLimit", DefaultSimilarTablesLimit);
		int lookupLimit =
			request.LookupResultLimit ?? GetIntSysSetting("DataForgeLookupResultLimit", DefaultLookupLimit);
		int relationshipsLimit = request.TableRelationshipsLimit ??
			GetIntSysSetting("DataForgeTableRelationshipsCountLimit", DefaultRelationshipsLimit);
		string scope = string.IsNullOrWhiteSpace(request.Scope) ? DefaultOAuthScope : request.Scope.Trim();

		string? tokenUrl = NormalizeTokenUrl(FirstNonEmpty(request.AuthAppUri, environmentSettings.AuthAppUri));
		string? clientId = FirstNonEmpty(request.ClientId, environmentSettings.ClientId);
		string? clientSecret = FirstNonEmpty(request.ClientSecret, environmentSettings.ClientSecret);

		if (HasOAuthSettings(tokenUrl, clientId, clientSecret)) {
			return new DataForgeResolvedConfig(
				serviceUrl,
				timeoutMs,
				similarTablesLimit,
				lookupLimit,
				relationshipsLimit,
				DataForgeAuthMode.OAuthClientCredentials,
				tokenUrl,
				clientId,
				clientSecret,
				scope);
		}

		if (request.AllowSysSettingsAuthFallback) {
			tokenUrl = NormalizeTokenUrl(GetStringSysSetting("IdentityServerUrl"));
			clientId = GetStringSysSetting("IdentityServerClientId");
			clientSecret = GetStringSysSetting("IdentityServerClientSecret");
			if (HasOAuthSettings(tokenUrl, clientId, clientSecret)) {
				return new DataForgeResolvedConfig(
					serviceUrl,
					timeoutMs,
					similarTablesLimit,
					lookupLimit,
					relationshipsLimit,
					DataForgeAuthMode.OAuthClientCredentials,
					tokenUrl,
					clientId,
					clientSecret,
					scope);
			}
		}

		return new DataForgeResolvedConfig(
			serviceUrl,
			timeoutMs,
			similarTablesLimit,
			lookupLimit,
			relationshipsLimit,
			DataForgeAuthMode.None,
			null,
			null,
			null,
			scope);
	}

	private string ResolveServiceUrl(DataForgeConfigRequest request) {
		ServiceUrlCandidate explicitCandidate = ValidateServiceUrlCandidate(
			request.ServiceUrl,
			"explicit Data Forge target",
			treatInvalidAsUnavailable: false);
		if (explicitCandidate.IsSuccess) {
			return explicitCandidate.Value!;
		}

		if (explicitCandidate.IsInvalid) {
			throw new InvalidOperationException(explicitCandidate.ErrorMessage);
		}

		ServiceUrlCandidate gatewayCandidate = GetGatewayServiceUrlCandidate();
		if (gatewayCandidate.IsSuccess) {
			return gatewayCandidate.Value!;
		}

		ServiceUrlCandidate directCandidate = GetDirectServiceUrlCandidate();
		if (directCandidate.IsSuccess) {
			return directCandidate.Value!;
		}

		throw new InvalidOperationException(BuildMissingServiceUrlError(gatewayCandidate, directCandidate));
	}

	private int GetIntSysSetting(string code, int defaultValue) {
		try {
			return sysSettingsManager.GetSysSettingValueByCode<int>(code);
		} catch {
			return defaultValue;
		}
	}

	private ServiceUrlCandidate GetGatewayServiceUrlCandidate() {
		try {
			return ValidateServiceUrlCandidate(
				sysSettingsManager.GetSysSettingValueByCode(DataForgeServiceUrlCode),
				"cliogate GetSysSettingValueByCode(DataForgeServiceUrl)",
				treatInvalidAsUnavailable: true);
		} catch (Exception ex) {
			return ServiceUrlCandidate.Unavailable(
				$"cliogate GetSysSettingValueByCode(DataForgeServiceUrl) failed: {ex.Message}");
		}
	}

	private ServiceUrlCandidate GetDirectServiceUrlCandidate() {
		DataForgeSysSettingReadResult readResult = directReader.ReadTextValue(DataForgeServiceUrlCode);
		if (!string.IsNullOrWhiteSpace(readResult.FailureReason)) {
			return ServiceUrlCandidate.Unavailable(
				$"direct SysSettings read failed: {readResult.FailureReason}");
		}

		return readResult.Found
			? ValidateServiceUrlCandidate(
				readResult.Value,
				"direct SysSettings read",
				treatInvalidAsUnavailable: false)
			: ServiceUrlCandidate.Missing("direct SysSettings read returned no value.");
	}

	private string? GetStringSysSetting(string code) {
		try {
			return NormalizeRawStringValue(sysSettingsManager.GetSysSettingValueByCode(code));
		} catch {
			return null;
		}
	}

	private static string BuildMissingServiceUrlError(
		ServiceUrlCandidate gatewayCandidate,
		ServiceUrlCandidate directCandidate) {
		if (gatewayCandidate.IsMissing && directCandidate.IsMissing) {
			return "DataForgeServiceUrl is not configured in Creatio SysSettings.";
		}

		return $"Failed to resolve DataForgeServiceUrl. Gateway path: {gatewayCandidate.ErrorMessage} Direct read: {directCandidate.ErrorMessage}";
	}

	private static bool HasOAuthSettings(string? tokenUrl, string? clientId, string? clientSecret) {
		return !string.IsNullOrWhiteSpace(tokenUrl)
			&& !string.IsNullOrWhiteSpace(clientId)
			&& !string.IsNullOrWhiteSpace(clientSecret);
	}

	private static string? NormalizeRawStringValue(string? rawValue) {
		if (string.IsNullOrWhiteSpace(rawValue)) {
			return null;
		}

		string trimmedValue = rawValue.Trim();
		if (trimmedValue.Length >= 2 && trimmedValue[0] == '"' && trimmedValue[^1] == '"') {
			try {
				string? deserializedValue = JsonSerializer.Deserialize<string>(trimmedValue);
				return string.IsNullOrWhiteSpace(deserializedValue) ? null : deserializedValue.Trim();
			} catch (JsonException) {
				// Fall back to the original raw string if the syssetting only looks like JSON.
			}
		}

		return trimmedValue;
	}

	private static string? NormalizeTokenUrl(string? tokenUrl) {
		if (string.IsNullOrWhiteSpace(tokenUrl)) {
			return null;
		}

		string normalized = tokenUrl.Trim().TrimEnd('/');
		return normalized.EndsWith("/connect/token", StringComparison.OrdinalIgnoreCase)
			? normalized
			: $"{normalized}/connect/token";
	}

	private static string? FirstNonEmpty(params string?[] values) {
		return values
			.Where(value => !string.IsNullOrWhiteSpace(value))
			.Select(value => value!.Trim())
			.FirstOrDefault();
	}

	private static ServiceUrlCandidate ValidateServiceUrlCandidate(
		string? rawValue,
		string sourceName,
		bool treatInvalidAsUnavailable) {
		string? normalizedValue = NormalizeRawStringValue(rawValue);
		if (string.IsNullOrWhiteSpace(normalizedValue)) {
			return ServiceUrlCandidate.Missing($"{sourceName} returned no value.");
		}

		if (LooksLikeHtml(normalizedValue)) {
			return treatInvalidAsUnavailable
				? ServiceUrlCandidate.Unavailable($"{sourceName} returned HTML instead of DataForgeServiceUrl.")
				: ServiceUrlCandidate.Invalid($"{sourceName} returned HTML instead of a valid DataForgeServiceUrl.");
		}

		if (!Uri.TryCreate(normalizedValue, UriKind.Absolute, out Uri? uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) {
			return treatInvalidAsUnavailable
				? ServiceUrlCandidate.Unavailable(
					$"{sourceName} returned '{normalizedValue}', which is not a valid absolute http/https URL.")
				: ServiceUrlCandidate.Invalid(
					$"{sourceName} returned '{normalizedValue}', which is not a valid absolute http/https URL.");
		}

		return ServiceUrlCandidate.Success(uri.AbsoluteUri.TrimEnd('/') + "/");
	}

	private static bool LooksLikeHtml(string value) {
		return value.StartsWith("<", StringComparison.Ordinal)
			|| value.Contains("<html", StringComparison.OrdinalIgnoreCase)
			|| value.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
	}

	private sealed record ServiceUrlCandidate(
		string? Value,
		string ErrorMessage,
		ServiceUrlCandidateKind Kind
	) {
		public bool IsSuccess => Kind == ServiceUrlCandidateKind.Success;
		public bool IsMissing => Kind == ServiceUrlCandidateKind.Missing;
		public bool IsInvalid => Kind == ServiceUrlCandidateKind.Invalid;

		public static ServiceUrlCandidate Success(string value) => new(value, string.Empty, ServiceUrlCandidateKind.Success);
		public static ServiceUrlCandidate Missing(string errorMessage) => new(null, errorMessage, ServiceUrlCandidateKind.Missing);
		public static ServiceUrlCandidate Invalid(string errorMessage) => new(null, errorMessage, ServiceUrlCandidateKind.Invalid);
		public static ServiceUrlCandidate Unavailable(string errorMessage) => new(null, errorMessage, ServiceUrlCandidateKind.Unavailable);
	}

	private enum ServiceUrlCandidateKind {
		Success,
		Missing,
		Invalid,
		Unavailable
	}
}
