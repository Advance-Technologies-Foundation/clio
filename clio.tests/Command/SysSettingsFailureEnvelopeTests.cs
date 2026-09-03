using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using Clio.Command;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

/// <summary>
/// Issue #1329: the classified cause, the recovery action and the correlation ID must travel on the
/// failure envelope instead of being discarded, while the legacy <c>error</c> text stays byte-identical.
/// </summary>
[TestFixture]
[Property("Module", "Command")]
[Category("Unit")]
public sealed class SysSettingsFailureEnvelopeTests {

	private const string Operation = "reading sys-setting";
	private const string CorrelationId = "abc123def456";

	[Test]
	[Description("A rejected credential is categorized as Authentication and carries the fixed cause, the fixed recovery action and the supplied correlation ID.")]
	public void CategorizeFailure_Should_Report_Authentication_With_Cause_And_Recovery() {
		// Arrange
		UnauthorizedAccessException exception = new("denied");

		// Act
		SysSettingFailure failure = SysSettingsCommand.CategorizeFailure(exception, Operation, CorrelationId);

		// Assert
		failure.Category.Should().Be(SysSettingErrorCategories.Authentication,
			because: "an agent branches on the category, so a credential rejection must be named as one");
		failure.Error.Should().Be("Authentication error reading sys-setting.",
			because: "the legacy message is pinned by the existing MCP envelope and its tests");
		failure.Cause.Should().Be("The environment rejected the credentials of the registered user.",
			because: "the cause the classifier already knew used to be discarded (issue #1329)");
		failure.RecoveryAction.Should().Contain("repair the registered profile",
			because: "the envelope has to name the operator's next step, not only the failure");
		failure.CorrelationId.Should().Be(CorrelationId,
			because: "the envelope must carry the ID that finds the matching log line");
	}

	[Test]
	[Description("A refused connection is categorized as Network with the network cause and recovery action rather than as a credential failure.")]
	public void CategorizeFailure_Should_Report_Network_For_A_Refused_Connection() {
		// Arrange
		SocketException exception = new((int)SocketError.ConnectionRefused);

		// Act
		SysSettingFailure failure = SysSettingsCommand.CategorizeFailure(exception, Operation, CorrelationId);

		// Assert
		failure.Category.Should().Be(SysSettingErrorCategories.Network,
			because: "a transport fault must not be reported as rejected credentials");
		failure.Error.Should().Be("Network error reading sys-setting.",
			because: "the legacy message for a transport fault is unchanged");
		failure.Cause.Should().Be("The environment could not be reached.",
			because: "the cause must say what happened without repeating server-controlled prose");
	}

	[Test]
	[Description("A DataProviderFailureException is categorized as ProviderFailure and its locally composed message becomes the cause, because that message is the only diagnosis available.")]
	public void CategorizeFailure_Should_Report_ProviderFailure_With_The_Composed_Message() {
		// Arrange
		DataProviderFailureException exception = new(
			"Failed reading records from entity schema 'SysSettings': the environment answered with a non-JSON page.");

		// Act
		SysSettingFailure failure = SysSettingsCommand.CategorizeFailure(exception, Operation, CorrelationId);

		// Assert
		failure.Category.Should().Be(SysSettingErrorCategories.ProviderFailure,
			because: "the provider reported an unsuccessful response, which is a distinct class from a transport fault");
		failure.Error.Should().Be(exception.Message,
			because: "the composed diagnostic was already surfaced as the error and stays there");
		failure.Cause.Should().Be(exception.Message,
			because: "ClassifyingDataProvider composes this text locally, so it is the actionable cause");
	}

	[Test]
	[Description("An argument rejection is categorized as Validation, so a caller can tell a bad request from an unreachable environment.")]
	public void CategorizeFailure_Should_Report_Validation_For_An_Argument_Rejection() {
		// Arrange
		ArgumentException exception = new("code is required.");

		// Act
		SysSettingFailure failure = SysSettingsCommand.CategorizeFailure(exception, Operation, CorrelationId);

		// Assert
		failure.Category.Should().Be(SysSettingErrorCategories.Validation,
			because: "clio refused the request before it reached the environment");
		failure.Error.Should().Be("code is required.",
			because: "an argument message is its own diagnosis and was already surfaced verbatim");
	}

	[Test]
	[Description("A failure that matches no arm is categorized as Unknown with the fixed generic cause instead of an empty envelope.")]
	public void CategorizeFailure_Should_Report_Unknown_For_An_Unmatched_Failure() {
		// Arrange
		NotSupportedException exception = new("unexpected");

		// Act
		SysSettingFailure failure = SysSettingsCommand.CategorizeFailure(exception, Operation, CorrelationId);

		// Assert
		failure.Category.Should().Be(SysSettingErrorCategories.Unknown,
			because: "an unclassifiable failure must still declare a category rather than leave the field null");
		failure.Error.Should().Be("Failed reading sys-setting.",
			because: "the legacy generic message is pinned by the existing tests");
		failure.Cause.Should().Be("The operation failed and no cause could be determined from the failure.",
			because: "an empty cause leaves the caller with nothing to act on");
	}

	[Test]
	[Description("The legacy CategorizeError overload still returns exactly the message the structured classification produces, so there is one classification and not two.")]
	public void CategorizeError_Should_Return_The_Structured_Errors_Message() {
		// Arrange
		HttpRequestException exception = new("boom", null, HttpStatusCode.Unauthorized);

		// Act
		string message = SysSettingsCommand.CategorizeError(exception, Operation);
		SysSettingFailure failure = SysSettingsCommand.CategorizeFailure(exception, Operation, CorrelationId);

		// Assert
		message.Should().Be(failure.Error,
			because: "the overload delegates, so the two answers cannot drift apart");
	}

	[Test]
	[Description("A failed read logs the failure with the SAME correlation ID it puts on the result, so an operator can find the log line the envelope refers to.")]
	public void TryGetSysSetting_Should_Log_The_Same_Correlation_Id_It_Returns() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.GetAllUsersDefaultWithType("MaxFileSize")
			.Returns(_ => throw new UnauthorizedAccessException("denied"));
		ILogger logger = Substitute.For<ILogger>();
		string loggedLine = null;
		logger.When(l => l.WriteError(Arg.Any<string>()))
			.Do(call => loggedLine = call.Arg<string>());
		SysSettingsCommand command = new(manager, logger, Substitute.For<IFileSystem>(),
			new OperationCorrelationIdProvider());

		// Act
		SysSettingGetResult result = command.TryGetSysSetting(new GetSysSettingArgs("local", "MaxFileSize"));

		// Assert
		result.Success.Should().BeFalse(
			because: "a rejected session is a failure, not an empty value");
		result.CorrelationId.Should().NotBeNullOrWhiteSpace(
			because: "issue #1329 requires a correlation ID on the failure envelope");
		loggedLine.Should().NotBeNull(
			because: "the envelope's correlation ID is useless without a log line carrying it");
		loggedLine.Should().Contain(result.CorrelationId,
			because: "the ID is the bridge between the envelope the caller sees and the log the operator reads");
		result.ErrorCategory.Should().Be(SysSettingErrorCategories.Authentication,
			because: "the classified category must reach the result, not only the log");
		result.Error.Should().Be("Authentication error reading sys-setting.",
			because: "the legacy error text is unchanged by the new fields");
	}

	[Test]
	[Description("Two operations get two different correlation IDs, so one failure cannot be mistaken for another in the log.")]
	public void OperationCorrelationIdProvider_Should_Issue_A_Distinct_Id_Per_Call() {
		// Arrange
		IOperationCorrelationIdProvider provider = new OperationCorrelationIdProvider();

		// Act
		string first = provider.New();
		string second = provider.New();

		// Assert
		first.Should().HaveLength(12,
			because: "the format matches the MCP generic path's correlation ID so one grep finds either");
		second.Should().NotBe(first,
			because: "an ID reused across operations cannot identify one of them");
	}
}
