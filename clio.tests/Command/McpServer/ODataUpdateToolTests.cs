using System;
using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using ModelContextProtocol.Server;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

[TestFixture]
[Property("Module", "McpServer")]
public sealed class ODataUpdateToolTests {
	private const string Guid = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";
	private const string MetadataUrl = "http://creatio/odata/Contact/$metadata";
	private const string KeyUrl = "http://creatio/odata/Contact(8ecab4a1-0ca3-4515-9399-efe0a19390bd)";

	private static JsonElement Obj(string json) => JsonDocument.Parse(json).RootElement.Clone();

	/// <summary>
	/// Minimal CSDL 4.0 document: Contact carries Name/JobTitle (plain), SomeGuid (plain Guid) and
	/// AccountId (a lookup — the Account navigation property declares <c>Partner="AccountId"</c>),
	/// plus the Account type.
	/// </summary>
	private static string CsdL() => $"""
		<?xml version="1.0" encoding="utf-8" standalone="no"?>
		<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
		  <edmx:DataServices>
		    <Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">
		      <EntityType Name="Contact">
		        <Key><PropertyRef Name="Id" /></Key>
		        <Property Name="Id" Type="Edm.Guid" Nullable="false" />
		        <Property Name="Name" Type="Edm.String" />
		        <Property Name="JobTitle" Type="Edm.String" />
		        <Property Name="SomeGuid" Type="Edm.Guid" />
		        <Property Name="AccountId" Type="Edm.Guid" />
		        <NavigationProperty Name="Account" Partner="AccountId" Type="Terrasoft.Configuration.OData.Account" />
		      </EntityType>
		      <EntityType Name="Account">
		        <Key><PropertyRef Name="Id" /></Key>
		        <Property Name="Id" Type="Edm.Guid" Nullable="false" />
		        <Property Name="Name" Type="Edm.String" />
		      </EntityType>
		    </Schema>
		  </edmx:DataServices>
		</edmx:Edmx>
		""";

	/// <summary>CSDL that does not declare Contact at all (type-missing → probe fallback).</summary>
	private const string CsdLWithoutContact = """
		<?xml version="1.0" encoding="utf-8" standalone="no"?>
		<edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx">
		  <edmx:DataServices>
		    <Schema Namespace="Terrasoft.Configuration.OData" xmlns="http://docs.oasis-open.org/odata/ns/edm">
		      <EntityType Name="Account">
		        <Key><PropertyRef Name="Id" /></Key>
		        <Property Name="Id" Type="Edm.Guid" Nullable="false" />
		      </EntityType>
		    </Schema>
		  </edmx:DataServices>
		</edmx:Edmx>
		""";

	private const string HtmlPage = "<html><head><title>404 - Not Found</title></head><body>error</body></html>";
	private const string NonJsonProbeBody = "IIS: The request could not be mapped to an application.";

	private static string UnknownPropertyError(string name) =>
		"{\"error\":{\"code\":\"-1\",\"message\":\"Could not find a property named '" + name +
		"' on type 'Terrasoft.Configuration.OData.Contact'.\"}}";

	/// <summary>A valid single-record OData response (no recognized error shape).</summary>
	private static string ProbeOk(string field) =>
		"{\"@odata.context\":\"http://creatio/odata/$metadata#Contact(" + Guid + ")\",\"Id\":\"" + Guid +
		"\",\"" + field + "\":\"probe\"}";

	private sealed class Fixture {
		public Fixture(string metadataBody, Func<string, string> probeBody) {
			Client = Substitute.For<IApplicationClient>();
			UrlBuilder = Substitute.For<IServiceUrlBuilder>();
			UrlBuilder.Build(Arg.Any<string>()).Returns(call => $"http://creatio/{call.Arg<string>()}");
			Client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>())
				.Returns(call => {
					string url = call.ArgAt<string>(0);
					if (url.EndsWith("/$metadata", StringComparison.Ordinal)) {
						return metadataBody;
					}
					return probeBody(url);
				});
			Resolver = Substitute.For<IToolCommandResolver>();
			Resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(Client);
			Resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(UrlBuilder);
			Tool = new ODataUpdateTool(Resolver);
		}

		public IApplicationClient Client { get; }
		public IServiceUrlBuilder UrlBuilder { get; }
		public IToolCommandResolver Resolver { get; }
		public ODataUpdateTool Tool { get; }
	}

	/// <summary>Happy-path fixture: $metadata resolves, so no $select probe may run.</summary>
	private static Fixture CsdLFixture() =>
		new(CsdL(), _ => throw new System.InvalidOperationException("probe must not run: $metadata is authoritative"));

	private static ODataWriteResponse Update(Fixture f, string data) =>
		f.Tool.Update(new ODataUpdateArgs {
			EnvironmentName = "dev",
			Entity = "Contact",
			Id = Guid,
			Data = Obj(data),
			Confirm = true
		});

	[Test]
	[Category("Unit")]
	[Description("Advertises a stable, destructive, idempotent MCP tool name for odata-update.")]
	public void Update_Should_Advertise_Stable_Tool_Name() {
		McpServerToolAttribute attribute = (McpServerToolAttribute)typeof(ODataUpdateTool)
			.GetMethod(nameof(ODataUpdateTool.Update))!
			.GetCustomAttributes(typeof(McpServerToolAttribute), false)
			.Single();

		attribute.Name.Should().Be(ODataUpdateTool.ToolName);
		attribute.ReadOnly.Should().BeFalse();
		attribute.Destructive.Should().BeTrue(because: "an update mutates existing remote state");
		attribute.Idempotent.Should().BeTrue(because: "re-applying the same field values is idempotent");
	}

	[Test]
	[Category("Unit")]
	[Description("Verifies fields via $metadata, then PATCHes the addressed key with the JSON body.")]
	public void Update_Should_Patch_Addressed_Key_With_Body() {
		Fixture f = CsdLFixture();
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(string.Empty);

		ODataWriteResponse response = Update(f, """{"Name":"New"}""");

		response.Success.Should().BeTrue(because: response.Error);
		f.Client.Received(1).ExecuteGetRequest(MetadataUrl, ODataFieldValidation.RequestTimeoutMs,
			ODataFieldValidation.TransientAttempts, ODataFieldValidation.TransientDelaySec);
		f.Client.DidNotReceive().ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains("?$select=", StringComparison.Ordinal)),
			Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()
			// because: $metadata is the primary validator; the $select probe runs only as a fallback
		);
		f.Client.Received(1).ExecutePatchRequest(KeyUrl, """{"Name":"New"}""", 30000);
	}

	[Test]
	[Category("Unit")]
	[Description("Fails when a data field is absent from the entity's CSDL; nothing is written, no probe runs.")]
	public void Update_Should_Reject_Field_Missing_From_Metadata() {
		Fixture f = new(CsdL(), _ => throw new System.InvalidOperationException("probe must not run: $metadata is authoritative"));

		ODataWriteResponse response = Update(f, """{"Name":"New","Color":"#fff"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("Color")
			.And.Contain("do not exist on the OData type of Contact")
			.And.Contain("$metadata")
			.And.Contain("nothing was written");
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Lists every data field missing from the CSDL type in a single failure message.")]
	public void Update_Should_Reject_Multiple_Unknown_Fields_At_Once() {
		Fixture f = CsdLFixture();

		ODataWriteResponse response = Update(f, """{"Name":"New","Color":"#fff","Phone":"123"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("Color")
			.And.Contain("Phone")
			.And.Contain("do not exist on the OData type of Contact");
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces an unverified (non-JSON, non-recognized) pre-write response and refuses to write.")]
	public void Update_Should_Reject_When_Probe_Body_Cannot_Be_Parsed() {
		Fixture f = new(HtmlPage, _ => NonJsonProbeBody);

		ODataWriteResponse response = Update(f, """{"Name":"New"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("could not be verified")
			.And.Contain("No write was performed");
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Surfaces an unrecognized OData error from the pre-write requests without writing.")]
	public void Update_Should_Reject_Before_Writing_When_Probe_Hits_Different_OData_Error() {
		const string serverError = """{"error":{"code":"-1","message":"The request is invalid."}}""";
		Fixture f = new(serverError, _ => serverError);

		ODataWriteResponse response = Update(f, """{"Name":"New","JobTitle":"CEO"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("The request is invalid")
			.And.Contain("pre-write")
			.And.Contain("not performed");
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Treats empty/ack PATCH bodies and valid-JSON write responses as success.")]
	public void Update_Should_Pass_Ack_Bodies_And_Valid_Json_Through() {
		Fixture f = CsdLFixture();
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(string.Empty, $"{{\"Id\":\"{Guid}\"}}");

		ODataWriteResponse first = Update(f, """{"Name":"New"}""");
		ODataWriteResponse second = Update(f, """{"Name":"Newer"}""");

		first.Success.Should().BeTrue(because: "an empty PATCH body is a valid 204 ack");
		second.Success.Should().BeTrue(because: "a valid single-record JSON body is a successful OData write");
	}

	[Test]
	[Category("Unit")]
	[Description("Returns a clean failure when the PATCH itself throws, without leaking internals.")]
	public void Update_Should_Fail_Cleanly_When_Patch_Throws() {
		Fixture f = CsdLFixture();
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Throws(new System.Net.Http.HttpRequestException("boom at /home/depot/odata"));

		ODataWriteResponse response = Update(f, """{"Name":"New"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("[redacted-path]")
			.And.NotContain("/home/depot");
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a data field whose name is not a valid OData member path, before any remote call.")]
	public void Update_Should_Reject_Malformed_Field_Name_Before_Any_Remote_Call() {
		Fixture f = CsdLFixture();

		ODataWriteResponse response = Update(f, """{"Name":"New","Name?$filter=Bad":"x"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("not a valid OData field name")
			.And.Contain("Name?$filter=Bad");
		f.Client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null, 0, 0, 0);
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a field set containing any malformed name without running the pre-write validation.")]
	public void Update_Should_Reject_Mixed_Malformed_And_Unknown_Field_Sets_Without_Patching() {
		Fixture f = CsdLFixture();

		ODataWriteResponse response = Update(f, """{"Name":"New","Name?$filter=Bad":"x","Color":"#fff"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("not a valid OData field name")
			.And.Contain("No write was performed");
		f.Client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null, 0, 0, 0);
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("When confirm is omitted, the tool refuses before any remote call.")]
	public void Update_Should_Not_Call_Remote_When_Not_Confirmed() {
		Fixture f = CsdLFixture();
		ODataUpdateArgs args = new() {
			EnvironmentName = "dev",
			Entity = "Contact",
			Id = Guid,
			Data = Obj("""{"Name":"New"}""")
		};

		ODataWriteResponse response = f.Tool.Update(args);

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("Refusing to update")
			.And.Contain("Contact")
			.And.Contain("\"confirm\": true");
		f.Client.DidNotReceiveWithAnyArgs().ExecuteGetRequest(null, 0, 0, 0);
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Rejects a lookup field set to the empty GUID, with a null-to-clear hint; nothing is written.")]
	public void Update_Should_Reject_EmptyGuid_On_Lookup_Field() {
		Fixture f = CsdLFixture();

		ODataWriteResponse response = Update(f, $"{{\"AccountId\":\"{EmptyGuid}\"}}");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("AccountId")
			.And.Contain(EmptyGuid)
			.And.Contain("null to clear")
			.And.Contain("No write was performed");
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Allows JSON null on a lookup field — that is the legitimate way to clear a reference.")]
	public void Update_Should_Allow_Null_On_Lookup_Field() {
		Fixture f = CsdLFixture();
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(string.Empty);

		ODataWriteResponse response = Update(f, """{"AccountId":null}""");

		response.Success.Should().BeTrue(because: "null clears the lookup; only the empty-GUID string is dropped by the platform");
		f.Client.Received(1).ExecutePatchRequest(KeyUrl, """{"AccountId":null}""", 30000);
	}

	[Test]
	[Category("Unit")]
	[Description("Allows the empty GUID on a plain Guid property — the silent-drop only affects lookup (Partner) fields.")]
	public void Update_Should_Allow_EmptyGuid_On_Plain_Guid_Field() {
		Fixture f = CsdLFixture();
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(string.Empty);

		ODataWriteResponse response = Update(f, $"{{\"SomeGuid\":\"{EmptyGuid}\"}}");

		response.Success.Should().BeTrue(because: "SomeGuid has no Partner attribute, so it is a plain Guid, not a lookup");
		f.Client.Received(1).ExecutePatchRequest(KeyUrl, $"{{\"SomeGuid\":\"{EmptyGuid}\"}}", 30000);
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to the $select probe when $metadata is not CSDL, and reports only the field the probe rejects.")]
	public void Update_Should_Fall_Back_To_Select_Probe_When_Metadata_Is_Not_Csl() {
		Fixture f = new(HtmlPage, url =>
			url.Contains("Color", StringComparison.Ordinal) ? UnknownPropertyError("Color") : ProbeOk("Name"));

		ODataWriteResponse response = Update(f, """{"Name":"New","JobTitle":"CEO","Color":"#fff"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("Color")
			.And.Contain("could not be verified against the service");
		response.Error.Should().NotContain("JobTitle", because: "the follow-up probe confirmed JobTitle exists");
		// The batch probe (full RequestTimeoutMs) pins the multi-field $select encoding and names only the
		// FIRST unknown (Color); the two follow-ups (Name, JobTitle) run at the shorter FollowUpProbeTimeoutMs
		// and both succeed, so only Color is reported.
		f.Client.Received(1).ExecuteGetRequest(
			$"{KeyUrl}?$select=Id%2CName%2CJobTitle%2CColor",
			ODataFieldValidation.RequestTimeoutMs, ODataFieldValidation.TransientAttempts, ODataFieldValidation.TransientDelaySec);
		f.Client.Received(2).ExecuteGetRequest(
			Arg.Is<string>(url => url.Contains("?$select=", StringComparison.Ordinal)),
			ODataFieldValidation.FollowUpProbeTimeoutMs, ODataFieldValidation.TransientAttempts, ODataFieldValidation.TransientDelaySec);
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Falls back to the $select probe when the CSDL declares no matching type; an all-ok probe lets the write proceed.")]
	public void Update_Should_Fall_Back_When_Metadata_Type_Missing() {
		Fixture f = new(CsdLWithoutContact, _ => ProbeOk("Name"));
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(string.Empty);

		ODataWriteResponse response = Update(f, """{"Name":"New"}""");

		response.Success.Should().BeTrue(because: "the probe verified every field; a missing CSDL type is a degraded path, not a failure");
		f.Client.Received(1).ExecutePatchRequest(KeyUrl, """{"Name":"New"}""", 30000);
	}

	[Test]
	[Category("Unit")]
	[Description("Sends the bounded retry parameters (30s timeout, 3 attempts, 1s delay) for the pre-write requests.")]
	public void Update_Should_Use_Bounded_Retry_For_PreWrite_Requests() {
		Fixture f = CsdLFixture();
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(string.Empty);

		ODataWriteResponse response = Update(f, """{"Name":"New"}""");

		response.Success.Should().BeTrue(because: response.Error);
		f.Client.Received(1).ExecuteGetRequest(MetadataUrl, ODataFieldValidation.RequestTimeoutMs,
			ODataFieldValidation.TransientAttempts, ODataFieldValidation.TransientDelaySec)
			// because: the retry budget must stay bounded so a dead metadata endpoint cannot hang the tool
		;
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts credential-bearing URIs surfaced by a failed pre-write validation.")]
	public void Update_Should_Redact_Sensitive_Tokens_In_PreWrite_Error() {
		const string serverError =
			"""{"error":{"code":"-1","message":"auth failed for http://admin:Sup3rS3cret@env.internal:80/odata"}}""";
		Fixture f = new(serverError, _ => serverError);

		ODataWriteResponse response = Update(f, """{"Name":"New"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("[redacted-uri]")
			.And.NotContain("Sup3rS3cret")
			.And.NotContain("admin@env.internal");
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("Redacts an internal host/credential URI surfaced in a non-JSON (unverified) pre-write probe body.")]
	public void Update_Should_Redact_Host_In_NonJson_Probe_Body() {
		const string nonJsonWithHost =
			"IIS: The request has been routed. See http://admin:Sup3rS3cret@env.internal:80/trace for details.";
		Fixture f = new(HtmlPage, _ => nonJsonWithHost);

		ODataWriteResponse response = Update(f, """{"Name":"New"}""");

		response.Success.Should().BeFalse();
		response.Error!.Should()
			.Contain("could not be verified")
			.And.Contain("[redacted-uri]")
			.And.NotContain("Sup3rS3cret")
			.And.NotContain("env.internal")
			.And.NotContain("admin@env");
		f.Client.DidNotReceiveWithAnyArgs().ExecutePatchRequest(null, null, 0);
	}

	[Test]
	[Category("Unit")]
	[Description("A pre-write probe reading a keyed entity whose column is named after an HttpError key (ExceptionMessage) is data, not a server error; the write proceeds.")]
	public void Update_Should_Treat_Error_Named_Probe_Column_As_Data() {
		// A log-shaped entity: the caller selects a column literally named ExceptionMessage, so the keyed
		// probe response echoes it alongside @odata.context and Id.
		Fixture f = new(HtmlPage, _ =>
			"{" +
			"\"@odata.context\":\"http://creatio/odata/$metadata#Log\"," +
			"\"Id\":\"" + Guid + "\"," +
			"\"Name\":\"probe\"," +
			"\"ExceptionMessage\":\"boom at /home/depot\"}");
		f.Client.ExecutePatchRequest(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>())
			.Returns(string.Empty);

		ODataWriteResponse response = Update(f, """{"Name":"New","ExceptionMessage":"boom at /home/depot"}""");

		response.Success.Should().BeTrue(because:
			"the probe body carries @odata.context and Id alongside the caller-chosen ExceptionMessage column, so it is a keyed entity read, not a server error; the write must proceed");
		f.Client.Received(1).ExecutePatchRequest(KeyUrl, """{"Name":"New","ExceptionMessage":"boom at /home/depot"}""", 30000);
	}
}
