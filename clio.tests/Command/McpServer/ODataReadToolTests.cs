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
[Property("Module", "McpServer")]
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

	[Test]
	[Category("Unit")]
	[Description("Rejects top=0 with a structured validation failure instead of silently returning the default page.")]
	public void Read_Should_Reject_Top_When_Zero() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Top = 0
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "top=0 is out of the documented 1-100 range and must not silently return the default page");
		response.Error.Should().Contain("top must be between 1 and 100",
			because: "the failure should explain the accepted top range");
		client.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a negative top so it can never silently fall through to an unbounded read.")]
	public void Read_Should_Reject_Top_When_Negative() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Top = -5
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a negative top is invalid and must be rejected, never silently treated as unbounded");
		response.Error.Should().Contain("-5",
			because: "the failure should echo the rejected value so the caller can see what was wrong");
		client.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a top above the documented maximum instead of issuing an unclamped query.")]
	public void Read_Should_Reject_Top_When_Above_Maximum() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Top = 9999
		});

		// Assert
		response.Success.Should().BeFalse(
			because: "a top above 100 exceeds the documented range and must be rejected rather than sent unclamped");
		response.Error.Should().Contain("top must be between 1 and 100",
			because: "the failure should explain the accepted top range");
		client.DidNotReceive().ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A valid in-range top is honored and forwarded as the OData $top value (regression guard).")]
	public void Read_Should_Honor_Valid_In_Range_Top() {
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
		ODataReadResponse response = tool.Read(new ODataReadArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Top = 50
		});

		// Assert
		response.Success.Should().BeTrue(
			because: "a top within the 1-100 range is valid and must still produce a query");
		// A valid in-range top must be forwarded verbatim as the OData $top value.
		urlBuilder.Received(1).Build("odata/Contact?$top=50");
	}

	[Test]
	[Category("Unit")]
	[Description("A server error body (ASP.NET EDM model NullReferenceException) is reported as a failure, not wrapped as a single-entity success.")]
	public void Read_Should_Surface_Server_Error_As_Failure() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/AddressType?$top=25");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"An error has occurred.\",\"ExceptionMessage\":\"Object reference not set to an instance of an object.\",\"ExceptionType\":\"System.NullReferenceException\",\"StackTrace\":\"   at Terrasoft.Web.OData.ODataEntityModelBuilder...\"}");
		ODataReadTool tool = new(commandResolver);

		ODataReadResponse response = tool.Read(new ODataReadArgs { EnvironmentName = "dev", Entity = "AddressType" });

		response.Success.Should().BeFalse(
			because: "an ASP.NET server error body must not be reported as a successful single-entity read");
		response.Error.Should().Contain("Object reference",
			because: "the ExceptionMessage should be surfaced to the caller");
	}

	[Test]
	[Category("Unit")]
	[Description("A Web API routing error body ({Message, MessageDetail}) for an unregistered controller is reported as a failure with the unregistered-entity hint, not wrapped as a single-entity success.")]
	public void Read_Should_Surface_Routing_Error_As_Failure() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/0/odata/UsrCustomerStatus?$top=25");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"No HTTP resource was found that matches the request URI '.../0/odata/UsrCustomerStatus'.\",\"MessageDetail\":\"No type was found that matches the controller named 'UsrCustomerStatus'.\"}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs { EnvironmentName = "dev", Entity = "UsrCustomerStatus" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a {Message, MessageDetail} 404 routing body must not be reported as a successful single-entity read");
		response.Error.Should().Contain("controller named 'UsrCustomerStatus'",
			because: "the MessageDetail should be surfaced so the caller sees the unregistered-controller cause");
		response.Error.Should().Contain(ODataResponseError.UnregisteredEntityHint,
			because: "the unregistered-entity hint (asserted via the shared constant to avoid literal drift) should steer the agent to wait-and-retry, not read this as a data gap");
	}

	[Test]
	[Category("Unit")]
	[Description("A bare {Message} body without MessageDetail is reported as a failure but without the unregistered-entity hint, so an unrelated error's cause is not misattributed to registration.")]
	public void Read_Should_Surface_Bare_Message_Body_As_Failure_Without_Registration_Hint() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/0/odata/Contact?$top=25");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"Authorization has been denied for this request.\"}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs { EnvironmentName = "dev", Entity = "Contact" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a bare {Message} body with no entity members is an error, not a single-entity record");
		response.Error.Should().Contain("Authorization has been denied",
			because: "the Message text should be surfaced verbatim to the caller");
		response.Error.Should().NotContain(ODataResponseError.UnregisteredEntityHint,
			because: "without MessageDetail the failure is not identifiable as a routing error, so the registration hint must not be appended");
	}

	[Test]
	[Category("Unit")]
	[Description("A real single-entity response that legitimately carries a Message column is returned as data (success:true), proving the routing-error heuristic does not misfire on genuine records.")]
	public void Read_Should_Not_Misclassify_Single_Entity_With_Message_Column_As_Error() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/odata/EmailMessageData?$top=1");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"@odata.context\":\"http://creatio/odata/$metadata#EmailMessageData/$entity\",\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Message\":\"Hello there\"}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs { EnvironmentName = "dev", Entity = "EmailMessageData", Top = 1 });

		// Assert
		response.Success.Should().BeTrue(
			because: "a single-entity body carrying @odata.context and other members is real data, even when it contains a column named Message");
		response.Count.Should().Be(1,
			because: "a single-entity response without a value wrapper counts as one record");
		response.Value!.Value.TryGetProperty("Message", out JsonElement messageColumn).Should().BeTrue(
			because: "the entity payload must be preserved verbatim, including its Message column");
		messageColumn.GetString().Should().Be("Hello there",
			because: "the real Message column value must not be swallowed by the routing-error detection");
	}

	[Test]
	[Category("Unit")]
	[Description("The absolute request URI carried by a bare-Message routing body (the shape hardened sites return once MessageDetail is stripped) is redacted before reaching the caller, matching the sibling error paths.")]
	public void Read_Should_Redact_Server_Uri_In_Routing_Error() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://secret-host:88/prod-app/0/odata/UsrCustomerStatus?$top=25");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"No HTTP resource was found that matches the request URI 'http://secret-host:88/prod-app/0/odata/UsrCustomerStatus'.\"}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs { EnvironmentName = "dev", Entity = "UsrCustomerStatus" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a bare-Message routing body must still be surfaced as a failure");
		response.Error.Should().NotContain("secret-host",
			because: "the environment host embedded in the routing Message must be redacted like every sibling error path, not leaked into the transcript");
	}

	[Test]
	[Category("Unit")]
	[Description("A {Message, MessageDetail} body whose content is NOT a routing miss (e.g. a validation error) is surfaced as a failure WITHOUT the wait-and-retry hint, so an unrelated non-transient failure is not misattributed to entity registration.")]
	public void Read_Should_Surface_NonRouting_Message_Detail_Without_Registration_Hint() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/0/odata/Contact?$top=25");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"The request is invalid.\",\"MessageDetail\":\"The value 'x' is not valid for property Name.\"}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs { EnvironmentName = "dev", Entity = "Contact" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a {Message, MessageDetail} body is still an error, not a single-entity record");
		response.Error.Should().Contain("not valid for property Name",
			because: "the MessageDetail should be surfaced so the caller sees the actual cause");
		response.Error.Should().NotContain(ODataResponseError.UnregisteredEntityHint,
			because: "the content is not a routing miss, so the wait-and-retry registration hint must NOT be appended to an unrelated failure");
	}

	[Test]
	[Category("Unit")]
	[Description("An empty {Message} body degrades to an explicit contentless-response message with no hint, mirroring the create-path guard so the empty-body fallback is covered on both paths.")]
	public void Read_Should_Surface_Empty_Message_Body_As_Failure_With_Explicit_Text() {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver commandResolver = Substitute.For<IToolCommandResolver>();
		commandResolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		commandResolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio/0/odata/Contact?$top=25");
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Message\":\"\"}");
		ODataReadTool tool = new(commandResolver);

		// Act
		ODataReadResponse response = tool.Read(new ODataReadArgs { EnvironmentName = "dev", Entity = "Contact" });

		// Assert
		response.Success.Should().BeFalse(
			because: "a body whose only member is an empty Message is an error, not data");
		response.Error.Should().Be("Creatio returned an empty error response.",
			because: "an empty error body must degrade to an explicit contentless message rather than an empty string");
		response.Error.Should().NotContain(ODataResponseError.UnregisteredEntityHint,
			because: "an empty body is not identifiable as a routing miss, so the registration hint must not be appended");
	}
}
