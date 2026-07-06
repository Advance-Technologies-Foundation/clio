using System.Net.Http;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

#region Class: CheckAuthCodeFlowOptions

/// <summary>
///     Options for the <c>check-auth-code-flow</c> command.
/// </summary>
[Verb("check-auth-code-flow", Aliases = ["auth-code-flow"],
	HelpText = "Check whether the environment can use the OAuth authorization code flow with Identity Service")]
public class CheckAuthCodeFlowOptions : IdentityCommandOptions
{
}

#endregion

#region Class: CheckAuthCodeFlowCommand

/// <summary>
///     Reads <c>identityServiceInfo/canUseAuthorizationCodeFlow</c> to report whether the environment
///     is configured to use the OAuth authorization code flow with the Identity Service.
/// </summary>
public class CheckAuthCodeFlowCommand : RemoteCommand<CheckAuthCodeFlowOptions>
{

	private readonly IServiceUrlBuilder _serviceUrlBuilder;

	/// <summary>
	///     Initializes a new instance of the <see cref="CheckAuthCodeFlowCommand" /> class.
	/// </summary>
	public CheckAuthCodeFlowCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings) {
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	/// <inheritdoc />
	public override HttpMethod HttpMethod => HttpMethod.Get;

	/// <inheritdoc />
	protected override string ServicePath =>
		_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.IdentityServiceInfoCanUseAuthorizationCodeFlow);

	/// <inheritdoc />
	protected override void ProceedResponse(string response, CheckAuthCodeFlowOptions options) {
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
		bool canUse = bool.TryParse(response.Trim(), out bool parsed) && parsed;
		if (options.Format == IdentityOutputFormat.Json) {
			Logger.WriteLine($"{{\"canUseAuthorizationCodeFlow\":{canUse.ToString().ToLowerInvariant()}}}");
			return;
		}
		Logger.WriteLine(canUse.ToString().ToLowerInvariant());
	}

}

#endregion
