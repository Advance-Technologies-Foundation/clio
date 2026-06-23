using System.Net.Http;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: RegenerateIdentitySigningKeyOptions

/// <summary>
///     Options for the <c>regenerate-identity-signing-key</c> command.
/// </summary>
[Verb("regenerate-identity-signing-key", Aliases = ["identity-regenerate-key"],
	HelpText = "Regenerate the instance identity-assertion signing key pair (the private key never leaves Creatio)")]
public class RegenerateIdentitySigningKeyOptions : IdentityCommandOptions
{
}

#endregion

#region Class: RegenerateIdentitySigningKeyCommand

/// <summary>
///     Triggers a server-side regeneration of the identity-assertion signing key pair via
///     <c>identityAssertion/regenerateSigningKey</c>. This is destructive: assertions signed with the
///     previous key stop validating and the new public key must be re-registered with Identity Service V3.
/// </summary>
public class RegenerateIdentitySigningKeyCommand : RemoteCommand<RegenerateIdentitySigningKeyOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>
	///     Initializes a new instance of the <see cref="RegenerateIdentitySigningKeyCommand" /> class.
	/// </summary>
	public RegenerateIdentitySigningKeyCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public override HttpMethod HttpMethod => HttpMethod.Post;

	/// <inheritdoc />
	protected override string ServicePath =>
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityAssertionRegenerateSigningKey);

	/// <inheritdoc />
	protected override string GetRequestData(RegenerateIdentitySigningKeyOptions options) => string.Empty;

	/// <inheritdoc />
	protected override void ProceedResponse(string response, RegenerateIdentitySigningKeyOptions options) {
		// The endpoint returns 204 No Content (empty body) on success; a non-empty body carries an
		// ErrorInfo payload (for example FeatureDisabled or AccessDenied), so success must not be
		// reported unconditionally.
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
			Logger.WriteLine("{\"status\":\"regenerated\"}");
			return;
		}
		Logger.WriteLine("OK");
	}

}

#endregion
