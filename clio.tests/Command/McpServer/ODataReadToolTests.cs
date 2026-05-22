using System;
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

		// Arrange (continued)
		JsonElement nameValue = JsonDocument.Parse("\"John\"").RootElement.Clone();

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Filters = new ODataFilters {
				All = [new ODataFilterCondition { Field = "Name", Op = "eq", Value = nameValue }]
			},
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

	[Test]
	[Category("Unit")]
	[Description("Structured filter with a GUID value in an Id-suffixed field must produce an unquoted GUID in the OData URL.")]
	public void Read_Should_Build_Unquoted_Guid_Filter_From_Structured_Filters() {
		// Arrange
		const string guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://host/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);
		JsonElement guidValue = JsonDocument.Parse($"\"{guid}\"").RootElement.Clone();

		// Act
		tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Filters = new ODataFilters {
				All = [new ODataFilterCondition { Field = "AccountId", Op = "eq", Value = guidValue }]
			}
		});

		// Assert — GUID in an Id-suffixed field must not be single-quoted
		urlBuilder.Received(1).Build("odata/Contact?$filter=AccountId%20eq%208ecab4a1-0ca3-4515-9399-efe0a19390bd&$top=25");
	}

	[Test]
	[Category("Unit")]
	[Description("Structured filter with a GUID value in an Id-suffixed navigation path must produce an unquoted GUID in the OData URL.")]
	public void Read_Should_Build_Unquoted_Guid_Filter_For_Navigation_Path() {
		// Arrange
		const string guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://host/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);
		JsonElement guidValue = JsonDocument.Parse($"\"{guid}\"").RootElement.Clone();

		// Act
		tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Filters = new ODataFilters {
				All = [new ODataFilterCondition { Field = "Account/Id", Op = "eq", Value = guidValue }]
			}
		});

		// Assert
		urlBuilder.Received(1).Build(
			"odata/Contact?$filter=Account%2FId%20eq%208ecab4a1-0ca3-4515-9399-efe0a19390bd&$top=25");
	}

	[Test]
	[Category("Unit")]
	[Description("Structured filter with a string value must produce a single-quoted string in the OData URL.")]
	public void Read_Should_Build_Single_Quoted_String_Filter_From_Structured_Filters() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://host/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);
		JsonElement stringValue = JsonDocument.Parse("\"Acme\"").RootElement.Clone();

		// Act
		tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Filters = new ODataFilters {
				All = [new ODataFilterCondition { Field = "Name", Op = "eq", Value = stringValue }]
			}
		});

		// Assert — non-Id string value must be wrapped in single quotes (%27)
		urlBuilder.Received(1).Build("odata/Account?$filter=Name%20eq%20%27Acme%27&$top=25");
	}

	[Test]
	[Category("Unit")]
	[Description("Structured filter with a number value must produce an unquoted numeric literal in the OData URL.")]
	public void Read_Should_Build_Unquoted_Number_Filter_From_Structured_Filters() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://host/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);
		JsonElement numberValue = JsonDocument.Parse("1000000").RootElement.Clone();

		// Act
		tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Account",
			Filters = new ODataFilters {
				All = [new ODataFilterCondition { Field = "AnnualRevenue", Op = "ge", Value = numberValue }]
			}
		});

		// Assert — numeric values must not be quoted
		urlBuilder.Received(1).Build("odata/Account?$filter=AnnualRevenue%20ge%201000000&$top=25");
	}

	[Test]
	[Category("Unit")]
	[Description("Structured in-array condition must expand to OR-joined equality clauses enclosed in parentheses in the OData URL.")]
	public void Read_Should_Expand_In_Array_To_Or_Joined_Conditions() {
		// Arrange
		const string guid1 = "11111111-1111-1111-1111-111111111111";
		const string guid2 = "22222222-2222-2222-2222-222222222222";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://host/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);
		JsonElement inArray = JsonDocument.Parse($"[\"{guid1}\",\"{guid2}\"]").RootElement.Clone();

		// Act
		tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Filters = new ODataFilters {
				All = [new ODataFilterCondition { Field = "StatusId", InValues = inArray }]
			}
		});

		// Assert — (StatusId eq guid1 or StatusId eq guid2)
		// %28=%28, %20=space, %29=)
		urlBuilder.Received(1).Build(
			"odata/Contact?$filter=%28StatusId%20eq%2011111111-1111-1111-1111-111111111111%20or%20StatusId%20eq%2022222222-2222-2222-2222-222222222222%29&$top=25");
	}

	[Test]
	[Category("Unit")]
	[Description("Multiple all conditions AND-join and multiple any conditions OR-join, then the two groups combine with AND.")]
	public void Read_Should_Combine_All_And_Any_Conditions_Into_Compound_Filter() {
		// Arrange
		const string guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://host/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);
		JsonElement guidValue = JsonDocument.Parse($"\"{guid}\"").RootElement.Clone();
		JsonElement trueValue = JsonDocument.Parse("true").RootElement.Clone();
		JsonElement status1 = JsonDocument.Parse("\"Active\"").RootElement.Clone();
		JsonElement status2 = JsonDocument.Parse("\"Pending\"").RootElement.Clone();

		// Act
		tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Filters = new ODataFilters {
				All = [
					new ODataFilterCondition { Field = "AccountId", Op = "eq", Value = guidValue },
					new ODataFilterCondition { Field = "IsActive", Op = "eq", Value = trueValue }
				],
				Any = [
					new ODataFilterCondition { Field = "Status", Op = "eq", Value = status1 },
					new ODataFilterCondition { Field = "Status", Op = "eq", Value = status2 }
				]
			}
		});

		// Assert — (AccountId eq guid and IsActive eq true) and (Status eq 'Active' or Status eq 'Pending')
		string expected = Uri.EscapeDataString(
			$"(AccountId eq {guid} and IsActive eq true) and (Status eq 'Active' or Status eq 'Pending')");
		urlBuilder.Received(1).Build($"odata/Contact?$filter={expected}&$top=25");
	}

	[Test]
	[Category("Unit")]
	[Description("When filters object is provided but contains no conditions the $filter parameter must be omitted from the URL.")]
	public void Read_Should_Omit_Filter_When_Structured_Filters_Produce_No_Conditions() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://host/{call.Arg<string>()}");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"value\":[]}");
		ODataReadTool tool = new(commandResolver);

		// Act
		tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Filters = new ODataFilters { All = null, Any = null },
			Top = 5
		});

		// Assert — no $filter when structured filters produce no conditions
		urlBuilder.Received(1).Build("odata/Contact?$top=5");
	}
}
