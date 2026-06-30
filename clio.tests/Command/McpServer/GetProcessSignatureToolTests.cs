using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.McpServer.Tools.ProcessDesigner;
using Clio.Command.ProcessModel;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using ProcessModelType = Clio.Command.ProcessModel.ProcessModel;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class GetProcessSignatureToolTests {

	[Test]
	[Category("Unit")]
	[Description("Resolves the command for the requested environment via the resolver, not the default command.")]
	public void GetProcessSignature_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetProcessSignatureCommand defaultCommand = new();
		FakeGetProcessSignatureCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetProcessSignatureCommand>(Arg.Any<GetProcessSignatureOptions>())
			.Returns(resolvedCommand);
		GetProcessSignatureTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetProcessSignatureResponse response = tool.GetProcessSignature(
			new GetProcessSignatureArgs("UsrProcess_e629820", null, "docker_fix2", null, null, null));

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command returns a successful signature");
		resolvedCommand.CapturedOptions.Should().NotBeNull(because: "the resolved command should run");
		resolvedCommand.CapturedOptions.ProcessName.Should().Be("UsrProcess_e629820",
			because: "the process name argument is mapped onto the options");
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2",
			because: "the environment argument is mapped onto the options");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the startup-time default command must not be used for an environment-bound call");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured failure when command resolution throws (e.g. unknown environment).")]
	public void GetProcessSignature_Should_Return_Error_When_Command_Resolution_Fails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeGetProcessSignatureCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetProcessSignatureCommand>(Arg.Any<GetProcessSignatureOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'missing' is not registered."));
		GetProcessSignatureTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetProcessSignatureResponse response = tool.GetProcessSignature(
			new GetProcessSignatureArgs("UsrProcess_e629820", null, "missing", null, null, null));

		// Assert
		response.Success.Should().BeFalse(because: "resolution failed, so no signature could be produced");
		response.Error.Should().Contain("missing",
			because: "the failure should surface the unresolved environment name");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Projects each process parameter into the signature: code, caption, direction, and lookup flag.")]
	public void TryGetSignature_Should_Project_Parameter_Code_Type_And_Lookup() {
		// Arrange
		Guid referenceSchemaUId = Guid.NewGuid();
		ProcessModelType model = new(Guid.NewGuid(), "UsrProcess_e629820") {
			Name = "Business process 6",
			Parameters = [
				new ProcessParameter {
					Name = "ProcessSchemaParameter2",
					DataValueType = Guid.Empty, // resolves to object; not asserted here
					Direction = ProcessParameterDirection.Input,
					Captions = new Dictionary<string, string> { ["en-US"] = "Parameter 2" }
				},
				new ProcessParameter {
					Name = "ProcessSchemaParameter1",
					Direction = ProcessParameterDirection.Input,
					ReferenceSchemaUId = referenceSchemaUId
				}
			]
		};
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>()).Returns(model);
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryGetSignature(
			new GetProcessSignatureOptions { ProcessName = "UsrProcess_e629820" },
			out GetProcessSignatureResponse response);

		// Assert
		ok.Should().BeTrue(because: "the process resolved successfully");
		response.Success.Should().BeTrue(because: "a resolved process yields a successful signature");
		response.ProcessCode.Should().Be("UsrProcess_e629820", because: "the response echoes the process code");
		response.ProcessCaption.Should().Be("Business process 6", because: "the response echoes the caption");
		response.Parameters.Should().HaveCount(2, because: "both process parameters are projected");
		response.Parameters[0].Name.Should().Be("ProcessSchemaParameter2", because: "the code is projected");
		response.Parameters[0].Caption.Should().Be("Parameter 2", because: "the localized caption is projected");
		response.Parameters[0].Direction.Should().Be("Input", because: "the direction is projected");
		response.Parameters[0].IsLookup.Should().BeFalse(because: "this parameter has no reference schema");
		response.Parameters[1].Name.Should().Be("ProcessSchemaParameter1", because: "the code is projected");
		response.Parameters[1].IsLookup.Should().BeTrue(because: "this parameter has a reference schema");
		response.Parameters[1].ReferenceSchemaUId.Should().Be(referenceSchemaUId.ToString(),
			because: "the lookup reference schema id is projected");
	}

	[Test]
	[Category("Unit")]
	[Description("Flags ProcessResolutionFailed for a NotFound error so callers can hard-fail on a missing process.")]
	public void TryGetSignature_Should_Flag_ResolutionFailed_For_NotFound_Error() {
		// Arrange
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>())
			.Returns(ErrorOr.Error.NotFound("GetProcessIdFromName", "Could not find process with name or caption:UsrMissing"));
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = "UsrMissing" },
			out GetProcessSignatureResponse response);

		// Assert
		ok.Should().BeFalse(because: "a not-found process cannot produce a signature");
		response.Success.Should().BeFalse(because: "the lookup failed");
		response.ProcessResolutionFailed.Should().BeTrue(because: "NotFound is a definitive resolution failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Flags ProcessResolutionFailed for an ambiguous-caption Conflict error.")]
	public void TryGetSignature_Should_Flag_ResolutionFailed_For_Ambiguous_Caption() {
		// Arrange
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>())
			.Returns(ErrorOr.Error.Conflict("GetProcessIdFromName", "Multiple processes match caption 'Business process 1'"));
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = "Business process 1" },
			out GetProcessSignatureResponse response);

		// Assert
		ok.Should().BeFalse(because: "an ambiguous caption cannot resolve to one process");
		response.Success.Should().BeFalse(because: "the lookup failed");
		response.ProcessResolutionFailed.Should().BeTrue(because: "Conflict is a definitive resolution failure");
	}

	[Test]
	[Category("Unit")]
	[Description("Does NOT flag ProcessResolutionFailed for a transient/transport failure, so callers can warn instead of block.")]
	public void TryGetSignature_Should_Not_Flag_ResolutionFailed_For_Transient_Failure() {
		// Arrange
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>())
			.Returns(ErrorOr.Error.Failure("GetProcessSchema", "Error at step: ExecuteRequest. timeout"));
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = "UsrProcess_e629820" },
			out GetProcessSignatureResponse response);

		// Assert
		ok.Should().BeFalse(because: "the signature could not be produced");
		response.Success.Should().BeFalse(because: "the call failed");
		response.ProcessResolutionFailed.Should().BeFalse(
			because: "a transport failure is not a definitive resolution failure and must not hard-block callers");
	}

	[Test]
	[Category("Unit")]
	[Description("Fails fast with a clear message when process-name is blank, without calling the generator.")]
	public void TryGetSignature_Should_Fail_When_Process_Name_Missing() {
		// Arrange
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		// Act
		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = " " },
			out GetProcessSignatureResponse response);

		// Assert
		ok.Should().BeFalse(because: "a blank process name is invalid input");
		response.Success.Should().BeFalse(because: "no lookup is attempted for blank input");
		response.Error.Should().Contain("process-name", because: "the error should name the missing argument");
	}

	// NOTE: the former GetProcessSignature_Should_Return_RequirementFailure_When_ProcessBuilderPackageMissing test
	// was removed — get-process-signature no longer carries [RequiresPackage("clioprocessbuilder")] (it reads the
	// built-in DataService, not ProcessDesignService — PR #715). The "not gated" invariant is locked in by
	// ProcessDesignerRequiresPackageAttributeTests.GetProcessSignatureOptions_ShouldNotDeclareProcessBuilderRequirement_*.

	private sealed class FakeGetProcessSignatureCommand : GetProcessSignatureCommand {
		public GetProcessSignatureOptions CapturedOptions { get; private set; }

		public FakeGetProcessSignatureCommand()
			: base(Substitute.For<IProcessModelGenerator>(), ConsoleLogger.Instance) {
		}

		public override bool TryGetSignature(GetProcessSignatureOptions options,
			out GetProcessSignatureResponse response) {
			CapturedOptions = options;
			response = new GetProcessSignatureResponse { Success = true, ProcessCode = options.ProcessName };
			return true;
		}
	}
}
