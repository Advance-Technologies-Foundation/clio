using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Web;
using Clio.Command.McpServer.Tools;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Command.McpServer;

/// <summary>
/// Pins the wire contract of the chunked <c>upload-report-template</c> call: the query string that
/// <c>PrintableSupport.BuildUploadQuery</c> composes and the design-service response that
/// <c>PrintableSupport.ParseUploadResponse</c> interprets. Both mirror Creatio's own
/// MSWordReportDesigner service client, so a silent drift here uploads a template that never
/// lands — with a success-looking tool result. The e2e suite cannot cover this without a writable
/// sandbox and a real .docx fixture, which is why the mapping is pinned at the unit level.
/// </summary>
[TestFixture]
[Category("Unit")]
[Property("Module", "McpServer")]
public sealed class PrintableTemplateUploadMappingTests {

	private const string ReportId = "8ecab4a1-0ca3-4515-9399-efe0a19390bd";

	private static Dictionary<string, string> ParseQuery(string query) {
		var parsed = HttpUtility.ParseQueryString(query);
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (string? key in parsed.AllKeys) {
			if (key is not null) {
				result[key] = parsed[key] ?? string.Empty;
			}
		}
		return result;
	}

	[Test]
	[Description("Every parameter the design service requires is present and carries the value the service expects.")]
	public void BuildUploadQuery_ShouldMapEveryServiceParameter() {
		Guid fileId = Guid.Parse("6f1f6a4f-1d31-4a2e-9a08-2c6d0f4c1e77");

		string query = PrintableSupport.BuildUploadQuery(ReportId, fileId, 4096, "Invoice.docx");

		Dictionary<string, string> parameters = ParseQuery(query);
		parameters["columnName"].Should().Be(PrintableSupport.TemplateColumnName);
		parameters["fileId"].Should().Be(fileId.ToString());
		parameters["mimeType"].Should().Be(PrintableSupport.WordMimeType);
		parameters["parentColumnName"].Should().Be("Id");
		parameters["reportId"].Should().Be(ReportId);
		// Pins current behavior: parentColumnValue carries the upload's own fileId, not the report id.
		// Not independently verified against Creatio's MSWordReportDesigner client — if that contract
		// says otherwise, fix the implementation and this assertion together.
		parameters["parentColumnValue"].Should().Be(fileId.ToString());
		parameters["totalFileLength"].Should().Be("4096");
		parameters["entitySchemaName"].Should().Be(PrintableSupport.TemplateUploadEntitySchema);
		parameters["fileName"].Should().Be("Invoice.docx");
		parameters["maxFileSizeSysSettingsName"].Should().Be(PrintableSupport.MaxFileSizeSettingName);
		JsonSerializer.Deserialize<Dictionary<string, string>>(parameters["additionalParams"])
			.Should().Contain(new KeyValuePair<string, string>("ReportId", ReportId));
	}

	[Test]
	[Description("A file name with spaces and non-ASCII characters survives the round trip through the query string.")]
	public void BuildUploadQuery_ShouldEscapeFileName() {
		string query = PrintableSupport.BuildUploadQuery(ReportId, Guid.NewGuid(), 1, "Рахунок для клієнта.docx");

		query.Should().NotContain("Рахунок для клієнта.docx",
			because: "the raw name must be percent-encoded, not embedded verbatim");
		ParseQuery(query)["fileName"].Should().Be("Рахунок для клієнта.docx");
	}

	[Test]
	[Description("A zero-length file still reports its size explicitly rather than omitting the parameter.")]
	public void BuildUploadQuery_ShouldReportZeroLengthExplicitly() {
		string query = PrintableSupport.BuildUploadQuery(ReportId, Guid.NewGuid(), 0, "Empty.docx");

		ParseQuery(query)["totalFileLength"].Should().Be("0");
	}

	[Test]
	[Description("The design service's errorInfo message is surfaced as the failure reason.")]
	public void ParseUploadResponse_ShouldSurfaceServiceErrorMessage() {
		string json = """{"success":false,"errorInfo":{"message":"Report template exceeds the allowed size."}}""";

		PrintableTemplateUploadResponse response = PrintableSupport.ParseUploadResponse(json, ReportId, "Invoice.docx");

		response.Success.Should().BeFalse();
		response.Error.Should().Be("Report template exceeds the allowed size.");
	}

	[Test]
	[Description("A failure without a usable errorInfo still fails, with a generic reason instead of a silent success.")]
	public void ParseUploadResponse_ShouldFailClosed_WhenErrorInfoIsMissing() {
		PrintableTemplateUploadResponse response =
			PrintableSupport.ParseUploadResponse("""{"success":false}""", ReportId, "Invoice.docx");

		response.Success.Should().BeFalse();
		response.Error.Should().Be("Template upload failed.");
	}

	[TestCase("", TestName = "ParseUploadResponse treats an empty body as a stored template")]
	[TestCase("OK", TestName = "ParseUploadResponse treats a non-JSON body as a stored template")]
	[TestCase("""{"success":true}""", TestName = "ParseUploadResponse treats an explicit success as a stored template")]
	[Description("The chunked upload answers with an empty, plain-text or JSON success body; all three mean the template was stored.")]
	public void ParseUploadResponse_ShouldReportSuccess(string json) {
		PrintableTemplateUploadResponse response = PrintableSupport.ParseUploadResponse(json, ReportId, "Invoice.docx");

		response.Success.Should().BeTrue();
		response.Error.Should().BeNull();
		response.Id.Should().Be(ReportId);
		response.FileName.Should().Be("Invoice.docx");
	}
}
