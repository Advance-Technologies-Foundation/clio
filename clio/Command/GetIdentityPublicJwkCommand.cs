using System.Net.Http;
using System.Text.Json;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: GetIdentityPublicJwkOptions

/// <summary>
///     Options for the <c>get-identity-public-jwk</c> command.
/// </summary>
[Verb("get-identity-public-jwk", Aliases = ["identity-public-jwk"],
	HelpText = "Get the instance public key (JWK) used to verify identity assertions in Identity Service V3")]
public class GetIdentityPublicJwkOptions : IdentityCommandOptions
{
}

#endregion

#region Class: GetIdentityPublicJwkCommand

/// <summary>
///     Reads the instance public key from <c>identityAssertion/publicJwk</c>. The JWK is registered
///     once with Identity Service V3 at onboarding so V3 can verify assertions signed by this instance.
/// </summary>
public class GetIdentityPublicJwkCommand : RemoteCommand<GetIdentityPublicJwkOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>
	///     Initializes a new instance of the <see cref="GetIdentityPublicJwkCommand" /> class.
	/// </summary>
	public GetIdentityPublicJwkCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public override HttpMethod HttpMethod => HttpMethod.Get;

	/// <inheritdoc />
	protected override string ServicePath =>
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionPublicJwk);

	/// <inheritdoc />
	protected override void ProceedResponse(string response, GetIdentityPublicJwkOptions options) {
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
		// Plain text: emit the JWK as a single compact line, ready to paste into Identity Service V3.
		try {
			using JsonDocument document = JsonDocument.Parse(response);
			Logger.WriteLine(JsonSerializer.Serialize(document.RootElement));
		}
		catch (JsonException e) {
			CommandSuccess = false;
			Logger.WriteError($"Failed to parse public JWK response: {e.Message}");
			Logger.WriteLine(response);
		}
	}

}

#endregion
