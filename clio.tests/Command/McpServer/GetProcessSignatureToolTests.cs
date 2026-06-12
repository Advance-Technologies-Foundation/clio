using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
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
	public void GetProcessSignature_Should_Resolve_Command_For_Requested_Environment() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetProcessSignatureCommand defaultCommand = new();
		FakeGetProcessSignatureCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetProcessSignatureCommand>(Arg.Any<GetProcessSignatureOptions>())
			.Returns(resolvedCommand);
		GetProcessSignatureTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetProcessSignatureResponse response = tool.GetProcessSignature(
			new GetProcessSignatureArgs("UsrProcess_e629820", null, "docker_fix2", null, null, null));

		response.Success.Should().BeTrue();
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.ProcessName.Should().Be("UsrProcess_e629820");
		resolvedCommand.CapturedOptions.Environment.Should().Be("docker_fix2");
		defaultCommand.CapturedOptions.Should().BeNull();
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void GetProcessSignature_Should_Return_Error_When_Command_Resolution_Fails() {
		ConsoleLogger.Instance.ClearMessages();
		FakeGetProcessSignatureCommand defaultCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetProcessSignatureCommand>(Arg.Any<GetProcessSignatureOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'missing' is not registered."));
		GetProcessSignatureTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		GetProcessSignatureResponse response = tool.GetProcessSignature(
			new GetProcessSignatureArgs("UsrProcess_e629820", null, "missing", null, null, null));

		response.Success.Should().BeFalse();
		response.Error.Should().Contain("missing");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	public void TryGetSignature_Should_Project_Parameter_Code_Type_And_Lookup() {
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

		bool ok = command.TryGetSignature(
			new GetProcessSignatureOptions { ProcessName = "UsrProcess_e629820" },
			out GetProcessSignatureResponse response);

		ok.Should().BeTrue();
		response.Success.Should().BeTrue();
		response.ProcessCode.Should().Be("UsrProcess_e629820");
		response.ProcessCaption.Should().Be("Business process 6");
		response.Parameters.Should().HaveCount(2);
		response.Parameters[0].Name.Should().Be("ProcessSchemaParameter2");
		response.Parameters[0].Caption.Should().Be("Parameter 2");
		response.Parameters[0].Direction.Should().Be("Input");
		response.Parameters[0].IsLookup.Should().BeFalse();
		response.Parameters[1].Name.Should().Be("ProcessSchemaParameter1");
		response.Parameters[1].IsLookup.Should().BeTrue();
		response.Parameters[1].ReferenceSchemaUId.Should().Be(referenceSchemaUId.ToString());
	}

	[Test]
	[Category("Unit")]
	public void TryGetSignature_Should_Flag_ResolutionFailed_For_NotFound_Error() {
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>())
			.Returns(ErrorOr.Error.NotFound("GetProcessIdFromName", "Could not find process with name or caption:UsrMissing"));
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = "UsrMissing" },
			out GetProcessSignatureResponse response);

		ok.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.ProcessResolutionFailed.Should().BeTrue();
	}

	[Test]
	[Category("Unit")]
	public void TryGetSignature_Should_Flag_ResolutionFailed_For_Ambiguous_Caption() {
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>())
			.Returns(ErrorOr.Error.Conflict("GetProcessIdFromName", "Multiple processes match caption 'Business process 1'"));
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = "Business process 1" },
			out GetProcessSignatureResponse response);

		ok.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.ProcessResolutionFailed.Should().BeTrue();
	}

	[Test]
	[Category("Unit")]
	public void TryGetSignature_Should_Not_Flag_ResolutionFailed_For_Transient_Failure() {
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		generator.Generate(Arg.Any<GenerateProcessModelCommandOptions>())
			.Returns(ErrorOr.Error.Failure("GetProcessSchema", "Error at step: ExecuteRequest. timeout"));
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = "UsrProcess_e629820" },
			out GetProcessSignatureResponse response);

		ok.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.ProcessResolutionFailed.Should().BeFalse();
	}

	[Test]
	[Category("Unit")]
	public void TryGetSignature_Should_Fail_When_Process_Name_Missing() {
		IProcessModelGenerator generator = Substitute.For<IProcessModelGenerator>();
		GetProcessSignatureCommand command = new(generator, ConsoleLogger.Instance);

		bool ok = command.TryGetSignature(new GetProcessSignatureOptions { ProcessName = " " },
			out GetProcessSignatureResponse response);

		ok.Should().BeFalse();
		response.Success.Should().BeFalse();
		response.Error.Should().Contain("process-name");
	}

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
