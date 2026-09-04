using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.McpServer.Tools;
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

	[Test]
	[Description("An unresolvable environment is reported as a Configuration failure whose cause is the resolver's own actionable text, not as Unknown with 'no cause could be determined' and 'retry' - advice that makes an agent loop.")]
	public void CategorizeFailure_Should_Report_Configuration_For_An_Unresolvable_Environment() {
		// Arrange
		EnvironmentResolutionException exception = new("Environment 'ghost' is not registered.");

		// Act
		SysSettingFailure failure = SysSettingsCommand.CategorizeFailure(exception, Operation, CorrelationId);

		// Assert
		failure.Category.Should().Be(SysSettingErrorCategories.Configuration,
			because: "nothing was sent anywhere - the environment could not be resolved from local config");
		failure.Cause.Should().Be("Environment 'ghost' is not registered.",
			because: "the resolver's message is clio-local and is the actionable cause");
		failure.RecoveryAction.Should().Contain("list-environments",
			because: "'retry the operation' on an unregistered name loops an agent forever");
		failure.Error.Should().Be("Failed reading sys-setting.",
			because: "the headline message keeps the generic label so resolver text is not promoted into it");
	}

	[Test]
	[Description("CategorizeAndLog writes exactly one log line carrying the correlation ID it returns, so a caller-visible ID always finds something.")]
	public void CategorizeAndLog_Should_Write_One_Line_Carrying_The_Returned_Id() {
		// Arrange
		ILogger logger = Substitute.For<ILogger>();
		List<string> lines = [];
		logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => lines.Add(call.Arg<string>()));

		// Act
		SysSettingFailure failure = SysSettingsCommand.CategorizeAndLog(
			new UnauthorizedAccessException("denied"), Operation, logger,
			new OperationCorrelationIdProvider());

		// Assert
		lines.Should().ContainSingle(
			because: "an ID on a result that no log line mentions invites the caller to quote a token that finds nothing")
			.Which.Should().Contain(failure.CorrelationId,
				because: "the log line and the envelope must carry the SAME ID");
	}

	[Test]
	[Description("PR #1373 review: the CLI TryUpdateSysSetting(SysSettingsOptions) overload writes the classified diagnosis AND keeps the `is not updated.` line an operator or apply-environment-manifest parser reads, with exactly one correlation ID bridging the two.")]
	public void TryUpdateSysSetting_Cli_Should_Report_The_Diagnosis_And_Bridge_The_Second_Line() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.When(m => m.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()))
			.Do(_ => throw new UnauthorizedAccessException("denied"));
		ILogger logger = Substitute.For<ILogger>();
		List<string> errors = [];
		logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => errors.Add(call.Arg<string>()));
		SysSettingsCommand command = new(manager, logger, Substitute.For<IFileSystem>(),
			new OperationCorrelationIdProvider());
		SysSettingsOptions options = new() { Code = "UsrSetting", Value = "x", Type = "Text" };

		// Act
		command.TryUpdateSysSetting(options);

		// Assert
		errors.Should().HaveCount(2,
			because: "the classified diagnosis and the legacy `is not updated.` signal are two distinct lines, and knowledge/Command/refused-syssetting-update-is-only-visible-as-a-writeerror.md pins the second as the apply-environment-manifest flow's only failure signal");
		errors[0].Should().Contain("Authentication error updating sys-setting.",
			because: "the CLI path must report WHY the write did not land, not only that it did not");
		errors[0].Should().Contain("The environment rejected the credentials of the registered user.",
			because: "the classified cause has to reach the operator running this interactively");
		errors[0].Should().Contain("repair the registered profile",
			because: "the recovery action is the operator's next step");
		errors[1].Should().Contain("SysSettings with code: UsrSetting is not updated.",
			because: "the legacy signal the Maintainer flow parses must stay byte-compatible at its head");
		string[] ids = [.. errors
			.Select(line => Regex.Match(line, @"\(correlation-id: ([0-9a-f]+)\)"))
			.Where(match => match.Success)
			.Select(match => match.Groups[1].Value)];
		ids.Should().HaveCount(2,
			because: "the second line used to carry no diagnosis at all, so the one line a parser reads pointed at nothing");
		ids.Distinct().Should().ContainSingle(
			because: "exactly ONE correlation ID is minted per failure - two different IDs would send an operator looking for two records");
	}

	[Test]
	[Description("PR #1373 review: a NON-exception refusal (UpdateSysSetting returning false) carries the four envelope fields too - all-null is what the contract publishes as success, so an agent could not tell a real refusal from one.")]
	public void TryUpdateSysSetting_Should_Classify_A_NonException_Refusal() {
		// Arrange
		ISysSettingsManager manager = Substitute.For<ISysSettingsManager>();
		manager.UpdateSysSetting(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<string>()).Returns(false);
		ILogger logger = Substitute.For<ILogger>();
		List<string> errors = [];
		logger.When(l => l.WriteError(Arg.Any<string>())).Do(call => errors.Add(call.Arg<string>()));
		SysSettingsCommand command = new(manager, logger, Substitute.For<IFileSystem>(),
			new OperationCorrelationIdProvider());

		// Act
		SysSettingUpdateResult result = command.TryUpdateSysSetting(
			new UpdateSysSettingArgs("dev", "UsrSetting", "x") { ValueTypeName = "Text" });

		// Assert
		result.Success.Should().BeFalse(because: "the manager reported the write was not applied");
		result.ErrorCategory.Should().Be(SysSettingErrorCategories.ProviderFailure,
			because: "the request reached the environment and was refused there - null would read as success to an agent branching on the category");
		result.Cause.Should().Be(SysSettingFailureTexts.RefusedUpdateCause,
			because: "the cause has to be fixed local text, not the absence of any diagnosis");
		result.RecoveryAction.Should().Be(SysSettingFailureTexts.RefusedUpdateRecovery,
			because: "#1222 requires the envelope to name the next step on the non-exception path too");
		result.CorrelationId.Should().NotBeNullOrWhiteSpace(
			because: "a refusal that mints no ID leaves an operator with nothing to quote");
		errors.Should().ContainSingle(
			because: "a correlation ID the caller can quote must have a log line to find")
			.Which.Should().Contain(result.CorrelationId,
				because: "the log line and the envelope must carry the SAME ID whichever way the failure arrived");
	}
}
