using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Clio.Common;
using ModelContextProtocol.Server;

namespace Clio.Command.McpServer.Tools;

/// <summary>
/// Performs one read-only request against a relative route of a registered Creatio environment.
/// </summary>
[McpServerToolType]
public sealed class CallServiceGetTool(
	IToolCommandResolver commandResolver,
	IHttpClientFactory httpClientFactory) {

	internal const string ToolName = "call-service-get";
	internal const int MaxBodyBytes = 64 * 1024;
	private const int DefaultTimeoutMs = 30_000;
	private const int MinTimeoutMs = 1_000;
	private const int MaxTimeoutMs = 120_000;
	private const int MaxServicePathLength = 2_048;

	/// <summary>
	/// Calls a relative service route using the selected environment's existing client.
	/// </summary>
	[McpServerTool(Name = ToolName, ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
	[McpToolExecution(
		Location = McpToolExecutionLocation.Worker,
		Lifetime = McpToolExecutionLifetime.PerCall,
		OperationFamily = McpToolOperationFamily.None,
		BudgetPolicy = McpToolBudgetPolicy.ParentKillDefault,
		RequiresClientRequests = McpToolClientRequests.None,
		SharedFileResource = McpToolSharedFileResource.None)]
	[Description(
		"Performs one HTTP GET against a relative service path on a registered Creatio environment. " +
		"Authentication is enabled by default; set authenticated=false to prove that the same endpoint " +
		"rejects an anonymous request. " +
		"Use it to verify a custom read-only service endpoint. The path must be relative to " +
		"the selected Creatio application; absolute URLs, authority paths, fragments, traversal and encoded " +
		"path separators are refused. Callers cannot provide headers, credentials, an origin or another HTTP " +
		"method. The result includes the HTTP status and a bounded JSON or plain-text response body, but no " +
		"response headers. HTML bodies are never returned.")]
	public async Task<CallServiceGetResponse> Execute(
		[Description("Parameters: environment-name and service-path (required); authenticated and timeout (optional).")] [Required]
		CallServiceGetArgs args,
		CancellationToken cancellationToken = default) {
		if (args is null) {
			return CallServiceGetResponse.Failure("invalid-request", "Arguments are required.");
		}
		if (string.IsNullOrWhiteSpace(args.EnvironmentName)) {
			return CallServiceGetResponse.Failure("invalid-request", "environment-name is required.");
		}
		if (!TryNormalizeServicePath(args.ServicePath, out string servicePath, out string pathError)) {
			return CallServiceGetResponse.Failure("invalid-service-path", pathError);
		}

		int timeout = args.Timeout is { } requested
			? Math.Clamp(requested, MinTimeoutMs, MaxTimeoutMs)
			: DefaultTimeoutMs;
		using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(timeout);
		CancellationToken requestToken = timeoutSource.Token;
		try {
			EnvironmentOptions options = new() { Environment = args.EnvironmentName.Trim() };
			bool authenticated = args.Authenticated ?? true;
			string requestUrl;
			CallServiceGetTransportResponse response;
			if (authenticated) {
				(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder) =
					commandResolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(options);
				if (applicationClient is not ICreatioApplicationClient client) {
					throw new NotSupportedException();
				}
				requestUrl = urlBuilder.Build(servicePath);
				response = await ExecuteAuthenticatedAsync(
					client, requestUrl, timeout, requestToken).ConfigureAwait(false);
			} else {
				IServiceUrlBuilder urlBuilder = commandResolver.Resolve<IServiceUrlBuilder>(options);
				requestUrl = urlBuilder.Build(servicePath);
				response = await ExecuteAnonymousAsync(requestUrl, requestToken).ConfigureAwait(false);
			}
			if (!HasSameOrigin(requestUrl, response.FinalUri)) {
				return CallServiceGetResponse.Failure(
					"target-redirected",
					"The request finished outside the selected Creatio environment; no response body was returned.",
					response.Status);
			}

			string? mediaType = response.MediaType;
			byte[] body = response.Body;
			if (body.Length > 0 && !IsAllowedBody(mediaType)) {
				return CallServiceGetResponse.Failure(
					"unexpected-content-type",
					"The endpoint did not return JSON or plain text; its response body was not exposed.",
					response.Status,
					mediaType);
			}

			string responseBody = body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(body);
			return new CallServiceGetResponse(true, response.Status, mediaType, responseBody, null, null);
		} catch (ResponseTooLargeException) {
			return CallServiceGetResponse.Failure(
				"response-too-large",
				$"The response exceeds the {MaxBodyBytes}-byte limit; narrow the endpoint response.");
		} catch (TimeoutException) {
			return CallServiceGetResponse.Failure("timeout", "The GET did not finish before the timeout.");
		} catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
			return CallServiceGetResponse.Failure("timeout", "The GET did not finish before the timeout.");
		} catch (HttpRequestException) {
			return CallServiceGetResponse.Failure("transport-failed", "The GET could not reach the selected Creatio environment.");
		} catch (IOException) {
			return CallServiceGetResponse.Failure("transport-failed", "The GET response could not be read.");
		} catch (NotSupportedException) {
			return CallServiceGetResponse.Failure("transport-unavailable", "The selected environment does not support authenticated HTTP response reads.");
		}
	}

	private async Task<CallServiceGetTransportResponse> ExecuteAuthenticatedAsync(
		ICreatioApplicationClient client,
		string requestUrl,
		int timeout,
		CancellationToken cancellationToken) {
		BoundedGetResponse response = await client.ExecuteGetResponseBoundedAsync(
			requestUrl, MaxBodyBytes, timeout, cancellationToken).ConfigureAwait(false);
		return new CallServiceGetTransportResponse(
			response.StatusCode,
			response.MediaType,
			response.FinalUri,
			response.Body);
	}

	private async Task<CallServiceGetTransportResponse> ExecuteAnonymousAsync(
		string requestUrl,
		CancellationToken cancellationToken) {
		using HttpClient client = httpClientFactory.CreateClient(EnvironmentAvailabilityProbe.HttpClientName);
		using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
		using HttpResponseMessage response = await client.SendAsync(
			request,
			HttpCompletionOption.ResponseHeadersRead,
			cancellationToken).ConfigureAwait(false);
		(byte[] body, bool tooLarge) = await ReadBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
		if (tooLarge) {
			throw new ResponseTooLargeException(MaxBodyBytes + 1, MaxBodyBytes);
		}
		return new CallServiceGetTransportResponse(
			(int)response.StatusCode,
			response.Content?.Headers.ContentType?.MediaType,
			response.RequestMessage?.RequestUri,
			body);
	}

	private static async Task<(byte[] Body, bool TooLarge)> ReadBodyAsync(
		HttpContent? content,
		CancellationToken cancellationToken) {
		if (content is null) {
			return ([], false);
		}
		if (content.Headers.ContentLength > MaxBodyBytes) {
			return ([], true);
		}
		await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		using MemoryStream destination = new(Math.Min(MaxBodyBytes + 1, 8 * 1024));
		byte[] buffer = new byte[8 * 1024];
		while (destination.Length <= MaxBodyBytes) {
			int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
			if (read == 0) {
				return (destination.ToArray(), false);
			}
			destination.Write(buffer, 0, read);
		}
		return ([], true);
	}

	private static bool TryNormalizeServicePath(string? requested, out string normalized, out string error) {
		normalized = string.Empty;
		error = string.Empty;
		if (string.IsNullOrWhiteSpace(requested)) {
			error = "service-path is required.";
			return false;
		}
		string value = requested.Trim();
		if (value.Length > MaxServicePathLength) {
			error = $"service-path cannot exceed {MaxServicePathLength} characters.";
			return false;
		}
		if (value[0] is '/' or '\\' || value.Contains('\\') || value.Contains('#')
			|| value.Any(char.IsControl) || Uri.TryCreate(value, UriKind.Absolute, out _)) {
			error = "service-path must be a relative Creatio route without an authority, fragment or backslash.";
			return false;
		}
		string route = value.Split('?', 2)[0];
		if (string.IsNullOrWhiteSpace(route) || ContainsUnsafePathSegment(route)) {
			error = "service-path contains traversal or an encoded path separator.";
			return false;
		}
		normalized = value;
		return true;
	}

	private static bool ContainsUnsafePathSegment(string route) {
		if (route.Contains("%2f", StringComparison.OrdinalIgnoreCase)
			|| route.Contains("%5c", StringComparison.OrdinalIgnoreCase)
			|| route.Contains("%2e", StringComparison.OrdinalIgnoreCase)) {
			return true;
		}
		return route.Split('/').Any(segment => segment is "." or "..");
	}

	private static bool HasSameOrigin(string requestUrl, Uri? finalUri) {
		if (finalUri is null) {
			return false;
		}
		Uri requested = new(requestUrl, UriKind.Absolute);
		return string.Equals(requested.Scheme, finalUri.Scheme, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(requested.Host, finalUri.Host, StringComparison.OrdinalIgnoreCase)
			&& requested.Port == finalUri.Port;
	}

	private static bool IsAllowedBody(string? mediaType) =>
		string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
		|| (mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ?? false)
		|| (mediaType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false)
			&& !string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase);

	private sealed record CallServiceGetTransportResponse(
		int Status,
		string? MediaType,
		Uri? FinalUri,
		byte[] Body);
}

/// <summary>Arguments for <see cref="CallServiceGetTool"/>.</summary>
public sealed record CallServiceGetArgs(
	[property: JsonPropertyName("environment-name"), Required]
	string? EnvironmentName = null,
	[property: JsonPropertyName("service-path"), Required]
	string? ServicePath = null,
	[property: JsonPropertyName("authenticated")]
	bool? Authenticated = null,
	[property: JsonPropertyName("timeout")]
	int? Timeout = null);

/// <summary>Bounded result of a service GET.</summary>
public sealed record CallServiceGetResponse(
	[property: JsonPropertyName("success")]
	bool Success,
	[property: JsonPropertyName("status"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	int? Status,
	[property: JsonPropertyName("contentType"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? ContentType,
	[property: JsonPropertyName("body"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Body,
	[property: JsonPropertyName("errorClass"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? ErrorClass,
	[property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	string? Error) {

	internal static CallServiceGetResponse Failure(
		string errorClass,
		string error,
		int? status = null,
		string? contentType = null) =>
		new(false, status, contentType, null, errorClass, error);
}
