using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command.McpServer;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Option B at every wired sink: each one, driven through its own seam, carries exactly the composition master's order
/// gives it - master's cuts where master had them, then the site-text redaction (master's frozen chain, Extra, Site),
/// then clio's own cut or cap after. Run over every decoded fixture row, every edge string and every shrinking-prefix
/// string (691 strings).
/// </summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class RedactorSinkCompositionTests {

	private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	/// <summary>The fixed composition set: the decoded fixture rows, the edge strings, the shrinking-prefix strings, and
	/// two password values of 21,000 characters that cross every cut and cap, so a cut moved or added before the chain
	/// shows at site-response too.</summary>
	internal static IReadOnlyList<string> CompositionSet() {
		string words = string.Join(' ', Enumerable.Repeat("zqword", 3000)) + " zqsecret";
		string quote = "\\" + "u0022";
		return [.. RedactionDifferentialCorpus.Strings()
			.Where(entry => entry.Id.EndsWith(":d", StringComparison.Ordinal) || entry.Id.StartsWith("e:", StringComparison.Ordinal)
				|| entry.Id.StartsWith("x:", StringComparison.Ordinal))
			.Select(entry => entry.Text),
			"Data Source=srv;Password=\\\"" + words + "\\\" end",
			"{" + quote + "password" + quote + ":" + quote + words + quote + "}"];
	}

	private static string Site(string text) => SensitiveErrorTextRedactor.RedactSiteText(text);

	private static string FencedSite(string text) => SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(text);

	private static string Cap(string value, int maxLength) =>
		(string)typeof(SiteResponse).GetMethod("Cap", AnyStatic)!.Invoke(null, [value, maxLength]);

	private static string Detected(string json) {
		using JsonDocument document = JsonDocument.Parse(json);
		return CreatioResponseError.TryDetect(document.RootElement, CreatioResponseContext.ODataPayload, out string message) ? message : null;
	}

	private static string ODataError(string text) => JsonSerializer.Serialize(new { error = new { code = "", message = text } });

	[Test]
	[Category("Unit")]
	[Description("COMPOSITION at every statically reachable sink, over the 691-string set: site-response's message and "
		+ "code are Cap(RedactSiteText(x)); the non-JSON preview is RedactSiteText of master's 500-character cut; the keyed "
		+ "write is RedactSiteText of the detected OData error; the designer's save error and the DataService failure are "
		+ "RedactSiteText(x); the batch item error, DataWriteDiagnostic.message, the compile diagnostic and the fenced "
		+ "rendering are the fenced site rendering, which cuts at master's 2,400 before its chain; the compile message tail "
		+ "is RedactSiteText per message; a refused delete's inner message is the fenced site rendering of master's "
		+ "first line cut at 500.")]
	public void EveryStaticSink_ShouldCarryMastersOrderThenTheSiteText() {
		// Arrange
		IReadOnlyList<string> set = CompositionSet();
		List<string> wrong = [];

		// Act
		foreach (string text in set) {
			string json = JsonSerializer.Serialize(new { success = false, errorInfo = new { message = text, errorCode = text } });
			SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, json));
			Check(response.Message == Cap(Site(text), SiteResponse.MaxMessageLength), "site-response.message", text, response.Message);
			Check(response.Code == Cap(Site(text), SiteResponse.MaxCodeLength), "site-response.code", text, response.Code);
			string preview = CreatioResponseError.DescribeNonJsonResponse(text);
			Check(preview.EndsWith(" Response: " + Site(CreatioResponseError.Truncate(text)), StringComparison.Ordinal),
				"non-JSON preview", text, preview);
			string keyed = ODataKeyedWrite.ValidateWriteResponse(ODataError(text));
			string detected = Detected(ODataError(text));
			Check(detected is null || keyed.StartsWith(Site(detected), StringComparison.Ordinal), "keyed write", text, keyed);
			string saved = PageSchemaMetadataHelper.ParseSaveErrorMessage(JObject.Parse(json), "-");
			Check(saved == (string.IsNullOrWhiteSpace(text) ? "-" : Site(text)), "designer save error", text, saved);
			string failure = null;
			try {
				DataServiceResponse.ThrowIfUnsuccessful(json, "Write");
			}
			catch (InvalidOperationException exception) {
				failure = exception.Message;
			}
			Check(failure is not null && failure.StartsWith("Write failed: " + Site(text), StringComparison.Ordinal), "DataService failure", text, failure);
			using (JsonDocument item = JsonDocument.Parse(JsonSerializer.Serialize(new { responseStatus = new { Message = text } }))) {
				string batch = (string)typeof(DataServiceBatchService).GetMethod("ReadError", AnyStatic)!.Invoke(null, [item.RootElement]);
				Check(batch == FencedSite(text), "batch item error", text, batch);
			}
			int responseAt = text.IndexOf(" Response: ", StringComparison.Ordinal);
			string diagnostic = DataWriteDiagnostic.Create("insert", "Account", null, true, true, false, text).Message;
			Check(diagnostic == FencedSite(responseAt >= 0 ? text[..responseAt] : text), "DataWriteDiagnostic.message", text, diagnostic);
			PackageBuildDiagnostic build = new("CS0103", text, "x.cs", 1, 2, false);
			string rendered = CompileStatusTool.RenderError(build);
			Check(rendered == FencedSite(build.Format(code => code, file => file)), "compile-status diagnostic", text, rendered);
			IReadOnlyList<string> tail = (IReadOnlyList<string>)typeof(CompileOperationRegistry).GetMethod("BuildMessageTail", AnyStatic)!
				.Invoke(null, [new LogMessage[] { new ErrorMessage(text) }]);
			Check(tail.Count == 1 && tail[0] == Site(text), "compile message tail", text, string.Join('|', tail));
			string inner = RecordDeletionService.ReadInnerMessage(JsonSerializer.Serialize(new {
				error = new { message = "An error has occurred.", innererror = new { message = text } }
			}));
			string firstLine = ProcessRunMonitor.FirstLine(text);
			Check(inner == FencedSite(firstLine.Length > 500 ? firstLine[..500] : firstLine), "refused delete's inner message", text, inner);
			CheckFenced(FencedSite(text), text, Site, "RedactUntrustedSiteTextOrNull");
			CheckFenced(SensitiveErrorTextRedactor.RedactUntrustedOrNull(text), text, SensitiveErrorTextRedactor.Redact, "RedactUntrustedOrNull");
		}

		// Assert
		set.Count.Should().BeGreaterThanOrEqualTo(200, because: "the composition set is the stated one");
		wrong.Should().BeEmpty(because: $"every sink carries master's order then the site text; {wrong.Count} found:\n"
			+ string.Join('\n', wrong.Take(20)));

		void Check(bool holds, string sink, string text, string output) {
			if (!holds) {
				wrong.Add($"{sink}: \"{Short(text)}\" -> \"{Short(output)}\"");
			}
		}

		// The fenced rendering: master's 2,400 cut, the redaction, the flattening, the fence words neutralized, then the
		// 300 cut and the fence.
		void CheckFenced(string fenced, string text, Func<string, string> redact, string entry) {
			string cut = text.Length > 2_400 ? text[..2_400] : text;
			string payload = FenceWords.Replace(SensitiveErrorTextRedactor.FlattenDisplayHostileRuns(redact(cut)), "(fence removed)");
			string expected = payload.Length == 0 ? null
				: "[untrusted-source-text begin] " + (payload.Length > 300 ? payload[..300] + "\u2026" : payload) + " [untrusted-source-text end]";
			Check(string.IsNullOrWhiteSpace(text) || fenced == expected, entry, text, fenced);
		}
	}

	private static readonly System.Text.RegularExpressions.Regex FenceWords =
		(System.Text.RegularExpressions.Regex)typeof(SensitiveErrorTextRedactor).GetMethod("FenceTokenRegex", AnyStatic)!.Invoke(null, null);

	private static string Short(string text) => text is null ? "<null>" : text.Length > 160 ? text[..160] + "…" : text;

	private static (IApplicationClient Client, IServiceUrlBuilder Urls) GetClient(string body) {
		IApplicationClient client = Substitute.For<IApplicationClient>();
		client.ExecuteGetRequest(default, default, default, default).ReturnsForAnyArgs(body);
		IServiceUrlBuilder urls = Substitute.For<IServiceUrlBuilder>();
		urls.Build(Arg.Any<string>()).Returns("http://creatio.invalid/odata/x");
		return (client, urls);
	}

	private static object Invoke(Type type, string name, params object[] arguments) {
		try {
			return type.GetMethods(AnyStatic).Single(method => method.Name == name && method.GetParameters().Length == arguments.Length)
				.Invoke(null, arguments);
		}
		catch (TargetInvocationException exception) when (exception.InnerException is not null) {
			throw exception.InnerException;
		}
	}

	private static string Property(object record, string name) => (string)record.GetType().GetProperty(name)!.GetValue(record);

	[Test]
	[Category("Unit")]
	[Description("COMPOSITION at the call sites that need a client, driven through them with a substitute client over the "
		+ "691-string set: odata-create's refused insert (ODataCreateTool), odata-field validation's metadata read "
		+ "(ODataFieldValidation.FetchMetadataOnce) and record probe (ProbeOnce), and email-template's read "
		+ "(EmailTemplateContentService.Read) each carry RedactSiteText of the OData error the site sent. Branding's "
		+ "TryQuerySingleId and the trigger smoke run's CreateTrigger make the same call on CreatioResponseError.TryDetect's "
		+ "output; SiteTextWiringTests' source guard pins that call.")]
	public void EveryClientCallSite_ShouldCarryTheSiteTextOfTheODataError() {
		// Arrange
		IReadOnlyList<string> set = CompositionSet();
		List<string> wrong = [];

		// Act
		foreach (string text in set) {
			string body = ODataError(text);
			string detected = Detected(body);
			if (detected is null) {
				continue;
			}
			string expected = Site(detected);
			(IApplicationClient client, IServiceUrlBuilder urls) = GetClient(body);
			object metadata = Invoke(typeof(ODataFieldValidation), "FetchMetadataOnce", client, urls, "Account", 1000, false);
			Check(Property(metadata, "ServerError") == expected, "ODataFieldValidation.FetchMetadataOnce", text, Property(metadata, "ServerError"));
			object probe = Invoke(typeof(ODataFieldValidation), "ProbeOnce", client, urls, "Account",
				"00000000-0000-0000-0000-000000000001", (IReadOnlyList<string>)["Id"], 1000);
			Check(Property(probe, "ServerError") == expected, "ODataFieldValidation.ProbeOnce", text, Property(probe, "ServerError"));
			string thrown = null;
			try {
				Invoke(typeof(EmailTemplateContentService), "Read", client, urls, "Activity", "x eq 1", "Id", 1);
			}
			catch (InvalidOperationException exception) {
				thrown = exception.Message;
			}
			Check(thrown == expected, "EmailTemplateContentService.Read", text, thrown);
		}
		foreach (string text in set.Where((_, index) => index % 3 == 0)) {
			string body = ODataError(text);
			string detected = Detected(body);
			if (detected is null) {
				continue;
			}
			ODataRowResult row = CreateOne(body).Results.Single();
			using JsonDocument document = JsonDocument.Parse(body);
			string expected = CreatioResponseError.AppendStructuredODataWriteError(Site(detected), document.RootElement);
			Check(row.Error == expected, "ODataCreateTool row error", text, row.Error);
		}

		// Assert
		wrong.Should().BeEmpty(because: $"every client call site carries RedactSiteText of the site's OData error; "
			+ $"{wrong.Count} found:\n" + string.Join('\n', wrong.Take(20)));

		void Check(bool holds, string sink, string text, string output) {
			if (!holds) {
				wrong.Add($"{sink}: \"{Short(text)}\" -> \"{Short(output)}\"");
			}
		}
	}

	private static ODataCreateBatchResponse CreateOne(string body) {
		IApplicationClient client = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(400, body));
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		resolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns((client, urlBuilder));
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio.invalid/odata/Account");
		MockFileSystem fileSystem = new();
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(),
			new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));
		return tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = JsonDocument.Parse("[{\"Name\":\"Acme\"}]").RootElement
		});
	}

	// Item 4: a page after the reason. The cut collapses it into one placeholder, so a sink with no cap of its own still
	// carries a short text.
	private static string Page(bool javaScript) {
		string unit = javaScript
			? "function f(a){return [a,{b:(c)}];} var x = (y) => { z[0] = (1); }; "
			: "<div class=\"x\"><span>(a) [b] {c}</span><a href=\"https://zq.example/p\">link</a></div>\n";
		string body = string.Concat(Enumerable.Repeat(unit, 100_000 / unit.Length + 1));
		return "Access denied for the record.\n<!DOCTYPE html><html><head><title>Error</title>"
			+ (javaScript ? "<script>" + body + "</script>" : "</head><body>" + body + "</body>") + "</html>";
	}

	[TestCase(false, TestName = "Page_Html100KB")]
	[TestCase(true, TestName = "Page_JavaScript100KB")]
	[Category("Unit")]
	[Description("A 100 KB HTML page and a 100 KB JavaScript page after the reason give a SHORT result at every sink with "
		+ "no cap of its own - the designer's save error, the DataService failure and email-template's read - because the "
		+ "site cut collapses the whole tail into one placeholder instead of keeping its brackets.")]
	public void UncappedSinks_ShouldCarryAShortText_WhenThePageAfterTheReasonIsLarge(bool javaScript) {
		// Arrange
		string page = Page(javaScript);
		string json = JsonSerializer.Serialize(new { success = false, errorInfo = new { message = page } });
		(IApplicationClient client, IServiceUrlBuilder urls) = GetClient(ODataError(page));

		// Act
		string saved = PageSchemaMetadataHelper.ParseSaveErrorMessage(JObject.Parse(json), "-");
		Action service = () => DataServiceResponse.ThrowIfUnsuccessful(json, "Write");
		Action email = () => Invoke(typeof(EmailTemplateContentService), "Read", client, urls, "Activity", "x eq 1", "Id", 1);

		// Assert
		page.Length.Should().BeGreaterThan(100_000);
		saved.Should().Be("Access denied for the record.\n[redacted]", because: "the page after the reason is one placeholder");
		service.Should().Throw<InvalidOperationException>().Which.Message.Length.Should().BeLessThan(200);
		email.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("Access denied for the record.\n[redacted]");
	}
}
