using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using Clio.Command;
using Clio.Common;
using CommandLine;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
///     Shared arrangement of the security-events command fixtures: a substitute application client that answers
///     every POST with <see cref="Answer" />, and a substitute logger.
/// </summary>
public abstract class SsoSecurityEventsCommandTestsBase<TOptions> : BaseCommandTests<TOptions>
	where TOptions : SsoSecurityEventsOptions
{

	protected const string FeatureOffAnswer =
		"{\"reason\":\"FeatureOff\",\"reasonDetail\":null,\"status\":{\"providerExists\":true,"
		+ "\"featureEnabled\":false,\"enabled\":false,\"siteStatus\":\"Off\"}}";

	protected const string SuccessAnswer =
		"{\"reason\":\"\",\"providerId\":\"11111111-1111-1111-1111-111111111111\",\"status\":{\"providerExists\":true,"
		+ "\"featureEnabled\":true,\"enabled\":true,\"siteAddress\":\"https://site.example.com\",\"siteStatus\":\"On\","
		+ "\"streamStatus\":\"enabled\",\"changedByOperation\":true}}";

	protected readonly IApplicationClient ApplicationClient = Substitute.For<IApplicationClient>();
	protected readonly ILogger Logger = Substitute.For<ILogger>();

	protected string Answer { get; set; } = SuccessAnswer;

	protected string SentBody { get; private set; }

	protected string SentUrl { get; private set; }

	protected int SentTimeout { get; private set; }

	protected int SentAttempts { get; private set; }

	protected override void AdditionalRegistrations(IServiceCollection containerBuilder) {
		base.AdditionalRegistrations(containerBuilder);
		containerBuilder.AddSingleton(ApplicationClient);
	}

	[SetUp]
	public override void Setup() {
		base.Setup();
		Answer = SuccessAnswer;
		SentBody = null;
		SentUrl = null;
		ApplicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => Capture(call));
		ApplicationClient
			.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Returns(call => Capture(call));
	}

	[TearDown]
	public override void TearDown() {
		ApplicationClient.ClearReceivedCalls();
		Logger.ClearReceivedCalls();
		base.TearDown();
	}

	protected TCommand Resolve<TCommand>() where TCommand : RemoteCommand<TOptions> {
		TCommand command = Container.GetRequiredService<TCommand>();
		command.Logger = Logger;
		return command;
	}

	[Test]
	[Description("--code is a required option of every security-events verb, so a call without it never parses")]
	public void Parse_ShouldRefuse_WhenCodeIsMissing() {
		// Arrange
		Parser parser = new(settings => settings.HelpWriter = null);

		// Act
		ParserResult<TOptions> result = parser.ParseArguments<TOptions>(Array.Empty<string>());

		// Assert
		result.Tag.Should().Be(ParserResultType.NotParsed, "because no provider is named");
		((NotParsed<TOptions>)result).Errors.OfType<MissingRequiredOptionError>()
			.Select(error => error.NameInfo.LongName)
			.Should().Contain("code", "because every method of the service is keyed by the provider code");
	}

	protected void AssertNothingSent(string because) {
		ApplicationClient.DidNotReceiveWithAnyArgs()
			.ExecutePostRequest(default, default, default, default, default);
		ApplicationClient.DidNotReceiveWithAnyArgs()
			.ExecuteNonReplayablePostRequest(default, default, default, default, default);
		SentBody.Should().BeNull(because);
	}

	private string Capture(NSubstitute.Core.CallInfo call) {
		SentUrl = call.ArgAt<string>(0);
		SentBody = call.ArgAt<string>(1);
		SentTimeout = call.ArgAt<int>(2);
		SentAttempts = call.ArgAt<int>(3);
		return Answer;
	}

}

[TestFixture]
[Property("Module", "Command")]
public class GetSsoSecurityEventsCommandTests : SsoSecurityEventsCommandTestsBase<GetSsoSecurityEventsOptions>
{

	private static GetSsoSecurityEventsOptions Options(string format = "text") =>
		new() { Code = "identity-v3", FormatRaw = format };

	[Test]
	[Description("Reads the state through GetProviderState with the provider code as the only member")]
	public void Execute_ShouldPostProviderCodeToGetProviderState_WhenCodeIsGiven() {
		// Arrange
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(0, "because the site answered with a reason code");
		SentUrl.Should().Be("http://localhost/0/rest/SsoSecurityEventsSettingsService/GetProviderState",
			"because a .NET Framework site serves the service under the 0/ workspace prefix");
		SentBody.Should().Be("{\"providerCode\":\"identity-v3\"}", "because GetProviderState takes the code only");
	}

	[Test]
	[Description("A read may be retried and replayed after a re-login, so it goes through the ordinary POST")]
	public void Execute_ShouldUseReplayablePost_WhenTheCallOnlyReads() {
		// Arrange
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		command.Execute(Options());

		// Assert
		ApplicationClient.Received(1).ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
			Arg.Any<int>(), Arg.Any<int>());
		ApplicationClient.DidNotReceiveWithAnyArgs()
			.ExecuteNonReplayablePostRequest(default, default, default, default, default);
		SentAttempts.Should().Be(3, "because a read keeps the command's default number of attempts");
	}

	[Test]
	[Description("A reason code is the site's answer, not a failure: it is printed and the exit code stays 0")]
	public void Execute_ShouldPrintReasonAndReturnZero_WhenTheSiteAnswersWithAReasonCode() {
		// Arrange
		Answer = FeatureOffAnswer;
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(0, "because only a call that itself failed exits non-zero");
		Logger.Received(1).WriteLine("reason: FeatureOff");
		Logger.Received(1).WriteLine("featureEnabled: false");
		Logger.Received(1).WriteLine("siteStatus: Off");
	}

	[Test]
	[Description("An empty reason code is success and is printed as none, with the state the call left")]
	public void Execute_ShouldPrintReasonNoneAndState_WhenTheReasonIsEmpty() {
		// Arrange
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(0, "because the call succeeded");
		Logger.Received(1).WriteLine("reason: none");
		Logger.Received(1).WriteLine("siteAddress: https://site.example.com");
		Logger.Received(1).WriteLine("streamStatus: enabled");
		Logger.Received(1).WriteLine("changedByOperation: true");
	}

	[Test]
	[Description("--format json prints the site's whole answer instead of the summary")]
	public void Execute_ShouldPrintWholeAnswer_WhenFormatIsJson() {
		// Arrange
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options("json"));

		// Assert
		result.Should().Be(0, "because the call succeeded");
		Logger.Received(1).WriteLine(Arg.Is<string>(text =>
			text.Contains("\"streamStatus\": \"enabled\"") && text.Contains("\"reason\": \"\"")));
		Logger.DidNotReceive().WriteLine("reason: none");
	}

	[Test]
	[Description("A site without the SsoSecurityEvents package answers 404, which is a failed call and says so")]
	public void Execute_ShouldReturnOneAndSayNotSupported_WhenTheServiceIsNotFound() {
		// Arrange
		ApplicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(1, "because the call itself failed: the site has no such service");
		Logger.Received(1).WriteError(Arg.Is<string>(text => text.Contains("not supported")
			&& text.Contains("SsoSecurityEvents")));
	}

	[Test]
	[Description("A 404 from the legacy WebRequest transport, wrapped in another exception, is recognised too")]
	public void Execute_ShouldReturnOneAndSayNotSupported_WhenAWrappedWebExceptionIsNotFound() {
		// Arrange
		HttpWebResponse notFound = Substitute.For<HttpWebResponse>();
		notFound.StatusCode.Returns(HttpStatusCode.NotFound);
		Exception wrapped = new InvalidOperationException("request failed",
			new WebException("The remote server returned an error: (404) Not Found.", null,
				WebExceptionStatus.ProtocolError, notFound));
		ApplicationClient
			.ExecutePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Throws(wrapped);
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(1, "because the call itself failed: the site has no such service");
		Logger.Received(1).WriteError(Arg.Is<string>(text => text.Contains("not supported")));
	}

	[Test]
	[Description("An HTML page (a lost session, a proxy) is not the service's answer and fails the command")]
	public void Execute_ShouldReturnOne_WhenTheAnswerIsNotJson() {
		// Arrange
		Answer = "<html><body>Login</body></html>";
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(1, "because nothing can be said about what the site did");
		Logger.Received(1).WriteError("Unexpected response; it is not an answer of SsoSecurityEventsSettingsService.");
	}

	[Test]
	[Description("A JSON object without a reason code is a platform error, not the service's answer")]
	public void Execute_ShouldReturnOne_WhenTheAnswerHasNoReason() {
		// Arrange
		Answer = "{\"Message\":\"An error has occurred.\"}";
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(1, "because the settings service always answers with a reason member");
	}

	[Test]
	[Description("An empty answer fails the command: nothing can be said about what the site did")]
	public void Execute_ShouldReturnOne_WhenTheAnswerIsEmpty() {
		// Arrange
		Answer = "  ";
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(1, "because an empty body is not an answer of the service");
		Logger.Received(1).WriteError("Empty response received from the site.");
	}

	[Test]
	[Description("A host that capitalises the members is still read: the answer is matched without regard to case")]
	public void Execute_ShouldReadTheAnswer_WhenMembersArePascalCase() {
		// Arrange
		Answer = "{\"Reason\":\"FeatureOff\",\"Status\":{\"FeatureEnabled\":false}}";
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(Options());

		// Assert
		result.Should().Be(0, "because the site did answer with a reason code");
		Logger.Received(1).WriteLine("reason: FeatureOff");
		Logger.Received(1).WriteLine("featureEnabled: false");
	}

	[Test]
	[Description("An empty --code is refused before anything is sent")]
	public void Execute_ShouldRefuseWithoutCall_WhenCodeIsBlank() {
		// Arrange
		GetSsoSecurityEventsCommand command = Resolve<GetSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(new GetSsoSecurityEventsOptions { Code = "  " });

		// Assert
		result.Should().Be(1, "because a blank code names no provider");
		AssertNothingSent("because a refused option must not reach the site");
	}

}

[TestFixture]
[Property("Module", "Command")]
public class EnableSsoSecurityEventsCommandTests : SsoSecurityEventsCommandTestsBase<EnableSsoSecurityEventsOptions>
{

	[Test]
	[Description("Turns events on through EnableEvents with the code and the site address, once and without replay")]
	public void Execute_ShouldPostCodeAndSiteAddressOnce_WhenSiteAddressIsGiven() {
		// Arrange
		EnableSsoSecurityEventsCommand command = Resolve<EnableSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(new EnableSsoSecurityEventsOptions {
			Code = "identity-v3", SiteAddress = " https://site.example.com "
		});

		// Assert
		result.Should().Be(0, "because the site answered");
		SentUrl.Should().EndWith("/rest/SsoSecurityEventsSettingsService/EnableEvents",
			"because enabling is the EnableEvents method");
		SentBody.Should().Be(
			"{\"providerCode\":\"identity-v3\",\"siteAddress\":\"https://site.example.com\",\"hostConfirmed\":false}",
			"because the site address is passed trimmed and an absent SSF URL is left to the site's default");
		ApplicationClient.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), 1, Arg.Any<int>());
	}

	[Test]
	[Description("Without --site-address the member is left out, so the site keeps its stored address")]
	public void Execute_ShouldOmitSiteAddress_WhenItIsNotGiven() {
		// Arrange
		EnableSsoSecurityEventsCommand command = Resolve<EnableSsoSecurityEventsCommand>();

		// Act
		command.Execute(new EnableSsoSecurityEventsOptions {
			Code = "okta", SsfDiscoveryUrl = "https://ssf.example.com/.well-known/ssf-configuration",
			ConfirmOtherHost = true
		});

		// Assert
		SentBody.Should().Be(
			"{\"providerCode\":\"okta\",\"discoveryUrl\":\"https://ssf.example.com/.well-known/ssf-configuration\","
			+ "\"hostConfirmed\":true}",
			"because an absent address must not overwrite the stored one and the SSF URL and confirmation are sent");
	}

	[Test]
	[Description("A refused events address is the site's answer: printed with its detail, exit code 0")]
	public void Execute_ShouldPrintReasonAndDetail_WhenTheSiteRefusesTheAddress() {
		// Arrange
		Answer = "{\"reason\":\"SiteAddressRefused\",\"reasonDetail\":\"delivery_endpoint_private_host_forbidden\"}";
		EnableSsoSecurityEventsCommand command = Resolve<EnableSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(new EnableSsoSecurityEventsOptions { Code = "identity-v3" });

		// Assert
		result.Should().Be(0, "because a reason code is the site's answer, not a failed call");
		Logger.Received(1).WriteLine("reason: SiteAddressRefused");
		Logger.Received(1).WriteLine("reasonDetail: delivery_endpoint_private_host_forbidden");
	}

}

[TestFixture]
[Property("Module", "Command")]
public class DisableSsoSecurityEventsCommandTests : SsoSecurityEventsCommandTestsBase<DisableSsoSecurityEventsOptions>
{

	[Test]
	[Description("Turns events off through DisableEvents with the code only, once and without replay")]
	public void Execute_ShouldPostCodeToDisableEventsOnce_WhenCodeIsGiven() {
		// Arrange
		DisableSsoSecurityEventsCommand command = Resolve<DisableSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(new DisableSsoSecurityEventsOptions { Code = "identity-v3" });

		// Assert
		result.Should().Be(0, "because the site answered");
		SentUrl.Should().EndWith("/rest/SsoSecurityEventsSettingsService/DisableEvents",
			"because disabling is the DisableEvents method");
		SentBody.Should().Be("{\"providerCode\":\"identity-v3\"}", "because DisableEvents takes the code only");
		ApplicationClient.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), 1, Arg.Any<int>());
		ApplicationClient.DidNotReceiveWithAnyArgs().ExecutePostRequest(default, default, default, default, default);
	}

	[Test]
	[Description("A 404 on a call that changes the site is reported as not supported, like on a read")]
	public void Execute_ShouldReturnOneAndSayNotSupported_WhenAWriteFindsNoService() {
		// Arrange
		ApplicationClient
			.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Throws(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));
		DisableSsoSecurityEventsCommand command = Resolve<DisableSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(new DisableSsoSecurityEventsOptions { Code = "identity-v3" });

		// Assert
		result.Should().Be(1, "because the site has no such service");
		Logger.Received(1).WriteError(Arg.Is<string>(text => text.Contains("not supported")));
		Logger.DidNotReceiveWithAnyArgs().WriteWarning(default);
	}

	[Test]
	[Description("A failed call that changes the site warns that the change may already be applied")]
	public void Execute_ShouldWarnTheChangeMayBeApplied_WhenAWriteFailsInTransport() {
		// Arrange
		ApplicationClient
			.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(),
				Arg.Any<int>())
			.Throws(new TimeoutException("The operation has timed out."));
		DisableSsoSecurityEventsCommand command = Resolve<DisableSsoSecurityEventsCommand>();

		// Act
		int result = command.Execute(new DisableSsoSecurityEventsOptions { Code = "identity-v3" });

		// Assert
		result.Should().Be(1, "because the call itself failed");
		Logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("may have applied the change")
			&& text.Contains("get-sso-security-events")));
	}

}

[TestFixture]
[Property("Module", "Command")]
public class SendSsoSecurityTestEventCommandTests : SsoSecurityEventsCommandTestsBase<SendSsoSecurityTestEventOptions>
{

	[Test]
	[Description("Sends the wait to SendTestEvent and gives the request time to outlive the site's own wait")]
	public void Execute_ShouldSendWaitAndExtendTimeout_WhenWaitIsSixtySeconds() {
		// Arrange
		Answer = "{\"reason\":\"TestNotReceived\",\"testResult\":\"NotReceived\","
			+ "\"status\":{\"siteAddress\":\"https://site.example.com\"}}";
		SendSsoSecurityTestEventCommand command = Resolve<SendSsoSecurityTestEventCommand>();

		// Act
		int result = command.Execute(new SendSsoSecurityTestEventOptions {
			Code = "identity-v3", WaitSeconds = 60, TimeOut = 5_000
		});

		// Assert
		result.Should().Be(0, "because a test event that did not arrive is the site's answer, not a failed call");
		SentUrl.Should().EndWith("/rest/SsoSecurityEventsSettingsService/SendTestEvent",
			"because the test event is the SendTestEvent method");
		SentBody.Should().Be("{\"providerCode\":\"identity-v3\",\"waitSeconds\":60}",
			"because the site waits on its side for the given seconds");
		SentTimeout.Should().Be(90_000,
			"because a shorter --timeout is raised so the request outlives the 60 s wait and the answer after it");
		Logger.Received(1).WriteLine("reason: TestNotReceived");
		Logger.Received(1).WriteLine("testResult: NotReceived");
		Logger.Received(1).WriteLine("siteAddress: https://site.example.com");
	}

	[Test]
	[Description("A short wait keeps the command's own timeout")]
	public void Execute_ShouldKeepDefaultTimeout_WhenWaitIsZero() {
		// Arrange
		SendSsoSecurityTestEventCommand command = Resolve<SendSsoSecurityTestEventCommand>();

		// Act
		command.Execute(new SendSsoSecurityTestEventOptions { Code = "identity-v3" });

		// Assert
		SentBody.Should().Be("{\"providerCode\":\"identity-v3\",\"waitSeconds\":0}",
			"because the default is not to wait");
		SentTimeout.Should().Be(new SendSsoSecurityTestEventOptions().TimeOut,
			"because the command's own timeout already covers a call without a wait");
	}

	[TestCase(-1)]
	[TestCase(61)]
	[Description("A wait outside 0-60 seconds is refused before anything is sent")]
	public void Execute_ShouldRefuseWithoutCall_WhenWaitIsOutOfRange(int waitSeconds) {
		// Arrange
		SendSsoSecurityTestEventCommand command = Resolve<SendSsoSecurityTestEventCommand>();

		// Act
		int result = command.Execute(new SendSsoSecurityTestEventOptions {
			Code = "identity-v3", WaitSeconds = waitSeconds
		});

		// Assert
		result.Should().Be(1, "because the site accepts a wait of at most 60 seconds");
		AssertNothingSent("because a refused option must not reach the site");
		Logger.Received(1).WriteError("--wait must be between 0 and 60 seconds.");
	}

}

[TestFixture]
[Property("Module", "Command")]
public class SetSsoSecurityEventsAddressCommandTests
	: SsoSecurityEventsCommandTestsBase<SetSsoSecurityEventsAddressOptions>
{

	[Test]
	[Description("Sends the new address and the move flag to SetSiteAddress, once and without replay")]
	public void Execute_ShouldPostAddressAndMoveFlagOnce_WhenMoveStreamIsSet() {
		// Arrange
		Answer = "{\"reason\":\"ForeignAddress\"}";
		SetSsoSecurityEventsAddressCommand command = Resolve<SetSsoSecurityEventsAddressCommand>();

		// Act
		int result = command.Execute(new SetSsoSecurityEventsAddressOptions {
			Code = "identity-v3", SiteAddress = "https://new.example.com", MoveStream = true
		});

		// Assert
		result.Should().Be(0, "because a refused move is the site's answer and the rename goes on");
		SentUrl.Should().EndWith("/rest/SsoSecurityEventsSettingsService/SetSiteAddress",
			"because the address change is the SetSiteAddress method");
		SentBody.Should().Be(
			"{\"providerCode\":\"identity-v3\",\"siteAddress\":\"https://new.example.com\",\"moveStream\":true}",
			"because the site needs the new address and whether to move its own stream");
		ApplicationClient.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), 1, Arg.Any<int>());
		Logger.Received(1).WriteLine("reason: ForeignAddress");
	}

	[Test]
	[Description("A blank --site-address is refused before anything is sent")]
	public void Execute_ShouldRefuseWithoutCall_WhenSiteAddressIsBlank() {
		// Arrange
		SetSsoSecurityEventsAddressCommand command = Resolve<SetSsoSecurityEventsAddressCommand>();

		// Act
		int result = command.Execute(new SetSsoSecurityEventsAddressOptions { Code = "identity-v3", SiteAddress = " " });

		// Assert
		result.Should().Be(1, "because SetSiteAddress has nothing to store without an address");
		AssertNothingSent("because a refused option must not reach the site");
	}

}

[TestFixture]
[Property("Module", "Command")]
[NonParallelizable]
public class UpdateSsoProviderCommandTests : SsoSecurityEventsCommandTestsBase<UpdateSsoProviderOptions>
{

	private const string SecretFromFile = "rotated-secret-from-file";

	private string _secretFile;

	public override void Setup() {
		base.Setup();
		_secretFile = FileSystem.Path.Combine(FileSystem.Path.GetTempPath(), "sso-client.secret");
		FileSystem.AddFile(_secretFile, new MockFileData(SecretFromFile + Environment.NewLine));
	}

	[Test]
	[Description("Sends the client id and the secret read from the file to UpsertProvider, leaving out what is not given")]
	public void Execute_ShouldSendClientIdAndFileSecret_WhenRotatingTheSecret() {
		// Arrange
		Answer = "{\"reason\":\"\",\"isCreated\":false,\"providerId\":\"11111111-1111-1111-1111-111111111111\"}";
		UpdateSsoProviderCommand command = Resolve<UpdateSsoProviderCommand>();

		// Act
		int result = command.Execute(new UpdateSsoProviderOptions {
			Code = "identity-v3", OidcClientId = "site-client", OidcClientSecretFile = _secretFile, IsCreatioId = true
		});

		// Assert
		result.Should().Be(0, "because the site updated the provider");
		SentUrl.Should().EndWith("/rest/SsoSecurityEventsSettingsService/UpsertProvider",
			"because an update in place is the UpsertProvider method");
		SentBody.Should().Be(
			"{\"providerCode\":\"identity-v3\",\"clientId\":\"site-client\",\"clientSecret\":\"" + SecretFromFile
			+ "\",\"isCreatioId\":true}",
			"because the stored name, URL and discovery URL are kept when they are not given");
		ApplicationClient.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), 1, Arg.Any<int>());
		Logger.Received(1).WriteLine("isCreated: false");
	}

	[Test]
	[Description("The secret is sent to the site only and never printed")]
	public void Execute_ShouldNotPrintTheSecret_WhenASecretIsSent() {
		// Arrange
		UpdateSsoProviderCommand command = Resolve<UpdateSsoProviderCommand>();

		// Act
		command.Execute(new UpdateSsoProviderOptions {
			Code = "okta", OidcClientId = "client", OidcClientSecret = "plain-secret"
		});

		// Assert
		SentBody.Should().Contain("\"clientSecret\":\"plain-secret\"", "because the site needs the new secret");
		Logger.DidNotReceive().WriteLine(Arg.Is<string>(text => text.Contains("plain-secret")));
		Logger.DidNotReceive().WriteInfo(Arg.Is<string>(text => text.Contains("plain-secret")));
		Logger.DidNotReceive().WriteError(Arg.Is<string>(text => text.Contains("plain-secret")));
	}

	[Test]
	[Description("No secret option keeps the stored secret, even with CLIO_OIDC_CLIENT_SECRET set in the shell")]
	public void Execute_ShouldLeaveSecretOut_WhenNoSecretOptionIsGivenAndTheEnvVarIsSet() {
		// Arrange
		string previous = Environment.GetEnvironmentVariable("CLIO_OIDC_CLIENT_SECRET");
		Environment.SetEnvironmentVariable("CLIO_OIDC_CLIENT_SECRET", "secret-of-another-provider");
		UpdateSsoProviderCommand command = Resolve<UpdateSsoProviderCommand>();
		try {
			// Act
			int result = command.Execute(new UpdateSsoProviderOptions { Code = "okta", OidcClientId = "client" });

			// Assert
			result.Should().Be(0, "because the site answered");
			SentBody.Should().Be("{\"providerCode\":\"okta\",\"clientId\":\"client\",\"isCreatioId\":false}",
				"because a secret left in the environment must not replace this provider's stored secret");
		}
		finally {
			Environment.SetEnvironmentVariable("CLIO_OIDC_CLIENT_SECRET", previous);
		}
	}

	[Test]
	[Description("The code identity-v3 is always sent as Creatio ID, so a rotation without --creatio-id keeps its template")]
	public void Execute_ShouldSendCreatioId_WhenCodeIsIdentityV3WithoutTheFlag() {
		// Arrange
		UpdateSsoProviderCommand command = Resolve<UpdateSsoProviderCommand>();

		// Act
		command.Execute(new UpdateSsoProviderOptions {
			Code = "identity-v3", OidcClientId = "site-client", OidcClientSecret = "new-secret"
		});

		// Assert
		SentBody.Should().EndWith("\"isCreatioId\":true}",
			"because a provider made under identity-v3 without the flag would get the Custom OpenID template");
	}

	[Test]
	[Description("Both secret options at once are refused before anything is sent")]
	public void Execute_ShouldRefuseWithoutCall_WhenBothSecretOptionsAreGiven() {
		// Arrange
		UpdateSsoProviderCommand command = Resolve<UpdateSsoProviderCommand>();

		// Act
		int result = command.Execute(new UpdateSsoProviderOptions {
			Code = "okta", OidcClientId = "client", OidcClientSecret = "a", OidcClientSecretFile = _secretFile
		});

		// Assert
		result.Should().Be(1, "because the two secret sources are mutually exclusive");
		AssertNothingSent("because a refused option must not reach the site");
	}

	[Test]
	[Description("Creatio ID with another code is refused: the site would update identity-v3 instead")]
	public void Execute_ShouldRefuseWithoutCall_WhenCreatioIdHasAnotherCode() {
		// Arrange
		UpdateSsoProviderCommand command = Resolve<UpdateSsoProviderCommand>();

		// Act
		int result = command.Execute(new UpdateSsoProviderOptions {
			Code = "my-creatio-id", OidcClientId = "client", IsCreatioId = true
		});

		// Assert
		result.Should().Be(1, "because the site stores Creatio ID under identity-v3 whatever code it is sent");
		AssertNothingSent("because a refused option must not reach the site");
	}

	[Test]
	[Description("A missing secret file fails the command before anything is sent")]
	public void Execute_ShouldFailWithoutCall_WhenTheSecretFileIsMissing() {
		// Arrange
		UpdateSsoProviderCommand command = Resolve<UpdateSsoProviderCommand>();

		// Act
		int result = command.Execute(new UpdateSsoProviderOptions {
			Code = "okta", OidcClientId = "client",
			OidcClientSecretFile = FileSystem.Path.Combine(FileSystem.Path.GetTempPath(), "missing.secret")
		});

		// Assert
		result.Should().Be(1, "because the secret the caller named cannot be read");
		AssertNothingSent("because no request is sent without the secret the caller asked for");
	}

}

[TestFixture]
[Property("Module", "Command")]
public class RemoveSsoProviderCommandTests : SsoSecurityEventsCommandTestsBase<RemoveSsoProviderOptions>
{

	[Test]
	[Description("--local-only is sent as localOnly, so the site calls nothing at the provider (a copied site)")]
	public void Execute_ShouldSendLocalOnlyOnce_WhenLocalOnlyIsSet() {
		// Arrange
		Answer = "{\"reason\":\"\",\"streamDeleted\":false}";
		RemoveSsoProviderCommand command = Resolve<RemoveSsoProviderCommand>();

		// Act
		int result = command.Execute(new RemoveSsoProviderOptions { Code = "identity-v3", LocalOnly = true });

		// Assert
		result.Should().Be(0, "because the site removed the provider");
		SentUrl.Should().EndWith("/rest/SsoSecurityEventsSettingsService/RemoveProvider",
			"because removal is the RemoveProvider method");
		SentBody.Should().Be("{\"providerCode\":\"identity-v3\",\"localOnly\":true}",
			"because a copy must not touch the source's stream");
		ApplicationClient.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(),
			Arg.Any<int>(), 1, Arg.Any<int>());
		Logger.Received(1).WriteLine("streamDeleted: false");
	}

	[Test]
	[Description("Without --local-only the site deletes its own stream first")]
	public void Execute_ShouldSendLocalOnlyFalse_WhenLocalOnlyIsNotSet() {
		// Arrange
		RemoveSsoProviderCommand command = Resolve<RemoveSsoProviderCommand>();

		// Act
		command.Execute(new RemoveSsoProviderOptions { Code = "okta" });

		// Assert
		SentBody.Should().Be("{\"providerCode\":\"okta\",\"localOnly\":false}",
			"because an ordinary remove leaves no stream of the site at the provider");
	}

}
