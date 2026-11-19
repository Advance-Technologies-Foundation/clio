using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Command.McpServer.Tools;
using Clio.Command.RuntimeVerification;
using Clio.Common;
using Clio.Package;
using Clio.Tests.Command;
using Clio.Tests.Command.McpServer;
using FluentAssertions;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using NSubstitute;
using NUnit.Framework;

namespace Clio.Tests.Common;

/// <summary>
/// Requirement (b) of the site-text redaction plan: every path that carries text a Creatio SITE wrote to a caller
/// goes through <see cref="SensitiveErrorTextRedactor.RedactSiteText"/>, and clio's own text does not.
/// </summary>
/// <remarks>
/// Each path is driven with one marker that only RedactSiteText removes - a sign-in name in SQL Server's own
/// phrase - and the test asserts the marker never reaches what the caller reads. Once rule 9 (PR B) lands, a SQL
/// fragment can serve as a second marker. <see cref="SourceGuard_ShouldKnowEverySiteTextExtractorCallSite"/>
/// fails when a new call site of a site-text extractor appears without a case here.
/// </remarks>
[TestFixture]
[Property("Module", "Common")]
public sealed class SiteTextWiringTests {

	internal const string Marker = "wiring_marker_login";

	internal const string SiteMessage = "Login failed for user '" + Marker + "'.";

	private static readonly string RepositoryRoot =
		Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

	private static string ODataError => JsonSerializer.Serialize(new { error = new { code = "", message = SiteMessage } });

	[Test]
	[Category("Unit")]
	[Description("The marker is one only RedactSiteText removes, so every wiring case below proves the site entry "
		+ "point was used rather than Redact.")]
	public void Marker_ShouldBeRemovedByRedactSiteTextOnly() {
		// Arrange

		// Act
		string redact = SensitiveErrorTextRedactor.Redact(SiteMessage);
		string site = SensitiveErrorTextRedactor.RedactSiteText(SiteMessage);

		// Assert
		redact.Should().Contain(Marker, because: "Redact keeps a sign-in phrase, which clio's own CLI text may hold");
		site.Should().NotContain(Marker, because: "RedactSiteText removes the login the database named");
		site.Should().Contain("Login failed for user", because: "the site's reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("site-response.message and site-response.code (SiteResponse.Carry) go through RedactSiteText.")]
	public void SiteResponse_ShouldCarryMessageAndCode_ThroughRedactSiteText() {
		// Arrange
		string body = JsonSerializer.Serialize(new { errorInfo = new { message = SiteMessage, errorCode = SiteMessage } });

		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, body));

		// Assert
		response.Message.Should().Be("Login failed for user '[redacted]'.", because: "the message is the site's text");
		response.Code.Should().NotContain(Marker, because: "the code is the site's text too");
	}

	[Test]
	[Category("Unit")]
	[Description("A site message longer than the cap is clamped before the redaction and still capped after it, so a "
		+ "page-sized message neither times out the chain nor exceeds the 2,000-character cap.")]
	public void SiteResponse_ShouldKeepTheCap_WhenTheSiteMessageIsPageSized() {
		// Arrange
		string body = JsonSerializer.Serialize(new { errorInfo = new { message = SiteMessage + " " + new string('x', 1_000_000) } });

		// Act
		SiteResponse response = SiteResponse.FromRefusal(new SiteHttpResponse(500, body));

		// Assert
		response.Message.Should().StartWith("Login failed for user '[redacted]'.",
			because: "the reason at the start survives; the chain did not fail closed");
		response.Message.Length.Should().Be(SiteResponse.MaxMessageLength, because: "the existing cap is kept");
	}

	[Test]
	[Category("Unit")]
	[Description("The OData server error of a keyed write (odata-update, and odata-delete through the record deletion "
		+ "service) goes through RedactSiteText, the FK hint appender included.")]
	public void KeyedWrite_ShouldRedactTheODataServerError_AsSiteText() {
		// Arrange

		// Act
		string error = ODataKeyedWrite.ValidateWriteResponse(ODataError);

		// Assert
		error.Should().NotContain(Marker, because: "CreatioResponseError's extracted message is the site's text");
		error.Should().Contain("Login failed for user", because: "the reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("The up-to-500-character preview of a non-JSON body (DescribeNonJsonResponse) goes through "
		+ "RedactSiteText: the sign-in name goes, and so does a stack trace after the reason.")]
	public void NonJsonPreview_ShouldRedactTheBody_AsSiteText() {
		// Arrange
		string body = SiteMessage + "\r\n   at Terrasoft.Core.Entities.Entity.Save()";

		// Act
		string error = ODataKeyedWrite.ValidateWriteResponse(body);

		// Assert
		error.Should().NotContain(Marker).And.NotContain("Terrasoft.Core",
			because: "the preview is the site's text, cut before its stack trace");
		error.Should().Contain("Response: Login failed for user '[redacted]'.", because: "the reason stays in the preview");
	}

	[TestCase("rejected eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJpbnZlbnRlZCJ9.aW52ZW50ZWRzaWduYXR1cmU", "eyJ",
		TestName = "NonJsonPreview_JwtAcrossTheCut")]
	[TestCase("Login failed for user 'zq_invented_login'.", "zq_", TestName = "NonJsonPreview_SignInNameAcrossTheCut")]
	[Category("Unit")]
	[Description("The non-JSON preview is redacted BEFORE it is cut to 500 characters: a JWT or a quoted sign-in name "
		+ "that crosses the cut goes whole instead of leaving the half before the cut, which matched no rule.")]
	public void NonJsonPreview_ShouldRedactBeforeTheCut_WhenASecretCrossesIt(string secretText, string fragment) {
		// Arrange
		string body = new string('x', 494 - secretText.IndexOf(fragment, StringComparison.Ordinal)) + " " + secretText;

		// Act
		string error = CreatioResponseError.DescribeNonJsonResponse(body);

		// Assert
		error.Should().NotContain(fragment, because: "no part of the secret may survive the preview's cut");
	}

	[Test]
	[Category("Unit")]
	[Description("A refused delete's inner message is not cut before the redaction either: a sign-in name after a long "
		+ "URI, crossing the old 500-character cut, goes whole.")]
	public void RecordDeletionInnerMessage_ShouldRedactBeforeAnyCut_WhenASecretCrossesTheOldCut() {
		// Arrange
		string message = "See https://" + new string('a', 450) + ".invalid/x Login failed for user 'zq_invented_login'.";
		string body = JsonSerializer.Serialize(new {
			error = new { message = "An error has occurred.", innererror = new { message } }
		});

		// Act
		string inner = RecordDeletionService.ReadInnerMessage(body);

		// Assert
		inner.Should().NotContain("zq_", because: "the name crossed the old cut and must still go");
		inner.Should().Contain("Login failed for user '[redacted]'", because: "the reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("odata-create's per-row error, its site-response and its diagnostic message all go through the site "
		+ "entry point, for a JSON OData error and for a non-JSON body.")]
	public void ODataCreate_ShouldKeepTheMarkerOutOfTheSerializedResult(
		[Values(400, 502)] int status, [Values(true, false)] bool json) {
		// Arrange
		IApplicationClient client = Substitute.For<IApplicationClient, IStatusPreservingApplicationClient>();
		((IStatusPreservingApplicationClient)client)
			.SendKeepingStatus(default, default, default, default, default, default, default)
			.ReturnsForAnyArgs(new SiteHttpResponse(status, json ? ODataError : SiteMessage));
		IServiceUrlBuilder urlBuilder = Substitute.For<IServiceUrlBuilder>();
		IToolCommandResolver resolver = Substitute.For<IToolCommandResolver>();
		resolver.Resolve<IApplicationClient>(Arg.Any<EnvironmentOptions>()).Returns(client);
		resolver.Resolve<IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>()).Returns(urlBuilder);
		resolver.ResolvePair<IApplicationClient, IServiceUrlBuilder>(Arg.Any<EnvironmentOptions>())
			.Returns((client, urlBuilder));
		urlBuilder.Build(Arg.Any<string>()).Returns("http://creatio.invalid/odata/Account");
		MockFileSystem fileSystem = new();
		ODataCreateTool tool = new(resolver, new OperationCorrelationIdProvider(),
			new ODataFileContract(fileSystem, new MockConfinedFileAccess(fileSystem)));

		// Act
		ODataCreateBatchResponse response = tool.Create(new ODataCreateArgs {
			EnvironmentName = "dev", Entity = "Account", Rows = JsonDocument.Parse("[{\"Name\":\"Acme\"}]").RootElement
		});

		// Assert
		response.Failed.Should().Be(1, because: "the site refused the row");
		JsonSerializer.Serialize(response).Should().NotContain(Marker,
			because: "no field of the result may carry the site's text past RedactSiteText");
	}

	[Test]
	[Category("Unit")]
	[Description("The inner message of a refused delete (odata-delete with ids, trigger smoke) goes through the fenced "
		+ "site rendering.")]
	public void RecordDeletionInnerMessage_ShouldBeFencedSiteText() {
		// Arrange
		string body = JsonSerializer.Serialize(new {
			error = new { message = "An error has occurred.", innererror = new { message = SiteMessage } }
		});

		// Act
		string inner = RecordDeletionService.ReadInnerMessage(body);

		// Assert
		inner.Should().Be("[untrusted-source-text begin] Login failed for user '[redacted]'. [untrusted-source-text end]",
			because: "the inner exception is the site's text");
	}

	[Test]
	[Category("Unit")]
	[Description("DataWriteDiagnostic.message goes through the fenced site rendering.")]
	public void DataWriteDiagnostic_ShouldCarryTheMessage_AsFencedSiteText() {
		// Arrange

		// Act
		DataWriteDiagnostic diagnostic = DataWriteDiagnostic.Create("insert", "Account", null, true, true, false, SiteMessage);

		// Assert
		diagnostic.Message.Should().NotContain(Marker, because: "the write's error text carries the site's words");
		diagnostic.Message.Should().Contain("Login failed for user", because: "the reason stays");
	}

	[Test]
	[Category("Unit")]
	[Description("The DataService envelope's errorInfo.message (DataServiceResponse) goes through RedactSiteText before "
		+ "it becomes the exception message every DataService write tool reports.")]
	public void DataServiceResponse_ShouldRedactTheServiceMessage_AsSiteText() {
		// Arrange
		string body = JsonSerializer.Serialize(new { success = false, errorInfo = new { message = SiteMessage } });

		// Act
		Action act = () => DataServiceResponse.ThrowIfUnsuccessful(body, "Write");

		// Assert
		act.Should().Throw<InvalidOperationException>()
			.Which.Message.Should().Be("Write failed: Login failed for user '[redacted]'.",
				because: "the service's message is the site's text");
	}

	[Test]
	[Category("Unit")]
	[Description("The designer's save error (PageSchemaMetadataHelper: create-page, update-page, sync-pages, the schema "
		+ "designers) goes through RedactSiteText; clio's default sentence is returned as it is.")]
	public void DesignerSaveError_ShouldBeRedactedAsSiteText() {
		// Arrange
		JObject failed = JObject.Parse(JsonSerializer.Serialize(new { success = false, errorInfo = new { message = SiteMessage } }));
		JObject empty = JObject.Parse("{\"success\":false}");

		// Act
		string error = PageSchemaMetadataHelper.ParseSaveErrorMessage(failed, "Failed to save page schema");
		string fallback = PageSchemaMetadataHelper.ParseSaveErrorMessage(empty, "Failed to save page schema");

		// Assert
		error.Should().Be("Login failed for user '[redacted]'.", because: "the designer's message is the site's text");
		fallback.Should().Be("Failed to save page schema", because: "clio's own sentence is not site text");
	}

	[Test]
	[Category("Unit")]
	[Description("compile-status: the compiler's diagnostics in the history and the message tail of a tracked compile "
		+ "go through the site entry point.")]
	public void CompileStatus_ShouldRedactCompilerTextAndMessageTail_AsSiteText() {
		// Arrange
		PackageBuildDiagnostic diagnostic = new("CS0103", SiteMessage, "C:\\inetpub\\x.cs", 1, 2, false);
		CompileOperationRegistry registry = new();
		CompileOperationRecord begun = registry.Begin("tenant", "dev", "UsrPkg");

		// Act
		string rendered = CompileStatusTool.RenderError(diagnostic);
		CompileOperationRecord finished = registry.Finish(begun.OperationId, 1, [new ErrorMessage(SiteMessage)]);

		// Assert
		rendered.Should().NotContain(Marker, because: "the compiler's text is the environment's");
		finished.MessageTail.Should().ContainSingle().Which.Should().NotContain(Marker,
			because: "the compile output tail quotes the site");
	}

	[Test]
	[Category("Unit")]
	[Description("The inverse: clio-run's serialized envelope keeps using Redact, so the site-only shapes, a version "
		+ "number and a JSON escape survive there.")]
	public void ClioRunEnvelope_ShouldKeepUsingRedact() {
		// Arrange
		string text = JsonSerializer.Serialize(new {
			success = false, error = SiteMessage + " clio 8.1.0.5 wrote ./MyPkg/descriptor.json\nPassword=hunter2"
		});
		CallToolResult result = new() { IsError = true, Content = [new TextContentBlock { Text = text }] };

		// Act
		ClioRunExecutor.RedactFailureContent(result);

		// Assert
		string error = JsonDocument.Parse(result.Content.OfType<TextContentBlock>().Single().Text)
			.RootElement.GetProperty("error").GetString();
		error.Should().Be(SiteMessage + " clio 8.1.0.5 wrote ./MyPkg/descriptor.json\nPassword=[redacted]",
			because: "the envelope is clio's own: Redact removes the credential and nothing only a site's text may lose");
	}

	[Test]
	[Category("Unit")]
	[Description("The inverse: clio's own log lines on a passthrough request keep using Redact.")]
	public void PassthroughLogLines_ShouldKeepUsingRedact() {
		// Arrange
		List<LogMessage> messages = [new InfoMessage("Wrote ./MyPkg/descriptor.json for user 'operator_login' token=abc123")];

		// Act
		McpPassthroughRedaction.RedactForPassthrough(messages, ToolCommandResolver.PassthroughKeyPrefix + "tenant");

		// Assert
		messages.Single().Value.Should().Be("Wrote ./MyPkg/descriptor.json for user 'operator_login' token=[redacted]",
			because: "a clio log line is not site text");
	}

	#region Source guard

	// Every call of an extractor that hands a caller text the SITE wrote, and how that call is wired. A call site
	// that appears in clio without an entry here fails SourceGuard_ShouldKnowEverySiteTextExtractorCallSite: add
	// it with a wiring case in this fixture (or in the fixture of the tool it belongs to) before adding it here.
	//   "central": the extractor itself applies the site entry point (asserted by CentralExtractors below);
	//   "call site": the caller must apply RedactSiteText to what it extracted (asserted next to the call);
	//   "discarded": the extracted text is thrown away and reaches no caller.
	private static readonly Dictionary<string, (int Count, string Wiring)> KnownCallSites = new(StringComparer.Ordinal) {
		["SiteResponse.FromRefusal|clio/Command/McpServer/Tools/ODataCreateTool.cs"] = (2, "central"),
		["SiteResponse.FromRefusal|clio/Command/McpServer/Tools/ODataUpdateTool.cs"] = (1, "central"),
		["SiteResponse.FromRefusal|clio/Command/RuntimeVerification/RecordDeletionService.cs"] = (1, "central"),
		["SiteResponse.FromRefusal|clio/Common/SiteResponseCapture.cs"] = (1, "central"),
		["CreatioResponseError.TryDetect|clio/Command/Branding/BrandingBackgroundService.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/EmailTemplateTool.cs"] = (2, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/ODataCreateTool.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/ODataFieldValidation.cs"] = (2, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/McpServer/Tools/ODataKeyedWrite.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Command/RuntimeVerification/TriggerSmokeRunner.cs"] = (1, "call site"),
		["CreatioResponseError.TryDetect|clio/Query/DataServiceQuery.cs"] = (1, "discarded"),
		["CreatioResponseError.DescribeNonJsonResponse|clio/Command/McpServer/Tools/ODataCreateTool.cs"] = (1, "central"),
		["CreatioResponseError.DescribeNonJsonResponse|clio/Command/McpServer/Tools/ODataFieldValidation.cs"] = (3, "central"),
		["CreatioResponseError.DescribeNonJsonResponse|clio/Command/McpServer/Tools/ODataKeyedWrite.cs"] = (1, "central"),
		["RecordDeletionService.ReadInnerMessage|clio/Command/RuntimeVerification/RecordDeletionService.cs"] = (1, "central"),
		["RecordDeletionService.ReadInnerMessage|clio/Command/RuntimeVerification/TriggerSmokeRunner.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/ClientUnitSchemaCreate.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/LocalizePageCommand.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/PageCreateOptions.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/PageUpdateOptions.cs"] = (1, "central"),
		["PageSchemaMetadataHelper.ParseSaveErrorMessage|clio/Command/SchemaDesignerHelper.cs"] = (1, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/ApplicationSectionLocalization.cs"] = (4, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/DataBindingDbCommand.cs"] = (5, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/DataBindingDbLocalizer.cs"] = (2, "central"),
		["DataServiceResponse.ThrowIfUnsuccessful|clio/Command/PackageDataBindingWriter.cs"] = (2, "central"),
	};

	// The extractors and the call shape of each, as grep would find it.
	private static readonly (string Name, Regex Call)[] Extractors = [
		("SiteResponse.FromRefusal", new Regex(@"\bSiteResponse\.FromRefusal\(")),
		("CreatioResponseError.TryDetect", new Regex(@"\bCreatioResponseError\.TryDetect\(")),
		("CreatioResponseError.DescribeNonJsonResponse", new Regex(@"(?<!static string )\bDescribeNonJsonResponse\(")),
		("RecordDeletionService.ReadInnerMessage", new Regex(@"(?<!static string )\bReadInnerMessage\(")),
		("PageSchemaMetadataHelper.ParseSaveErrorMessage", new Regex(@"(?<!static string )\bParseSaveErrorMessage\(")),
		("DataServiceResponse.ThrowIfUnsuccessful", new Regex(@"\bDataServiceResponse\.ThrowIfUnsuccessful\(")),
	];

	// Where each "central" extractor - and the two fenced item paths - applies the site entry point: the file, the
	// member that must hold it, and the call that must appear inside that member.
	private static readonly (string File, string Member, string Call)[] CentralExtractors = [
		("clio/Common/SiteResponse.cs", "private static string Carry(", "SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Common/CreatioResponseError.cs", "internal static string DescribeNonJsonResponse(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/RuntimeVerification/RecordDeletionService.cs", "internal static string ReadInnerMessage(",
			"UntrustedText.FencedSiteText("),
		("clio/Command/PageSchemaMetadataHelper.cs", "internal static string ParseSaveErrorMessage(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Common/DataServiceResponse.cs", "internal static void ThrowIfUnsuccessful(string response",
			"SensitiveErrorTextRedactor.RedactSiteText("),
		("clio/Command/DataServiceBatchCommand.cs", "private static string ReadError(",
			"SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull("),
		("clio/Common/DataWriteDiagnostic.cs", "public static DataWriteDiagnostic Create(",
			"SensitiveErrorTextRedactor.RedactUntrustedSiteTextOrNull(message)"),
		("clio/Command/McpServer/Tools/CompileStatusTool.cs", "internal static string RenderError(",
			"UntrustedText.FencedSiteText("),
		("clio/Command/McpServer/Tools/CompileOperationRegistry.cs", "private static IReadOnlyList<string> BuildMessageTail(",
			"SensitiveErrorTextRedactor.RedactSiteText("),
	];

	[Test]
	[Category("Unit")]
	[Description("A grep over clio's sources finds exactly the known call sites of the site-text extractors: a new one "
		+ "fails here until it has a wiring case and an entry in KnownCallSites.")]
	public void SourceGuard_ShouldKnowEverySiteTextExtractorCallSite() {
		// Arrange
		Dictionary<string, int> found = new(StringComparer.Ordinal);

		// Act
		foreach (string file in ClioSources()) {
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot, file));
			foreach ((string name, Regex call) in Extractors) {
				int count = lines.Count(line => !IsComment(line) && call.IsMatch(line));
				if (count > 0) {
					found[$"{name}|{file}"] = count;
				}
			}
		}

		// Assert
		found.Should().BeEquivalentTo(KnownCallSites.ToDictionary(pair => pair.Key, pair => pair.Value.Count),
			because: "every call site of a site-text extractor must be listed with its wiring - a new one needs a "
				+ "wiring case proving its text reaches the caller only through RedactSiteText");
	}

	[Test]
	[Category("Unit")]
	[Description("Every 'call site' extractor use applies RedactSiteText to what it extracted within a few lines, and "
		+ "every 'discarded' use really discards the text.")]
	public void SourceGuard_ShouldFindRedactSiteTextNextToEveryCallSiteExtraction() {
		// Arrange
		List<string> unwired = [];

		// Act
		foreach ((string key, (int _, string wiring)) in KnownCallSites.Where(pair => pair.Value.Wiring != "central")) {
			string[] parts = key.Split('|');
			Regex call = Extractors.Single(extractor => extractor.Name == parts[0]).Call;
			string[] lines = File.ReadAllLines(Path.Combine(RepositoryRoot, parts[1]));
			for (int index = 0; index < lines.Length; index++) {
				if (IsComment(lines[index]) || !call.IsMatch(lines[index])) {
					continue;
				}
				string window = string.Join('\n', lines.Skip(index).Take(30));
				bool ok = wiring == "discarded"
					? window.Contains("out string _)", StringComparison.Ordinal)
					: window.Contains("SensitiveErrorTextRedactor.RedactSiteText(", StringComparison.Ordinal);
				if (!ok) {
					unwired.Add($"{parts[1]}:{index + 1} ({wiring})");
				}
			}
		}

		// Assert
		unwired.Should().BeEmpty(because: "the server text CreatioResponseError.TryDetect extracts is the site's own");
	}

	[Test]
	[Category("Unit")]
	[Description("Every centrally wired extractor applies the site entry point inside its own body.")]
	public void SourceGuard_ShouldFindTheSiteEntryPointInsideEveryCentralExtractor() {
		// Arrange
		List<string> unwired = [];

		// Act
		foreach ((string file, string member, string call) in CentralExtractors) {
			string source = File.ReadAllText(Path.Combine(RepositoryRoot, file));
			int start = source.IndexOf(member, StringComparison.Ordinal);
			string body = start < 0 ? string.Empty : MemberBody(source, start);
			if (!body.Contains(call, StringComparison.Ordinal)) {
				unwired.Add($"{file}: {member} lacks {call}");
			}
		}

		// Assert
		unwired.Should().BeEmpty(because: "a central extractor is the only place its callers' site text is redacted");
	}

	private static IEnumerable<string> ClioSources() =>
		Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "clio"), "*.cs", SearchOption.AllDirectories)
			.Select(path => Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'))
			.Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
			.OrderBy(path => path, StringComparer.Ordinal);

	private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

	// The member's text from its signature to the end of its body (or of its expression body), by brace counting.
	private static string MemberBody(string source, int start) {
		int open = source.IndexOf('{', start);
		int arrow = source.IndexOf("=>", start, StringComparison.Ordinal);
		if (arrow >= 0 && (open < 0 || arrow < open)) {
			int end = source.IndexOf(";\n", arrow, StringComparison.Ordinal);
			return source[start..(end < 0 ? source.Length : end)];
		}
		int depth = 0;
		for (int index = open; index < source.Length; index++) {
			depth += source[index] switch { '{' => 1, '}' => -1, _ => 0 };
			if (depth == 0) {
				return source[start..(index + 1)];
			}
		}
		return source[start..];
	}

	#endregion
}

/// <summary>Requirement (b) for the DataService batch path: a batch item's error is the site's text.</summary>
[TestFixture]
[Property("Module", "Common")]
public sealed class SiteTextBatchWiringTests : BaseCommandTests<DataServiceBatchOptions> {

	private IApplicationClient _client;
	private DataServiceBatchCommand _command;

	protected override void AdditionalRegistrations(Microsoft.Extensions.DependencyInjection.IServiceCollection services) {
		_client = Substitute.For<IApplicationClient>();
		Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, _client);
		Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services,
			Substitute.For<System.IO.Abstractions.IFileSystem>());
	}

	public override void Setup() {
		base.Setup();
		_command = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
			.GetRequiredService<DataServiceBatchCommand>(Container);
	}

	[Test]
	[Category("Unit")]
	[Description("A batch item's responseStatus message (batch item errors of the DataService write tools) goes "
		+ "through the fenced site rendering, in the item's error and in its diagnostic.")]
	public void BatchItemError_ShouldBeFencedSiteText() {
		// Arrange
		DataServiceBatchOperation operation = new("update", "Contact", Guid.NewGuid(),
			new() { ["Name"] = new(1, JsonSerializer.SerializeToElement("Synthetic")) });
		_client.ExecuteNonReplayablePostRequest(Arg.Any<string>(), Arg.Any<string>(), 30000, 1, 1).Returns(call => {
			using JsonDocument sent = JsonDocument.Parse(call.ArgAt<string>(1));
			string queryId = sent.RootElement.GetProperty("items")[0].GetProperty("queryId").GetString();
			return JsonSerializer.Serialize(new {
				queryResults = new[] {
					new { queryId, success = false, responseStatus = new { Message = SiteTextWiringTests.SiteMessage } }
				}
			});
		});

		// Act
		DataServiceBatchResult result = _command.ExecuteBatch([operation]);

		// Assert
		result.Items.Should().ContainSingle().Which.Error.Should().Contain("Login failed for user '[redacted]'.",
			because: "the item error is the site's text, and its reason stays");
		JsonSerializer.Serialize(result).Should().NotContain(SiteTextWiringTests.Marker,
			because: "neither the item error nor its diagnostic may carry the login");
	}
}
