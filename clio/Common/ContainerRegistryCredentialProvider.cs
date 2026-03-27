using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Clio.Common;

/// <summary>
/// Represents registry credentials resolved from the local Docker credential configuration.
/// </summary>
/// <param name="Username">Registry username.</param>
/// <param name="Password">Registry password or token.</param>
public sealed record ContainerRegistryCredentials(string Username, string Password);

/// <summary>
/// Resolves registry credentials from the local Docker-compatible credential configuration.
/// </summary>
public interface IContainerRegistryCredentialProvider {
	/// <summary>
	/// Attempts to resolve credentials for the provided registry endpoint.
	/// </summary>
	/// <param name="registryPrefix">Registry prefix supplied by the user.</param>
	/// <param name="endpoint">Registry endpoint currently being probed.</param>
	/// <returns>Resolved credentials, or <see langword="null"/> when no local credentials are configured.</returns>
	ContainerRegistryCredentials TryResolveCredentials(string registryPrefix, Uri endpoint);
}

/// <summary>
/// Resolves registry credentials from Docker config auth entries and credential helpers.
/// </summary>
public sealed class ContainerRegistryCredentialProvider(IFileSystem fileSystem, IProcessExecutor processExecutor)
	: IContainerRegistryCredentialProvider {
	private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
	private readonly IProcessExecutor _processExecutor =
		processExecutor ?? throw new ArgumentNullException(nameof(processExecutor));

	/// <inheritdoc />
	public ContainerRegistryCredentials TryResolveCredentials(string registryPrefix, Uri endpoint) {
		string configPath = GetDockerConfigPath();
		if (!_fileSystem.ExistsFile(configPath)) {
			return null;
		}

		using Stream configStream = _fileSystem.FileOpenStream(configPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using JsonDocument config = JsonDocument.Parse(configStream);

		List<string> lookupKeys = BuildLookupKeys(registryPrefix, endpoint);
		if (TryResolveInlineAuth(config.RootElement, lookupKeys, out ContainerRegistryCredentials inlineCredentials)) {
			return inlineCredentials;
		}

		if (TryResolveCredentialHelper(config.RootElement, lookupKeys, out string helperName)
			&& TryResolveWithCredentialHelper(helperName, lookupKeys, out ContainerRegistryCredentials helperCredentials)) {
			return helperCredentials;
		}

		return null;
	}

	private string GetDockerConfigPath() {
		string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		return _fileSystem.Combine(userProfile, ".docker", "config.json");
	}

	private List<string> BuildLookupKeys(string registryPrefix, Uri endpoint) {
		HashSet<string> lookupKeys = new(StringComparer.OrdinalIgnoreCase);
		void Add(string value) {
			if (!string.IsNullOrWhiteSpace(value)) {
				lookupKeys.Add(value.Trim().TrimEnd('/'));
			}
		}

		string trimmedPrefix = registryPrefix?.Trim().TrimEnd('/') ?? string.Empty;
		Add(trimmedPrefix);
		Add(endpoint.Authority);
		Add(endpoint.GetLeftPart(UriPartial.Authority));

		string registryHost = trimmedPrefix.Split('/', 2, StringSplitOptions.RemoveEmptyEntries)[0];
		Add(registryHost);
		Add($"https://{registryHost}");
		Add($"http://{registryHost}");

		return [.. lookupKeys];
	}

	private bool TryResolveInlineAuth(JsonElement root, IReadOnlyCollection<string> lookupKeys,
		out ContainerRegistryCredentials credentials) {
		credentials = null;
		if (!root.TryGetProperty("auths", out JsonElement auths) || auths.ValueKind != JsonValueKind.Object) {
			return false;
		}

		foreach (JsonProperty authEntry in auths.EnumerateObject()) {
			if (!MatchesLookupKey(authEntry.Name, lookupKeys)) {
				continue;
			}

			if (!authEntry.Value.TryGetProperty("auth", out JsonElement authValue)
				|| authValue.ValueKind != JsonValueKind.String) {
				continue;
			}

			string encodedAuth = authValue.GetString();
			if (string.IsNullOrWhiteSpace(encodedAuth)) {
				continue;
			}

			string decodedAuth = Encoding.UTF8.GetString(Convert.FromBase64String(encodedAuth));
			int separatorIndex = decodedAuth.IndexOf(':', StringComparison.Ordinal);
			if (separatorIndex <= 0) {
				continue;
			}

			credentials = new ContainerRegistryCredentials(
				decodedAuth[..separatorIndex],
				decodedAuth[(separatorIndex + 1)..]);
			return true;
		}

		return false;
	}

	private bool TryResolveCredentialHelper(JsonElement root, IReadOnlyCollection<string> lookupKeys,
		out string helperName) {
		helperName = null;
		if (root.TryGetProperty("credHelpers", out JsonElement credHelpers)
			&& credHelpers.ValueKind == JsonValueKind.Object) {
			foreach (JsonProperty helperEntry in credHelpers.EnumerateObject()) {
				if (MatchesLookupKey(helperEntry.Name, lookupKeys)
					&& helperEntry.Value.ValueKind == JsonValueKind.String
					&& !string.IsNullOrWhiteSpace(helperEntry.Value.GetString())) {
					helperName = helperEntry.Value.GetString();
					return true;
				}
			}
		}

		if (root.TryGetProperty("credsStore", out JsonElement credsStore)
			&& credsStore.ValueKind == JsonValueKind.String
			&& !string.IsNullOrWhiteSpace(credsStore.GetString())) {
			helperName = credsStore.GetString();
			return true;
		}

		return false;
	}

	private bool TryResolveWithCredentialHelper(string helperName, IReadOnlyCollection<string> lookupKeys,
		out ContainerRegistryCredentials credentials) {
		credentials = null;
		string helperProgram = $"docker-credential-{helperName}";
		foreach (string lookupKey in lookupKeys) {
			ProcessExecutionResult result = _processExecutor.ExecuteAndCaptureAsync(
				new ProcessExecutionOptions(helperProgram, "get") {
					StandardInput = lookupKey + Environment.NewLine
				}).GetAwaiter().GetResult();
			if (!result.Started || result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput)) {
				continue;
			}

			using JsonDocument response = JsonDocument.Parse(result.StandardOutput);
			if (!response.RootElement.TryGetProperty("Username", out JsonElement usernameProperty)
				|| !response.RootElement.TryGetProperty("Secret", out JsonElement secretProperty)) {
				continue;
			}

			string username = usernameProperty.GetString();
			string secret = secretProperty.GetString();
			if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(secret)) {
				continue;
			}

			credentials = new ContainerRegistryCredentials(username, secret);
			return true;
		}

		return false;
	}

	private bool MatchesLookupKey(string configuredKey, IReadOnlyCollection<string> lookupKeys) {
		string normalizedConfiguredKey = NormalizeLookupKey(configuredKey);
		foreach (string lookupKey in lookupKeys) {
			if (string.Equals(normalizedConfiguredKey, NormalizeLookupKey(lookupKey), StringComparison.OrdinalIgnoreCase)) {
				return true;
			}
		}

		return false;
	}

	private string NormalizeLookupKey(string value) {
		if (string.IsNullOrWhiteSpace(value)) {
			return string.Empty;
		}

		string trimmedValue = value.Trim().TrimEnd('/');
		if (Uri.TryCreate(trimmedValue, UriKind.Absolute, out Uri absoluteValue)) {
			return absoluteValue.Authority;
		}

		return trimmedValue.Split('/', 2, StringSplitOptions.RemoveEmptyEntries)[0];
	}
}
