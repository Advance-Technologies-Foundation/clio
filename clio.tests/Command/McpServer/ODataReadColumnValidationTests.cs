using System;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

	private const string InvalidQueryBody =
		"{\"error\":{\"code\":\"\",\"message\":\"The query specified in the URI is not valid. Could not find a property named 'X' on type 'Terrasoft.Configuration.OData.Activity'.\"}}";

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
		        <NavigationProperty Name="Account" Type="Terrasoft.Configuration.OData.Account" />
		      </EntityType>
		      <EntityType Name="Account" BaseType="Terrasoft.Configuration.OData.BaseEntity">
		        <Property Name="Name" Type="Edm.String" />
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
	private string _dataBody;
	private int _dataRequests;
	private string _metadataBody;
	private int _metadataRequests;

	[SetUp]
	public void SetUp() {
		_dataBody = InvalidQueryBody;
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
				return _dataBody;
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
	[Description("When Creatio rejects CategoryId on Activity, the rejection names it and suggests ActivityCategoryId.")]
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
		_dataRequests.Should().Be(1, because: "the query is sent once and the metadata explains its rejection");
		_metadataRequests.Should().Be(1, because: "only the rejected query fetches the metadata");
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

		_dataBody = ValidBody;

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Id", "ActivityCategoryId", "ActivityCategory/Name"),
			filters: Filter("ActivityCategory/Id"), orderBy: "CreatedOn desc"));

		// Assert
		response.Success.Should().BeTrue(because: "every name is on the entity (CreatedOn is inherited)");
		_dataRequests.Should().Be(1, because: "a valid query is sent exactly once");
		_metadataRequests.Should().Be(0,
			because: "an accepted query must not pay for the multi-megabyte metadata document");
	}

	[Test]
	[Category("Unit")]
	[Description("When the metadata cannot be fetched the server's rejection is returned without unknown-members.")]
	public void Read_Should_Return_The_Server_Rejection_When_Metadata_Is_Unavailable() {
		// Arrange
		ODataReadTool tool = BuildTool();
		_metadataBody = string.Empty;

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("CategoryId")));

		// Assert
		response.ErrorCode.Should().Be("invalid-query", because: "the server's classification stands");
		response.UnknownMembers.Should().BeNull(because: "no verdict is possible without the metadata");
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
		response.ErrorCode.Should().Be("invalid-query", because: "the server's classification stands");
		response.UnknownMembers.Should().BeNull(because: "no verdict is possible without the type");
	}

	[Test]
	[Category("Unit")]
	[Description("A two-segment expand must end in a navigation; a mis-cased navigation still gets a member suggestion behind it.")]
	public void Read_Should_Reject_Expand_Ending_In_A_Column_And_Suggest_Behind_MisCased_Navigation() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("activitycategory/Nmae"),
			expand: Cols("ActivityCategory/Name")));

		// Assert
		response.UnknownMembers.Should().HaveCount(2, because: "the expand path ends in a column and the select member is misspelled");
		response.UnknownMembers!.Single(m => m.Argument == "select").Suggestions.Should()
			.Contain("ActivityCategory/Name", because: "the navigation is matched case-insensitively and spelled as the metadata spells it");
	}

	[Test]
	[Category("Unit")]
	[Description("A name that differs from a valid one only by letter case is never reported as unknown.")]
	public void Read_Should_Not_Block_Case_Only_Differences() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("title")));

		// Assert
		response.UnknownMembers.Should().BeNull(because: "a case-only difference counts as known");
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
		_dataRequests.Should().Be(1, because: "the query itself is still sent");
	}
	[TestCase("filters")]
	[TestCase("order-by")]
	[Category("Unit")]
	[Description("QA ENG-103020: in a filter or order-by a lookup is suggested as its navigation path ActivityCategory/Id, never as the ActivityCategoryId column Creatio rejects there.")]
	public void Read_Should_Suggest_Navigation_Path_For_Lookup_In_Filter_And_OrderBy(string argument) {
		// Arrange
		ODataReadTool tool = BuildTool();
		ODataReadArgs args = argument == "filters"
			? Args("Activity", filters: Filter("CategoryId"))
			: Args("Activity", orderBy: "CategoryId desc");

		// Act
		ODataReadResponse response = tool.Read(args);

		// Assert
		ODataUnknownMember member = response.UnknownMembers.Should().ContainSingle().Subject;
		member.Argument.Should().Be(argument, because: "the name is reported where it was sent");
		member.Suggestions.Should().Contain("ActivityCategory/Id",
			because: "the navigation path is the form Creatio accepts in a filter and order-by");
		member.Suggestions.Should().NotContain("ActivityCategoryId",
			because: "the scalar lookup column is rejected again in this context");
	}

	[Test]
	[Category("Unit")]
	[Description("QA ENG-103020: a filter on the scalar lookup column ActivityCategoryId is reported with the navigation path ActivityCategory/Id.")]
	public void Read_Should_Report_Scalar_Lookup_Column_In_Filter() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", filters: Filter("ActivityCategoryId")));

		// Assert
		ODataUnknownMember member = response.UnknownMembers.Should().ContainSingle(
			because: "Creatio rejects the scalar column in a filter").Subject;
		member.Suggestions[0].Should().Be("ActivityCategory/Id", because: "the usable form comes first");
		member.Suggestions.Should().NotContain("ActivityCategoryId", because: "it is the rejected name itself");
	}

	[Test]
	[Category("Unit")]
	[Description("In select the scalar lookup column stays the suggestion; the navigation path is a filter-only correction.")]
	public void Read_Should_Keep_Scalar_Suggestion_In_Select() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("CategoryId")));

		// Assert
		response.UnknownMembers!.Single().Suggestions.Should().Contain("ActivityCategoryId",
			because: "the scalar column is valid in select").And.NotContain("ActivityCategory/Id",
			because: "select keeps the scalar form");
	}

	[Test]
	[Category("Unit")]
	[Description("QA ENG-103020: a misspelled navigation keeps the rest of the path (Acount/Name -> Account/Name), and a short expand prefix finds the navigation (Acc -> Account).")]
	public void Read_Should_Keep_Path_Tail_And_Match_Short_Prefix() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Acount/Name"), expand: Cols("Acc")));

		// Assert
		response.UnknownMembers!.Single(m => m.Argument == "select").Suggestions[0].Should().Be("Account/Name",
			because: "the navigation is corrected and the member behind it is kept");
		response.UnknownMembers.Single(m => m.Argument == "expand").Suggestions.Should().Contain("Account",
			because: "Acc is a prefix of the Account navigation");
	}
	[Test]
	[Category("Unit")]
	[Description("A bare navigation in a filter is reported with its navigation path, and the message says the lookup exists but is not usable there.")]
	public void Read_Should_Report_Bare_Navigation_In_Filter_As_Wrong_Form() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", filters: Filter("ActivityCategory")));

		// Assert
		response.UnknownMembers!.Single().Suggestions[0].Should().Be("ActivityCategory/Id",
			because: "Creatio filters a lookup only through its navigation path");
		response.Error.Should().Contain("not usable in filters",
			because: "the name exists on the type, so the message must not claim it is missing");
	}

	[Test]
	[Category("Unit")]
	[Description("A misspelled navigation whose member matches nothing behind it falls back to the navigation alone, never to a path known to be invalid.")]
	public void Read_Should_Not_Suggest_A_Known_Invalid_Tail() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Acount/Zzzzzz")));

		// Assert
		response.UnknownMembers!.Single().Suggestions.Should().Contain("Account",
			because: "the navigation itself is the safe correction").And.NotContain("Account/Zzzzzz",
			because: "Account has no such member");
	}

	[Test]
	[Category("Unit")]
	[Description("Review #1798: the metadata explanation is APPENDED to the server's rejection, so the rejection's own text and the list of names sent survive when the query also failed for another reason.")]
	public void Read_Should_Append_The_Explanation_To_The_Rejection() {
		// Arrange
		ODataReadTool baselineTool = BuildTool();
		_metadataBody = string.Empty;
		ODataReadArgs args = Args("Activity", select: Cols("Id", "CategoryId"), filters: Filter("AccountId"));
		string rejection = baselineTool.Read(args).Error;
		_metadataBody = Csdl;
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(args);

		// Assert
		response.Error.Should().StartWith(rejection,
			because: "the rejection's own sentence, the names sent and the lookup hint must all survive");
		response.Error.Should().Contain("Names this request sent",
			because: "the caller still needs the list of names that went out");
		response.Error.Should().Contain("Checked against the OData metadata of Activity",
			because: "the unknown-name explanation follows the rejection");
		response.Error.Length.Should().BeGreaterThan(rejection.Length,
			because: "the explanation is added, not substituted");
		response.UnknownMembers.Should().NotBeNullOrEmpty(because: "the structured list is still reported");
	}

	[Test]
	[Category("Unit")]
	[Description("Review #1798: a declared *Id column with no navigation of the same stem (ExternalId) is an ordinary column and is never reported in a filter; a scalar lookup column is reported only because its navigation exists.")]
	public void Read_Should_Not_Report_A_Declared_Id_Column_Without_A_Navigation_In_Filter() {
		// Arrange
		_metadataBody = Csdl.Replace("<Property Name=\"Title\" Type=\"Edm.String\" />",
			"<Property Name=\"Title\" Type=\"Edm.String\" /><Property Name=\"ExternalId\" Type=\"Edm.String\" />");
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", select: Cols("Titel"), filters: new ODataFilters {
			All = [
				new ODataFilterCondition { Field = "ExternalId", Op = "eq", Value = JsonSerializer.SerializeToElement("x") },
				new ODataFilterCondition { Field = "ActivityCategoryId", Op = "eq", Value = JsonSerializer.SerializeToElement("x") }
			]
		}));

		// Assert
		response.UnknownMembers.Select(m => m.Name).Should().BeEquivalentTo(["Titel", "ActivityCategoryId"],
			because: "ExternalId has no External navigation, so it is a plain column Creatio filters on directly");
		response.Error.Should().Contain("'ExternalId' is an ordinary column of Activity",
			because: "the name-only lookup hint in the rejection tells the caller to rewrite it, and the metadata proves that wrong");
		response.Error.Should().NotContain("'ActivityCategoryId' is an ordinary column",
			because: "a scalar column with a navigation of the same stem is a lookup");
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-103117: every name is valid (ExternalId is a plain string column) and the query is rejected for an operand-type mismatch: the plain-column correction is still shown and no unknown members are reported.")]
	public void Read_Should_Show_The_Plain_Column_Correction_When_No_Name_Is_Unknown() {
		// Arrange
		_metadataBody = Csdl.Replace("<Property Name=\"Title\" Type=\"Edm.String\" />",
			"<Property Name=\"Title\" Type=\"Edm.String\" /><Property Name=\"ExternalId\" Type=\"Edm.String\" />");
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", filters: new ODataFilters {
			All = [new ODataFilterCondition { Field = "ExternalId", Op = "eq", Value = JsonSerializer.SerializeToElement(123) }]
		}));

		// Assert
		response.Error.Should().Contain("'ExternalId' is an ordinary column of Activity",
			because: "the rejection's name-only hint suggests External/Id and the metadata proves that wrong");
		response.Error.Should().Contain("the rejection has another cause",
			because: "no name is unknown, and the explanation must not assert which other cause applies");
		response.Error.Should().NotContain("operand types", because: "the cause was never verified");
		response.UnknownMembers.Should().BeNull(because: "nothing is unknown");
		response.ValidColumnCount.Should().BeNull(because: "it is present only with unknown-members");
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-103117: a plain Id column used only in order-by gets no correction, because the lookup hint is built from filters only.")]
	public void Read_Should_Return_The_Rejection_Unchanged_When_Plain_Id_Column_Is_Only_In_OrderBy() {
		// Arrange
		ODataReadTool baselineTool = BuildTool();
		_metadataBody = string.Empty;
		ODataReadArgs args = Args("Activity", orderBy: "ExternalId");
		string rejection = baselineTool.Read(args).Error;
		_metadataBody = Csdl.Replace("<Property Name=\"Title\" Type=\"Edm.String\" />",
			"<Property Name=\"Title\" Type=\"Edm.String\" /><Property Name=\"ExternalId\" Type=\"Edm.String\" />");
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(args);

		// Assert
		response.Error.Should().Be(rejection, because: "no lookup hint was generated for an order-by name");
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-103117: every name is valid and a real lookup is filtered through its navigation path: no explanation is added, the rejection is returned unchanged.")]
	public void Read_Should_Return_The_Rejection_Unchanged_When_Names_Are_Valid_And_No_Plain_Id_Column() {
		// Arrange
		ODataReadTool baselineTool = BuildTool();
		_metadataBody = string.Empty;
		ODataReadArgs args = Args("Activity", filters: Filter("ActivityCategory/Id"));
		string rejection = baselineTool.Read(args).Error;
		_metadataBody = Csdl;
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(args);

		// Assert
		response.Error.Should().Be(rejection, because: "nothing in the metadata contradicts the request");
		response.UnknownMembers.Should().BeNull(because: "no name is unknown");
	}

	[Test]
	[Category("Unit")]
	[Description("ENG-103117: a real lookup column in a filter still gets its /Id hint next to an ordinary Id column's correction.")]
	public void Read_Should_Keep_The_Lookup_Hint_Beside_The_Plain_Column_Correction() {
		// Arrange
		_metadataBody = Csdl.Replace("<Property Name=\"Title\" Type=\"Edm.String\" />",
			"<Property Name=\"Title\" Type=\"Edm.String\" /><Property Name=\"ExternalId\" Type=\"Edm.String\" />");
		ODataReadTool tool = BuildTool();

		// Act
		ODataReadResponse response = tool.Read(Args("Activity", filters: new ODataFilters {
			All = [
				new ODataFilterCondition { Field = "ExternalId", Op = "eq", Value = JsonSerializer.SerializeToElement(123) },
				new ODataFilterCondition { Field = "ActivityCategoryId", Op = "eq", Value = JsonSerializer.SerializeToElement("x") }
			]
		}));

		// Assert
		response.UnknownMembers.Should().ContainSingle(because: "only the lookup is reported").Which
			.Suggestions[0].Should().Be("ActivityCategory/Id");
		response.Error.Should().Contain("'ExternalId' is an ordinary column");
		response.Error.Should().NotContain("'ActivityCategoryId' is an ordinary column");
	}

	[Test]
	[Category("Unit")]
	[Description("Review #1798: the metadata is fetched once per rejected query, as one attempt bounded by MetadataTimeoutMs.")]
	public void Read_Should_Fetch_Metadata_Once_With_A_Bounded_Timeout() {
		// Arrange
		ODataReadTool tool = BuildTool();

		// Act
		tool.Read(Args("Activity", select: Cols("CategoryId")));

		// Assert
		_metadataRequests.Should().Be(1, because: "one rejected query costs exactly one metadata fetch");
		_client.Received(1).ExecuteGetRequest(Arg.Is<string>(url => url.Contains("$metadata")),
			ODataReadColumnValidation.MetadataTimeoutMs, 1, Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("Review #1798: odata-read-to-file explains an invalid-query rejection the same way odata-read does and writes no file.")]
	public void ReadToFile_Should_Explain_An_Invalid_Query_Like_The_Inline_Read() {
		// Arrange
		MockFileSystem fileSystem = new();
		string outputFile = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"odata-col-{Guid.NewGuid():N}.json");
		ICreatioApplicationClient client = Substitute.For<ICreatioApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		client.ExecuteGetRequestBoundedAsync(
				Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
			.Returns(Task.FromResult(Encoding.UTF8.GetBytes(InvalidQueryBody)));
		client.ExecuteGetRequest(Arg.Is<string>(url => url.Contains("$metadata")), Arg.Any<int>(), Arg.Any<int>(),
			Arg.Any<int>()).Returns(Csdl);
		ODataReadToFileTool tool = new(resolver, new OperationCorrelationIdProvider(),
			new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataReadResponse response = tool.ReadToFile(new ODataReadToFileArgs {
			EnvironmentName = "dev-col", Entity = "Activity", OutputFile = outputFile,
			Select = Cols("Id", "CategoryId")
		});

		// Assert
		response.ErrorCode.Should().Be("invalid-query", because: "the server's classification stands");
		response.UnknownMembers.Should().ContainSingle(because: "CategoryId is the one unknown name")
			.Which.Suggestions.Should().Contain("ActivityCategoryId", because: "both read paths suggest the same names");
		response.ValidColumnCount.Should().Be(4, because: "the column count is reported on both paths");
		response.Error.Should().Contain("Creatio rejected this OData query as invalid",
			because: "the rejection's own sentence is kept");
		fileSystem.File.Exists(outputFile).Should().BeFalse(because: "a rejected read never publishes a file");
	}
}
