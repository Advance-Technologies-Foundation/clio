using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Tests.Common;

/// <summary>
/// Every entry point and every sink that carries a site's text, driven through its own seam in ONE clio build: master's
/// (loaded into its own load context by the baseline regeneration) or this one. The seams are read by reflection in both,
/// so master's recorded outputs and this build's outputs come from the same calls.
/// </summary>
/// <remarks>
/// A sink is the code that writes the site's text where a caller reads it, run in that build's own order: master cut the
/// fenced input at 2,400, the non-JSON preview at 500 and a refused delete's inner message to its first line and 500
/// before its chain, and capped <c>site-response.message</c> after it. Where a sink's own output is the raw text and the
/// MCP layer redacts it afterwards (the DataService failure, the designer's save error), the sink is read with that
/// <c>Redact</c>, as it reaches the caller. Call sites inside an instance method that needs a live client (Branding,
/// odata-create's insert, odata-field validation, the trigger smoke run) are read through their seam,
/// <c>CreatioResponseError.TryDetect</c>, and the expression the call site applies to it: <c>Redact</c> on master,
/// <c>RedactSiteText</c> here; <c>SiteTextWiringTests</c>' source guard pins that expression at each call site.
/// </remarks>
internal sealed class RedactionSinks {

	private readonly Assembly _clio;
	private readonly Type _redactor;
	private readonly bool _siteBuild;

	internal RedactionSinks(Assembly clio) {
		_clio = clio;
		_redactor = Type("Clio.Common.SensitiveErrorTextRedactor");
		_siteBuild = _redactor.GetMethod("RedactSiteText", AnyStatic, [typeof(string)]) is not null;
	}

	private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	private Type Type(string name) => _clio.GetType(name, throwOnError: true);

	private static object Call(Type type, string name, Type[] parameters, params object[] arguments) {
		MethodInfo method = type.GetMethod(name, AnyStatic, parameters)
			?? throw new MissingMethodException(type.FullName, name);
		try {
			return method.Invoke(null, arguments);
		}
		catch (TargetInvocationException exception) when (exception.InnerException is not null) {
			throw exception.InnerException;
		}
	}

	private string Text(string name, string text) => (string)Call(_redactor, name, [typeof(string)], text) ?? string.Empty;

	/// <summary>True for a build that has the site-text entry point (this one), false for master's.</summary>
	internal bool SiteBuild => _siteBuild;

	internal string Redact(string text) => Text("Redact", text);

	internal string RedactCredentials(string text) => Text("RedactCredentials", text);

	internal string RedactJsonCredentialProperties(string text) => Text("RedactJsonCredentialProperties", text);

	internal string Fenced(string text) => Text("RedactUntrustedOrNull", text);

	internal string Console(string text) => Text("RedactForConsoleOrNull", text);

	/// <summary>What a site-text call site applies: <c>RedactSiteText</c> here, <c>Redact</c> on master.</summary>
	internal string SiteText(string text) => Text(_siteBuild ? "RedactSiteText" : "Redact", text);

	/// <summary>The fenced rendering a site-text call site uses: the site variant here, the plain one on master.</summary>
	internal string FencedSiteText(string text) => Text(_siteBuild ? "RedactUntrustedSiteTextOrNull" : "RedactUntrustedOrNull", text);

	private object SiteResponse(string message, string code) {
		object answer = Activator.CreateInstance(Type("Clio.Common.SiteHttpResponse"), (int?)500,
			JsonSerializer.Serialize(new { errorInfo = new { message, errorCode = code } }));
		return Call(Type("Clio.Common.SiteResponse"), "FromRefusal", [Type("Clio.Common.SiteHttpResponse")], answer);
	}

	internal string SiteResponseMessage(string text) {
		object response = SiteResponse(text, "E1");
		return (string)response.GetType().GetProperty("Message")!.GetValue(response) ?? string.Empty;
	}

	internal string SiteResponseCode(string text) {
		object response = SiteResponse("refused", text);
		return (string)response.GetType().GetProperty("Code")!.GetValue(response) ?? string.Empty;
	}

	/// <summary>The non-JSON preview as its callers return it: the sentence, Redacted.</summary>
	internal string NonJsonPreview(string text) =>
		Redact((string)Call(Type("Clio.Common.CreatioResponseError"), "DescribeNonJsonResponse", [typeof(string)], text));

	/// <summary>The OData error text <c>CreatioResponseError.TryDetect</c> reads from <c>{"error":{"message":…}}</c>.</summary>
	internal string DetectedODataError(string text) {
		using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { error = new { code = "", message = text } }));
		Type error = Type("Clio.Common.CreatioResponseError");
		Type context = Type("Clio.Common.CreatioResponseContext");
		MethodInfo detect = error.GetMethod("TryDetect", AnyStatic, [typeof(JsonElement), context, typeof(string).MakeByRefType()])!;
		object[] arguments = [document.RootElement, Enum.Parse(context, "ODataPayload"), null];
		return (bool)detect.Invoke(null, arguments)! ? (string)arguments[2] : string.Empty;
	}

	/// <summary>odata-create's insert, odata-field validation and the trigger smoke run: the call site's redaction of
	/// the detected error.</summary>
	internal string ODataErrorCallSite(string text) => SiteText(DetectedODataError(text));

	/// <summary>
	/// Branding's and email-template's OData error, as the caller returns it: master passed the detected text on raw
	/// (<c>error = serverError</c>, <c>throw new InvalidOperationException(error)</c>) and the MCP layer redacted it; this
	/// build redacts it as site text at the call site, and the MCP layer still redacts it. Read through that caller's
	/// <c>Redact</c>, so the comparison with master is not vacuous.
	/// </summary>
	internal string RawODataErrorThroughTheCaller(string text) {
		string detected = DetectedODataError(text);
		return Redact(_siteBuild ? Text("RedactSiteText", detected) : detected);
	}

	/// <summary>odata-update and odata-delete with a key: <c>ODataKeyedWrite.ValidateWriteResponse</c>.</summary>
	internal string KeyedWrite(string text) =>
		(string)Call(Type("Clio.Command.McpServer.Tools.ODataKeyedWrite"), "ValidateWriteResponse", [typeof(string)],
			JsonSerializer.Serialize(new { error = new { code = "", message = text } })) ?? string.Empty;

	/// <summary><c>DataServiceResponse.ThrowIfUnsuccessful</c>'s exception message, Redacted as the MCP layer returns it.</summary>
	internal string DataServiceFailure(string text) {
		try {
			Call(Type("Clio.Common.DataServiceResponse"), "ThrowIfUnsuccessful", [typeof(string), typeof(string)],
				JsonSerializer.Serialize(new { success = false, errorInfo = new { message = text } }), "Write");
			return string.Empty;
		}
		catch (InvalidOperationException exception) {
			return Redact(exception.Message);
		}
	}

	/// <summary><c>PageSchemaMetadataHelper.ParseSaveErrorMessage</c>, Redacted as every caller returns it.</summary>
	internal string DesignerSaveError(string text) =>
		Redact((string)Call(Type("Clio.Command.PageSchemaMetadataHelper"), "ParseSaveErrorMessage", [typeof(JObject), typeof(string)],
			JObject.Parse(JsonSerializer.Serialize(new { success = false, errorInfo = new { message = text } })), "-"));

	/// <summary><c>DataServiceBatchService.ReadError</c>: a batch item's error.</summary>
	internal string BatchItemError(string text) {
		using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new { success = false, responseStatus = new { Message = text } }));
		return (string)Call(Type("Clio.Command.DataServiceBatchService"), "ReadError", [typeof(JsonElement)], document.RootElement)
			?? string.Empty;
	}

	private object Diagnostic(string text) => Activator.CreateInstance(Type("Clio.Package.PackageBuildDiagnostic"),
		"CS0103", text, "x.cs", (int?)1, (int?)2, false);

	/// <summary><c>CompileStatusTool.RenderError</c>: a compiler diagnostic.</summary>
	internal string CompileDiagnostic(string text) =>
		(string)Call(Type("Clio.Command.McpServer.Tools.CompileStatusTool"), "RenderError", [Type("Clio.Package.PackageBuildDiagnostic")],
			Diagnostic(text)) ?? string.Empty;

	/// <summary>The text <c>RenderError</c> fences: the diagnostic's own rendering.</summary>
	internal string CompileDiagnosticText(string text) {
		object diagnostic = Diagnostic(text);
		Func<string, string> same = value => value;
		return (string)diagnostic.GetType().GetMethod("Format")!.Invoke(diagnostic, [same, same]);
	}

	/// <summary><c>CompileOperationRegistry.BuildMessageTail</c> over one compiler message.</summary>
	internal string CompileMessageTail(string text) {
		Type logMessage = Type("Clio.Common.LogMessage");
		Array messages = Array.CreateInstance(logMessage, 1);
		messages.SetValue(Activator.CreateInstance(Type("Clio.Common.ErrorMessage"), text), 0);
		Type list = typeof(IReadOnlyList<>).MakeGenericType(logMessage);
		IEnumerable<string> tail = (IEnumerable<string>)Call(Type("Clio.Command.McpServer.Tools.CompileOperationRegistry"),
			"BuildMessageTail", [list], messages);
		return string.Join('\n', tail);
	}

	/// <summary><c>RecordDeletionService.ReadInnerMessage</c>: a refused delete's inner message.</summary>
	internal string RefusedDeleteInnerMessage(string text) =>
		(string)Call(Type("Clio.Command.RuntimeVerification.RecordDeletionService"), "ReadInnerMessage", [typeof(string)],
			JsonSerializer.Serialize(new { error = new { message = "An error has occurred.", innererror = new { message = text } } }))
		?? string.Empty;

	/// <summary><c>ProcessRunMonitor.FirstLine</c>, which master runs before the refused delete's cut.</summary>
	internal string FirstLine(string text) =>
		(string)Call(Type("Clio.Command.RuntimeVerification.ProcessRunMonitor"), "FirstLine", [typeof(string)], text);

	/// <summary><c>DataWriteDiagnostic.Create(…).Message</c>.</summary>
	internal string DataWriteDiagnosticMessage(string text) {
		object diagnostic = Call(Type("Clio.Common.DataWriteDiagnostic"), "Create",
			[typeof(string), typeof(string), typeof(int?), typeof(bool), typeof(bool), typeof(bool), typeof(string)],
			"insert", "Account", null, true, true, false, text);
		return (string)diagnostic.GetType().GetProperty("Message")!.GetValue(diagnostic) ?? string.Empty;
	}

	/// <summary>
	/// One compared output of the baseline. <paramref name="Code"/> is its letter in the baseline file;
	/// <paramref name="Run"/> is the sink; <paramref name="Uncut"/>, for a sink that cuts AFTER the chain (the fenced
	/// renderings at 300, <c>site-response</c> at its cap), is master's chain over the same input with that cut left out.
	/// A token master's output lacks only because of a cut after the chain was never redacted: it was cut, and a build
	/// whose redaction leaves its text shorter may show it inside the window. So the baseline records what BOTH master's
	/// output and its uncut companion removed. A cut BEFORE the chain is in both, as master ran it.
	/// </summary>
	internal sealed record Sink(char Code, string Name, Func<RedactionSinks, string, string> Run,
		Func<RedactionSinks, string, string> Uncut = null);

	// Master's 2,400 cut before the fenced rendering's chain.
	private static string FenceInput(string text) => text.Length > 2_400 ? text[..2_400] : text;

	/// <summary>Every compared entry point and sink, by its baseline letter.</summary>
	internal static readonly Sink[] All = [
		new('R', "Redact", (sinks, text) => sinks.Redact(text)),
		new('C', "RedactCredentials", (sinks, text) => sinks.RedactCredentials(text)),
		new('J', "RedactJsonCredentialProperties", (sinks, text) => sinks.RedactJsonCredentialProperties(text)),
		new('F', "RedactUntrustedOrNull", (sinks, text) => sinks.Fenced(text), (sinks, text) => sinks.Redact(FenceInput(text))),
		new('K', "RedactForConsoleOrNull", (sinks, text) => sinks.Console(text), (sinks, text) => sinks.Redact(FenceInput(text))),
		new('M', "site-response.message", (sinks, text) => sinks.SiteResponseMessage(text), (sinks, text) => sinks.SiteText(text)),
		new('D', "site-response.code", (sinks, text) => sinks.SiteResponseCode(text), (sinks, text) => sinks.SiteText(text)),
		new('N', "non-JSON preview", (sinks, text) => sinks.NonJsonPreview(text)),
		new('O', "OData error call sites (odata-create, field validation, trigger smoke)", (sinks, text) => sinks.ODataErrorCallSite(text)),
		new('W', "keyed write (odata-update, odata-delete)", (sinks, text) => sinks.KeyedWrite(text)),
		new('H', "Branding's OData error, through the caller's Redact", (sinks, text) => sinks.RawODataErrorThroughTheCaller(text)),
		new('L', "email-template's OData error, through the caller's Redact", (sinks, text) => sinks.RawODataErrorThroughTheCaller(text)),
		new('S', "DataService failure", (sinks, text) => sinks.DataServiceFailure(text)),
		new('G', "designer save error", (sinks, text) => sinks.DesignerSaveError(text)),
		new('B', "batch item error", (sinks, text) => sinks.BatchItemError(text), (sinks, text) => sinks.SiteText(FenceInput(text))),
		new('X', "compile-status diagnostic", (sinks, text) => sinks.CompileDiagnostic(text),
			(sinks, text) => sinks.SiteText(FenceInput(sinks.CompileDiagnosticText(text)))),
		new('T', "compile message tail", (sinks, text) => sinks.CompileMessageTail(text)),
		new('I', "refused delete's inner message", (sinks, text) => sinks.RefusedDeleteInnerMessage(text), (sinks, text) => {
			string firstLine = sinks.FirstLine(text);
			return sinks.SiteText(FenceInput(firstLine.Length > 500 ? firstLine[..500] : firstLine));
		}),
		new('Y', "DataWriteDiagnostic.message", (sinks, text) => sinks.DataWriteDiagnosticMessage(text), (sinks, text) => {
			int preview = text.IndexOf(" Response: ", StringComparison.Ordinal);
			return sinks.SiteText(FenceInput(preview >= 0 ? text[..preview] : text));
		})
	];

	/// <summary>
	/// The outputs of this build compared with a baseline letter: the sink itself, and for <c>R</c> and <c>F</c> also
	/// the site-text entry point, which master's site sinks ran as <c>Redact</c> and the plain fenced rendering.
	/// </summary>
	internal static readonly (string Name, char Code, Func<RedactionSinks, string, string> Run)[] Compared =
		[.. All.Select(sink => (sink.Name, sink.Code, sink.Run)),
			("RedactSiteText", 'R', (sinks, text) => sinks.SiteText(text)),
			("RedactUntrustedSiteTextOrNull", 'F', (sinks, text) => sinks.FencedSiteText(text))];

	/// <summary>
	/// The sinks whose own output master left raw, each read through the caller's <c>Redact</c> as it reaches the MCP
	/// result: the DataService failure (S), the designer's save error (G), Branding's (H) and email-template's (L) OData
	/// error. None is compared vacuously.
	/// </summary>
	internal static readonly char[] ReadThroughTheCallersRedact = ['S', 'G', 'H', 'L'];
}
