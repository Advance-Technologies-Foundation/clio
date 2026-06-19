using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: GetIdentityAssertionOptions

/// <summary>
///     Options for the <c>get-identity-assertion</c> command.
/// </summary>
[Verb("get-identity-assertion", Aliases = ["identity-assertion"],
	HelpText = "Issue a short-lived signed identity assertion (JWT) for the current authorized user")]
public class GetIdentityAssertionOptions : IdentityCommandOptions
{
}

#endregion

#region Class: IdentityAssertionResponse

/// <summary>
///     Subset of the Creatio current-user assertion payload used for plain-text rendering.
/// </summary>
internal sealed record IdentityAssertionResponse
{

	[JsonPropertyName("assertion")]
	public string Assertion { get; init; }

	[JsonPropertyName("assertionType")]
	public string AssertionType { get; init; }

	[JsonPropertyName("expiresIn")]
	public int ExpiresIn { get; init; }

	[JsonPropertyName("expiresAt")]
	public DateTime ExpiresAt { get; init; }

}

#endregion

#region Class: GetIdentityAssertionCommand

/// <summary>
///     Requests a signed identity assertion from <c>identityAssertion/currentUser</c>. The assertion
///     is the token the Creatio frontend passes to the AI chat to start the Identity Service V3
///     token-exchange flow on behalf of the authorized user.
/// </summary>
public class GetIdentityAssertionCommand : RemoteCommand<GetIdentityAssertionOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>
	///     Initializes a new instance of the <see cref="GetIdentityAssertionCommand" /> class.
	/// </summary>
	public GetIdentityAssertionCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public override HttpMethod HttpMethod => HttpMethod.Post;

	/// <inheritdoc />
	protected override string ServicePath =>
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionCurrentUser);

	/// <inheritdoc />
	protected override string GetRequestData(GetIdentityAssertionOptions options) => string.Empty;

	/// <inheritdoc />
	protected override void ProceedResponse(string response, GetIdentityAssertionOptions options) {
		if (string.IsNullOrWhiteSpace(response)) {
			CommandSuccess = false;
			Logger.WriteError("Empty response received from server.");
			Logger.WriteError($"Endpoint: {ServiceUri}");
			return;
		}
		if (IdentityOutput.TryParseError(response, out string error, out string description)) {
			CommandSuccess = false;
			if (options.Format == IdentityOutputFormat.Json) {
				Logger.WriteLine(IdentityOutput.Pretty(response));
			}
			else {
				Logger.WriteError($"{error}: {description}");
			}
			return;
		}
		if (options.Format == IdentityOutputFormat.Json) {
			Logger.WriteLine(IdentityOutput.Pretty(response));
			return;
		}
		try {
			IdentityAssertionResponse model =
				JsonSerializer.Deserialize<IdentityAssertionResponse>(response, IdentityOutput.CaseInsensitive);
			if (string.IsNullOrWhiteSpace(model?.Assertion)) {
				CommandSuccess = false;
				Logger.WriteError("Response did not contain an assertion token.");
				Logger.WriteLine(response);
				return;
			}
			Logger.WriteInfo($"Assertion type: {model.AssertionType}; expires in {model.ExpiresIn}s (at {model.ExpiresAt:u}).");
			Logger.WriteLine(model.Assertion);
		}
		catch (JsonException e) {
			CommandSuccess = false;
			Logger.WriteError($"Failed to parse assertion response: {e.Message}");
			Logger.WriteLine(response);
		}
	}

}

#endregion
