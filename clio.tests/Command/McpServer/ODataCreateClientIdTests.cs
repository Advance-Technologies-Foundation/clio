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

	// What Creatio answers a POST whose Id already exists: the insert fails on the primary key.
	private const string DuplicateKeyAnswer =
		"{\"error\":{\"code\":\"\",\"message\":\"An error has occured.\",\"innererror\":{\"message\":"
		+ "\"Violation of PRIMARY KEY constraint 'PKAccount'. Cannot insert duplicate key in object 'dbo.Account'.\"}}}";

	private const string NotFoundAnswer =
		"{\"error\":{\"code\":\"\",\"message\":\"Resource not found for the segment 'Account'.\"}}";

	private const string ModelBindingRejection =
		"{\"error\":{\"code\":\"\",\"message\":\"The input was not valid.\"}}";

	private static string Csdl(string idType) => $"""
		<?xml version="1.0" encoding="utf-8" standalone="no"?>
		<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
		  <edmx:DataServices>
		    <Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">
		      <EntityType Name="Account">
		        <Key><PropertyRef Name="Id" /></Key>
		        <Property Name="Id" Type="{idType}" Nullable="false" />
		        <Property Name="Name" Type="Edm.String" />
		      </EntityType>
		    </Schema>
		  </edmx:DataServices>
		</edmx:Edmx>
		""";

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

	[TestCase("[{\"Id\":\"5d1f4ad8-3a4e-4f02-9e1b-7c8a2b3c4d5e\",\"Name\":\"Acme\"}]",
		TestName = "Create_ShouldSendTheRowAsGiven_WhenItHasAnId")]
	[TestCase("[{\"id\":\"5d1f4ad8-3a4e-4f02-9e1b-7c8a2b3c4d5e\",\"Name\":\"Acme\"}]",
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
		fixture.Create("[{\"Id\":\"5d1f4ad8-3a4e-4f02-9e1b-7c8a2b3c4d5e\",\"Name\":\"Acme\"}]");

		// Assert
		fixture.Client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(default, default, default, default);
	}

	[Test]
	[Description("The hidden second send: the answer clio sees is a duplicate-key error, and the record read back by the Id clio sent exists, so the row is created and must not be re-sent")]
	public void Create_ShouldReportTheRowCreated_WhenAHiddenSecondSendIsRefusedAsADuplicateKey() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer, recordExists: true);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		string sentId = fixture.SentBodies.Single().GetProperty("Id").GetString();
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeTrue(because: "the record with the Id only clio's sends of this row carry exists");
		row.Id.Should().Be(sentId);
		row.Success.Should().BeFalse(because: "the site's answer was still an error, and the result keeps it");
		row.RetryGuidance.Should().Contain("do NOT re-send");
		row.Diagnostic.SideEffect.Should().Be("acknowledged");
		response.Unverified.Should().Be(0, because: "the read-back settled the row");
		fixture.ReadBackUrls.Should().Equal([$"{CollectionUrl}({sentId})?$select=Id"]);
	}

	[Test]
	[Description("A row whose answer is unknown and whose record is not found keeps record-created null, and names the Id it was sent with so a re-send with that Id cannot create it twice")]
	public void Create_ShouldKeepTheRowUnknownWithItsSentId_WhenTheReadBackFindsNothing() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer, recordExists: false);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		string sentId = fixture.SentBodies.Single().GetProperty("Id").GetString();
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeNull(because: "a write still in progress can land after the read");
		row.RetryGuidance.Should().Contain(sentId);
		response.Unverified.Should().Be(1);
		JsonSerializer.Serialize(row).Should().Contain($"\"sent-id\":\"{sentId}\"");
	}

	[Test]
	[Description("A transport failure leaves the row unknown; the record read back by the sent Id settles it")]
	public void Create_ShouldReportTheRowCreated_WhenThePostFailsInTransportButTheRecordExists() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postThrows: true, recordExists: true);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeTrue();
		row.Id.Should().Be(fixture.SentBodies.Single().GetProperty("Id").GetString());
	}

	[Test]
	[Description("An Id the caller chose may have existed before this call, so its read-back would prove nothing; the row stays unknown and nothing is read")]
	public void Create_ShouldNotReadBack_WhenTheCallerChoseTheId() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer, recordExists: true);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Id\":\"5d1f4ad8-3a4e-4f02-9e1b-7c8a2b3c4d5e\",\"Name\":\"Acme\"}]");

		// Assert
		response.Results.Single().RecordCreated.Should().BeNull();
		fixture.ReadBackUrls.Should().BeEmpty();
		JsonSerializer.Serialize(response.Results.Single()).Should().NotContain("sent-id");
	}

	[Test]
	[Description("A row Creatio rejected while reading the body is known not created: no read-back, no sent-id")]
	public void Create_ShouldNotReadBack_WhenTheRowIsKnownNotCreated() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: ModelBindingRejection, recordExists: true);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		response.Results.Single().RecordCreated.Should().BeFalse();
		fixture.ReadBackUrls.Should().BeEmpty();
	}

	[Test]
	[Description("Through the status-keeping client the product uses: the POST carries the generated Id, and a 500 duplicate-key answer is settled by the read-back")]
	public void Create_ShouldSettleADuplicateKeyAnswer_ThroughTheStatusKeepingClient() {
		// Arrange
		Fixture fixture = new(Csdl("Edm.Guid"), postAnswer: DuplicateKeyAnswer, recordExists: true, postStatus: 500);

		// Act
		ODataCreateBatchResponse response = fixture.Create("[{\"Name\":\"Acme\"}]");

		// Assert
		ODataRowResult row = response.Results.Single();
		row.RecordCreated.Should().BeTrue();
		row.SiteResponse.Status.Should().Be(500, because: "the site's own answer is still reported");
		((IStatusPreservingApplicationClient)fixture.Client).Received(1).SendKeepingStatus(SiteRequestVerb.Post,
			CollectionUrl, Arg.Is<string>(body => body.Contains($"\"Id\":\"{row.Id}\"")), Arg.Any<int>(), 1,
			Arg.Any<int>(), false);
	}

	/// <summary>
	/// A client over a fake site: <c>$metadata</c> answers the given CSDL, every POST answers <c>postAnswer</c> (or
	/// throws), and a read of one record answers it when <c>recordExists</c>, echoing the Id from the URL.
	/// </summary>
	private sealed class Fixture {
		private readonly ODataCreateTool _tool;

		internal Fixture(string metadata, string postAnswer = null, bool recordExists = false, bool postThrows = false,
			int? postStatus = null) {
			Client = postStatus is null
				? Substitute.For<IApplicationClient>()
				: Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
			IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
			urls.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
			IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
			resolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>())
				.Returns((Client, urls));
			Client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns(call => AnswerGet(call.Arg<string>(), metadata, recordExists));
			string Answer(string body) {
				SentRaw.Add(body);
				if (postThrows) {
					throw new System.Net.Http.HttpRequestException("The connection was closed.");
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

		private string AnswerGet(string url, string metadata, bool recordExists) {
			if (url == MetadataUrl) {
				return metadata;
			}
			ReadBackUrls.Add(url);
			int open = url.IndexOf('(');
			int close = url.IndexOf(')');
			return recordExists && open > 0 && close > open
				? $"{{\"@odata.context\":\"http://creatio/odata/$metadata#Account/$entity\",\"Id\":\"{url[(open + 1)..close]}\"}}"
				: NotFoundAnswer;
		}
	}
}
