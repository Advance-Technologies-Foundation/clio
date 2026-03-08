using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using Clio.Package;
using Clio.Workspaces;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public class UserTaskToolTests {

	[Test]
	[Description("Resolves the create user task command for the requested environment and maps structured MCP parameters into command options.")]
	[Category("Unit")]
	public void CreateUserTask_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCreateUserTaskCommand defaultCommand = new();
		FakeCreateUserTaskCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateUserTaskCommand>(Arg.Any<CreateUserTaskOptions>()).Returns(resolvedCommand);
		CreateUserTaskTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.CreateUserTask(new CreateUserTaskArgs(
			"UsrSendInvoice",
			"MyPackage",
			"Send invoice",
			"docker_fix2",
			@"C:\Projects\clio-with-core-and-ui\workspace",
			"Creates and sends invoice",
			"en-US",
			new List<UserTaskParameterArgs> {
				new("IsError", "Is error", "Boolean", Direction: "Out", Resulting: true, Serializable: true),
				new("AccountRef", "Account reference", "Lookup", Lookup: "Account"),
				new("MyList", "My list", "Serializable list of composite values",
					Items: new List<UserTaskParameterArgs> {
						new("Bool1", "Bool1", "Boolean")
					})
			}));

		// Assert
		result.ExitCode.Should().Be(0, "because the tool should forward a valid command payload");
		commandResolver.Received(1).Resolve<CreateUserTaskCommand>(Arg.Is<CreateUserTaskOptions>(options =>
			options.Code == "UsrSendInvoice"
			&& options.Package == "MyPackage"
			&& options.Title == "Send invoice"
			&& options.Environment == "docker_fix2"
			&& options.WorkspacePath == @"C:\Projects\clio-with-core-and-ui\workspace"
			&& options.Parameters != null
			&& options.ParameterItems != null));
		defaultCommand.CapturedOptions.Should().BeNull("because the environment-aware tool should use the resolved command");
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.Parameters.Should().BeEquivalentTo(new[] {
			"code=IsError;title=Is error;type=Boolean;direction=Out;resulting=true;serializable=true",
			"code=AccountRef;title=Account reference;type=Lookup;lookup=Account",
			"code=MyList;title=My list;type=Serializable list of composite values"
		});
		resolvedCommand.CapturedOptions.ParameterItems.Should().BeEquivalentTo(new[] {
			"parent=MyList;code=Bool1;title=Bool1;type=Boolean"
		});
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Description("Resolves the modify user task parameters command for the requested environment and maps add/remove parameter inputs into command options.")]
	[Category("Unit")]
	public void ModifyUserTaskParameters_Should_Resolve_Command_For_Requested_Environment() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeModifyUserTaskParametersCommand defaultCommand = new();
		FakeModifyUserTaskParametersCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<ModifyUserTaskParametersCommand>(Arg.Any<ModifyUserTaskParametersOptions>())
			.Returns(resolvedCommand);
		ModifyUserTaskParametersTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CommandExecutionResult result = tool.ModifyUserTaskParameters(new ModifyUserTaskParametersArgs(
			"UsrSendInvoice",
			"docker_fix2",
			@"C:\Projects\clio-with-core-and-ui\workspace",
			"en-US",
			new List<UserTaskParameterArgs> {
				new("IsError", "Is error", "Boolean", Direction: "1"),
				new("AccountRef", "Account reference", "Lookup", Lookup: "Account"),
				new("MyList", "My list", "Serializable list of composite values",
					Items: new List<UserTaskParameterArgs> {
						new("Bool1", "Bool1", "Boolean")
					})
			},
			new[] {
				new UserTaskParameterItemArgs(
					"ExistingList",
					"ChildText",
					"Child text",
					"Text",
					Items: new List<UserTaskParameterArgs> {
						new("GrandchildFlag", "Grandchild flag", "Boolean")
					})
			},
			new[] { "ObsoleteFlag" },
			new[] { new UserTaskParameterDirectionArgs("ExistingText", "Out") }));

		// Assert
		result.ExitCode.Should().Be(0, "because the tool should forward a valid modification request");
		commandResolver.Received(1).Resolve<ModifyUserTaskParametersCommand>(
			Arg.Is<ModifyUserTaskParametersOptions>(options =>
				options.UserTaskName == "UsrSendInvoice"
				&& options.Environment == "docker_fix2"
				&& options.WorkspacePath == @"C:\Projects\clio-with-core-and-ui\workspace"));
		defaultCommand.CapturedOptions.Should().BeNull("because the environment-aware tool should use the resolved command");
		resolvedCommand.CapturedOptions.Should().NotBeNull();
		resolvedCommand.CapturedOptions.RemoveParameters.Should().BeEquivalentTo(new[] { "ObsoleteFlag" });
		resolvedCommand.CapturedOptions.AddParameters.Should().BeEquivalentTo(new[] {
			"code=IsError;title=Is error;type=Boolean;direction=1",
			"code=AccountRef;title=Account reference;type=Lookup;lookup=Account",
			"code=MyList;title=My list;type=Serializable list of composite values"
		});
		resolvedCommand.CapturedOptions.AddParameterItems.Should().BeEquivalentTo(new[] {
			"parent=MyList;code=Bool1;title=Bool1;type=Boolean",
			"parent=ExistingList;code=ChildText;title=Child text;type=Text",
			"parent=ChildText;code=GrandchildFlag;title=Grandchild flag;type=Boolean"
		});
		resolvedCommand.CapturedOptions.SetDirections.Should().ContainSingle()
			.Which.Should().Be("ExistingText=Out");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCreateUserTaskCommand : CreateUserTaskCommand {
		public CreateUserTaskOptions CapturedOptions { get; private set; }

		public FakeCreateUserTaskCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IWorkspacePathBuilder>(),
				Substitute.For<IJsonConverter>(),
				Substitute.For<IFileSystem>(),
				Substitute.For<IFileDesignModePackages>(),
				Substitute.For<IUserTaskMetadataDirectionApplier>(),
				Substitute.For<IUserTaskLookupSchemaResolver>()) {
		}

		public override int Execute(CreateUserTaskOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}

	private sealed class FakeModifyUserTaskParametersCommand : ModifyUserTaskParametersCommand {
		public ModifyUserTaskParametersOptions CapturedOptions { get; private set; }

		public FakeModifyUserTaskParametersCommand()
			: base(
				Substitute.For<IApplicationClient>(),
				new EnvironmentSettings(),
				Substitute.For<IServiceUrlBuilder>(),
				Substitute.For<IWorkspacePathBuilder>(),
				Substitute.For<IJsonConverter>(),
				Substitute.For<IFileSystem>(),
				Substitute.For<IFileDesignModePackages>(),
				Substitute.For<IUserTaskMetadataDirectionApplier>(),
				Substitute.For<IUserTaskLookupSchemaResolver>()) {
		}

		public override int Execute(ModifyUserTaskParametersOptions options) {
			CapturedOptions = options;
			return 0;
		}
	}
}
