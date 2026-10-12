using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ODataReadColumnValidationTests {
	private const string ValidBody = "{\"@odata.context\":\"http://creatio/odata/$metadata#Activity\",\"value\":[]}";

	private const string Csdl = """
		<?xml version="1.0" encoding="utf-8" standalone="no"?>
		<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
		  <edmx:DataServices>
		    <Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">
		      <EntityType Name="BaseEntity">
		        <Property Name="Id" Type="Edm.Guid" />
		        <Property Name="CreatedOn" Type="Edm.DateTimeOffset" />
		      </EntityType>
		      <EntityType Name="Activity" BaseType="Terrasoft.Configuration.OData.BaseEntity">
		        <Property Name="Title" Type="Edm.String" />
		        <Property Name="ActivityCategoryId" Type="Edm.Guid" />
		        <NavigationProperty Name="ActivityCategory" Type="Terrasoft.Configuration.OData.ActivityCategory" />
		      </EntityType>
		      <EntityType Name="ActivityCategory" BaseType="Terrasoft.Configuration.OData.BaseEntity">
		        <Property Name="Name" Type="Edm.String" />
		      </EntityType>
		      <EntityType Name="SysModule" BaseType="Terrasoft.Configuration.OData.BaseEntity">
		        <Property Name="Caption" Type="Edm.String" />
		        <Property Name="Code" Type="Edm.String" />
		      </EntityType>
		    </Schema>
		  </edmx:DataServices>
		</edmx:Edmx>
		""";

	private IApplicationClient _client;
	private int _dataRequests;
	private string _metadataBody;
	private int _metadataRequests;

	[SetUp]
	public void SetUp() {
		ODataReadColumnValidation.ClearCache();
		_dataRequests = 0;
		_metadataRequests = 0;
		_metadataBody = Csdl;
	}

	private ODataReadTool BuildTool() {
		_client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(_client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns(call => {
				if (call.Arg<string>().Contains("$metadata")) {
					_metadataRequests++;
					return _metadataBody;
				}
				_dataRequests++;
				return ValidBody;
			});
		return new ODataReadTool(resolver, new OperationCorrelationIdProvider(), Substitute.For<ILogger>());
	}

	private static JsonElement Cols(params string[] names) => JsonSerializer.SerializeToElement(names);

	private static ODataReadArgs Args(string entity, JsonElement? select = null, ODataFilters filters = null,
		string orderBy = null, JsonElement? expand = null) =>
		filters is null
			? new() { Entity = entity, EnvironmentName = "dev-col", Select = select, OrderBy = orderBy, Expand = expand }
			: new() {
				Entity = entity, EnvironmentName = "dev-col", Select = select, Filters = filters, OrderBy = orderBy,
				Expand = expand
			};

	private static ODataFilters Filter(string field) =>
		new() { All = [new ODataFilterCondition { Field = field, Op = "eq", Value = JsonSerializer.SerializeToElement("x") }] };

	[Test]
	[Category("Unit")]
	[Description("CategoryId on Activity is rejected locally and ActivityCategoryId is suggested; no data request is sent.")]
	public void Read_Should_Suggest_ActivityCategoryId_For_CategoryId() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Id", "CategoryId")));

		// Assert
		response.Success.Should().BeFalse(because: "CategoryId is not an Activity column");
		response.ErrorCode.Should().Be("invalid-query", because: "it is a query-shape failure");
		response.UnknownMembers.Should().ContainSingle(because: "only one name is unknown");
		response.UnknownMembers![0].Name.Should().Be("CategoryId", because: "the caller's spelling is echoed");
		response.UnknownMembers[0].Suggestions.Should().Contain("ActivityCategoryId",
			because: "the valid column contains the unknown name");
		response.ValidColumnCount.Should().Be(4, because: "Id, CreatedOn, Title, ActivityCategoryId");
		_dataRequests.Should().Be(0, because: "nothing may be sent to Creatio for an unknown column");
	}

	[Test]
	[Category("Unit")]
	[Description("SysModuleCaption on SysModule suggests Caption (entity-name prefix stripped) and a filter on an unknown column is caught too.")]
	public void Read_Should_Suggest_Caption_For_SysModuleCaption_And_Check_Filters() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("SysModule", select: Cols("SysModuleCaption"),
			filters: Filter("SysPackageId")));

		// Assert
		response.UnknownMembers.Should().HaveCount(2, because: "one select name and one filter field are unknown");
		response.UnknownMembers![0].Suggestions.Should().Contain("Caption", because: "the prefix-stripped name matches");
		response.UnknownMembers[1].Argument.Should().Be("filters", because: "the filter field is reported as such");
		_dataRequests.Should().Be(0, because: "the query is refused locally");
	}

	[Test]
	[Category("Unit")]
	[Description("Navigation paths, expand and order-by are validated; a misspelled member behind a navigation gets a path suggestion.")]
	public void Read_Should_Validate_Navigation_Paths_Expand_And_OrderBy() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", filters: Filter("ActivityCategory/Nmae"),
			orderBy: "Titel desc", expand: Cols("ActivityCategori")));

		// Assert
		response.UnknownMembers.Should().HaveCount(3, because: "filter path, order-by and expand are all misspelled");
		response.UnknownMembers!.Single(m => m.Argument == "filters").Suggestions.Should()
			.Contain("ActivityCategory/Name", because: "the member behind the navigation is suggested");
		response.UnknownMembers.Single(m => m.Argument == "order-by").Suggestions.Should().Contain("Title",
			because: "it is one edit away");
		response.UnknownMembers.Single(m => m.Argument == "expand").Suggestions.Should().Contain("ActivityCategory",
			because: "expand suggests navigations");
	}

	[Test]
	[Category("Unit")]
	[Description("A lookup written as ActivityCategory/Id is valid; the raw ActivityCategoryId column is valid; a valid query is sent.")]
	public void Read_Should_Send_A_Valid_Query() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Id", "ActivityCategoryId", "ActivityCategory/Name"),
			filters: Filter("ActivityCategory/Id"), orderBy: "CreatedOn desc"));

		// Assert
		response.Success.Should().BeTrue(because: "every name is on the entity (CreatedOn is inherited)");
		_dataRequests.Should().Be(1, because: "a valid query is sent exactly once");
	}

	[Test]
	[Category("Unit")]
	[Description("When the metadata cannot be fetched the query is sent unchecked.")]
	public void Read_Should_Fall_Back_To_Sending_The_Query_When_Metadata_Is_Unavailable() {
		// Arrange
		ODataReadTool tool = BuildTool();
		_metadataBody = string.Empty;

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("CategoryId")));

		// Assert
		response.Success.Should().BeTrue(because: "an unverifiable name must never block the query: " + response.Error);
		_dataRequests.Should().Be(1, because: "the query goes out as it did before validation existed");
	}

	[Test]
	[Category("Unit")]
	[Description("A metadata document without the entity also falls back.")]
	public void Read_Should_Fall_Back_When_Metadata_Lacks_The_Entity() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		_metadataBody = Csdl.Replace("EntityType Name=\"Activity\" ", "EntityType Name=\"Other\" ");
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Whatever")));

		// Assert
		response.Success.Should().BeTrue(because: "no verdict is possible without the type");
		_dataRequests.Should().Be(1, because: "the query is sent");
	}

	[Test]
	[Category("Unit")]
	[Description("Metadata is cached per entity and environment; an unknown name against a cached shape triggers one refresh.")]
	public void Read_Should_Cache_Metadata_And_Refresh_Once_On_Unknown_Name() {
		// Arrange
		ODataReadTool tool = BuildTool();
		tool.Read(Args("Activity", select: Cols("Title")));
		tool.Read(Args("Activity", select: Cols("Id")));

		// Act
		int afterValid = _metadataRequests;
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Nope")));

		// Assert
		afterValid.Should().Be(1, because: "the second valid read reuses the cached shape");
		_metadataRequests.Should().Be(2, because: "an unknown name against a cached shape refreshes the metadata once");
		response.Success.Should().BeFalse(because: "the refreshed metadata still lacks the column");
	}

	[Test]
	[Category("Unit")]
	[Description("A name that differs from a valid one only by letter case is passed to the server.")]
	public void Read_Should_Not_Block_Case_Only_Differences() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("title")));

		// Assert
		response.Success.Should().BeTrue(because: "case sensitivity is the service's decision");
	}

	[Test]
	[Category("Unit")]
	[Description("A query with nothing to check does not fetch metadata.")]
	public void Read_Should_Not_Fetch_Metadata_When_Nothing_Is_Referenced() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		tool.Read(Args("Activity"));

		// Assert
		_metadataRequests.Should().Be(0, because: "the multi-megabyte document is only fetched when a name needs checking");
	}
}
