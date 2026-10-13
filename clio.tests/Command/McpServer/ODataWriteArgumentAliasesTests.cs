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
public sealed class ODataWriteArgumentAliasesTests {
	private const string RecordId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	private const string Csdl = """
		<?xml version="1.0" encoding="utf-8" standalone="no"?>
		<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
		  <edmx:DataServices>
		    <Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">
		      <EntityType Name="Contact">
		        <Property Name="Id" Type="Edm.Guid" />
		        <Property Name="Name" Type="Edm.String" />
		      </EntityType>
		    </Schema>
		  </edmx:DataServices>
		</edmx:Edmx>
		""";

	private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

	private static IODataFileContract NoFiles() => Substitute.For<IODataFileContract>();

	private static (IApplicationClient Client, IToolCommandResolver Resolver) Wire() {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		urlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns((client, urlBuilder));
		return (client, resolver);
	}

	private static ODataCreateArgs CreateArgs(string extra, string rows = null) =>
		JsonSerializer.Deserialize<ODataCreateArgs>(
			"{\"entity\":\"Contact\",\"environment-name\":\"dev\"" + (rows is null ? "" : ",\"rows\":" + rows)
			+ (extra.Length > 0 ? "," + extra : "") + "}")!;

	private static ODataUpdateArgs UpdateArgs(string extra, string data = null) =>
		JsonSerializer.Deserialize<ODataUpdateArgs>(
			"{\"entity\":\"Contact\",\"id\":\"" + RecordId + "\",\"environment-name\":\"dev\",\"confirm\":true"
			+ (data is null ? "" : ",\"data\":" + data) + (extra.Length > 0 ? "," + extra : "") + "}")!;

	[TestCase("payload")]
	[TestCase("values")]
	[TestCase("body")]
	[TestCase("data")]
	[Category("Unit")]
	[Description("Every create synonym executes: a single object becomes one row and warnings names rows.")]
	public void Create_Should_Accept_Synonym_And_Wrap_A_Single_Object(string alias) {
		// Arrange
		(IApplicationClient client, IToolCommandResolver resolver) = Wire();
		client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataCreateBatchResponse response = tool.Create(CreateArgs($"\"{alias}\":{{\"Name\":\"Acme\"}}"));

		// Assert
		response.Error.Should().BeNull(because: "the synonym is unambiguous");
		response.Created.Should().Be(1, because: "the object became one row");
		response.Warnings.Should().ContainSingle().Which.Should().Contain("'rows'", because: "the canonical name is reported");
		client.Received(1).ExecuteNonReplayablePostRequest(Arg.Any<string>(), "{\"Name\":\"Acme\"}",
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
	}

	[Test]
	[Category("Unit")]
	[Description("A synonym holding an array is used as the rows array.")]
	public void Create_Should_Accept_Synonym_Array() {
		// Arrange
		(IApplicationClient client, IToolCommandResolver resolver) = Wire();
		client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
			.Returns("{\"Id\":\"11111111-1111-1111-1111-111111111111\"}");
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataCreateBatchResponse response = tool.Create(CreateArgs("\"values\":[{\"Name\":\"A\"},{\"Name\":\"B\"}]"));

		// Assert
		response.Created.Should().Be(2, because: "both rows are posted");
	}

	[TestCase("\"payload\":{\"Name\":\"x\"}", "[{\"Name\":\"a\"}]")]
	[TestCase("\"payload\":{\"Name\":\"x\"},\"body\":{\"Name\":\"y\"}", null)]
	[TestCase("\"payload\":{\"Name\":\"x\"},\"rows-file\":\"f.json\"", null)]
	[Category("Unit")]
	[Description("Ambiguous create inputs are rejected and nothing is posted.")]
	public void Create_Should_Reject_Ambiguous_Payloads(string extra, string rows) {
		// Arrange
		(IApplicationClient client, IToolCommandResolver resolver) = Wire();
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataCreateBatchResponse response = tool.Create(CreateArgs(extra, rows));

		// Assert
		response.Error.Should().Contain("Ambiguous", because: "two payloads were supplied");
		client.DidNotReceiveWithAnyArgs().ExecuteNonReplayablePostRequest(default, default, default, default, default);
	}

	[TestCase("values")]
	[TestCase("payload")]
	[TestCase("body")]
	[TestCase("fields")]
	[Category("Unit")]
	[Description("Every update synonym executes the PATCH and warnings names data.")]
	public void Update_Should_Accept_Synonym(string alias) {
		// Arrange
		(IApplicationClient client, IToolCommandResolver resolver) = Wire();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(Csdl);
		client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>()).Returns(string.Empty);
		ODataUpdateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataWriteResponse response = tool.Update(UpdateArgs($"\"{alias}\":{{\"Name\":\"New\"}}"));

		// Assert
		response.Success.Should().BeTrue(because: response.Error);
		response.Warnings.Should().ContainSingle().Which.Should().Contain("'data'", because: "the canonical name is reported");
		client.Received(1).ExecutePatchRequest(Arg.Any<string>(), "{\"Name\":\"New\"}", Arg.Any<int>());
	}

	[TestCase("\"values\":{\"Name\":\"x\"}", "{\"Name\":\"a\"}")]
	[TestCase("\"values\":{\"Name\":\"x\"},\"fields\":{\"Name\":\"y\"}", null)]
	[TestCase("\"values\":{\"Name\":\"x\"},\"rows-file\":\"f.json\"", null)]
	[Category("Unit")]
	[Description("Ambiguous update inputs are rejected and nothing is patched.")]
	public void Update_Should_Reject_Ambiguous_Payloads(string extra, string data) {
		// Arrange
		(IApplicationClient client, IToolCommandResolver resolver) = Wire();
		ODataUpdateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataWriteResponse response = tool.Update(UpdateArgs(extra, data));

		// Assert
		response.Success.Should().BeFalse(because: "two payloads were supplied");
		response.Error.Should().Contain("Ambiguous", because: "the refusal names the conflict");
		client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("The legacy rename rejection of other tools is untouched: BuildLegacyAliasError is not changed.")]
	public void Unknown_Create_Argument_Is_Still_Rejected() {
		// Arrange
		(IApplicationClient _, IToolCommandResolver resolver) = Wire();
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataCreateBatchResponse response = tool.Create(CreateArgs("\"bogus\":1", "[{\"Name\":\"a\"}]"));

		// Assert
		response.Error.Should().Contain("Unknown args", because: "only the payload synonyms are accepted");
	}
	[Test]
	[Category("Unit")]
	[Description("An update whose synonym is read but whose call is then refused (unknown field) does not claim the synonym was accepted.")]
	public void Update_Refused_After_Synonym_Should_Not_Say_Accepted() {
		// Arrange
		(IApplicationClient client, IToolCommandResolver resolver) = Wire();
		client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(Csdl);
		ODataUpdateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataWriteResponse response = tool.Update(UpdateArgs("\"values\":{\"Bogus\":\"New\"}"));

		// Assert
		response.Success.Should().BeFalse(because: "Bogus is not a Contact field");
		response.Warnings.Should().ContainSingle().Which.Should().NotContain("accepted",
			because: "a refused call accepted nothing").And.Contain("refused", because: "the note says the call was refused")
			.And.Contain("'data'", because: "the canonical name is still taught");
		client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(default, default, default);
	}

	[Test]
	[Category("Unit")]
	[Description("A create whose synonym is read but whose request is refused (unknown argument) does not claim the synonym was accepted.")]
	public void Create_Refused_After_Synonym_Should_Not_Say_Accepted() {
		// Arrange
		(IApplicationClient client, IToolCommandResolver resolver) = Wire();
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(), NoFiles());

		// Act
		ODataCreateBatchResponse response = tool.Create(CreateArgs("\"payload\":{\"Name\":\"x\"},\"bogus\":1"));

		// Assert
		response.Error.Should().NotBeNull(because: "bogus is an unknown argument");
		response.Warnings.Should().ContainSingle().Which.Should().NotContain("accepted",
			because: "a refused call accepted nothing").And.Contain("'rows'", because: "the canonical name is still taught");
		client.DidNotReceiveWithAnyArgs().ExecuteNonReplayablePostRequest(default, default, default, default, default);
	}
}
