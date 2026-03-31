using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Clio.Common;

/// <summary>
/// Validates whether a registry target is reachable and appears writable before an image build starts.
/// </summary>
public interface IContainerRegistryPreflightService {
	/// <summary>
	/// Checks whether the requested registry push target is reachable and accepts upload initiation.
	/// </summary>
	/// <param name="registryPrefix">Registry or repository prefix supplied by the user.</param>
	/// <param name="registryImageReference">Fully qualified image reference that would be pushed.</param>
	/// <returns>Preflight result describing whether the push target appears usable.</returns>
	ContainerRegistryPreflightResult ValidatePushTarget(string registryPrefix, string registryImageReference);
}

/// <summary>
/// Represents the outcome of a registry push preflight probe.
/// </summary>
/// <param name="Success">Whether the registry target appears usable for push.</param>
/// <param name="Endpoint">Resolved registry endpoint used during probing.</param>
/// <param name="Message">Human-readable outcome message.</param>
/// <param name="RequiresAuthentication">Whether the registry explicitly requested authentication.</param>
public sealed record ContainerRegistryPreflightResult(
	bool Success,
	string Endpoint,
	string Message,
	bool RequiresAuthentication = false);

/// <summary>
/// Default registry preflight implementation backed by the Docker Registry HTTP API v2.
/// </summary>
public sealed class ContainerRegistryPreflightService(
	HttpClient httpClient,
	IContainerRegistryCredentialProvider credentialProvider)
	: IContainerRegistryPreflightService {
	private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
	private readonly IContainerRegistryCredentialProvider _credentialProvider =
		credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));

	/// <inheritdoc />
	public ContainerRegistryPreflightResult ValidatePushTarget(string registryPrefix, string registryImageReference) {
		if (string.IsNullOrWhiteSpace(registryPrefix)) {
			throw new ArgumentException("Registry prefix is required.", nameof(registryPrefix));
		}

		if (string.IsNullOrWhiteSpace(registryImageReference)) {
			throw new ArgumentException("Registry image reference is required.", nameof(registryImageReference));
		}

		string repositoryName = ExtractRepositoryName(registryImageReference);
		List<string> probeErrors = [];
		foreach (Uri endpoint in BuildCandidateEndpoints(registryPrefix)) {
			try {
				ContainerRegistryCredentials credentials = _credentialProvider.TryResolveCredentials(registryPrefix, endpoint);
				using HttpResponseMessage anonymousPingResponse =
					SendRegistryRequest(HttpMethod.Get, new Uri(endpoint, "v2/"), credentials: null);
				HttpResponseMessage pingResponse = anonymousPingResponse;
				if (anonymousPingResponse.StatusCode == HttpStatusCode.Unauthorized && credentials is not null) {
					anonymousPingResponse.Dispose();
					pingResponse = SendRegistryRequest(HttpMethod.Get, new Uri(endpoint, "v2/"), credentials);
				}

				if (pingResponse.StatusCode == HttpStatusCode.Unauthorized && credentials is null) {
					pingResponse.Dispose();
					return new ContainerRegistryPreflightResult(
						false,
						endpoint.ToString(),
						$"Registry '{endpoint}' requires authentication.",
						RequiresAuthentication: true);
				}

				if (!pingResponse.IsSuccessStatusCode) {
					probeErrors.Add(
						$"{endpoint} returned {(int)pingResponse.StatusCode} {pingResponse.ReasonPhrase} for GET /v2/.");
					continue;
				}

				Uri uploadUri = new(endpoint, $"v2/{repositoryName}/blobs/uploads/");
				using HttpResponseMessage anonymousUploadResponse =
					SendRegistryRequest(HttpMethod.Post, uploadUri, credentials: null, includeEmptyBody: true);
				HttpResponseMessage uploadResponse = anonymousUploadResponse;
				if (anonymousUploadResponse.StatusCode == HttpStatusCode.Unauthorized && credentials is not null) {
					anonymousUploadResponse.Dispose();
					uploadResponse = SendRegistryRequest(HttpMethod.Post, uploadUri, credentials, includeEmptyBody: true);
				}

				if (uploadResponse.StatusCode == HttpStatusCode.Accepted) {
					TryAbortUpload(endpoint, uploadResponse, credentials);
					uploadResponse.Dispose();
					pingResponse.Dispose();
					return new ContainerRegistryPreflightResult(
						true,
						endpoint.ToString(),
						$"Registry '{endpoint}' accepted an upload probe for repository '{repositoryName}'.");
				}

				if (uploadResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
					uploadResponse.Dispose();
					pingResponse.Dispose();
					return new ContainerRegistryPreflightResult(
						false,
						endpoint.ToString(),
						$"Registry '{endpoint}' rejected upload initiation for repository '{repositoryName}' with {(int)uploadResponse.StatusCode} {uploadResponse.ReasonPhrase}.",
						RequiresAuthentication: uploadResponse.StatusCode == HttpStatusCode.Unauthorized);
				}

				probeErrors.Add(
					$"{endpoint} returned {(int)uploadResponse.StatusCode} {uploadResponse.ReasonPhrase} for POST /v2/{repositoryName}/blobs/uploads/.");
				uploadResponse.Dispose();
				pingResponse.Dispose();
			}
			catch (Exception exception) {
				probeErrors.Add($"{endpoint} probe failed: {exception.Message}");
			}
		}

		string combinedMessage = probeErrors.Count == 0
			? $"No reachable registry endpoint was found for '{registryPrefix}'."
			: string.Join(Environment.NewLine, probeErrors);
		return new ContainerRegistryPreflightResult(false, string.Empty, combinedMessage);
	}

	private IEnumerable<Uri> BuildCandidateEndpoints(string registryPrefix) {
		string trimmedPrefix = registryPrefix.Trim().TrimEnd('/');
		if (Uri.TryCreate(trimmedPrefix, UriKind.Absolute, out Uri absolutePrefix)) {
			yield return new Uri($"{absolutePrefix.Scheme}://{absolutePrefix.Authority}/");
			yield break;
		}

		string authority = trimmedPrefix.Split('/', 2, StringSplitOptions.RemoveEmptyEntries)[0];
		yield return new Uri($"https://{authority}/");
		yield return new Uri($"http://{authority}/");
	}

	private string ExtractRepositoryName(string registryImageReference) {
		int registrySeparatorIndex = registryImageReference.IndexOf('/', StringComparison.Ordinal);
		if (registrySeparatorIndex < 0 || registrySeparatorIndex == registryImageReference.Length - 1) {
			throw new InvalidOperationException(
				$"Registry image reference '{registryImageReference}' does not contain a repository path.");
		}

		string repositoryWithTag = registryImageReference[(registrySeparatorIndex + 1)..];
		int digestSeparatorIndex = repositoryWithTag.IndexOf('@', StringComparison.Ordinal);
		if (digestSeparatorIndex >= 0) {
			repositoryWithTag = repositoryWithTag[..digestSeparatorIndex];
		}

		int lastSlashIndex = repositoryWithTag.LastIndexOf('/');
		int tagSeparatorIndex = repositoryWithTag.LastIndexOf(':');
		if (tagSeparatorIndex > lastSlashIndex) {
			repositoryWithTag = repositoryWithTag[..tagSeparatorIndex];
		}

		return repositoryWithTag;
	}

	private HttpResponseMessage SendRegistryRequest(HttpMethod method, Uri requestUri,
		ContainerRegistryCredentials credentials, bool includeEmptyBody = false) {
		using HttpRequestMessage request = new(method, requestUri);
		if (includeEmptyBody) {
			request.Content = new ByteArrayContent([]);
		}

		if (credentials is not null) {
			string authToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{credentials.Username}:{credentials.Password}"));
			request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authToken);
		}

		return _httpClient.SendAsync(request).GetAwaiter().GetResult();
	}

	private void TryAbortUpload(Uri endpoint, HttpResponseMessage uploadResponse, ContainerRegistryCredentials credentials) {
		Uri uploadLocation = uploadResponse.Headers.Location;
		if (uploadLocation is null) {
			return;
		}

		Uri resolvedUploadLocation = uploadLocation.IsAbsoluteUri ? uploadLocation : new Uri(endpoint, uploadLocation);
		try {
			using HttpResponseMessage _ = SendRegistryRequest(HttpMethod.Delete, resolvedUploadLocation, credentials);
		}
		catch {
			// Best-effort cleanup only. The probe already succeeded.
		}
	}
}
