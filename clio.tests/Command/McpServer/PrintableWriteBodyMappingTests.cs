using System.Linq;
using System.Text.Json;
using Clio.Command.McpServer.Tools;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins the OData request body that <c>create-printable</c> and <c>update-printable</c> send, so the
/// kebab-cased MCP arguments keep mapping onto the exact Creatio column names.
/// </summary>
/// <remarks>
/// The column casing is not guessable from the argument names — <c>convert-in-pdf</c> maps to
/// <c>ConvertInPDF</c> (all-caps PDF), and the entity-schema argument maps to <c>SysEntitySchemaId</c>,
/// not <c>EntitySchemaId</c>. A silent rename on either side would be accepted by OData as an unknown
/// property and the flag would simply never take effect, so this is asserted rather than left to the
/// e2e suite (PR #651 review, first-pass Minor).
/// </remarks>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PrintableWriteBodyMappingTests {

	private const string RecordId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";
	private const string EntitySchemaId = "1b9dc2f8-8e2c-4d67-8f4c-2a1d0f3e7b55";
	private const string SysModuleId = "6a1f4b2c-3d5e-4f70-9a81-b2c3d4e5f607";

	private IApplicationClient _client;
	private IServiceUrlBuilder _urlBuilder;
	private IToolCommandResolver _resolver;

	[SetUp]
	public void SetUp() {
		_client = Substitute.For<IApplicationClient>();
		_urlBuilder = Substitute.For<IServiceUrlBuilder>();
		_resolver = Substitute.For<IToolCommandResolver>();
		_resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(_client);
		_resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(_urlBuilder);
		_urlBuilder.Build(Arg.Any<string>()).Returns(call => $"https://stand.creatio.com/0/{call.Arg<string>()}");
		// The type lookup runs first on create; an empty payload sends it down the well-known-constant path.
		_client.ExecuteGetRequest(Arg.Any<string>(), Arg.Any<int>()).Returns("{\"value\":[]}");
	}

	/// <summary>Reads the JSON body of the single request the given method made.</summary>
	private JsonDocument CapturedBody(string methodName) {
		string body = _client.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == methodName)
			.Select(call => call.GetArguments()[1] as string)
			.SingleOrDefault();
		body.Should().NotBeNull(
			because: $"the tool must have made exactly one {methodName} call for its body to be assertable");
		return JsonDocument.Parse(body);
	}

	[Test]
	[Description("create-printable maps every supplied argument onto its Creatio column, including the all-caps ConvertInPDF and the Sys-prefixed SysEntitySchemaId.")]
	public void CreatePrintable_Should_Map_Every_Argument_To_Its_Creatio_Column() {
		// Arrange
		PrintableCreateTool tool = new(_resolver);

		// Act
		tool.Create(new PrintableCreateArgs {
			EnvironmentName = "dev",
			Caption = "  Contact card  ",
			EntitySchemaId = EntitySchemaId,
			SysModuleId = SysModuleId,
			ShowInSection = true,
			ShowInCard = false,
			ConvertInPdf = true,
			MacrosSettings = "{\"columns\":[]}"
		});

		// Assert
		using JsonDocument body = CapturedBody("ExecutePostRequest");
		JsonElement root = body.RootElement;
		root.GetProperty("Caption").GetString().Should().Be("Contact card",
			because: "the caption is trimmed before it reaches Creatio");
		root.GetProperty("SysEntitySchemaId").GetString().Should().Be(EntitySchemaId,
			because: "the bound object column is SysEntitySchemaId, not EntitySchemaId");
		root.GetProperty("SysModuleId").GetString().Should().Be(SysModuleId,
			because: "the optional section binding maps straight onto SysModuleId");
		root.GetProperty("ShowInSection").GetBoolean().Should().BeTrue(
			because: "show-in-section maps onto ShowInSection");
		root.GetProperty("ShowInCard").GetBoolean().Should().BeFalse(
			because: "an explicit false must be sent, not dropped as if it were omitted");
		root.GetProperty("ConvertInPDF").GetBoolean().Should().BeTrue(
			because: "the column is spelled ConvertInPDF with an all-caps PDF — OData would silently " +
				"ignore a differently-cased property and the flag would never take effect");
		root.GetProperty("MacrosSettings").GetString().Should().Be("{\"columns\":[]}",
			because: "macros-settings is Creatio's internal column-mapping format and is passed through verbatim");
		root.TryGetProperty("TypeId", out _).Should().BeTrue(
			because: "the report type is always set to MS Word by the tool, never by the caller");
	}

	[Test]
	[Description("create-printable omits every optional column the caller did not supply, so Creatio keeps its own defaults instead of receiving nulls.")]
	public void CreatePrintable_Should_Omit_Unsupplied_Optional_Columns() {
		// Arrange
		PrintableCreateTool tool = new(_resolver);

		// Act
		tool.Create(new PrintableCreateArgs {
			EnvironmentName = "dev",
			Caption = "Contact card",
			EntitySchemaId = EntitySchemaId
		});

		// Assert
		using JsonDocument body = CapturedBody("ExecutePostRequest");
		JsonElement root = body.RootElement;
		foreach (string column in new[] { "SysModuleId", "ShowInSection", "ShowInCard", "ConvertInPDF", "MacrosSettings" }) {
			root.TryGetProperty(column, out _).Should().BeFalse(
				because: $"'{column}' was not supplied, so sending it would overwrite Creatio's own default");
		}
	}

	[Test]
	[Description("update-printable sends only the supplied columns, with the same ConvertInPDF casing as create.")]
	public void UpdatePrintable_Should_Patch_Only_Supplied_Columns() {
		// Arrange
		PrintableUpdateTool tool = new(_resolver);

		// Act
		tool.Update(new PrintableUpdateArgs {
			EnvironmentName = "dev",
			Id = RecordId,
			ConvertInPdf = false,
			Confirm = true
		});

		// Assert
		using JsonDocument body = CapturedBody("ExecutePatchRequest");
		JsonElement root = body.RootElement;
		root.GetProperty("ConvertInPDF").GetBoolean().Should().BeFalse(
			because: "update must use the same all-caps column spelling as create, and must send an explicit false");
		root.EnumerateObject().Should().HaveCount(1,
			because: "a patch must carry only what the caller asked to change, so untouched columns keep their values");
	}
}
