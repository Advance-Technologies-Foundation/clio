using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Unit coverage for the <c>place-section-in-workplace</c> MCP tool: safety flags, argument mapping onto the
/// environment-scoped service, defaults, argument validation, and error redaction.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public class PlaceSectionInWorkplaceToolTests {

	private const string EnvironmentName = "dev";

	[Test]
	[Description("Publishes the tool as a destructive, idempotent, non-read-only write so the host confirms it.")]
	public void PlaceSectionInWorkplaceTool_ShouldDeclareDestructiveIdempotentFlags() {
		// Arrange & Act
		McpServerToolAttribute attribute = typeof(PlaceSectionInWorkplaceTool)
			.GetMethod(nameof(PlaceSectionInWorkplaceTool.PlaceSectionInWorkplace))!
			.GetCustomAttribute<McpServerToolAttribute>()!;

		// Assert
		attribute.Name.Should().Be(PlaceSectionInWorkplaceTool.ToolName, because: "the tool is published under its constant");
		attribute.ReadOnly.Should().BeFalse(because: "the tool writes navigation rows and bindings");
		attribute.Destructive.Should().BeTrue(because: "it moves a placement and grants visibility, like the binding tools");
		attribute.Idempotent.Should().BeTrue(because: "a re-run converges and creates nothing new");
		attribute.OpenWorld.Should().BeFalse(because: "it only touches the addressed environment");
	}

	[Test]
	[Description("Is not resident in tools/list, so the byte budget of the resident surface is unaffected.")]
	public void PlaceSectionInWorkplaceTool_ShouldNotBeResident() {
		// Act
		bool resident = Clio.Command.McpServer.McpCoreToolProfile.CoreToolTypes.Contains(typeof(PlaceSectionInWorkplaceTool));

		// Assert
		resident.Should().BeFalse(because: "the tool is long-tail and reached through clio-run");
	}

	[Test]
	[Description("Forwards every argument to the resolved service and returns its result with the re-login next step.")]
	public void PlaceSectionInWorkplace_ShouldForwardArgumentsAndReturnResult() {
		// Arrange
		IWorkplacePlacementService service = Substitute.For<IWorkplacePlacementService>();
		WorkplacePlacementRequest captured = null;
		service.Place(Arg.Do<WorkplacePlacementRequest>(request => captured = request)).Returns(SampleResult());
		PlaceSectionInWorkplaceTool tool = CreateTool(service);

		// Act
		PlaceSectionInWorkplaceResponse response = tool.PlaceSectionInWorkplace(new PlaceSectionInWorkplaceArgs(
			EnvironmentName, "UsrOrders", "Orders", "UsrOrdersApp", "mobile", ["Sales managers"], false, 3, "new", "portal"));

		// Assert
		response.Success.Should().BeTrue(because: response.Error ?? "the service succeeded");
		response.Workplace.Name.Should().Be("Orders", because: "the service result is returned as is");
		response.NextStep.Should().Contain("log out", because: "navigation lists are cached per session");
		captured.Should().Be(new WorkplacePlacementRequest(EnvironmentName, "UsrOrdersApp", "UsrOrders", "Orders",
			"mobile", captured.Roles, false, 3, "new", "portal"), because: "every scalar argument is forwarded unchanged");
		captured.Roles.Should().Equal(["Sales managers"], because: "the roles are forwarded");
	}

	[Test]
	[Description("Applies the defaults: web client type, no roles, and moving the My applications placement.")]
	public void PlaceSectionInWorkplace_ShouldApplyDefaults_WhenOptionalArgumentsAreOmitted() {
		// Arrange
		IWorkplacePlacementService service = Substitute.For<IWorkplacePlacementService>();
		WorkplacePlacementRequest captured = null;
		service.Place(Arg.Do<WorkplacePlacementRequest>(request => captured = request)).Returns(SampleResult());
		PlaceSectionInWorkplaceTool tool = CreateTool(service);

		// Act
		tool.PlaceSectionInWorkplace(new PlaceSectionInWorkplaceArgs(EnvironmentName, "UsrOrders", "Orders"));

		// Assert
		captured.ClientType.Should().Be("web", because: "web is the default client type");
		captured.Roles.Should().BeEmpty(because: "no role is granted unless asked");
		captured.RemoveFromMyApplications.Should().BeTrue(because: "a new app should leave My applications");
		captured.PackageName.Should().BeNull(because: "the service resolves the current package like get-target-package");
		captured.Position.Should().BeNull(because: "the platform keeps its position unless one is given");
		captured.Mode.Should().BeNull(because: "an omitted mode means reuse-or-create");
		captured.WorkplaceType.Should().BeNull(because: "an omitted type keeps the platform default and checks nothing");
	}

	[TestCase(null, "UsrOrders", "Orders", "environment-name")]
	[TestCase(EnvironmentName, null, "Orders", "section-code")]
	[TestCase(EnvironmentName, "UsrOrders", " ", "workplace-name")]
	[Description("Fails with a structured error naming the missing required argument, before the service runs.")]
	public void PlaceSectionInWorkplace_ShouldFail_WhenRequiredArgumentIsMissing(string environment, string code,
		string workplace, string expectedField) {
		// Arrange
		IWorkplacePlacementService service = Substitute.For<IWorkplacePlacementService>();
		PlaceSectionInWorkplaceTool tool = CreateTool(service);

		// Act
		PlaceSectionInWorkplaceResponse response =
			tool.PlaceSectionInWorkplace(new PlaceSectionInWorkplaceArgs(environment, code, workplace));

		// Assert
		response.Success.Should().BeFalse(because: "a required argument is missing");
		response.Error.Should().Contain(expectedField, because: "the message names the missing argument");
		service.DidNotReceiveWithAnyArgs().Place(default);
	}

	[Test]
	[Description("Accepts mode=none without a workplace name and returns where the section is, with no re-login step.")]
	public void PlaceSectionInWorkplace_ShouldReportOnly_WhenModeIsNone() {
		// Arrange
		IWorkplacePlacementService service = Substitute.For<IWorkplacePlacementService>();
		WorkplaceLocation location = new(Guid.NewGuid(), "My applications");
		service.Place(Arg.Any<WorkplacePlacementRequest>())
			.Returns(new WorkplacePlacementResult(null, null, null, [], [], [location]));
		PlaceSectionInWorkplaceTool tool = CreateTool(service);

		// Act
		PlaceSectionInWorkplaceResponse response = tool.PlaceSectionInWorkplace(
			new PlaceSectionInWorkplaceArgs(EnvironmentName, "UsrOrders", Mode: "none"));

		// Assert
		response.Success.Should().BeTrue(because: response.Error ?? "reporting needs no workplace name");
		response.SectionWorkplaces.Should().Equal([location], because: "the caller always learns where the section is");
		response.NextStep.Should().BeNull(because: "nothing changed, so there is nothing to log in again for");
	}

	[TestCase("sectionCode", "section-code")]
	[TestCase("workplace", "workplace-name")]
	[TestCase("workplaceType", "workplace-type")]
	[TestCase("workplace_type", "workplace-type")]
	[TestCase("type", "workplace-type")]
	[Description("Rejects a legacy argument spelling with a rename hint instead of dropping it.")]
	public void PlaceSectionInWorkplace_ShouldRejectALegacyAlias(string legacyName, string canonicalName) {
		// Arrange
		IWorkplacePlacementService service = Substitute.For<IWorkplacePlacementService>();
		PlaceSectionInWorkplaceTool tool = CreateTool(service);
		PlaceSectionInWorkplaceArgs args = new(EnvironmentName, "UsrOrders", "Orders") {
			ExtensionData = new() { [legacyName] = JsonDocument.Parse("\"x\"").RootElement }
		};

		// Act
		PlaceSectionInWorkplaceResponse response = tool.PlaceSectionInWorkplace(args);

		// Assert
		response.Success.Should().BeFalse(because: "a misspelled argument must not be silently ignored");
		response.Error.Should().Contain(canonicalName, because: "the hint names the canonical argument");
		service.DidNotReceiveWithAnyArgs().Place(default);
	}

	[Test]
	[Description("Turns a service failure into a structured error with the host redacted.")]
	public void PlaceSectionInWorkplace_ShouldReturnRedactedFailure_WhenServiceThrows() {
		// Arrange
		IWorkplacePlacementService service = Substitute.For<IWorkplacePlacementService>();
		service.Place(Arg.Any<WorkplacePlacementRequest>()).Throws(new InvalidOperationException(
			"SelectQuery failed at http://creatio.local:8080/0/DataService"));
		PlaceSectionInWorkplaceTool tool = CreateTool(service);

		// Act
		PlaceSectionInWorkplaceResponse response =
			tool.PlaceSectionInWorkplace(new PlaceSectionInWorkplaceArgs(EnvironmentName, "UsrOrders", "Orders"));

		// Assert
		response.Success.Should().BeFalse(because: "the service failed");
		response.Error.Should().NotContain("creatio.local", because: "the host must not cross the MCP boundary");
		response.NextStep.Should().BeNull(because: "a failure has no re-login step");
		response.WritesStarted.Should().BeNull(because: "a thrown failure comes before the first write");
	}

	[Test]
	[Description("Reports a failure after the first write as success=false with writes-started, the written rows and the re-login step, so the caller does not read it as 'nothing changed'.")]
	public void PlaceSectionInWorkplace_ShouldReturnWrittenRows_WhenServiceFailsAfterFirstWrite() {
		// Arrange
		Guid workplaceId = Guid.NewGuid();
		IWorkplacePlacementService service = Substitute.For<IWorkplacePlacementService>();
		service.Place(Arg.Any<WorkplacePlacementRequest>()).Returns(new WorkplacePlacementResult("UsrOrdersApp", null,
			null, [], ["reset failed"], null, "Tell the user to log out and back in.",
			"Upsert failed at http://creatio.local:8080/0/DataService",
			[new WorkplacePlacementWrite("SysWorkplace", workplaceId, "SysWorkplace_Orders")]));
		PlaceSectionInWorkplaceTool tool = CreateTool(service);

		// Act
		PlaceSectionInWorkplaceResponse response =
			tool.PlaceSectionInWorkplace(new PlaceSectionInWorkplaceArgs(EnvironmentName, "UsrOrders", "Orders"));
		string json = JsonSerializer.Serialize(response);

		// Assert
		response.Success.Should().BeFalse(because: "the call did not finish");
		response.WritesStarted.Should().BeTrue(because: "rows were written before the failure");
		response.Written.Should().ContainSingle(write => write.Id == workplaceId,
			because: "the caller must learn which rows already exist");
		response.Error.Should().NotContain("creatio.local", because: "the host must not cross the MCP boundary");
		response.NextStep.Should().Contain("log out", because: "the written rows are visible only after a re-login");
		json.Should().Contain("\"writes-started\":true").And.Contain("\"written\"",
			because: "the contract names the fields in kebab-case");
	}

	private static WorkplacePlacementResult SampleResult() =>
		new("UsrOrdersApp",
			new WorkplacePlacementWorkplace(Guid.NewGuid(), "Orders", "Web", "general", 4, true, "SysWorkplace_Orders"),
			new WorkplacePlacementSection(Guid.NewGuid(), "UsrOrders", Guid.NewGuid(), "moved",
				"SysModuleInWorkplace_UsrOrders"),
			[],
			[],
			[],
			"Tell the user to log out and back in.");

	private static PlaceSectionInWorkplaceTool CreateTool(IWorkplacePlacementService service) {
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IWorkplacePlacementService>(Arg.Any<EnvironmentOptions>()).Returns(service);
		return new PlaceSectionInWorkplaceTool(ConsoleLogger.Instance, commandResolver);
	}
}
