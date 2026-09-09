using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command;
using Clio.Command.ProcessModel;
using Clio.Common;
using ErrorOr;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command;

[TestFixture]
[Category("Unit")]
[Property("Module", "Command")]
public sealed class CreateBusinessProcessCommandTests {
	private const string SampleDescriptor =
		"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[],\"flows\":[]}";

	private ICreateBusinessProcessService _createBusinessProcessService;
	private IProcessDescriber _processDescriber;
	private ILogger _logger;
	private CreateBusinessProcessCommand _command;

	[SetUp]
	public void Setup() {
		_createBusinessProcessService = Substitute.For<ICreateBusinessProcessService>();
		// The command reads the saved process back to catch a server that discarded an email block while
		// answering success. These descriptors carry no email block, so the substitute is never consulted.
		_processDescriber = Substitute.For<IProcessDescriber>();
		_logger = Substitute.For<ILogger>();
		_command = new CreateBusinessProcessCommand(_createBusinessProcessService, _processDescriber, _logger);
	}

	[TearDown]
	public void TearDown() {
		_createBusinessProcessService.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static CreateBusinessProcessResult BuildResult() =>
		new("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55");

	[Test]
	[Category("Unit")]
	[Description("Warns when the saved element has no record filter: it applies the permission change to EVERY record of its object - silently, on an element with no output parameters, and nothing on the platform refuses that state.")]
	public void Execute_ShouldWarn_WhenTheElementHasNoRecordFilter() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = "{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[{\"name\":\"Grant\","
			+ "\"type\":\"changeAccessRights\",\"accessRights\":{\"object\":\"Order\"}}],\"flows\":[]}"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		DescribedElement element = new() {
			Name = "Grant",
			AdditionalData = new Dictionary<string, JsonElement> {
				["accessRights"] = JsonDocument.Parse("{\"object\":\"Order\"}").RootElement.Clone()
			}
		};
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>())
			.Returns(new DescribeProcessResult { Elements = [element] });
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		warnings.Should().ContainSingle(message => message.Contains("NO record filter"),
			because: "the block landed but the element is now unbounded - it will act on EVERY record of the "
				+ "object - and on an element with no output "
				+ "parameters that is indistinguishable from success");
	}

	[Test]
	[Category("Unit")]
	[Description("Writes every server warning out as a WARNING. A warning here is a caveat on a SUCCESSFUL build: an outcome that applied and is not what the caller would assume, so one deserialized and then dropped is the same defect as one never sent. NOTE ON THE ORIGINAL DESCRIPTION, kept because the correction is the useful part: it claimed 'deleting the loop that writes them left the whole suite green - measured, 8 273 passed against the mutation'. That measurement was real and its conclusion was wrong. This test and its negative twin below were written with [Description] but WITHOUT [Test], so neither had ever run - the mutation survived because nothing was executing, not because the coverage was weak. Attributes added; the pair now actually guards the channel.")]
	public void Execute_ShouldWriteWarnings_WhenTheServerReportsThem() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = SampleDescriptor,
			PackageName = "MyApp"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(new CreateBusinessProcessResult("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55",
				new[] { "Connection 'OmniChat' is not registered", "Connection 'Account' was CLEARED" }));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a warning is a caveat on a SUCCESSFUL build, not a failure");
		_logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("OmniChat")));
		_logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("CLEARED")));
	}

	[Test]
	[Category("Unit")]
	[Description("Says so when the read-back does not contain the element at all: the check did not happen, which is not the same as the configuration having landed.")]
	public void Execute_ShouldWarn_WhenTheElementIsAbsentFromTheReadBack() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = "{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[{\"name\":\"Grant\","
			+ "\"type\":\"changeAccessRights\",\"accessRights\":{\"object\":\"Order\"}}],\"flows\":[]}"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>()).Returns(
			new DescribeProcessResult { Elements = [new DescribedElement { Name = "SomethingElse" }] });
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		warnings.Should().ContainSingle(message => message.Contains("Could not verify"),
			because: "an element the read-back never returned cannot prove a drop, but it cannot prove a "
				+ "success either, and this guard must never let the second read as the first");
	}

	[Test]
	[Category("Unit")]
	[Description("Warns when the saved process carries no accessRights block: a CrtProcessBuilder that predates the Change access rights element discards the block and still answers success, and the element has no output parameters, so this read-back is the only signal that a grant or revoke did not land.")]
	public void Execute_ShouldWarn_WhenTheAccessRightsBlockWasDiscarded() {
		// Arrange
		const string descriptor =
			"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[{\"name\":\"Grant\","
			+ "\"type\":\"changeAccessRights\",\"accessRights\":{\"object\":\"Order\"}}],\"flows\":[]}";
		CreateBusinessProcessOptions options = new() { Environment = "sandbox", DescriptorJson = descriptor };
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>()).Returns(
			new DescribeProcessResult { Elements = [new DescribedElement { Name = "Grant" }] });
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "the build itself succeeded; a discarded block is reported as a warning, not a failure");
		warnings.Should().ContainSingle(message =>
			message.Contains("'Grant'") && message.Contains("accessRights"),
			because: "the caller must be told the element is unconfigured, or an unapplied revoke passes as applied");
	}

	[Test]
	[Category("Unit")]
	[Description("Warns that the verification could not be performed when the read-back fails, instead of reporting the same silence as a verified success.")]
	public void Execute_ShouldWarn_WhenTheAccessRightsReadBackCannotBeObtained() {
		// Arrange
		const string descriptor =
			"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[{\"name\":\"Grant\","
			+ "\"type\":\"changeAccessRights\",\"accessRights\":{\"object\":\"Order\"}}],\"flows\":[]}";
		CreateBusinessProcessOptions options = new() { Environment = "sandbox", DescriptorJson = descriptor };
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>())
			.Returns(Error.Failure("Describe.Failed", "the environment did not answer"));
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "an unreadable description is not evidence of a drop, so it must not fail the command");
		warnings.Should().ContainSingle(message => message.Contains("Could not verify"),
			because: "reporting 'verified' and 'could not check' identically would let an unapplied revoke pass "
				+ "as applied on an element that reports nothing at run time");
	}

	[Test]
	[Category("Unit")]
	[Description("Reads the saved process back at most once even when the payload carries both an email and an accessRights block, so the success path does not pay two identical round trips.")]
	public void Execute_ShouldDescribeOnce_WhenThePayloadCarriesBothBlocks() {
		// Arrange
		const string descriptor =
			"{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":["
			+ "{\"name\":\"Grant\",\"type\":\"changeAccessRights\",\"accessRights\":{\"object\":\"Order\"}},"
			+ "{\"name\":\"Mail\",\"type\":\"sendEmail\",\"email\":{\"mode\":\"auto\"}}],\"flows\":[]}";
		CreateBusinessProcessOptions options = new() { Environment = "sandbox", DescriptorJson = descriptor };
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>()).Returns(
			new DescribeProcessResult { Elements = [] });

		// Act
		_command.Execute(options);

		// Assert
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), null, false);
	}

	[Test]
	[Category("Unit")]
	[Description("Writes no warning when the server reported none, so an empty channel cannot train a reader to ignore it. Pairs with the test above: without this one, a loop that warned unconditionally would also pass. This is also the only create-side guard against a warning channel that fires SPURIOUSLY, which is the failure mode every read-back guard on this command can introduce - so it running matters more than its own subject suggests. It had no [Test] attribute until the flow-label review found it.")]
	public void Execute_ShouldNotWriteAnyWarning_WhenTheServerReportsNone() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = SampleDescriptor,
			PackageName = "MyApp"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a build with no caveats is an ordinary success");
		_logger.DidNotReceiveWithAnyArgs().WriteWarning(default!);
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the inline descriptor JSON and package override to the build service and logs the created schema on success.")]
	public void Execute_ShouldMapInlineDescriptorToService_WhenDescriptorJsonProvided() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = SampleDescriptor,
			PackageName = "MyApp"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a successful build should return the standard success exit code");
		_createBusinessProcessService.Received(1).BuildProcess(
			"sandbox",
			Arg.Is<CreateBusinessProcessRequest>(request =>
				request.DescriptorJson == SampleDescriptor &&
				request.PackageNameOverride == "MyApp"));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("UsrSampleProcess")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs guidance when no inline descriptor JSON is provided.")]
	public void Execute_ShouldFail_WhenNoDescriptorProvided() {
		// Act
		int result = _command.Execute(new CreateBusinessProcessOptions { Environment = "sandbox" });

		// Assert
		result.Should().Be(1,
			because: "the command requires an inline descriptor to build a process");
		_createBusinessProcessService.DidNotReceiveWithAnyArgs().BuildProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("A process descriptor is required.")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs a readable error when the call omits environment-name.")]
	public void Execute_ShouldFail_WhenEnvironmentIsMissing() {
		// Act
		int result = _command.Execute(new CreateBusinessProcessOptions { DescriptorJson = SampleDescriptor });

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when the environment is missing");
		_createBusinessProcessService.DidNotReceiveWithAnyArgs().BuildProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs the service exception message when the build service throws.")]
	public void Execute_ShouldFail_WhenServiceThrows() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = SampleDescriptor
		};
		_createBusinessProcessService.BuildProcess(Arg.Any<string>(), Arg.Any<CreateBusinessProcessRequest>())
			.Returns<CreateBusinessProcessResult>(_ =>
				throw new InvalidOperationException("Package 'Custom' was not found."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should propagate service-level failures as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Package 'Custom' was not found.")));
	}
	[Test]
	[Category("Unit")]
	[Description("A descriptor carrying ONLY an approval block still triggers the read-back check. The two expectation checks share one describe, and the short-circuit that skips it must require BOTH to be empty — flipping its && to || would silently stop verifying approval for every payload without an email block, which is most of them.")]
	public void Execute_ShouldStillVerifyApproval_WhenTheDescriptorCarriesNoEmailBlock() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = "{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":["
				+ "{\"name\":\"Approve1\",\"type\":\"approval\",\"approval\":{\"object\":\"Order\","
				+ "\"approver\":{\"type\":\"user\",\"employee\":\"Anna\"}}}],\"flows\":[]}"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		_command.Execute(options);

		// Assert
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), false);
	}

	[Test]
	[Category("Unit")]
	[Description("A descriptor whose ONLY special content is a flow label still triggers the read-back, and the warning still reaches the caller. Both halves are pinned here because both mutations are invisible otherwise: dropping the `expectedLabels.Count == 0` clause from the short-circuit makes the label guard dead code for exactly the payload it was written for — a two-branch decision with labels and nothing else — and deleting the WriteWarning block loses the only signal a caller gets. This fixture already records that the create side once shipped a channel with service-level tests only and a deleted emission left 8 273 tests green.")]
	public void Execute_ShouldWarn_WhenAFlowLabelWasDiscarded() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = "{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":["
				+ "{\"name\":\"Decide\",\"type\":\"userTask\"},{\"name\":\"Yes\",\"type\":\"endEvent\"}],"
				+ "\"flows\":[{\"source\":\"Decide\",\"target\":\"Yes\",\"label\":\"Approved\"}]}"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		// The saved flow comes back with NO label - what a package below the capability version leaves behind
		// after answering success.
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>())
			.Returns(new DescribeProcessResult {
				Elements = [],
				Flows = [new DescribedFlow { Source = "Decide", Target = "Yes" }]
			});
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a dropped label is a caveat about a build that SUCCEEDED, never a failure");
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), null, false);
		warnings.Should().ContainSingle(warning => warning.Contains("Decide -> Yes ('Approved')"),
			because: "the caller has to be told which label is not drawn, by the only handle they have on the "
				+ "flow");
		warnings.Should().ContainSingle(warning => warning.Contains("install-process-builder"),
			because: "the remedy has to arrive with the finding, or the caller audits their own payload");
	}

	[Test]
	[Category("Unit")]
	[Description("A label that LANDED emits no warning at all. This is the one mutation the rest of the label coverage cannot see: replacing BuildWarning(MissingLabels(described, expected)) with BuildWarning(expected) - dropping the filter - leaves every MissingLabels unit test green and every dropped-label assertion green, because both still hold. What changes is that EVERY successful labelled build then tells the caller their labels were discarded and points them at a destructive package install. A guard that fires on success is worse than no guard: it trains a reader to ignore the channel that carries the true findings.")]
	public void Execute_ShouldNotWarn_WhenTheFlowLabelLanded() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = "{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":[{\"name\":\"Decide\",\"type\":\"userTask\"},{\"name\":\"Yes\",\"type\":\"endEvent\"}],\"flows\":[{\"source\":\"Decide\",\"target\":\"Yes\",\"label\":\"Approved\"}]}"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		// The saved flow comes back carrying exactly the label that was sent: the ordinary, healthy outcome.
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>())
			.Returns(new DescribeProcessResult {
				Elements = [],
				Flows = [new DescribedFlow { Source = "Decide", Target = "Yes", Label = "Approved" }]
			});
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a verified label is an ordinary success");
		warnings.Should().BeEmpty(
			because: "the label is drawn exactly as asked, so there is nothing to caveat - and a warning here "
				+ "would name a real flow and a real label, which is what makes a spurious one credible");
	}

	[Test]
	[Category("Unit")]
	[Description("When the read-back itself fails, a labels-only payload is told the check did not happen rather than nothing at all. The intent-based unverified warning is silent for such a payload — it configures no block — so without this the command would print a plain success on a build whose labels were all discarded. That matters more than for the sibling guards: the decision not to raise the package floor for this field rests entirely on the read-back being able to report a drop.")]
	public void Execute_ShouldReportLabelsUnverified_WhenTheReadBackFails() {
		// Arrange
		CreateBusinessProcessOptions options = new() {
			Environment = "sandbox",
			DescriptorJson = "{\"name\":\"UsrSampleProcess\",\"packageName\":\"Custom\",\"elements\":["
				+ "{\"name\":\"Decide\",\"type\":\"userTask\"},{\"name\":\"Yes\",\"type\":\"endEvent\"}],"
				+ "\"flows\":[{\"source\":\"Decide\",\"target\":\"Yes\",\"label\":\"Approved\"}]}"
		};
		_createBusinessProcessService.BuildProcess("sandbox", Arg.Any<CreateBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null, Arg.Any<bool>())
			.Returns(Error.Failure(description: "the request timed out"));
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an unreadable description is not evidence of a drop");
		warnings.Should().ContainSingle(warning => warning.Contains("Could not verify")
				&& warning.Contains("Decide -> Yes ('Approved')")
				&& warning.Contains("the request timed out"),
			because: "'could not check' and 'verified' must not look the same, and the reason is what makes "
				+ "the caveat actionable");
	}

}
