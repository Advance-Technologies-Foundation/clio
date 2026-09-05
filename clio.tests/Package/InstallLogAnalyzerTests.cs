using Clio.Common.Responses;
using Clio.Package;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Package;

/// <summary>
/// GH-1299: the platform answers <c>InstallPackage</c> with <c>success:false</c> and the generic message
/// "Packages installation failed" for a run whose only problem was a schema skipped because it was
/// modified on the environment. These tests pin the classification that keeps such a run a success.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "Package")]
public sealed class InstallLogAnalyzerTests {

	private const string LocallyModifiedLog =
		"2026-09-05 10:12:31,101 [21] INFO  Terrasoft.Configuration - Start package installation\r\n"
		+ "2026-09-05 10:12:33,455 [21] WARN  Terrasoft.Configuration - Unable to install Schema \"UsrI1299Svc\""
		+ " into package \"UsrIssue1299\", because the element has been modified locally.\r\n"
		+ "2026-09-05 10:12:35,900 [21] INFO  Terrasoft.Configuration - Package installation finished\r\n";

	private const string CleanLog =
		"2026-09-05 10:20:01,001 [21] INFO  Terrasoft.Configuration - Start package installation\r\n"
		+ "2026-09-05 10:20:09,777 [21] INFO  Terrasoft.Configuration - Package installation finished\r\n";

	private static BaseResponse CreateResponse(string errorCode, string message) =>
		new() {
			Success = false,
			ErrorInfo = new ErrorInfo { ErrorCode = errorCode, Message = message }
		};

	[Test]
	[Description("Every log line reporting a locally modified schema is returned, including when several schemas were skipped in the same run.")]
	public void GetLocallyModifiedSchemaLines_ShouldReturnEveryReportedLine_WhenSeveralSchemasWereSkipped() {
		// Arrange
		string log = LocallyModifiedLog
			+ "2026-09-05 10:12:34,001 [21] WARN  Terrasoft.Configuration - Unable to install Schema"
			+ " \"UsrI1299Page\" into package \"UsrIssue1299\", because the element has been modified locally.\r\n";

		// Act
		var lines = InstallLogAnalyzer.GetLocallyModifiedSchemaLines(log);

		// Assert
		lines.Should().HaveCount(2,
			because: "both skipped schemas must be surfaced to the operator as warnings");
		lines.Should().OnlyContain(line => line.Contains("has been modified locally"),
			because: "only the locally-modified notices belong in this collection");
	}

	[Test]
	[Description("A log with no locally modified schema yields no warning lines, so a clean install prints nothing extra.")]
	public void GetLocallyModifiedSchemaLines_ShouldReturnEmpty_WhenNoSchemaWasSkipped() {
		// Act
		var lines = InstallLogAnalyzer.GetLocallyModifiedSchemaLines(CleanLog);

		// Assert
		lines.Should().BeEmpty(
			because: "a clean installation must not produce locally-modified warnings");
	}

	[Test]
	[Description("A missing or blank installation log is handled without throwing and reports no skipped schema.")]
	public void GetLocallyModifiedSchemaLines_ShouldReturnEmpty_WhenLogIsBlank() {
		// Act
		var fromNull = InstallLogAnalyzer.GetLocallyModifiedSchemaLines(null);
		var fromWhitespace = InstallLogAnalyzer.GetLocallyModifiedSchemaLines("   ");

		// Assert
		fromNull.Should().BeEmpty(because: "a null log carries no information and must not throw");
		fromWhitespace.Should().BeEmpty(because: "a blank log carries no information and must not throw");
	}

	[Test]
	[Description("Schema names are extracted from the platform message and repeated names are reported once, in the order they appear.")]
	public void GetLocallyModifiedSchemaNames_ShouldReturnDistinctNamesInOrder_WhenASchemaIsReportedTwice() {
		// Arrange
		string log = LocallyModifiedLog
			+ "2026-09-05 10:12:34,001 [21] WARN  Terrasoft.Configuration - Unable to install Schema"
			+ " \"UsrI1299Page\" into package \"UsrIssue1299\", because the element has been modified locally.\r\n"
			+ "2026-09-05 10:12:34,300 [21] WARN  Terrasoft.Configuration - Unable to install Schema"
			+ " \"UsrI1299Svc\" into package \"UsrIssue1299\", because the element has been modified locally.\r\n";

		// Act
		var names = InstallLogAnalyzer.GetLocallyModifiedSchemaNames(log);

		// Assert
		names.Should().Equal(new[] {"UsrI1299Svc", "UsrI1299Page"},
			because: "the summary must name each skipped schema once, in the order the platform reported it");
	}

	[Test]
	[Description("A run that reached the platform completion marker is recognized as completed.")]
	public void IsInstallationCompleted_ShouldReturnTrue_WhenCompletionMarkerIsPresent() {
		// Act
		bool completed = InstallLogAnalyzer.IsInstallationCompleted(LocallyModifiedLog);

		// Assert
		completed.Should().BeTrue(
			because: "the log ends with the platform's \"Package installation finished\" marker");
	}

	[Test]
	[Description("A run that stopped before the completion marker is not recognized as completed, so its failure is never downgraded.")]
	public void IsInstallationCompleted_ShouldReturnFalse_WhenCompletionMarkerIsAbsent() {
		// Arrange
		const string abortedLog = "Start package installation\r\nUnable to install Schema \"UsrI1299Svc\""
			+ " into package \"UsrIssue1299\", because the element has been modified locally.\r\n";

		// Act
		bool completed = InstallLogAnalyzer.IsInstallationCompleted(abortedLog);

		// Assert
		completed.Should().BeFalse(
			because: "without the completion marker the installation cannot be assumed to have finished");
	}

	[Test]
	[Description("The platform success message is detected regardless of the casing the platform used.")]
	public void IsSuccessMessagePresent_ShouldIgnoreCase_WhenLogUsesDifferentCasing() {
		// Act
		bool present = InstallLogAnalyzer.IsSuccessMessagePresent("INFO - Application Installed Successfully");

		// Assert
		present.Should().BeTrue(
			because: "the --fail-on-error log check must not depend on the platform's casing");
	}

	[Test]
	[Description("The platform's generic \"Packages installation failed\" message is recognized as carrying no specific reason.")]
	public void IsGenericInstallationFailure_ShouldReturnTrue_WhenResponseCarriesOnlyTheGenericMessage() {
		// Act
		bool generic = InstallLogAnalyzer.IsGenericInstallationFailure(
			CreateResponse("Exception", "Packages installation failed"));

		// Assert
		generic.Should().BeTrue(
			because: "this is the exact answer the platform gives when it only skipped a locally modified schema");
	}

	[Test]
	[Description("A trailing period on the generic message does not turn it into a specific reason.")]
	public void IsGenericInstallationFailure_ShouldReturnTrue_WhenGenericMessageEndsWithAPeriod() {
		// Act
		bool generic = InstallLogAnalyzer.IsGenericInstallationFailure(
			CreateResponse("Exception", "Packages installation failed."));

		// Assert
		generic.Should().BeTrue(
			because: "punctuation added by a future platform build must not silently restore the wrong exit code");
	}

	[Test]
	[Description("A failure response with no message at all is not the generic shape, so it is never downgraded.")]
	public void IsGenericInstallationFailure_ShouldReturnFalse_WhenResponseCarriesNoMessage() {
		// Act
		bool generic = InstallLogAnalyzer.IsGenericInstallationFailure(new BaseResponse { Success = false });

		// Assert
		generic.Should().BeFalse(
			because: "an empty errorInfo is what the platform sends for failures whose detail lives only in the log, such as an invalid archive");
	}

	[Test]
	[Description("A completed run whose failure response carries no message keeps its non-zero outcome.")]
	public void ShouldTreatAsSuccess_ShouldReturnFalse_WhenResponseCarriesNoMessage() {
		// Act
		bool treatAsSuccess = InstallLogAnalyzer.ShouldTreatAsSuccess(
			new BaseResponse { Success = false }, LocallyModifiedLog, false);

		// Assert
		treatAsSuccess.Should().BeFalse(
			because: "only the exact generic message was proven to mean \"nothing but a locally modified schema\"");
	}

	[Test]
	[Description("A response naming a concrete reason is not generic, so a real failure is never downgraded.")]
	public void IsGenericInstallationFailure_ShouldReturnFalse_WhenResponseNamesASpecificReason() {
		// Act
		bool generic = InstallLogAnalyzer.IsGenericInstallationFailure(
			CreateResponse("InvalidGZipArchiveException", "The package archive is invalid or corrupted."));

		// Assert
		generic.Should().BeFalse(
			because: "a named reason is a real failure and must keep the non-zero exit code");
	}

	[Test]
	[Description("A completed run whose only problem was a locally modified schema is treated as a success.")]
	public void ShouldTreatAsSuccess_ShouldReturnTrue_WhenCompletedRunOnlySkippedLocallyModifiedSchemas() {
		// Act
		bool treatAsSuccess = InstallLogAnalyzer.ShouldTreatAsSuccess(
			CreateResponse("Exception", "Packages installation failed"), LocallyModifiedLog, false);

		// Assert
		treatAsSuccess.Should().BeTrue(
			because: "the package was installed and the skipped schema is a warning, not a failure (GH-1299)");
	}

	[Test]
	[Description("--fail-on-error keeps the reported failure even when the only problem was a locally modified schema.")]
	public void ShouldTreatAsSuccess_ShouldReturnFalse_WhenFailOnErrorIsRequested() {
		// Act
		bool treatAsSuccess = InstallLogAnalyzer.ShouldTreatAsSuccess(
			CreateResponse("Exception", "Packages installation failed"), LocallyModifiedLog, true);

		// Assert
		treatAsSuccess.Should().BeFalse(
			because: "--fail-on-error is the strict mode for scripts and must not swallow anything the platform reported");
	}

	[Test]
	[Description("A run that never reached the completion marker keeps its reported failure.")]
	public void ShouldTreatAsSuccess_ShouldReturnFalse_WhenCompletionMarkerIsAbsent() {
		// Arrange
		const string abortedLog = "Unable to install Schema \"UsrI1299Svc\" into package \"UsrIssue1299\","
			+ " because the element has been modified locally.\r\n";

		// Act
		bool treatAsSuccess = InstallLogAnalyzer.ShouldTreatAsSuccess(
			CreateResponse("Exception", "Packages installation failed"), abortedLog, false);

		// Assert
		treatAsSuccess.Should().BeFalse(
			because: "an installation that did not finish must not be reported as successful");
	}

	[Test]
	[Description("A reported failure with no locally modified schema in the log keeps its non-zero outcome.")]
	public void ShouldTreatAsSuccess_ShouldReturnFalse_WhenNoSchemaWasSkipped() {
		// Act
		bool treatAsSuccess = InstallLogAnalyzer.ShouldTreatAsSuccess(
			CreateResponse("Exception", "Packages installation failed"), CleanLog, false);

		// Assert
		treatAsSuccess.Should().BeFalse(
			because: "without a locally-modified skip there is nothing that explains the reported failure away");
	}

	[Test]
	[Description("A failure naming a concrete reason keeps its non-zero outcome even when a schema was also skipped.")]
	public void ShouldTreatAsSuccess_ShouldReturnFalse_WhenResponseNamesASpecificReason() {
		// Act
		bool treatAsSuccess = InstallLogAnalyzer.ShouldTreatAsSuccess(
			CreateResponse("Exception", "Cannot compile configuration"), LocallyModifiedLog, false);

		// Assert
		treatAsSuccess.Should().BeFalse(
			because: "the platform named a real reason, which the locally-modified skip does not explain");
	}

	[Test]
	[Description("The failure description repeats the error code and message the service returned.")]
	public void DescribeFailure_ShouldReturnErrorCodeAndMessage_WhenResponseNamesAReason() {
		// Act
		string description = InstallLogAnalyzer.DescribeFailure(
			CreateResponse("InvalidGZipArchiveException", "The package archive is invalid."), CleanLog);

		// Assert
		description.Should().Be("InvalidGZipArchiveException: The package archive is invalid.",
			because: "the final line must say what actually failed instead of a bare \"Error\"");
	}

	[Test]
	[Description("When --fail-on-error failed only on the missing success message, the description says so instead of blaming the service.")]
	public void DescribeFailure_ShouldPointAtTheMissingSuccessMessage_WhenLogCheckFailed() {
		// Act
		string description = InstallLogAnalyzer.DescribeFailure(new BaseResponse { Success = true }, CleanLog, false);

		// Assert
		description.Should().Contain(InstallLogAnalyzer.SuccessMessage,
			because: "the operator has to learn which log requirement was not met");
		description.Should().Contain("--fail-on-error",
			because: "the requirement exists only because --fail-on-error was requested");
	}

	[Test]
	[Description("A failure with no message falls back to the tail of the installation log so the line is never empty.")]
	public void DescribeFailure_ShouldFallBackToTheLogTail_WhenResponseCarriesNoMessage() {
		// Act
		string description = InstallLogAnalyzer.DescribeFailure(new BaseResponse { Success = false }, CleanLog);

		// Assert
		description.Should().NotBeNullOrWhiteSpace(
			because: "the closing error line must always carry something actionable");
		description.Should().Contain("Package installation finished",
			because: "the log tail is the only evidence available when the service names no reason");
	}

	[Test]
	[Description("The log tail keeps the last non-empty lines and skips the blank ones.")]
	public void GetMeaningfulLogTail_ShouldReturnLastNonEmptyLines_WhenLogHasBlankLines() {
		// Arrange
		const string log = "first\r\n\r\nsecond\r\n   \r\nthird\r\nfourth\r\n\r\n";

		// Act
		string tail = InstallLogAnalyzer.GetMeaningfulLogTail(log, 2);

		// Assert
		tail.Should().Be("third | fourth",
			because: "blank lines carry no information and would push the useful lines out of the tail");
	}

	[Test]
	[Description("A blank log produces an empty tail rather than throwing.")]
	public void GetMeaningfulLogTail_ShouldReturnEmpty_WhenLogIsBlank() {
		// Act
		string tail = InstallLogAnalyzer.GetMeaningfulLogTail("   ");

		// Assert
		tail.Should().BeEmpty(because: "there is nothing to quote from a blank log");
	}

	[Test]
	[Description("A failure description with a message but no error code repeats the message alone, with no stray separator.")]
	public void DescribeFailure_ShouldReturnTheMessageAlone_WhenResponseHasNoErrorCode() {
		// Act
		string description = InstallLogAnalyzer.DescribeFailure(CreateResponse(null, "Cannot compile configuration"),
			CleanLog);

		// Assert
		description.Should().Be("Cannot compile configuration",
			because: "an absent error code must not produce a line that starts with a colon");
	}

	[Test]
	[Description("A log without the platform success message is reported as missing it, which is what the --fail-on-error check relies on.")]
	public void IsSuccessMessagePresent_ShouldReturnFalse_WhenLogLacksTheMessage() {
		// Act
		bool present = InstallLogAnalyzer.IsSuccessMessagePresent(CleanLog);

		// Assert
		present.Should().BeFalse(
			because: "the fixture log finishes the installation but never claims overall success");
	}

	[Test]
	[Description("Asking for a non-positive number of tail lines yields an empty tail instead of the whole log.")]
	public void GetMeaningfulLogTail_ShouldReturnEmpty_WhenLineCountIsNotPositive() {
		// Act
		string tail = InstallLogAnalyzer.GetMeaningfulLogTail(CleanLog, 0);

		// Assert
		tail.Should().BeEmpty(because: "a caller asking for no lines must not be handed the entire log");
	}

	[Test]
	[Description("A locally-modified notice the platform phrased without a quoted element name still counts as a skip but contributes no name.")]
	public void GetLocallyModifiedSchemaNames_ShouldReturnEmpty_WhenTheLineCarriesNoQuotedName() {
		// Arrange
		const string log = "The element has been modified locally and was left unchanged.\r\n";

		// Act
		var lines = InstallLogAnalyzer.GetLocallyModifiedSchemaLines(log);
		var names = InstallLogAnalyzer.GetLocallyModifiedSchemaNames(log);

		// Assert
		lines.Should().HaveCount(1, because: "the skip itself is still reported to the operator");
		names.Should().BeEmpty(
			because: "no name can be extracted, so the summary falls back to pointing at the messages above");
	}

}
