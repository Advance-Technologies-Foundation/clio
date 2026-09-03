using System;
using System.Collections.Generic;
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null)
			.Returns(new DescribeProcessResult { Elements = [element] });
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		warnings.Should().ContainSingle(message => message.Contains("NO record filter"),
			because: "the block landed but the element cannot act, and on an element with no output "
				+ "parameters that is indistinguishable from success");
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null).Returns(
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null).Returns(
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null)
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null).Returns(
			new DescribeProcessResult { Elements = [] });

		// Act
		_command.Execute(options);

		// Assert
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), null);
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
}
