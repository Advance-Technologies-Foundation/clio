using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// odata-create sends its own <c>Id</c> with a row that has none, so a second send of the same row cannot create a
/// second record. creatio.client sends a request again after a 401, a sign-in redirect or a 403 while the sign-in
/// changed, without telling clio, so the answer clio sees can be the answer to that second send.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class ODataCreateClientIdTests {

	private const string CollectionUrl = "http://creatio/odata/Account";
	private const string MetadataUrl = "http://creatio/odata/$metadata";
	private const string CallerId = "5d1f4ad8-3a4e-4f02-9e1b-7c8a2b3c4d5e";

	// What Creatio answers a POST whose Id already exists: the insert fails on the primary key.
	private const string DuplicateKeyAnswer =
		"{\"error\":{\"code\":\"\",\"message\":\"An error has occured.\",\"innererror\":{\"message\":"
		+ "\"Violation of PRIMARY KEY constraint 'PKAccount'. Cannot insert duplicate key in object 'dbo.Account'.\"}}}";

	private const string NotFoundAnswer =
		"{\"error\":{\"code\":\"\",\"message\":\"Resource not found for the segment 'Account'.\"}}";

	private const string ModelBindingRejection =
		"{\"error\":{\"code\":\"\",\"message\":\"The input was not valid.\"}}";

	private static string Csdl(string idType, bool withData = false) => $"""
		<?xml version="1.0" encoding="utf-8" standalone="no"?>
		<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
		  <edmx:DataServices>
		    <Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">
		      <EntityType Name="Account">
		        <Key><PropertyRef Name="Id" /></Key>
		        <Property Name="Id" Type="{idType}" Nullable="false" />
		        <Property Name="Name" Type="Edm.String" />
		        {(withData ? "<Property Name=\"Data\" Type=\"Edm.Stream\" />" : "")}
		      </EntityType>
		    </Schema>
		  </edmx:DataServices>
		</edmx:Edmx>
		""";

	// A read of one record that finds it: the record, with the Id taken from the URL.
	private static string Found(string url) {
		int open = url.IndexOf('(');
		int close = url.IndexOf(')');
		return $"{{\"@odata.context\":\"http://creatio/odata/$metadata#Account/$entity\",\"Id\":\"{url[(open + 1)..close]}\"}}";
	}

	[Test]
	[Description("A row without Id is sent with a new GUID Id when $metadata declares the entity's Id as Edm.Guid; the row's own fields are kept")]
	public void Create_ShouldSendAGeneratedId_WhenTheRowHasNone() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"));

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		response.Created.Should().Be(1);
		JsonElement sent = fixture.SentBodies.Should().ContainSingle().Subject;
		Guid.TryParse(sent.GetProperty("Id").GetString(), out Guid id).Should().BeTrue(
			because: "a second send of the same row must carry the same key, so the site refuses it as a duplicate");
		id.Should().NotBe(Guid.Empty);
		sent.GetProperty("Name").GetString().Should().Be("Acme");
	}

	[Test]
	[Description("Each row of a batch gets its own Id")]
	public void Create_ShouldSendADistinctId_PerRow() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"));

		// Act
		fixture.Create("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"}]");

		// Assert
		fixture.SentBodies.Select(body => body.GetProperty("Id").GetString()).Should().OnlyHaveUniqueItems()
			.And.HaveCount(2);
	}

	[TestCase("[{\"@odata.type\":\"#Terrasoft.Configuration.OData.Account\",\"Name\":\"Acme\"}]",
		TestName = "Create_ShouldAddTheId_AfterALeadingODataType")]
	[TestCase("[{ \"Name\" : \"Acme\",\n  \"Address\": { \"City\": \"Kyiv\" }, \"Tags\": [1, 2] }]",
		TestName = "Create_ShouldAddTheId_ToARowWithWhitespaceAndNestedValues")]
	[TestCase("[{\"Name\":\"Ünïcödé \\u2603\"}]", TestName = "Create_ShouldAddTheId_ToARowWithUnicode")]
	[Description("The Id is added as the row's last member: every member the caller wrote keeps its value and order, so a leading @odata.type stays first")]
	public void Create_ShouldAddTheIdAsTheLastMember(string rows) {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"));
		JsonElement written = JsonDocument.Parse(rows).RootElement[0];

		// Act
		fixture.Create(rows);

		// Assert
		JsonProperty[] sent = fixture.SentBodies.Single().EnumerateObject().ToArray();
		sent.Select(property => property.Name).Should().Equal(
			written.EnumerateObject().Select(property => property.Name).Append("Id"));
		sent.SkipLast(1).Select(property => property.Value.GetRawText()).Should().Equal(
			written.EnumerateObject().Select(property => property.Value.GetRawText()));
	}

	[TestCase("[{\"Id\":\"" + CallerId + "\",\"Name\":\"Acme\"}]", TestName = "Create_ShouldSendTheRowAsGiven_WhenItHasAnId")]
	[TestCase("[{\"id\":\"" + CallerId + "\",\"Name\":\"Acme\"}]",
		TestName = "Create_ShouldSendTheRowAsGiven_WhenItHasAnIdInAnotherCase")]
	[TestCase("[{\"Id\":null,\"Name\":\"Acme\"}]", TestName = "Create_ShouldSendTheRowAsGiven_WhenItSetsIdToNull")]
	[Description("A row that names its own Id, in any case or as null, is sent exactly as the caller wrote it")]
	public void Create_ShouldSendTheRowAsGiven_WhenItNamesItsId(string rows) {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"));
		string row = JsonDocument.Parse(rows).RootElement[0].GetRawText();

		// Act
		fixture.Create(rows);

		// Assert
		fixture.SentRaw.Should().Equal([row]);
	}

	[TestCase("Edm.Int32", TestName = "Create_ShouldSendTheRowAsGiven_WhenTheKeyIsNumeric")]
	[TestCase("Edm.String", TestName = "Create_ShouldSendTheRowAsGiven_WhenTheKeyIsText")]
	[Description("Only an Edm.Guid Id is generated: an entity keyed on another type gets the row as written")]
	public void Create_ShouldSendTheRowAsGiven_WhenTheKeyIsNotAGuid(string idType) {
		// Arrange
		Fixture fixture = new(Csdl(idType));

		// Act
		fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		fixture.SentRaw.Should().Equal(["{\"Name\":\"Acme\"}"]);
	}

	[Test]
	[Description("When $metadata cannot be read, the key type is unknown and the row is sent as written")]
	public void Create_ShouldSendTheRowAsGiven_WhenTheMetadataIsUnavailable() {
		// Arrange
		Fixture fixture = new("<html>Service unavailable</html>");

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		response.Created.Should().Be(1, because: "an unreadable $metadata never fails the insert");
		fixture.SentRaw.Should().Equal(["{\"Name\":\"Acme\"}"]);
	}

	[Test]
	[Description("A batch of rows without Id reads $metadata once, for the key type, however many rows it sends")]
	public void Create_ShouldReadTheMetadataOnce_ForABatchOfRowsWithoutId() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"));

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"},{\"Name\":\"Initech\"}]");

		// Assert
		response.Created.Should().Be(3);
		fixture.Client.Received(1).ExecuteGetRequest(MetadataUrl, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>());
		fixture.ReadBackUrls.Should().BeEmpty(because: "a created row needs no read-back");
	}

	[Test]
	[Description("A batch whose rows all carry an Id does not read $metadata: there is nothing to generate")]
	public void Create_ShouldNotReadTheMetadata_WhenEveryRowHasAnId() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"));

		// Act
		fixture.Create("[{\"Id\":\"" + CallerId + "\",\"Name\":\"Acme\"}]");

		// Assert
		fixture.Client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default, default, default, default);
	}

	[Test]
	[Description("With the type map read up front for the key, a row without Id that sets a binary column is refused before any POST")]
	public void Create_ShouldRefuseABinaryColumnBeforeThePost_WhenTheKeyCheckLoadedTheTypeMap() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid", withData: true));

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"logo.png\",\"Data\":\"iVBORw0KGgo=\"}]");

		// Assert
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeFalse();
		row.Error.Should().Contain("'Data'");
		row.Diagnostic.SideEffect.Should().Be("not-attempted");
		fixture.SentRaw.Should().BeEmpty();
	}

	[Test]
	[Description("The hidden second send: the answer clio sees is a duplicate-key error, and the record read back by the Id clio sent exists, so the row is a created row and the counters say so")]
	public void Create_ShouldReportTheRowCreated_WhenAHiddenSecondSendIsRefusedAsADuplicateKey() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer, readBack: Found);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		string sentId = fixture.SentBodies.Single().GetProperty("Id").GetString();
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeTrue(because: "the record with the Id only clio's sends of this row carry exists");
		row.Success.Should().BeTrue(because: "the row was inserted, which is what success means");
		row.Id.Should().Be(sentId);
		row.Error.Should().BeNull();
		row.RetryGuidance.Should().Contain("do NOT re-send").And.NotContain("Read the records",
			because: "clio's own advice for an unknown row must not be quoted into the guidance of a created one");
		row.Diagnostic.SideEffect.Should().Be("acknowledged");
		response.Created.Should().Be(1);
		response.Failed.Should().Be(0, because: "a caller that re-sends failed rows must not re-send this one");
		response.Unverified.Should().Be(0);
		fixture.ReadBackUrls.Should().Equal([$"{CollectionUrl}({sentId})?$select=Id"]);
		fixture.Client.Received(1).ExecuteGetRequest($"{CollectionUrl}({sentId})?$select=Id",
			Arg.Is<int>(timeout => timeout > 0 && timeout <= ODataCreateTool.ReadBackTimeoutMs), 1, 0);
	}

	[TestCase(NotFoundAnswer, TestName = "Create_ShouldKeepTheRowUnknownWithItsSentId_WhenTheReadBackFindsNothing")]
	[TestCase("<html><body>Sign in</body></html>", TestName = "Create_ShouldKeepTheRowUnknownWithItsSentId_WhenTheReadBackIsAPage")]
	[TestCase("", TestName = "Create_ShouldKeepTheRowUnknownWithItsSentId_WhenTheReadBackIsEmpty")]
	[TestCase("{\"Id\":\"" + CallerId + "\"}", TestName = "Create_ShouldKeepTheRowUnknownWithItsSentId_WhenTheReadBackIsAnotherRecord")]
	[TestCase(null, TestName = "Create_ShouldKeepTheRowUnknownWithItsSentId_WhenTheReadBackThrows")]
	[Description("Only the record itself settles the row: any other read-back answer, or a failed read, keeps record-created null and names the Id the row was sent with, so a re-send with that Id cannot create it twice")]
	public void Create_ShouldKeepTheRowUnknownWithItsSentId_WhenTheReadBackDoesNotReturnTheRecord(string readBackAnswer) {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer,
			readBack: _ => readBackAnswer ?? throw new System.Net.Http.HttpRequestException("The connection was closed."));

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		string sentId = fixture.SentBodies.Single().GetProperty("Id").GetString();
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeNull(because: "a write still in progress can land after the read");
		row.Success.Should().BeFalse();
		row.RetryGuidance.Should().Contain(sentId).And.Contain("duplicate key means the record exists");
		response.Unverified.Should().Be(1);
		JsonSerializer.Serialize(row).Should().Contain($"\"sent-id\":\"{sentId}\"");
	}

	[Test]
	[Description("A transport failure leaves the row unknown; the record read back by the sent Id settles it")]
	public void Create_ShouldReportTheRowCreated_WhenThePostFailsInTransportButTheRecordExists() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postThrows: true, readBack: Found);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeTrue();
		row.Success.Should().BeTrue();
		row.Id.Should().Be(fixture.SentBodies.Single().GetProperty("Id").GetString());
		row.RetryGuidance.Should().Contain("The connection was closed.");
	}

	[Test]
	[Description("An Id the caller chose may have existed before this call, so its read-back would prove nothing; the row stays unknown and nothing is read")]
	public void Create_ShouldNotReadBack_WhenTheCallerChoseTheId() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer, readBack: Found);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Id\":\"" + CallerId + "\",\"Name\":\"Acme\"}]");

		// Assert
		response.Results.Single().RecordCreated.Should().BeNull();
		fixture.ReadBackUrls.Should().BeEmpty();
		JsonSerializer.Serialize(response.Results.Single()).Should().NotContain("sent-id");
	}

	[Test]
	[Description("A row Creatio rejected while reading the body is known not created: no read-back, no sent-id")]
	public void Create_ShouldNotReadBack_WhenTheRowIsKnownNotCreated() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: ModelBindingRejection, readBack: Found);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		response.Results.Single().RecordCreated.Should().BeFalse();
		fixture.ReadBackUrls.Should().BeEmpty();
	}

	[Test]
	[Description("A site that answers a created row with another Id does not keep a sent Id: the later rows go without one, and the batch warns")]
	public void Create_ShouldStopSendingIds_WhenTheSiteAnswersAnotherId() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: "{\"Id\":\"22222222-2222-2222-2222-222222222222\"}");

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"}]");

		// Assert
		response.Created.Should().Be(2);
		fixture.SentRaw[1].Should().Be("{\"Name\":\"Globex\"}");
		response.Warnings.Should().ContainSingle().Which.Should().Contain("another Id");
	}

	[Test]
	[Description("When a later row shows the site does not keep a sent Id, an earlier unknown row loses its sent-id: a re-send with it would not be refused either")]
	public void Create_ShouldDropTheSentIdOfEarlierRows_WhenALaterRowShowsTheSiteDoesNotKeepIt() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"),
			postAnswers: [DuplicateKeyAnswer, "{\"Id\":\"22222222-2222-2222-2222-222222222222\"}"]);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"}]");

		// Assert
		ODataRowResult first = response.Results[0];
		first.RecordCreated.Should().BeNull();
		JsonSerializer.Serialize(first).Should().NotContain("sent-id");
		first.RetryGuidance.Should().Contain("Do NOT retry blindly");
		response.Warnings.Should().ContainSingle().Which.Should().Contain("no row of this batch carries sent-id");
	}

	[Test]
	[Description("An empty answer to a created row names no Id: the row reports the one clio sent, and the key check does not trip")]
	public void Create_ShouldReportTheSentId_WhenACreatedRowIsAnsweredWithAnEmptyBody() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswers: ["", ""]);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"},{\"Name\":\"Globex\"}]");

		// Assert
		response.Results.Select(row => row.Id).Should().Equal(
			fixture.SentBodies.Select(body => body.GetProperty("Id").GetString()));
		response.Warnings.Should().BeNull();
		fixture.SentBodies.Select(body => body.TryGetProperty("Id", out JsonElement id) && id.ValueKind == JsonValueKind.String)
			.Should().OnlyContain(hasId => hasId, because: "an empty answer is no sign that the site replaced the Id");
	}

	[Test]
	[Description("The synonym warning and the key warning are both reported")]
	public void Create_ShouldReportBothWarnings_WhenRowsComeAsASynonymAndTheSiteDoesNotKeepTheId() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: "{\"Id\":\"22222222-2222-2222-2222-222222222222\"}");

		// Act
		ODataCreateBatchResponse response = fixture.CreateAsPayload("[{\"Name\":\"Acme\"}]");

		// Assert
		response.Warnings.Should().HaveCount(2);
		response.Warnings.Should().Contain(warning => warning.Contains("payload"));
		response.Warnings.Should().Contain(warning => warning.Contains("another Id"));
	}

	[Test]
	[Description("Through the status-keeping client the product uses: the POST carries the generated Id, and a 500 duplicate-key answer is settled by the read-back while its site-response stays")]
	public void Create_ShouldSettleADuplicateKeyAnswer_ThroughTheStatusKeepingClient() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer, readBack: Found, postStatus: 500);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeTrue();
		row.SiteResponse.Status.Should().Be(500, because: "the site's own answer is still reported");
		row.RetryGuidance.Should().Contain("HTTP 500, \"An error has occured.\"",
			because: "the guidance quotes what the request got: the site's status and its own message");
		((IStatusPreservingApplicationClient)fixture.Client).Received(1).SendKeepingStatus(SiteRequestVerb.Post,
			CollectionUrl, Arg.Is<string>(body => body.Contains($"\"Id\":\"{row.Id}\"")), Arg.Any<int>(), 1,
			Arg.Any<int>(), false);
	}

	/// <summary>
	/// A client over a fake site: <c>$metadata</c> answers the given CSDL, every POST answers <c>postAnswer</c> (or
	/// throws, or answers a created record with the Id it was sent), and a read of one record answers what
	/// <c>readBack</c> returns for its URL (by default, that it does not exist).
	/// </summary>
	private sealed class Fixture {
		private readonly ODataCreateTool _tool;

		internal Fixture(string metadata, string postAnswer = null, Func<string, string> readBack = null,
			bool postThrows = false, int? postStatus = null, IReadOnlyList<string> postAnswers = null) {
			Client = postStatus is null
				? Substitute.For<IApplicationClient>()
				: Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
			IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
			urls.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
			IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
			resolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>())
				.Returns((Client, urls));
			Client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns(call => {
					string url = call.Arg<string>();
					if (url == MetadataUrl) {
						return metadata;
					}
					ReadBackUrls.Add(url);
					return (readBack ?? (_ => NotFoundAnswer))(url);
				});
			string Answer(string body) {
				SentRaw.Add(body);
				if (postThrows) {
					throw new System.Net.Http.HttpRequestException("The connection was closed.");
				}
				if (postAnswers is not null) {
					return postAnswers[SentRaw.Count - 1];
				}
				if (postAnswer is not null) {
					return postAnswer;
				}
				// Created, as Creatio answers: the record echoes the Id it was sent with, or a server-made one.
				JsonElement row = JsonDocument.Parse(body).RootElement;
				string id = row.TryGetProperty("Id", out JsonElement sentId) && sentId.ValueKind == JsonValueKind.String
					? sentId.GetString()
					: "11111111-1111-1111-1111-111111111111";
				return $"{{\"Id\":\"{id}\"}}";
			}
			if (postStatus is int status) {
				((IStatusPreservingApplicationClient)Client)
					.SendKeepingStatus(default, default, default, default, default, default, default)
					.ReturnsForAnyArgs(call => new SiteHttpResponse(status, Answer(call.ArgAt<string>(2))));
			} else {
				Client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
						Arg.Any<int>(), Arg.Any<int>())
					.Returns(call => Answer(call.ArgAt<string>(1)));
			}
			_tool = new ODataCreateTool(resolver, new OperationCorrelationIdProvider(),
				new ODataFileContract(Substitute.For<System.IO.Abstractions.IFileSystem>(),
					new MockConfinedFileAccess(Substitute.For<System.IO.Abstractions.IFileSystem>())));
		}

		internal IApplicationClient Client { get; }

		internal List<string> SentRaw { get; } = [];

		internal IEnumerable<JsonElement> SentBodies => SentRaw.Select(body => JsonDocument.Parse(body).RootElement);

		internal List<string> ReadBackUrls { get; } = [];

		internal ODataCreateBatchResponse Create(string rows) =>
			_tool.Create(new ODataCreateArgs {
				EnvironmentName = "dev", Entity = "Account", Rows = JsonDocument.Parse(rows).RootElement.Clone()
			});

		// The rows go as the synonym "payload", which the tool accepts and names in a warning.
		internal ODataCreateBatchResponse CreateAsPayload(string rows) =>
			_tool.Create(new ODataCreateArgs {
				EnvironmentName = "dev", Entity = "Account",
				ExtensionData = new Dictionary<string, JsonElement> {
					["payload"] = JsonDocument.Parse(rows).RootElement.Clone()
				}
			});
	}
}
