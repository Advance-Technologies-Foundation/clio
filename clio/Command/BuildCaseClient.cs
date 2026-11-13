using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Clio.Command.ProcessModel;
using Clio.Common;
using Clio.UserEnvironment;

namespace Clio.Command;

/// <summary>How a <c>BuildCase</c> pass ended at the transport level.</summary>
public enum BuildCaseReplyKind {
	/// <summary>The server answered with the <c>BuildCaseResult</c> envelope.</summary>
	Envelope,

	/// <summary>The server answered the route with a 404: the installed package has no <c>BuildCase</c>.</summary>
	RouteMissing,

	/// <summary>The server answered with a body that is not the envelope (an HTML page, a bare fault string).</summary>
	NotAnEnvelope,

	/// <summary>The request failed before any answer could be read (connection, timeout, a non-404 status).</summary>
	TransportFailure
}

/// <summary>The answer to one <c>BuildCase</c> pass.</summary>
/// <param name="Kind">How the pass ended at the transport level.</param>
/// <param name="Response">The parsed envelope; only for <see cref="BuildCaseReplyKind.Envelope"/>.</param>
/// <param name="Detail">What the server or the transport said otherwise, already capped for a caller.</param>
public sealed record BuildCaseReply(BuildCaseReplyKind Kind, BuildCaseResponse Response, string Detail);

/// <summary>
/// Sends one <c>ProcessDesignService.BuildCase</c> pass and classifies the answer, so the caller can tell a package
/// without the operation from an answer whose outcome is unknown.
/// </summary>
public interface IBuildCaseClient {
	/// <summary>
	/// Posts <paramref name="request"/> wrapped under <c>request</c> and returns the classified answer. Never throws
	/// for a transport fault: what a fault MEANS depends on whether the pass could have written, which only the
	/// caller knows.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <param name="request">The pass to send.</param>
	BuildCaseReply Send(string environmentName, BuildCaseRequest request);
}

/// <inheritdoc />
public sealed class BuildCaseClient(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder)
	: IBuildCaseClient {

	/// <summary>The longest server or transport text quoted back.</summary>
	internal const int MaxDetailLength = 400;

	private const string EnvelopeName = "BuildCaseResult";

	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public BuildCaseReply Send(string environmentName, BuildCaseRequest request) {
		ArgumentNullException.ThrowIfNull(request);
		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		string url = serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.BuildCase, environmentSettings);
		string body = new JsonObject {
			["request"] = JsonSerializer.SerializeToNode(request, JsonOptions)
		}.ToJsonString();
		string responseBody;
		try {
			using IOwnedApplicationClient client = applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
			responseBody = client.ExecutePostRequest(url, body);
		} catch (Exception exception) when (IsNotFound(exception)) {
			return new BuildCaseReply(BuildCaseReplyKind.RouteMissing, null, Cap(exception.Message));
		} catch (Exception exception) {
			return new BuildCaseReply(BuildCaseReplyKind.TransportFailure, null, Cap(exception.Message));
		}
		return Classify(responseBody);
	}

	private static BuildCaseReply Classify(string responseBody) {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, "The server answered with an empty body.");
		}
		JsonNode root;
		try {
			root = JsonNode.Parse(responseBody);
		} catch (JsonException) {
			return new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, Cap(responseBody));
		}
		if (root is not JsonObject envelope || envelope[EnvelopeName] is not JsonObject result) {
			string serverText = ProcessDesignServerFault.ReadServerText(responseBody);
			return new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, serverText ?? Cap(responseBody));
		}
		try {
			BuildCaseResponse response = result.Deserialize<BuildCaseResponse>(JsonOptions);
			return response is null
				? new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, Cap(responseBody))
				: new BuildCaseReply(BuildCaseReplyKind.Envelope, response, null);
		} catch (JsonException) {
			return new BuildCaseReply(BuildCaseReplyKind.NotAnEnvelope, null, Cap(responseBody));
		}
	}

	// Both transports clio runs on surface a 404 this way: HttpClient as HttpRequestException.StatusCode and the
	// legacy WebRequest path as WebException.Response, anywhere in the chain.
	private static bool IsNotFound(Exception exception) {
		Stack<Exception> pending = new();
		pending.Push(exception);
		while (pending.Count > 0) {
			Exception current = pending.Pop();
			if (current is HttpRequestException { StatusCode: HttpStatusCode.NotFound }
				|| current is WebException { Response: HttpWebResponse { StatusCode: HttpStatusCode.NotFound } }) {
				return true;
			}
			if (current is AggregateException aggregate) {
				foreach (Exception inner in aggregate.InnerExceptions) {
					pending.Push(inner);
				}
			} else if (current.InnerException is not null) {
				pending.Push(current.InnerException);
			}
		}
		return false;
	}

	private static string Cap(string text) {
		string trimmed = (text ?? string.Empty).Trim();
		return trimmed.Length <= MaxDetailLength ? trimmed : trimmed[..MaxDetailLength] + "...";
	}
}
