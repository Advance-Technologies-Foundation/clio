using System;
using System.Text.Json;

namespace Clio.Common;

/// <summary>
/// Base class for thin clients over native Creatio configuration services. Centralizes the shared
/// request/response plumbing — building the service URL from a <see cref="ServiceUrlBuilder.KnownRoute"/>,
/// serializing the request, POSTing it via <see cref="IApplicationClient"/>, and deserializing the JSON
/// response — so each concrete client only declares its route, its request/response shapes, and the mapping.
/// </summary>
public abstract class CreatioServiceClient
{
	private static readonly JsonSerializerOptions ResponseJsonOptions = new() {
		PropertyNameCaseInsensitive = true
	};

	// Redacted first, then capped: a token cut by the cap would no longer be recognised by the redactor.
	private const int MaxBodyPreviewLength = 200;

	private readonly IApplicationClient _applicationClient;
	private readonly IServiceUrlBuilder _urlBuilder;

	/// <summary>
	/// Initializes a new instance of the <see cref="CreatioServiceClient"/> class.
	/// </summary>
	protected CreatioServiceClient(IApplicationClient applicationClient, IServiceUrlBuilder urlBuilder) {
		_applicationClient = applicationClient;
		_urlBuilder = urlBuilder;
	}

	/// <summary>
	/// POSTs <paramref name="request"/> to the service endpoint identified by <paramref name="route"/> and
	/// deserializes the JSON response into <typeparamref name="TResponse"/>.
	/// </summary>
	/// <typeparam name="TResponse">The expected response shape.</typeparam>
	/// <param name="route">The known service route to call.</param>
	/// <param name="request">The request payload; serialized with default options (honors JsonPropertyName).</param>
	/// <param name="requestOptions">The request timeout and retry settings.</param>
	/// <returns>The deserialized response.</returns>
	/// <exception cref="InvalidOperationException">The service returned an empty or non-JSON response.</exception>
	/// <exception cref="TimeoutException">The options' <see cref="CreatioRequestOptions.Deadline"/> is spent: nothing
	/// was sent.</exception>
	protected TResponse PostAndDeserialize<TResponse>(ServiceUrlBuilder.KnownRoute route, object request,
		CreatioRequestOptions requestOptions) {
		string url = _urlBuilder.Build(route);
		string requestData = JsonSerializer.Serialize(request);
		CreatioRequestOptions sendOptions = requestOptions.ForNextRequest();
		string response = _applicationClient.ExecutePostRequest(url, requestData,
			sendOptions.TimeOut, sendOptions.MaxAttempts, sendOptions.RetryDelay);
		if (string.IsNullOrWhiteSpace(response)) {
			throw new InvalidOperationException($"Empty response from {url}.");
		}
		try {
			return JsonSerializer.Deserialize<TResponse>(response, ResponseJsonOptions);
		}
		catch (JsonException) {
			// An HTML page (a login redirect, an ASP.NET error page) is never previewed: it can carry session cookies,
			// request tokens and stack traces, and this text reaches the log and an agent transcript.
			throw new InvalidOperationException(TextUtilities.LooksLikeMarkup(response)
				? $"Unexpected response from {url}: an HTML page instead of JSON (a login redirect or a server error page)."
				: $"Unexpected response from {url}: "
					+ TextUtilities.SanitizeForDisplay(SensitiveErrorTextRedactor.Redact(response), MaxBodyPreviewLength));
		}
	}
}
