using System;
using System.Linq;
using System.Text.Json;

namespace Clio.Common.DataForge;

public interface IDataForgeConfigResolver {
	DataForgeResolvedConfig Resolve(DataForgeConfigRequest request);
}

public sealed class DataForgeConfigResolver(
	EnvironmentSettings environmentSettings,
	ISysSettingsManager sysSettingsManager)
	: IDataForgeConfigResolver {
	private const string DefaultOAuthScope = "use_enrichment";
	private const int DefaultTimeoutMs = 30_000;
	private const int DefaultSimilarTablesLimit = 50;
	private const int DefaultLookupLimit = 5;
	private const int DefaultRelationshipsLimit = 5;

	public DataForgeResolvedConfig Resolve(DataForgeConfigRequest request) {
		ArgumentNullException.ThrowIfNull(request);

		string serviceUrl = FirstNonEmpty(
			request.ServiceUrl,
			GetStringSysSetting("DataForgeServiceUrl"))
			?? throw new InvalidOperationException(
				"DataForgeServiceUrl is required to call dataforge-service.");

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

	private int GetIntSysSetting(string code, int defaultValue) {
		try {
			return sysSettingsManager.GetSysSettingValueByCode<int>(code);
		} catch {
			return defaultValue;
		}
	}

	private string? GetStringSysSetting(string code) {
		try {
			string value = sysSettingsManager.GetSysSettingValueByCode(code);
			if (string.IsNullOrWhiteSpace(value)) {
				return null;
			}

			string trimmedValue = value.Trim();
			if (trimmedValue.Length >= 2 && trimmedValue[0] == '"' && trimmedValue[^1] == '"') {
				try {
					string? deserializedValue = JsonSerializer.Deserialize<string>(trimmedValue);
					return string.IsNullOrWhiteSpace(deserializedValue) ? null : deserializedValue.Trim();
				} catch (JsonException) {
					// Fall back to the original raw string if the syssetting only looks like JSON.
				}
			}

			return trimmedValue;
		} catch {
			return null;
		}
	}

	private static bool HasOAuthSettings(string? tokenUrl, string? clientId, string? clientSecret) {
		return !string.IsNullOrWhiteSpace(tokenUrl)
			&& !string.IsNullOrWhiteSpace(clientId)
			&& !string.IsNullOrWhiteSpace(clientSecret);
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
}
