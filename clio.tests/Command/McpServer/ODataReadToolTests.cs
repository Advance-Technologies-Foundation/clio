using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
public sealed class ODataReadToolTests {
	[Test]
	[Category("Unit")]
	[Description("Advertises a stable read-only MCP tool name for odata-read.")]
	public void Read_Should_Advertise_Stable_Tool_Name() {
		// Arrange

		// Act
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataReadTool)
			.GetMethod(nameof(ODataReadTool.Read))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		// Assert
		attribute.Name.Should().Be(ODataReadTool.ToolName,
			because: "the MCP tool name must stay stable for callers and tests");
		attribute.ReadOnly.Should().BeTrue(
			because: "odata-read only queries Creatio records");
		attribute.Destructive.Should().BeFalse(
			because: "odata-read must not mutate remote Creatio state");
	}

	[Test]
	[Category("Unit")]
	[Description("Builds an escaped OData URL from query arguments and returns the value array from the OData response.")]
	public void Read_Should_Query_OData_And_Return_Value_Array() {
		// Arrange
		IApplicationClient applicationClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder serviceUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(applicationClient);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(serviceUrlBuilder);
		serviceUrlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		applicationClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Name\":\"John\"}]}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Filter = "Name eq 'John'",
			Select = ["Id", "Name"],
			Top = 1
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "valid OData responses should return a successful structured payload");
		response.Count.Should().Be(1,
			because: "the response should report the returned array length");
		response.Value!.Value.ValueKind.Should().Be(JsonValueKind.Array,
			because: "OData collection responses should preserve the value array");
		serviceUrlBuilder.Received(1).Build("odata/Contact?$filter=Name%20eq%20%27John%27&$select=Id%2CName&$top=1");
		applicationClient.Received(1).ExecuteGetRequest(
			"http://creatio/odata/Contact?$filter=Name%20eq%20%27John%27&$select=Id%2CName&$top=1",
			30_000,
			1,
			1);
	}

	[Test]
	[Category("Unit")]
	[Description("Resolves environment-scoped client dependencies when odata-read receives an environment name.")]
	public void Read_Should_Resolve_Environment_Scoped_Client_When_Environment_Is_Provided() {
		// Arrange
		IApplicationClient environmentClient = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder environmentUrlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(environmentClient);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(environmentUrlBuilder);
		environmentUrlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://env/{call.Arg<string>()}");
		environmentClient.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact"
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "the environment-scoped client response should be returned when resolution succeeds");
		commandResolver.Received(1).Resolve<IApplicationClient>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
		commandResolver.Received(1).Resolve<IServiceUrlBuilder>(Arg.Is<EnvironmentOptions>(options =>
			options.Environment == "dev"));
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a structured validation failure when the required entity argument is missing.")]
	public void Read_Should_Return_Failure_When_Entity_Is_Missing() {
		// Arrange
		ODataReadTool tool = new(Substitute.For<IToolCommandResolver>());

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = " "
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "missing entity names should be reported without attempting remote calls");
		response.Error.Should().Be("entity is required.",
			because: "the failure should identify the missing required argument");
	}
}
