using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clio.Common;

/// <inheritdoc/>
internal sealed class ApplicationUserCultureProvider : IApplicationUserCultureProvider
{
	private const string UserInfoEndpoint = "ServiceModel/UserInfoService.svc/GetCurrentUserInfo";
	private const string FallbackCultureName = "en-US";

	private readonly IApplicationClient _applicationClient;
	private string? _cachedCultureName;

	public ApplicationUserCultureProvider(IApplicationClient applicationClient) {
		_applicationClient = applicationClient;
	}

	/// <inheritdoc/>
	public string GetUserCultureName() {
		if (_cachedCultureName != null) {
			return _cachedCultureName;
		}
		_cachedCultureName = FetchUserCultureName();
		return _cachedCultureName;
	}

	private string FetchUserCultureName() {
		try {
			string responseJson = _applicationClient.ExecutePostRequest(UserInfoEndpoint, "{}");
			if (string.IsNullOrWhiteSpace(responseJson)) {
				return FallbackCultureName;
			}
			UserInfoResponse? response = JsonSerializer.Deserialize<UserInfoResponse>(responseJson,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			string? cultureName = response?.UserInfo?.CultureInfo?.SysCultureName;
			return string.IsNullOrWhiteSpace(cultureName) ? FallbackCultureName : cultureName;
		} catch {
			return FallbackCultureName;
		}
	}

	private sealed class UserInfoResponse
	{
		[JsonPropertyName("userInfo")]
		public UserInfoDto? UserInfo { get; set; }
	}

	private sealed class UserInfoDto
	{
		[JsonPropertyName("cultureInfo")]
		public CultureInfoDto? CultureInfo { get; set; }
	}

	private sealed class CultureInfoDto
	{
		[JsonPropertyName("sysCultureName")]
		public string? SysCultureName { get; set; }
	}
}
