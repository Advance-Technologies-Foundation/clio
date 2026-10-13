using System;
using System.Text.Json.Nodes;
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
		// The command reads the saved process back - for the configuration guards, and since ENG-103023 on every
		// success for the post-write summary. A readable empty process is the default; a test that exercises a
		// guard configures its own read-back.
		_processDescriber = Substitute.For<IProcessDescriber>();
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(new DescribeProcessResult {
				Name = "UsrSampleProcess", SchemaUId = "uid", Elements = [], Flows = [], Parameters = []
			});
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
	[Description("An addMapping onto a process parameter the process already binds to a DIFFERENT element succeeds and warns, naming the binding it replaced (ENG-103081). The before-read the digest already takes is what makes the replaced binding visible; modify has no removeMapping, so the overwrite was silent.")]
	public void Execute_ShouldWarn_WhenAnAddMappingReplacesAStoredBinding() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = """
				[{"op":"addMapping","mapping":{"targetProcessParameter":"PName","sourceElement":"ReadB",
					"sourceElementParameter":"ResultEntity","sourceColumn":"Name"}}]
				"""
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(new DescribeProcessResult {
				Name = "UsrSampleProcess", SchemaUId = "uid", Elements = [], Flows = [],
				Parameters = [new DescribedParameter {
					Name = "PName", Source = "Script", Value = "[#...#]", SourceElement = "ReadA",
					SourceElementParameter = "ResultEntity", SourceColumn = "Name"
				}]
			});

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "the binding warning is a caveat on an edit that applied");
		_logger.Received(1).WriteWarning(Arg.Is<string>(text =>
			text.Contains("replaced the existing binding of process parameter 'PName'")
			&& text.Contains("was ReadA.ResultEntity.Name")));
	}

	[Test]
	[Category("Unit")]
	[Description("An addMapping onto a parameter with no stored binding raises no binding warning - the channel stays quiet for the ordinary single mapping, so the warning cannot train a reader to ignore it.")]
	public void Execute_ShouldNotWarn_WhenAnAddMappingBindsAnUnboundParameter() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = """
				[{"op":"addMapping","mapping":{"targetProcessParameter":"PName","sourceElement":"ReadB",
					"sourceElementParameter":"ResultEntity","sourceColumn":"Name"}}]
				"""
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0, because: "an ordinary mapping edit succeeds");
		_logger.DidNotReceive().WriteWarning(Arg.Is<string>(text => text.Contains("process parameter 'PName'")));
	}

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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(new DescribeProcessResult { Elements = [element] });
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		// Two reads: one before the edit for the digest's change list, one after for the guards and the summary.
		// The before-read needs no version standing; the after-read does, for the digest.
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, false, true);
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, true, true);
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>()).Returns(
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(new DescribeProcessResult { Elements = [element] });
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		// Two reads: one before the edit for the digest's change list, one after for the guards and the summary.
		// The before-read needs no version standing; the after-read does, for the digest.
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, false, true);
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, true, true);
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>()).Returns(new DescribeProcessResult {
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>()).Returns(
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>()).Returns(
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
		_logger.Received(1).WriteInfo(Arg.Is<string>(message => message.Contains("Process 'UsrSampleProcess' edited")));
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

	[Test]
	[Category("Unit")]
	[Description("An operations array whose ONLY special content is a flow label still triggers the read-back, and the warning still reaches the caller. On the modify path the drop is worse than on the build path: an edit normally lands on a designer-authored process where a label already exists, so a caller relabelling a branch against an old package is told the edit succeeded while the OLD label is still what is drawn. Both mutations are otherwise invisible — dropping the `expectedLabels.Count == 0` clause makes the guard dead code for a labels-only edit, and deleting the WriteWarning block loses the only signal. NOTE: this fixture pinned the WRONG wording until the pre-merge review. The read-back here returns 'Rejected', so the outcome is a MISMATCH, and the assertion demanded the absent-case sentence ('no diagram label ... (Approved)') while its own because-clause said 'the connector still says something else'. The test was documenting the defect. It now asserts the mismatch branch, including that the destructive package remedy is NOT prescribed for a cause the package version did not create.")]
	public void Execute_ShouldWarn_WhenAFlowLabelWasDiscarded() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setFlow\",\"source\":\"Decide\",\"target\":\"Yes\","
				+ "\"kind\":\"sequence\",\"label\":\"Approved\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		// The old label is STILL on the flow - the shape a package below the capability version leaves after
		// answering success, and the one a caller cannot distinguish from "my edit applied".
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(new DescribeProcessResult {
				Elements = [],
				Flows = [new DescribedFlow { Source = "Decide", Target = "Yes", Label = "Rejected" }]
			});
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		int result = _command.Execute(options);

		// Assert
		result.Should().Be(0,
			because: "a dropped label is a caveat about an edit that SUCCEEDED, never a failure");
		// Two reads: one before the edit for the digest's change list, one after for the guards and the summary.
		// The before-read needs no version standing; the after-read does, for the digest.
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, false, true);
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, true, true);
		warnings.Should().ContainSingle(
			warning => warning.Contains("Decide -> Yes (asked for 'Approved', drawn 'Rejected')"),
			because: "the caller asked for 'Approved' and the connector still says something else - so the "
				+ "warning has to carry BOTH, and the old label is the datum that tells them the edit did not "
				+ "take rather than that the field vanished");
		warnings.Should().NotContain(warning => warning.Contains("install-process-builder"),
			because: "a package that discards the field leaves NOTHING on the flow; text coming back means "
				+ "the field arrived, so recommending a configuration build and an instance restart here "
				+ "prescribes a destructive remedy for a cause it cannot fix");
	}

	[Test]
	[Category("Unit")]
	[Description("A label that LANDED emits no warning at all. See the create twin for why this is the mutation the other label tests cannot catch: dropping the MissingLabels filter keeps every one of them green while turning every successful labelled edit into a false report that the label was discarded. Pinned on both write paths because the emission is per-command and one path can regress alone.")]
	public void Execute_ShouldNotWarn_WhenTheFlowLabelLanded() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setFlow\",\"source\":\"Decide\",\"target\":\"Yes\",\"kind\":\"sequence\",\"label\":\"Approved\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		// The edit took: the flow comes back with the new label.
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
			because: "the relabel is drawn exactly as asked, so there is nothing to caveat");
	}

	[Test]
	[Category("Unit")]
	[Description("A label addressed by UId reaches the caller as an explicit 'could not verify' caveat. This pins the EMISSION, which nothing did: BlockExpectationReporter.ReportFlowLabels has no fixture of its own and neither command fixture sent a UId-addressed label, so deleting the two lines that emit this was green. What went undetected is exactly the silence the [RequiresPackage] floor was left unraised on the strength of avoiding - the same defect class as the two dead tests this remediation revived, in the same file family.")]
	public void Execute_ShouldReportALabelAddressedByUidAsUnverifiable() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setFlow\",\"source\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"target\":\"Yes\",\"kind\":\"sequence\",\"label\":\"Approved\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		// The read-back reports endpoints as element NAMES, so the UId-addressed expectation can never match
		// it - which is the whole point: the label may have landed or been discarded and nothing here can say.
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
		result.Should().Be(0, because: "an unverifiable label is a caveat on an edit that SUCCEEDED");
		warnings.Should().ContainSingle(warning => warning.Contains("addressed by UId"),
			because: "the caller has to learn the check did not happen, and why, or they read silence as "
				+ "confirmation");
		warnings.Should().NotContain(warning => warning.Contains("shows no diagram label"),
			because: "nothing was found to be missing - claiming a drop here would be a finding the read-back "
				+ "cannot support");
	}

	[Test]
	[Category("Unit")]
	[Description("When one label is unverifiable and another really was dropped, BOTH reach the caller and the caveat comes FIRST. The order is load-bearing and is stated as such in ReportFlowLabels, matching what ReportDescribed does for the block guards: a caller who reads a definite finding first and a 'could not check' second is being invited to treat the second as an afterthought. Inverting the two emission lines was green before this.")]
	public void Execute_ShouldReportTheUnverifiableCaveatBeforeTheDroppedLabel() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setFlow\",\"source\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"target\":\"Yes\",\"kind\":\"sequence\",\"label\":\"Approved\"},{\"op\":\"setFlow\",\"source\":\"Decide\",\"target\":\"No\",\"kind\":\"sequence\",\"label\":\"Rejected\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		// Decide->No exists and came back with NO label: a real drop. The UId-addressed one cannot be checked.
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(new DescribeProcessResult {
				Elements = [],
				Flows = [new DescribedFlow { Source = "Decide", Target = "No" }]
			});
		List<string> warnings = [];
		_logger.When(logger => logger.WriteWarning(Arg.Any<string>()))
			.Do(call => warnings.Add(call.Arg<string>()));

		// Act
		_command.Execute(options);

		// Assert
		int caveat = warnings.FindIndex(warning => warning.Contains("addressed by UId"));
		int dropped = warnings.FindIndex(warning => warning.Contains("shows no diagram label"));
		caveat.Should().BeGreaterThanOrEqualTo(0, because: "the unverifiable label must be reported");
		dropped.Should().BeGreaterThanOrEqualTo(0, because: "the genuinely dropped label must be reported");
		caveat.Should().BeLessThan(dropped,
			because: "what could not be CHECKED is read first and what was found wrong second - the order "
				+ "ReportDescribed uses for the block guards, and the reason is the same: a caveat after a "
				+ "definite finding reads as an afterthought");
	}

	[Test]
	[Category("Unit")]
	[Description("When the read-back itself fails, a labels-only edit is told the check did not happen. The intent-based unverified warning says nothing for such a payload — it configures no block — so silence here would print a plain success on an edit whose label was discarded, and the decision not to raise the package floor for this field depends on the read-back being able to speak.")]
	public void Execute_ShouldReportLabelsUnverified_WhenTheReadBackFails() {
		// Arrange
		ModifyBusinessProcessOptions options = new() {
			Environment = "sandbox",
			ProcessName = "UsrSampleProcess",
			OperationsJson = "[{\"op\":\"setFlow\",\"source\":\"Decide\",\"target\":\"Yes\","
				+ "\"kind\":\"sequence\",\"label\":\"Approved\"}]"
		};
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), ProcessWriteDigest.DigestCulture, Arg.Any<bool>(), Arg.Any<bool>())
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
			because: "'could not check' and 'verified' must not look the same to the caller");
	}


	[Test]
	[Description("ENG-103023: a successful edit reports the saved version's summary and what the edit changed, from a read before and a read after, so the caller need not describe the process to confirm it.")]
	public void Execute_ShouldReportSummaryAndChanges_AfterASuccessfulEdit() {
		// Arrange
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(DigestProcess("Start", "Old"), DigestProcess("Start", "New"));

		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox", ProcessName = "UsrSampleProcess", OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(0, because: "the edit succeeded");
		JsonNode digest = JsonNode.Parse(DigestLine() ?? "null");
		digest.Should().NotBeNull(because: "every successful edit carries its digest");
		digest!["summary"]!["elements"]!.AsArray().Should().HaveCount(2, because: "the summary is of the saved state");
		digest["changes"]!["elementsAdded"]!.AsArray().Select(node => node!.GetValue<string>())
			.Should().Equal(["New"], because: "the element the edit added is named");
		digest["changes"]!["elementsRemoved"]!.AsArray().Select(node => node!.GetValue<string>())
			.Should().Equal(["Old"], because: "the element the edit removed is named");
	}

	[Test]
	[Description("ENG-103023: a failed read before the edit costs only the change list - the digest says why, the edit still succeeds.")]
	public void Execute_ShouldSayWhyChangesAreUnavailable_WhenTheReadBeforeFails() {
		// Arrange
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(Error.Failure(description: "stand unreachable"), DigestProcess("Start"));

		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox", ProcessName = "UsrSampleProcess", OperationsJson = SampleOperations
		});

		// Assert
		result.Should().Be(0, because: "the digest never changes the outcome of a write that landed");
		JsonNode digest = JsonNode.Parse(DigestLine())!;
		digest["changesUnavailable"]!.GetValue<string>().Should().Contain("stand unreachable",
			because: "an unknown change list must not read as an empty one");
		digest["changes"].Should().BeNull(because: "nothing was compared");
	}

	[Test]
	[Description("ENG-103165: an edit of a process that CALLS another one is not read back when no guard needs the read. The read would build the caller's cached schema instance with its callees' current parameters, and the server drops that instance only when the caller is saved again, so a parameter a callee gains afterwards would be missing from every later describe of the caller. The read before the edit is still taken: the save drops what it built.")]
	public void Execute_ShouldNotReadACallerBack_WhenNoGuardNeedsTheRead() {
		// Arrange
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		DescribeProcessResult caller = DigestProcess("Start", "CallOrder");
		caller.Elements[1].BuildType = "subprocess";
		caller.Elements[1].SubProcess = new DescribedSubProcess { Process = "UsrOrderProcess" };
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(caller);

		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox", ProcessName = "UsrSampleProcess",
			OperationsJson = """[{"op":"addParameter","parameter":{"name":"Note","type":"Text","direction":"In"}}]"""
		});

		// Assert
		result.Should().Be(0, because: "the edit succeeded");
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<bool>());
		_processDescriber.DidNotReceive().Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), true,
			Arg.Any<bool>());
		DigestLine().Should().BeNull(because: "a summary needs the read that would pin the caller");
		_logger.Received(1).WriteInfo(ProcessWriteDigest.CallerNotReadBack);
		_logger.DidNotReceiveWithAnyArgs().WriteWarning(default!);
	}

	[Test]
	[Description("ENG-103165: an edit that ADDS a sub-process element makes the process a caller, so it is not read back either when no guard needs the read, even though the read before the edit showed no sub-process element.")]
	public void Execute_ShouldNotReadBack_WhenTheEditAddsASubProcessElement() {
		// Arrange
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(DigestProcess("Start", "End"));

		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox", ProcessName = "UsrSampleProcess",
			OperationsJson = """
				[{"op":"addElement","element":{"name":"CallOrder","type":"subProcess",
					"subProcess":{"processName":"UsrOrderProcess"}}}]
				"""
		});

		// Assert
		result.Should().Be(0, because: "the edit succeeded");
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<bool>());
		_logger.Received(1).WriteInfo(ProcessWriteDigest.CallerNotReadBack);
	}

	[Test]
	[Description("ENG-103165: an edit that points an element at a called process through setElement makes the process a caller, so it is not read back when no guard needs the read.")]
	public void Execute_ShouldNotReadBack_WhenTheEditSetsASubProcessBlock() {
		// Arrange
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(DigestProcess("Start", "End"));

		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox", ProcessName = "UsrSampleProcess",
			OperationsJson = """
				[{"op":"setElement","elementName":"CallOrder",
					"elementUpdate":{"subProcess":{"processName":"UsrOrderProcess"}}}]
				"""
		});

		// Assert
		result.Should().Be(0, because: "the edit succeeded");
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(),
			Arg.Any<bool>());
		_logger.Received(1).WriteInfo(ProcessWriteDigest.CallerNotReadBack);
	}

	[Test]
	[Description("ENG-103165: a caller IS read back when the edit binds a process parameter, because the ENG-103081 check that a binding's source runs on every start judges the saved graph - skipping the read would drop that warning silently.")]
	public void Execute_ShouldReadACallerBack_WhenTheEditBindsAProcessParameter() {
		// Arrange
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		DescribeProcessResult caller = DigestProcess("Start", "CallOrder");
		caller.Elements[1].SubProcess = new DescribedSubProcess { Process = "UsrOrderProcess" };
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(caller);

		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox", ProcessName = "UsrSampleProcess",
			OperationsJson = """[{"op":"addMapping","mapping":{"targetProcessParameter":"Attempts","value":"3"}}]"""
		});

		// Assert
		result.Should().Be(0, because: "the edit succeeded");
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), true, Arg.Any<bool>());
		_logger.DidNotReceive().WriteInfo(ProcessWriteDigest.CallerNotReadBack);
	}

	[Test]
	[Description("ENG-103165: a caller IS read back when a guard needs the read - the label guard is the only signal that a label landed, and it took this read before ENG-103023 too - so the digest comes with it.")]
	public void Execute_ShouldReadACallerBack_WhenAGuardNeedsTheRead() {
		// Arrange
		_modifyBusinessProcessService.ModifyProcess("sandbox", Arg.Any<ModifyBusinessProcessRequest>())
			.Returns(BuildResult());
		DescribeProcessResult caller = DigestProcess("Start", "CallOrder");
		caller.Elements[1].SubProcess = new DescribedSubProcess { Process = "UsrOrderProcess" };
		_processDescriber.Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
			.Returns(caller);

		// Act
		int result = _command.Execute(new ModifyBusinessProcessOptions {
			Environment = "sandbox", ProcessName = "UsrSampleProcess",
			OperationsJson = """[{"op":"setFlow","source":"Start","target":"CallOrder","label":"Go"}]"""
		});

		// Assert
		result.Should().Be(0, because: "the edit succeeded");
		_processDescriber.Received(1).Describe(Arg.Any<ProcessIdentity>(), Arg.Any<string>(), true, Arg.Any<bool>());
		DigestLine().Should().NotBeNull(because: "the guard's read-back carries the digest, as for any other process");
		_logger.DidNotReceive().WriteInfo(ProcessWriteDigest.CallerNotReadBack);
	}

	private static DescribeProcessResult DigestProcess(params string[] elements) => new() {
		Name = "UsrSampleProcess", SchemaUId = "uid",
		Elements = [.. elements.Select(name => new DescribedElement { Name = name, BuildType = "formulatask" })],
		Flows = elements.Length > 1 ? [new DescribedFlow { Source = elements[0], Target = elements[1] }] : [],
		Parameters = []
	};

	private string DigestLine() => _logger.ReceivedCalls()
		.Where(call => call.GetMethodInfo().Name == nameof(ILogger.WriteInfo))
		.Select(call => call.GetArguments()[0] as string)
		.SingleOrDefault(text => text != null && text.StartsWith("{\"summary\""));
}
