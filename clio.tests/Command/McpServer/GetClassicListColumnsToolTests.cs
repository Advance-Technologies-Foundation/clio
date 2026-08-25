using System;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[NonParallelizable]
[Property("Module", "McpServer")]
public class GetClassicListColumnsToolTests {

	[TearDown]
	public void TearDown() {
		ConsoleLogger.Instance.ClearMessages();
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve maps schema-name and environment-name to an environment-scoped command and returns its source-aware response.")]
	public void Resolve_ShouldUseEnvironmentScopedCommand_WhenArgumentsAreValid() {
		// Arrange
		FakeGetClassicListColumnsCommand defaultCommand = CreateCommand();
		FakeGetClassicListColumnsCommand resolvedCommand = CreateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(resolvedCommand);
		GetClassicListColumnsTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(new GetClassicListColumnsArgs("ContactSectionV2") {
			EnvironmentName = "dev"
		});

		// Assert
		response.Success.Should().BeTrue(because: "the resolved command returns a successful source-aware response");
		resolvedCommand.CapturedOptions.SchemaName.Should().Be("ContactSectionV2",
			because: "schema-name identifies the Classic section to inspect");
		resolvedCommand.CapturedOptions.Environment.Should().Be("dev",
			because: "environment-name must drive tenant-scoped command resolution");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "the startup command must not execute for an environment-scoped request");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns a typed failure when the MCP request explicitly passes null args.")]
	public void Resolve_ShouldReturnTypedFailure_WhenArgsAreNull() {
		// Arrange
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		GetClassicListColumnsTool tool = new(CreateCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(null);

		// Assert
		response.Success.Should().BeFalse(because: "args:null is invalid but must not escape as an exception");
		response.Error.Should().Contain("args", because: "the typed failure should identify the missing argument object");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve redacts backend URIs from both error and notes before returning the response to an MCP caller.")]
	public void Resolve_ShouldRedactSensitiveText_WhenCommandResponseContainsBackendUri() {
		// Arrange
		FakeGetClassicListColumnsCommand resolvedCommand = CreateCommand(new GetClassicListColumnsResponse {
			Success = false,
			Error = "POST https://secret-host.example.com/0/DataService failed",
			Notes = ["Loaded from https://secret-host.example.com/0/schema"]
		});
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(resolvedCommand);
		GetClassicListColumnsTool tool = new(CreateCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(new GetClassicListColumnsArgs("ContactSectionV2") {
			EnvironmentName = "dev"
		});

		// Assert
		response.Error.Should().Contain("[redacted-uri]",
			because: "the MCP error channel must not expose a backend URI");
		response.Error.Should().NotContain("secret-host.example.com",
			because: "the backend host is sensitive connection detail");
		response.Notes.Should().ContainSingle().Which.Should().Contain("[redacted-uri]",
			because: "notes are a second response channel and need the same redaction");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns a typed failure instead of throwing when the environment-scoped command cannot be resolved.")]
	public void Resolve_ShouldReturnTypedFailure_WhenCommandResolutionFails() {
		// Arrange — an unknown or unregistered environment-name is the most common MCP caller mistake
		FakeGetClassicListColumnsCommand defaultCommand = CreateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(_ => throw new InvalidOperationException("Environment 'ghost' is not registered"));
		GetClassicListColumnsTool tool = new(defaultCommand, ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(new GetClassicListColumnsArgs("ContactSectionV2") {
			EnvironmentName = "ghost"
		});

		// Assert
		response.Success.Should().BeFalse(because: "a resolution failure must travel as a structured payload");
		response.Error.Should().Contain("ghost",
			because: "the caller needs to see which environment could not be resolved");
		defaultCommand.CapturedOptions.Should().BeNull(
			because: "an unresolvable environment must never silently fall back to the startup command");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve forwards ignore-profile to the command so the caller can ask for the statically declared set only.")]
	public void Resolve_ShouldForwardIgnoreProfile_WhenTheArgumentIsSet() {
		// Arrange
		FakeGetClassicListColumnsCommand resolvedCommand = CreateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(resolvedCommand);
		GetClassicListColumnsTool tool = new(CreateCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(
			new GetClassicListColumnsArgs("AccountSectionV2", true) { EnvironmentName = "dev" });

		// Assert
		response.Success.Should().BeTrue(because: "forwarding a flag must not disturb the response envelope");
		resolvedCommand.CapturedOptions.IgnoreProfile.Should().BeTrue(
			because: "without forwarding, the tool would silently answer the profile-first question instead");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve reads the saved profile by default when ignore-profile is omitted from the MCP arguments.")]
	public void Resolve_ShouldDefaultToReadingTheProfile_WhenIgnoreProfileIsOmitted() {
		// Arrange — the argument is nullable so it stays optional in the tool schema; the default has to be the
		// profile-first answer, because that is the set the Classic list actually renders.
		FakeGetClassicListColumnsCommand resolvedCommand = CreateCommand();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(resolvedCommand);
		GetClassicListColumnsTool tool = new(CreateCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		tool.Resolve(new GetClassicListColumnsArgs("AccountSectionV2") { EnvironmentName = "dev" });

		// Assert
		resolvedCommand.CapturedOptions.IgnoreProfile.Should().BeFalse(
			because: "an omitted argument must not be mapped as if the caller had opted out of the profile");
	}

	[Test]
	[Category("Unit")]
	[Description("Resolve returns the profile provenance fields so a consumer can tell a shared default from a personal layout.")]
	public void Resolve_ShouldReturnProfileProvenance_WhenTheCommandResolvesFromAProfile() {
		// Arrange
		GetClassicListColumnsResponse profileResponse = new() {
			Success = true,
			SectionSchema = "AccountSectionV2",
			Entity = "Account",
			Source = "profile",
			View = "GridDataView",
			ViewType = "listed",
			ProfileScope = "shared",
			Columns = [new ClassicListColumnInfo("Name", "Name")]
		};
		FakeGetClassicListColumnsCommand resolvedCommand = CreateCommand(profileResponse);
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<GetClassicListColumnsCommand>(Arg.Any<GetClassicListColumnsOptions>())
			.Returns(resolvedCommand);
		GetClassicListColumnsTool tool = new(CreateCommand(), ConsoleLogger.Instance, commandResolver);

		// Act
		GetClassicListColumnsResponse response = tool.Resolve(
			new GetClassicListColumnsArgs("AccountSectionV2") { EnvironmentName = "dev" });

		// Assert
		response.Source.Should().Be("profile", because: "the source discriminator is the load-bearing contract");
		response.View.Should().Be("GridDataView", because: "the consumer needs the view the answer came from");
		response.ViewType.Should().Be("listed",
			because: "a grid stores two configurations, so the reported one has to be named");
		response.ProfileScope.Should().Be("shared",
			because: "this is what keeps a personal layout from being read as the section's canonical set");
	}

	private static FakeGetClassicListColumnsCommand CreateCommand(
		GetClassicListColumnsResponse response = null) => new(response);

	private sealed class FakeGetClassicListColumnsCommand : GetClassicListColumnsCommand {
		public GetClassicListColumnsOptions CapturedOptions { get; private set; }
		private readonly GetClassicListColumnsResponse _response;

		public FakeGetClassicListColumnsCommand(GetClassicListColumnsResponse response)
			: base(Substitute.For<IClassicListColumnResolver>(), ConsoleLogger.Instance) {
			_response = response;
		}

		public override bool TryResolve(
			GetClassicListColumnsOptions options,
			out GetClassicListColumnsResponse response) {
			CapturedOptions = options;
			response = _response ?? new GetClassicListColumnsResponse {
				Success = true,
				SectionSchema = options.SchemaName,
				Entity = "Contact",
				Source = "entity-default",
				Columns = [new ClassicListColumnInfo("Name", "Full name")]
			};
			return response.Success;
		}
	}
}
