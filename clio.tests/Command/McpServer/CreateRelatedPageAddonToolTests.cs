using System;
using System.Collections.Generic;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.RelatedPages;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public class CreateRelatedPageAddonToolTests {

	private static CreateRelatedPageAddonArgs Args(
		string entitySchemaName = "UsrDeliveryItem",
		string packageName = "UsrDeliveryTracking",
		IReadOnlyList<RelatedPageArg> pages = null,
		string environmentName = "dev") =>
		new(
			entitySchemaName,
			packageName,
			pages ?? new[] { new RelatedPageArg("UsrDeliveryItemFormPage", true, null, null, null, null) },
			null,
			environmentName,
			null,
			null,
			null);

	[Test]
	[Category("Unit")]
	[Description("Resolves the command for the requested environment and maps the pages array into the command options, returning the resolved command's response.")]
	public void CreateRelatedPageAddon_Resolves_Command_For_Requested_Environment_And_Maps_Pages() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		FakeCommand defaultCommand = new();
		FakeCommand resolvedCommand = new();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateRelatedPageAddonCommand>(Arg.Any<CreateRelatedPageAddonOptions>())
			.Returns(resolvedCommand);
		CreateRelatedPageAddonTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		CreateRelatedPageAddonResponse response = tool.CreateRelatedPageAddon(Args(pages: new[] {
			new RelatedPageArg("UsrDeliveryItemFormPage", true, null, null, null, null),
			new RelatedPageArg("UsrDeliveryItemAddPage", null, true, null, null, null)
		}));

		// Assert
		// The tool must surface the RESOLVED command's response (FakeCommand echoes the mapped options),
		// not a default — so these prove the resolved command ran and its response flowed back out.
		response.Success.Should().BeTrue(
			because: "the resolved command reports success for a valid pages payload");
		response.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "the tool returns the resolved command's response built from the mapped options");
		response.PageCount.Should().Be(2,
			because: "both mapped page entries reached the resolved command");
		resolvedCommand.Captured.Should().NotBeNull(
			because: "the tool must invoke the environment-resolved command, not the startup default");
		resolvedCommand.Captured.EntitySchemaName.Should().Be("UsrDeliveryItem",
			because: "entity-schema-name maps onto the command options");
		resolvedCommand.Captured.PackageName.Should().Be("UsrDeliveryTracking",
			because: "package-name maps onto the command options");
		resolvedCommand.Captured.Environment.Should().Be("dev",
			because: "the requested environment-name is threaded onto the command options");
		resolvedCommand.Captured.Pages.Should().HaveCount(2,
			because: "both pages-array entries are mapped to RelatedPageSpec");
		resolvedCommand.Captured.Pages[0].IsDefault.Should().BeTrue(
			because: "the first entry's is-default flag maps through");
		resolvedCommand.Captured.Pages[1].IsAdd.Should().BeTrue(
			because: "the second entry's is-add flag maps through");
		defaultCommand.Captured.Should().BeNull(
			because: "the startup-injected default command must not be invoked when an environment is requested");
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a blank entity-schema-name in the structured response without resolving a command.")]
	public void CreateRelatedPageAddon_Rejects_Missing_Entity_Schema_Name_Without_Resolving() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CreateRelatedPageAddonResponse response = tool.CreateRelatedPageAddon(Args(entitySchemaName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank entity-schema-name is invalid input");
		response.Error.Should().Contain("entity-schema-name",
			because: "the error should name the missing field");
		// Input validation must short-circuit before any command resolution.
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateRelatedPageAddonCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a blank package-name in the structured response without resolving a command.")]
	public void CreateRelatedPageAddon_Rejects_Missing_Package_Name_Without_Resolving() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CreateRelatedPageAddonResponse response = tool.CreateRelatedPageAddon(Args(packageName: " "));

		// Assert
		response.Success.Should().BeFalse(
			because: "a blank package-name is invalid input");
		response.Error.Should().Contain("package-name",
			because: "the error should name the missing field");
		// Input validation must short-circuit before any command resolution.
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateRelatedPageAddonCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects an empty pages array in the structured response without resolving a command.")]
	public void CreateRelatedPageAddon_Rejects_Empty_Pages_Without_Resolving() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CreateRelatedPageAddonResponse response = tool.CreateRelatedPageAddon(Args(pages: Array.Empty<RelatedPageArg>()));

		// Assert
		response.Success.Should().BeFalse(
			because: "at least one page entry is required");
		response.Error.Should().Contain("pages",
			because: "the error should explain that pages is empty");
		// Input validation must short-circuit before any command resolution.
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateRelatedPageAddonCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a null entry inside the pages array in the structured response without resolving a command.")]
	public void CreateRelatedPageAddon_Rejects_Null_Pages_Entry_Without_Resolving() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		CreateRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CreateRelatedPageAddonResponse response = tool.CreateRelatedPageAddon(Args(pages: new RelatedPageArg[] { null }));

		// Assert
		response.Success.Should().BeFalse(
			because: "a null pages entry is invalid and must not reach mapping (NRE)");
		response.Error.Should().Contain("pages",
			because: "the error should explain that a null pages entry was provided");
		// Input validation must short-circuit before any command resolution.
		commandResolver.DidNotReceiveWithAnyArgs().Resolve<CreateRelatedPageAddonCommand>(default!);
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Returns the resolver's failure inside the structured response when command resolution throws.")]
	public void CreateRelatedPageAddon_Returns_Error_When_Command_Resolution_Fails() {
		// Arrange
		ConsoleLogger.Instance.ClearMessages();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<CreateRelatedPageAddonCommand>(Arg.Any<CreateRelatedPageAddonOptions>())
			.Returns(_ => throw new InvalidOperationException("boom"));
		CreateRelatedPageAddonTool tool = new(new FakeCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		CreateRelatedPageAddonResponse response = tool.CreateRelatedPageAddon(Args());

		// Assert
		response.Success.Should().BeFalse(
			because: "a failed command resolution is reported as a failure, not surfaced as an exception");
		response.Error.Should().Contain("boom",
			because: "the resolver's exception message is carried into the structured response");
		ConsoleLogger.Instance.ClearMessages();
	}

	private sealed class FakeCommand : CreateRelatedPageAddonCommand {
		public CreateRelatedPageAddonOptions Captured { get; private set; }

		public FakeCommand()
			: base(Substitute.For<IRelatedPageAddonService>(), ConsoleLogger.Instance) {
		}

		public override bool TryCreate(CreateRelatedPageAddonOptions options, out CreateRelatedPageAddonResponse response) {
			Captured = options;
			response = new CreateRelatedPageAddonResponse {
				Success = true,
				EntitySchemaName = options.EntitySchemaName,
				PageCount = options.Pages?.Count ?? 0
			};
			return true;
		}
	}
}
