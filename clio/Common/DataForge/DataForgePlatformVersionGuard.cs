using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Clio.Common.DataForge;

/// <summary>
/// Verifies that the target Creatio platform supports bundled Data Forge services.
/// </summary>
public interface IDataForgePlatformVersionGuard {
	/// <summary>
	/// Throws when the connected Creatio platform version is lower than the minimum Data Forge requirement.
	/// </summary>
	void EnsureSupported();
}

/// <summary>
/// Checks the Creatio platform version through <c>GetApplicationInfo</c> before Data Forge proxy calls.
/// </summary>
public sealed partial class DataForgePlatformVersionGuard(
	IApplicationClient applicationClient,
	IServiceUrlBuilder serviceUrlBuilder) : IDataForgePlatformVersionGuard {
	internal const string MinimumCreatioPlatformVersion = "10.0.0";
	internal const int VersionCheckTimeoutMs = 60_000;
	private static readonly Version MinimumVersion = new(MinimumCreatioPlatformVersion);
	private static readonly string[] PreferredVersionFields = [
		"ProductVersion",
		"productVersion",
		"CoreVersion",
		"coreVersion",
		"Version",
		"version"
	];

	private bool _isSupported;

	private static readonly Version DevBuildVersion = new(0, 0, 0, 0);

	/// <inheritdoc />
	public void EnsureSupported() {
		if (_isSupported) {
			return;
		}

		string response = applicationClient.ExecutePostRequest(
			serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.GetApplicationInfo),
			string.Empty,
			VersionCheckTimeoutMs,
			1,
			1);
		string versionText = ExtractVersion(response);
		Version currentVersion = ParseVersion(versionText);
		if (currentVersion == DevBuildVersion) {
			_isSupported = true;
			return;
		}
		if (currentVersion < MinimumVersion) {
			throw new InvalidOperationException(
				$"DataForge MCP tools require Creatio platform version {MinimumCreatioPlatformVersion} or later. " +
				$"Current Creatio platform version: {versionText}. CrtDataForge is included in supported platform versions.");
		}

		_isSupported = true;
	}

	private static string ExtractVersion(string response) {
		if (string.IsNullOrWhiteSpace(response)) {
			throw CreateUnknownVersionException();
		}

		using JsonDocument document = JsonDocument.Parse(response);
		foreach (string fieldName in PreferredVersionFields) {
			if (TryFindString(document.RootElement, fieldName, out string? version)
					&& !string.IsNullOrWhiteSpace(version)
					&& VersionPattern().IsMatch(version)) {
				return version;
			}
		}

		throw CreateUnknownVersionException();
	}

	private static bool TryFindString(JsonElement element, string propertyName, out string? value) {
		switch (element.ValueKind) {
			case JsonValueKind.Object:
				foreach (JsonProperty property in element.EnumerateObject()) {
					if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)
							&& property.Value.ValueKind == JsonValueKind.String) {
						value = property.Value.GetString();
						return true;
					}
					if (TryFindString(property.Value, propertyName, out value)) {
						return true;
					}
				}
				break;
			case JsonValueKind.Array:
				foreach (JsonElement item in element.EnumerateArray()) {
					if (TryFindString(item, propertyName, out value)) {
						return true;
					}
				}
				break;
		}

		value = null;
		return false;
	}

	private static Version ParseVersion(string versionText) {
		Match match = VersionPattern().Match(versionText);
		if (!match.Success || !Version.TryParse(match.Value, out Version? version)) {
			throw CreateUnknownVersionException();
		}

		return version;
	}

	private static InvalidOperationException CreateUnknownVersionException() {
		return new InvalidOperationException(
			$"Unable to verify Creatio platform version. DataForge MCP tools require Creatio platform version " +
			$"{MinimumCreatioPlatformVersion} or later.");
	}

	[GeneratedRegex(@"\d+(?:\.\d+){1,3}", RegexOptions.CultureInvariant)]
	private static partial Regex VersionPattern();
}
