using System;
using System.Collections.Generic;
using System.Net;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// PR #1374 review: what the CONSOLE gets, as distinct from what the MCP envelope gets.
/// </summary>
/// <remarks>
/// Issue #1333 neutralizes the one kind of server text it lets through, and for a field a model reads
/// that neutralization includes the <c>[untrusted-source-text …]</c> fence. A terminal is not a model's
/// context window: there the fence has no audience and reads as clio malfunctioning. So the failure
/// carries both renderings and each sink picks - and these tests are what keeps the console sink from
/// silently drifting back onto the agent rendering.
/// <para>
/// The second half of the fixture covers the global CLI renderer,
/// <c>ExceptionReadableMessageExtension</c>: its server-detail arm runs for ~20 commands, so replacing
/// the outer exception outright, or preempting the WebException enrichment, is a regression far outside
/// sys-settings.
/// </para>
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "Common")]
public sealed class ConsoleFacingDiagnosticsTests {

	private const string FenceMarker = "untrusted-source-text";

	private const string PlatformValidationProse = "Column 'Name' is required.";

	private static IDataProvider BuildFailing(string errorMessage) {
		IItemsResponse response = Substitute.For<IItemsResponse>();
		response.Success.Returns(false);
		response.ErrorMessage.Returns(errorMessage);
		response.Items.Returns(new List<Dictionary<string, object>>());
		IDataProvider inner = Substitute.For<IDataProvider>();
		inner.GetItems(Arg.Any<ISelectQuery>()).Returns(response);
		return new ClassifyingDataProvider(inner);
	}

	private static ISelectQuery BuildSelectQuery(string schemaName) {
		ISelectQuery query = Substitute.For<ISelectQuery>();
		query.RootSchemaName.Returns(schemaName);
		return query;
	}

	[Test]
	[Description("A provider failure carries both renderings: Message keeps the agent fence the MCP envelope needs, ConsoleMessage carries the same diagnosis without it")]
	public void ProviderFailure_Should_Carry_Both_The_Fenced_And_The_Console_Rendering() {
		// Arrange
		IDataProvider sut = BuildFailing(PlatformValidationProse);

		// Act
		Action act = () => sut.GetItems(BuildSelectQuery("SysSettings"));

		// Assert
		DataProviderFailureException exception =
			act.Should().Throw<DataProviderFailureException>().Which;
		exception.Message.Should().Contain(FenceMarker,
			because: "the MCP envelope is read by a model, which has to be told this text is observed "
			+ "data rather than an instruction");
		exception.ConsoleMessage.Should().NotContain(FenceMarker,
			because: "nobody at a terminal is the audience for the fence - it reads as clio malfunctioning");
		exception.ConsoleMessage.Should().Contain(PlatformValidationProse,
			because: "the platform's own validation prose IS the diagnosis; dropping it destroys it");
	}

	[Test]
	[Description("The CLI renderer prints the unfenced rendering at default verbosity: this is the line an operator reads")]
	public void ReadableMessage_NonDebug_Should_Not_Carry_The_Agent_Fence() {
		// Arrange
		IDataProvider provider = BuildFailing(PlatformValidationProse);
		Action act = () => provider.GetItems(BuildSelectQuery("SysSettings"));
		DataProviderFailureException exception =
			act.Should().Throw<DataProviderFailureException>().Which;

		// Act
		string rendered = exception.GetReadableMessageException();

		// Assert
		rendered.Should().NotContain(FenceMarker,
			because: "the fence belongs to the agent rendering only");
		rendered.Should().Contain(PlatformValidationProse,
			because: "the operator still needs to know what the platform refused");
	}

	[Test]
	[Description("`clio set-syssetting` prints an ordinary platform validation failure with no fence on the console line")]
	public void SetSysSetting_Console_Line_Should_Not_Carry_The_Agent_Fence() {
		// Arrange
		ServerReportedFailureText described = ServerReportedFailureText.Describe(PlatformValidationProse);

		// Assert
		described.Cause.Should().Contain(FenceMarker,
			because: "the agent rendering is unchanged - the MCP envelope still reads Cause");
		described.ConsoleCause.Should().NotContain(FenceMarker,
			because: "the console rendering is what _logger.WriteError prints for set-syssetting");
		described.ConsoleCause.Should().Contain(PlatformValidationProse,
			because: "the diagnosis survives in both renderings; only the framing differs");
		described.ComposeConsoleMessage("updating sys-setting").Should()
			.NotContain(FenceMarker, because: "the composed console line must be fence-free end to end");
	}

	[Test]
	[Description("Server-authored text is still scrubbed, flattened and capped on the console rendering - dropping the fence must not drop the neutralization")]
	public void Console_Rendering_Should_Still_Neutralize_The_Text() {
		// Arrange
		const string hostile =
			"Column 'Name' is required. token=eyJhbGciOiJIUzI1NiJ9.abcdefgh.ijklmnop "
			+ "see https://internal.example.com/secret \u2028 contact admin@example.com";

		// Act
		string console = ServerReportedFailureText.Describe(hostile).ConsoleCause;

		// Assert
		console.Should().NotContain(FenceMarker, because: "no fence on the console rendering");
		foreach (string secret in (string[])["eyJhbGciOiJIUzI1NiJ9", "admin@example.com",
				"https://internal.example.com/secret", "\u2028"]) {
			console.Should().NotContain(secret,
				because: "the text is still server-authored, so a token, an address, a URI and a forged "
				+ "line break are removed exactly as they are for the agent rendering");
		}
	}

	[Test]
	[Description("The classified log line does not print one composed diagnostic twice - which, where that diagnostic is fenced, meant two begin/end pairs on a single line")]
	public void FailureLogLine_Should_Not_Repeat_The_Diagnosis() {
		// Arrange
		DataProviderFailureException exception = new(
			ServerReportedFailureText.Describe(PlatformValidationProse)
				.ComposeMessage("reading sys-setting"));

		// Act
		string line = SysSettingsCommand.DescribeFailureForLog(
			SysSettingsCommand.CategorizeFailure(exception, "reading sys-setting", "abc123"));

		// Assert
		line.Split($"[{FenceMarker} begin]").Length.Should().Be(2,
			because: "the Error and the Cause are the same string on this arm, so the fenced excerpt "
			+ "must appear once - the line used to carry two begin/end pairs");
		line.Should().Contain("Action: ", because: "the recovery action is still on the line");
		line.Should().Contain("(correlation-id: abc123)", because: "the correlation ID is still last");
	}

	[Test]
	[Description("The CLI renderer keeps the outer exception's context instead of replacing it with the carrier's message: without it a command that wraps a provider failure to say WHICH operation failed printed only the inner diagnosis")]
	public void ReadableMessage_Should_Keep_The_Outer_Context() {
		// Arrange
		DataProviderFailureException carrier = new("Failed reading sys-setting: the provider refused.");
		InvalidOperationException outer = new("Could not install the package 'UsrPkg'.", carrier);

		// Act
		string rendered = outer.GetReadableMessageException();

		// Assert
		rendered.Should().Contain("Could not install the package 'UsrPkg'.",
			because: "the outer exception's type and message used to vanish with no trace at all");
		rendered.Should().Contain("the provider refused.",
			because: "the carrier's own diagnosis is still what names the cause");
	}

	[Test]
	[Description("A carrier whose chain holds a 401 WebException still surfaces the HTTP status at default verbosity: the enrichment arm's own comment says this is what lets CI tell auth apart from connect failures")]
	public void ReadableMessage_Should_Keep_The_WebException_Enrichment() {
		// Arrange
		WebException webException = new("The remote server returned an error: (401) Unauthorized.",
			null, WebExceptionStatus.ProtocolError, null);
		SessionRejectedException carrier = new(
			"Authentication failed while reading sys-setting: Creatio rejected the credentials.",
			serverDetail: "5: Authentication failed.", innerException: webException);

		// Act
		string rendered = carrier.GetReadableMessageException();

		// Assert
		rendered.Should().Contain("WebException: ProtocolError",
			because: "the new carrier arm precedes the enrichment arm, so it has to contribute the same "
			+ "structured status itself or the 401-vs-connect signal is lost from the non-debug line");
		rendered.Should().Contain("Creatio rejected the credentials.",
			because: "the fixed local diagnostic is still the headline");
	}

	[Test]
	[Description("At debug verbosity the render is not narrower than ToString(): the outer's type and message and every inner above the carrier are present")]
	public void ReadableMessage_Debug_Should_Not_Drop_The_Outer_Chain() {
		// Arrange
		DataProviderFailureException carrier = new("Failed reading sys-setting: the provider refused.",
			serverDetail: PlatformValidationProse);
		InvalidOperationException middle = new("Package installation aborted.", carrier);
		AggregateException outer = new(middle);

		// Act
		string rendered = outer.GetReadableMessageException(debug: true);

		// Assert
		rendered.Should().Contain("AggregateException",
			because: "the outer's own type used to be dropped entirely at debug verbosity");
		rendered.Should().Contain("Package installation aborted.",
			because: "an inner ABOVE the carrier is part of the diagnosis and used to vanish");
		rendered.Should().Contain("DataProviderFailureException",
			because: "the carrier is named where it sits in the chain");
		rendered.Should().Contain("server detail:",
			because: "the excerpt is the one thing --debug is turned on for");
	}

	[Test]
	[Description("A proven session rejection reaches Authentication through the PUBLIC read path, not only through CategorizeFailure: the fix is positional, so nothing but an end-to-end assertion pins it")]
	public void TryGetSysSetting_Should_Report_Authentication_For_A_Rejected_Session() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("SslCertificateThumbprint")
			.Returns(_ => throw new SessionRejectedException(
				"Authentication failed while reading sys-setting 'SslCertificateThumbprint': "
				+ "The password for the registered user has expired.",
				"5: Your password has expired."));
		SysSettingsCommand command = new(manager, Substitute.For<ILogger>(),
			Substitute.For<IFileSystem>(), new OperationCorrelationIdProvider());

		// Act
		SysSettingGetResult result =
			command.TryGetSysSetting(new GetSysSettingArgs("local", "SslCertificateThumbprint"));

		// Assert
		result.Success.Should().BeFalse(because: "the environment rejected the session");
		result.ErrorCategory.Should().Be(SysSettingErrorCategories.Authentication,
			because: "the user-visible defect was `get-sys-setting SslCertificateThumbprint` reporting "
			+ "Network: the operand name matched the TLS-prose rule through the composed message, and only "
			+ "a test that drives the real entry point catches a change to the label composition or to the "
			+ "order of the arms");
		result.RecoveryAction.Should().Be(
			"Verify the environment credentials (for an expired password, repair the registered profile) and retry.",
			because: "the recovery action is what tells the operator which repair to attempt");
		result.CorrelationId.Should().NotBeNullOrWhiteSpace(
			because: "the correlation ID is the bridge to the debug line carrying the server excerpt");
		result.Error.Should().NotContain("Your password has expired",
			because: "the server excerpt never reaches a caller-visible field (issue #1333)");
	}
}
