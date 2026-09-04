using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Linq;
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
public sealed class ModifyBusinessProcessCommandTests {
	private const string SampleOperations =
		"[{\"op\":\"removeElement\",\"elementName\":\"StartEvent1\"}]";

	private IModifyBusinessProcessService _modifyBusinessProcessService;
	private IProcessDescriber _processDescriber;
	private ILogger _logger;
	private ModifyBusinessProcessCommand _command;

	[SetUp]
	public void Setup() {
		_modifyBusinessProcessService = Substitute.For<IModifyBusinessProcessService>();
		// The command reads the process back to catch a server that discarded an email block while answering
		// success. These operations carry no email block, so the substitute is never consulted.
		_processDescriber = Substitute.For<IProcessDescriber>();
		_logger = Substitute.For<ILogger>();
		_command = new ModifyBusinessProcessCommand(_modifyBusinessProcessService, _processDescriber, _logger);
	}

	[TearDown]
	public void TearDown() {
		_modifyBusinessProcessService.ClearReceivedCalls();
		_processDescriber.ClearReceivedCalls();
		_logger.ClearReceivedCalls();
	}

	private static ModifyBusinessProcessResult BuildResult() =>
		new("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55", 1);

	[Test]
	[Category("Unit")]
	[Description("Reads the process back by UId when the edit was addressed by uid and the server omitted the schema name, instead of reporting the permissions check as unperformable.")]
	public void Execute_ShouldVerifyByUid_WhenTheResultCarriesNoSchemaName() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessUid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55",
			OperationsJson = "[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(new ModifyBusinessProcessResult(null, "5c58c4c4-134b-4744-9c67-96d9c69c9d55", 1));
		DescribedElement element = new() {
			Name = "Grant",
			Filter = new DescribedFilter {
				Object = "Order",
				Conditions = [new DescribedFilterCondition { Column = "Id" }]
			},
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
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), null);
		warnings.Should().NotContain(message => message.Contains("Could not verify"),
			because: "the UId was in hand the whole time, so declaring the check unperformable would be a "
				+ "wrong warning in a workflow the tool supports");
	}

	[Test]
	[Category("Unit")]
	[Description("Keeps the exit code at 0 when verification itself throws: the edit already landed, and reporting it as failed would invite a retry that re-applies replace semantics.")]
	public void Execute_ShouldStillSucceed_WhenVerificationThrows() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null)
			.Returns(_ => throw new InvalidOperationException("read-back exploded"));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0,
			because: "the permissions were already changed; exit 1 would tell the caller to retry a "
				+ "replace-semantics write that had in fact applied");
	}

	[Test]
	[Category("Unit")]
	[Description("The email half of the merged guard still reaches the logger, so folding the two block checks into one read-back did not silently drop it.")]
	public void Execute_ShouldStillWarn_WhenTheEmailBlockWasDiscarded() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setElement\",\"elementName\":\"Mail\",\"elementUpdate\":{\"email\":{\"mode\":\"auto\"}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null).Returns(
			new DescribeProcessResult { Elements = [new DescribedElement { Name = "Mail", Email = null }] });
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		warnings.Should().ContainSingle(message => message.Contains("'email'"),
			because: "the merged guard changed the email check's call site and early-return condition, so "
				+ "nothing else proves that half still fires");
	}

	[Test]
	[Category("Unit")]
	[Description("Does NOT claim an addElement accessRights block was dropped when a setElement in the same array configures that element - the payload the warning itself recommends.")]
	public void Execute_ShouldNotWarnAboutAddElement_WhenASetElementConfiguresTheSameElement() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson =
				"[{\"op\":\"addElement\",\"element\":{\"name\":\"Grant\",\"type\":\"changeAccessRights\","
				+ "\"accessRights\":{\"object\":\"Order\"}}},"
				+ "{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		DescribedElement element = new() {
			Name = "Grant",
			Filter = new DescribedFilter {
				Object = "Order",
				Conditions = [new DescribedFilterCondition { Column = "Id" }]
			},
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
		warnings.Should().NotContain(message => message.Contains("sent with addElement"),
			because: "the setElement configured the element, so claiming it was created without permission "
				+ "configuration would be false - and a warning that is wrong in the workflow this code "
				+ "recommends teaches callers to ignore the true ones too");
	}

	[Test]
	[Category("Unit")]
	[Description("Warns when the saved element has no record filter: it will apply the permission change to EVERY record of its object - silently, on an element with no output parameters, and nothing on the platform refuses that state.")]
	public void Execute_ShouldWarn_WhenTheElementHasNoRecordFilter() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
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
			because: "the block landed but the element is now unbounded - it will act on EVERY record of the "
				+ "object - which is indistinguishable from a correct success "
				+ "on an element that reports nothing at run time");
	}

	[Test]
	[Category("Unit")]
	[Description("A batch whose ONLY operation is clearFilter carries no accessRights block, so every block-shaped check skips it - yet clearing the filter is what moves a Change access rights element from narrowing to acting on EVERY record of its object. This path used to return before the read-back, so the most dangerous edit the surface offers was the one edit it never checked.")]
	public void Execute_ShouldStillReadBack_WhenTheBatchOnlyClearsTheFilter() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"clearFilter\",\"elementName\":\"Grant\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		DescribedElement element = new() {
			Name = "Grant",
			UserTaskName = "ChangeAdminRightsUserTask",
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
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), null);
		warnings.Should().ContainSingle(message => message.Contains("EVERY record of the target object"),
			because: "the element is left acting on every row of its object and carries no output parameter to "
				+ "say so, so this warning is the only signal the caller gets");
	}

	[Test]
	[Category("Unit")]
	[Description("When the read-back itself fails after a filter-only batch, the caller must still be told - but in the filter's words, not the block's. The command cannot know the element type at that point (the read-back it needed is the thing that failed), so claiming the 'accessRights' configuration could not be verified would be false for the readData and changeData elements that share the clearFilter operation.")]
	public void Execute_ShouldReportTheFilter_NotAccessRights_WhenTheReadBackFails() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"clearFilter\",\"elementName\":\"Grant\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null)
			.Returns(Error.Failure(description: "the environment refused the read"));
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "the edit applied; an unreadable read-back is not evidence it did not");
		warnings.Should().ContainSingle(message => message.Contains("record filter this edit changed"),
			because: "silence here would be indistinguishable from a verified success on the single most "
				+ "dangerous edit this surface offers");
		warnings.Should().NotContain(message => message.Contains("'accessRights' configuration"),
			because: "this batch sent no accessRights block at all, and the element whose type would justify "
				+ "that wording is exactly what the failed read-back could not tell us");
	}

	[Test]
	[Category("Unit")]
	[Description("A clearFilter on a readData element must not be reported as an access-rights problem. The operation is legal on readData/changeData/signalStart, none of which hold access-rights state, so accusing them trains callers to ignore the message on the one element type that can actually widen.")]
	public void Execute_ShouldNotClaimAnAccessRightsProblem_WhenTheClearedElementIsNotOne() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"clearFilter\",\"elementName\":\"ReadOrders\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), null).Returns(new DescribeProcessResult {
			Elements = [new DescribedElement { Name = "ReadOrders", UserTaskName = "ReadDataUserTask" }]
		});
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int exitCode = _command.Execute(options);

		// Assert
		exitCode.Should().Be(0, because: "clearing a readData filter is an ordinary, successful edit");
		warnings.Should().BeEmpty(
			because: "a readData element has no access-rights state, so there is nothing here that could not be "
				+ "verified - a warning would be a false accusation about the commonest use of the operation");
	}

	[Test]
	[Description("Warns when the edited process carries no accessRights block: a CrtProcessBuilder that predates the Change access rights element discards the block and still answers success, so the edit reports an applied operation whose permission change never landed.")]
	public void Execute_ShouldWarn_WhenTheAccessRightsBlockWasDiscarded() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox", ProcessName = "UsrSampleProcess", OperationsJson = "[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
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
			because: "the edit itself applied; a discarded block is reported as a warning, not a failure");
		warnings.Should().ContainSingle(message =>
			message.Contains("'Grant'") && message.Contains("accessRights"),
			because: "on a revoke this is the only signal that the permissions are still in place");
	}

	[Test]
	[Description("Says the verification could not be performed when the read-back fails, rather than reporting the same silence as a verified success.")]
	public void Execute_ShouldWarn_WhenTheAccessRightsReadBackCannotBeObtained() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox", ProcessName = "UsrSampleProcess", OperationsJson = "[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
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
			because: "an unreadable description is not evidence of a drop, so it must not fail the edit");
		warnings.Should().ContainSingle(message => message.Contains("Could not verify"),
			because: "'verified' and 'could not check' must not reach the caller as the same empty output");
	}

	[Test]
	[Description("Says so when the read-back does not contain the element at all: the check did not happen, which is not the same as the configuration having landed.")]
	public void Execute_ShouldWarn_WhenTheElementIsAbsentFromTheReadBack() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox", ProcessName = "UsrSampleProcess", OperationsJson = "[{\"op\":\"setElement\",\"elementName\":\"Grant\",\"elementUpdate\":{\"accessRights\":{\"add\":[]}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
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
	[Description("Warns that an accessRights block sent with addElement was not applied: the server applies only the email and performer blocks there, so the element is created unconfigured.")]
	public void Execute_ShouldWarn_WhenAccessRightsIsSentWithAddElement() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"addElement\",\"element\":{\"name\":\"Grant\",\"type\":\"changeAccessRights\",\"accessRights\":{\"object\":\"Order\"}}}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		warnings.Should().ContainSingle(message =>
			message.Contains("addElement") && message.Contains("setElement"),
			because: "the block is dropped by design, but the caller is left with the same unconfigured "
				+ "element as a silent drop and needs to be told how to configure it");
	}

	[Test]
	[Description("Writes every server warning out as a WARNING, not merely parsing it: the two outcomes it carries (a connection on an unregistered column, a cleared binding) are invisible in describe afterwards, so a warning that is deserialized and then dropped is the same defect as one never sent.")]
	public void Execute_ShouldWriteWarnings_WhenTheServerReportsThem() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(new ModifyBusinessProcessResult("UsrSampleProcess", "5c58c4c4-134b-4744-9c67-96d9c69c9d55", 1,
				new[] { "Connection 'OmniChat' is not registered", "Connection 'Account' was CLEARED" }));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "a warning is a caveat on a SUCCESSFUL edit, not a failure");
		_logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("OmniChat")));
		_logger.Received(1).WriteWarning(Arg.Is<string>(text => text.Contains("CLEARED")));
	}

	[Test]
	[Description("Writes no warning when the server reported none, so an empty channel cannot train a reader to ignore it.")]
	public void Execute_ShouldNotWriteAnyWarning_WhenTheServerReportsNone() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert — stated as an assertion with a reason rather than a bare DidNotReceive, so the intent is
		// legible and the emptiness is what the test actually claims.
		_logger.ReceivedCalls()
			.Count(call => call.GetMethodInfo().Name == nameof(ILogger.WriteWarning))
			.Should().Be(0,
				because: "the server reported nothing, and a warning invented from an absent member would train a "
					+ "reader to ignore the channel that carries the two outcomes describe cannot show afterwards");
		result.Should().Be(0, because: "no warnings is the ordinary successful edit");
	}

	[Test]
	[Category("Unit")]
	[Description("Forwards the process identity and inline operations to the modify service and logs the result on success.")]
	public void Execute_ShouldMapInlineOperationsToService_WhenOperationsJsonProvided() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a successful edit should return the standard success exit code");
		_modifyBusinessProcessService.Received(1).ModifyProcess(
			"sandbox",
			Arg.Is<ModifyBusinessProcessRequest>(request =>
				request.ProcessName == "UsrSampleProcess" &&
				request.OperationsJson == SampleOperations));
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("UsrSampleProcess")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs guidance when neither --name nor --uid is provided.")]
	public void Execute_ShouldFail_WhenNoIdentityProvided() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox",
			OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(1,
			because: "the command needs a process identity to edit");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("--name") && message.Contains("--uid")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and rejects the edit when both --name and --uid are provided.")]
	public void Execute_ShouldFail_WhenBothNameAndUidProvided() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			ProcessUid = "5c58c4c4-134b-4744-9c67-96d9c69c9d55",
			OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(1,
			because: "the process identity must be unambiguous — exactly one of --name or --uid is allowed");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("not both")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs guidance when no inline operations array is provided.")]
	public void Execute_ShouldFail_WhenNoOperationsProvided() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess"
		});

		// Assert
		result.Should().Be(1,
			because: "the command requires an inline operations array");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message =>
			message.Contains("An operations array is required.")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs a readable error when the call omits environment-name.")]
	public void Execute_ShouldFail_WhenEnvironmentIsMissing() {
		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(1,
			because: "the command should fail fast when the environment is missing");
		_modifyBusinessProcessService.DidNotReceiveWithAnyArgs().ModifyProcess(default!, default!);
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("Environment name is required")));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a failure exit code and logs the service exception message when the modify service throws.")]
	public void Execute_ShouldFail_WhenServiceThrows() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = SampleOperations
		};
		_modifyBusinessProcessService.ModifyProcess(Arg.Any<string>(), Arg.Any<ModifyBusinessProcessRequest>())
			.Returns<ModifyBusinessProcessResult>(_ =>
				throw new InvalidOperationException("Element 'StartEvent1' was not found in the process."));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(1,
			because: "the command should propagate service-level failures as a non-zero exit code");
		_logger.Received(1).WriteError(Arg.Is<string>(message => message.Contains("StartEvent1")));
	}
}
