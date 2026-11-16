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

/// <summary>How one call of a <c>ProcessDesignService</c> case operation ended at the transport level.</summary>
public enum DynamicCaseReplyKind {
	/// <summary>The server answered with the operation's <c>&lt;Operation&gt;Result</c> envelope.</summary>
	Envelope,

	/// <summary>The server answered the route with a 404: the installed package does not have the operation.</summary>
	RouteMissing,

	/// <summary>The server answered with a body that is not the envelope (an HTML page, a bare fault string).</summary>
	NotAnEnvelope,

	/// <summary>The request failed before any answer could be read (connection, timeout, a non-404 status).</summary>
	TransportFailure
}

/// <summary>The answer to one call of a <c>ProcessDesignService</c> case operation.</summary>
/// <param name="Kind">How the call ended at the transport level.</param>
/// <param name="Response">The parsed envelope; only for <see cref="DynamicCaseReplyKind.Envelope"/>.</param>
/// <param name="Detail">What the server or the transport said otherwise, already capped for a caller.</param>
/// <typeparam name="TResponse">The operation's result contract.</typeparam>
public sealed record DynamicCaseReply<TResponse>(DynamicCaseReplyKind Kind, TResponse Response, string Detail)
	where TResponse : class;

/// <summary>
/// Opens sessions against <c>ProcessDesignService</c>'s dynamic-case operations (<c>BuildCase</c>,
/// <c>DescribeCase</c>, <c>ModifyCase</c>).
/// </summary>
public interface IBuildCaseClient {
	/// <summary>
	/// Opens one session on the environment: every call sent through it shares ONE application client, so one tool
	/// call logs in once however many passes it sends.
	/// </summary>
	/// <param name="environmentName">Registered clio environment name.</param>
	/// <returns>The session; the caller disposes it, which releases its client.</returns>
	/// <exception cref="InvalidOperationException">The environment is not registered.</exception>
	IDynamicCaseSession OpenSession(string environmentName);
}

/// <summary>
/// One login's worth of calls to the dynamic-case operations. Each call posts its request wrapped under
/// <c>request</c> and returns the classified answer, so the caller can tell a package without the operation from an
/// answer whose outcome is unknown. No call throws for a transport fault: what a fault MEANS depends on whether the
/// call could have written, which only the caller knows.
/// </summary>
public interface IDynamicCaseSession : IDisposable {
	/// <summary>Sends one <c>BuildCase</c> pass.</summary>
	/// <param name="request">The pass to send.</param>
	DynamicCaseReply<BuildCaseResponse> Build(BuildCaseRequest request);

	/// <summary>Sends one <c>DescribeCase</c> call.</summary>
	/// <param name="request">The case, or the object, to read.</param>
	DynamicCaseReply<DescribeCaseResponse> Describe(DescribeCaseRequest request);

	/// <summary>Sends one <c>ModifyCase</c> call.</summary>
	/// <param name="request">The change, or its dry run.</param>
	DynamicCaseReply<ModifyCaseResponse> Modify(ModifyCaseRequest request);
}

/// <inheritdoc />
public sealed class BuildCaseClient(
	ISettingsRepository settingsRepository,
	IApplicationClientFactory applicationClientFactory,
	IServiceUrlBuilder serviceUrlBuilder)
	: IBuildCaseClient {

	/// <summary>The longest server or transport text quoted back.</summary>
	internal const int MaxDetailLength = 400;

	private static readonly JsonSerializerOptions JsonOptions = new() {
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true
	};

	/// <inheritdoc />
	public IDynamicCaseSession OpenSession(string environmentName) {
		EnvironmentSettings environmentSettings = settingsRepository.FindEnvironment(environmentName)
			?? throw new InvalidOperationException(EnvironmentNotFoundError.Build(environmentName, settingsRepository));
		return new Session(environmentSettings, applicationClientFactory, serviceUrlBuilder);
	}

	// The client is created on the first call rather than here, inside the same classification as the call, so a
	// failure to create it stays a transport fault of that call instead of an exception of the session.
	private sealed class Session(
		EnvironmentSettings environmentSettings,
		IApplicationClientFactory applicationClientFactory,
		IServiceUrlBuilder serviceUrlBuilder)
		: IDynamicCaseSession {

		private IOwnedApplicationClient _client;
		private bool _disposed;

		public DynamicCaseReply<BuildCaseResponse> Build(BuildCaseRequest request) =>
			Send<BuildCaseResponse>(ServiceUrlBuilder.KnownRoute.BuildCase, "BuildCaseResult", request);

		public DynamicCaseReply<DescribeCaseResponse> Describe(DescribeCaseRequest request) =>
			Send<DescribeCaseResponse>(ServiceUrlBuilder.KnownRoute.DescribeCase, "DescribeCaseResult", request);

		public DynamicCaseReply<ModifyCaseResponse> Modify(ModifyCaseRequest request) =>
			Send<ModifyCaseResponse>(ServiceUrlBuilder.KnownRoute.ModifyCase, "ModifyCaseResult", request);

		public void Dispose() {
			if (_disposed) {
				return;
			}
			_disposed = true;
			_client?.Dispose();
			_client = null;
		}

		private DynamicCaseReply<TResponse> Send<TResponse>(ServiceUrlBuilder.KnownRoute route, string envelopeName,
			object request) where TResponse : class {
			ArgumentNullException.ThrowIfNull(request);
			ObjectDisposedException.ThrowIf(_disposed, this);
			string url = serviceUrlBuilder.Build(route, environmentSettings);
			string body = new JsonObject {
				["request"] = JsonSerializer.SerializeToNode(request, request.GetType(), JsonOptions)
			}.ToJsonString();
			string responseBody;
			try {
				_client ??= applicationClientFactory.CreateOwnedEnvironmentClient(environmentSettings);
				responseBody = _client.ExecutePostRequest(url, body);
			} catch (Exception exception) when (IsNotFound(exception)) {
				return new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.RouteMissing, null, Cap(exception.Message));
			} catch (Exception exception) {
				return new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.TransportFailure, null,
					Cap(exception.Message));
			}
			return Classify<TResponse>(responseBody, envelopeName);
		}
	}

	private static DynamicCaseReply<TResponse> Classify<TResponse>(string responseBody, string envelopeName)
		where TResponse : class {
		if (string.IsNullOrWhiteSpace(responseBody)) {
			return new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.NotAnEnvelope, null,
				"The server answered with an empty body.");
		}
		JsonNode root;
		try {
			root = JsonNode.Parse(responseBody);
		} catch (JsonException) {
			return new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.NotAnEnvelope, null, Cap(responseBody));
		}
		if (root is not JsonObject envelope || envelope[envelopeName] is not JsonObject result) {
			string serverText = ProcessDesignServerFault.ReadServerText(responseBody);
			return new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.NotAnEnvelope, null,
				serverText ?? Cap(responseBody));
		}
		try {
			TResponse response = result.Deserialize<TResponse>(JsonOptions);
			return response is null
				? new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.NotAnEnvelope, null, Cap(responseBody))
				: new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.Envelope, response, null);
		} catch (JsonException) {
			return new DynamicCaseReply<TResponse>(DynamicCaseReplyKind.NotAnEnvelope, null, Cap(responseBody));
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
