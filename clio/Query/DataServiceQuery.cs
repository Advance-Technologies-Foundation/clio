using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Common;
using CommandLine;
using CommandLine.Text;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Query;

[Verb("call-service", Aliases = ["cs"], HelpText = "Call Service Request")]
public class CallServiceCommandOptions : RemoteCommandOptions {

	#region Properties: Public

	/// <summary>
	/// Gets or sets the package maintainer used for the target environment.
	/// </summary>
	[Option("maintainer", Required = false, HelpText = "Maintainer name")]
	public new string Maintainer {
		get => base.Maintainer;
		set => base.Maintainer = value;
	}

	/// <summary>
	/// Gets or sets the HTTP method used for the service request.
	/// </summary>
	[Option('m', "method", Required = false, HelpText = "HTTP method", Separator = ';')]
	public string HttpMethodName { get; set; }

	[Option('f', "input", Required = false, HelpText = "Request file", Separator = ' ')]
	public string RequestFileName { get; set; }

	[Option('b', "body", Required = false, HelpText = "Request body JSON")]
	public string RequestBody { get; set; }

	[Option('d', "destination", Required = false, HelpText = "Destination set")]
	public string ResultFileName { get; set; }

	[Option("service-path", Required = false, HelpText =
		"Route service path, relative to the Creatio application root. Use 'odata/Entity'; the "
		+ "equivalent '/odata/Entity', '0/odata/Entity' and '/0/odata/Entity' forms are accepted and "
		+ "the optional '0/' application alias is stripped, including a repeated '0/0/' prefix")]
	public string ServicePath { get; set; }

	[Option('v', "variables", Required = false, HelpText = "Result file", Separator = ';')]
	public IEnumerable<string> Variables { get; set; }

	/// <summary>
	/// Canonical usage examples. They live here rather than in the generated markdown so that
	/// `clio call-service --help` shows the accepted --service-path forms and the next
	/// `__generate-help-artifacts` run reproduces them instead of dropping hand-written ones.
	/// </summary>
	[Usage(ApplicationAlias = "clio")]
	public static IEnumerable<Example> Examples =>
		new List<Example> {
			new("Read an OData collection",
				new CallServiceCommandOptions {
					HttpMethodName = "GET", ServicePath = "odata/BulkEmailCategory"
				}),
			new("The same route with the optional application-root alias",
				new CallServiceCommandOptions {
					HttpMethodName = "GET", ServicePath = "/0/odata/BulkEmailCategory"
				}),
			new("Post a request body and save the response",
				new CallServiceCommandOptions {
					HttpMethodName = "POST", ServicePath = "ServiceModel/EntityDataService.svc",
					RequestFileName = "request.json", ResultFileName = "result.json"
				})
		};

	#endregion

}

[Verb("dataservice", Aliases = ["ds"], HelpText = "DataService Request")]
public class DataServiceQueryOptions : CallServiceCommandOptions {

	#region Properties: Public

	[Option('t', "type", Required = true, HelpText = "Operation type", Separator = ' ')]
	public string OperationType { get; set; }

	#endregion

}

public class CallServiceCommand : BaseServiceCommand<CallServiceCommandOptions> {

	#region Constructors: Public

	public CallServiceCommand(IApplicationClient applicationClient,
		EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem)
		: base(applicationClient, settings, serviceUrlBuilder, fileSystem){ }

	#endregion

}

public class DataServiceQuery : BaseServiceCommand<DataServiceQueryOptions> {

	#region Constructors: Public

	public DataServiceQuery(IApplicationClient applicationClient,
		EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilder, IFileSystem fileSystem)
		: base(applicationClient, settings, serviceUrlBuilder, fileSystem){ }

	#endregion

	#region Methods: Protected

	protected override string BuildUrl(DataServiceQueryOptions options){
		return options.OperationType.ToUpperInvariant() switch {
					"SELECT" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Select),
					"INSERT" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Insert),
					"UPDATE" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Update),
					"DELETE" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Delete),
					var _ => throw new Exception("Unknown operation type")
				};
	}

	#endregion

}

public abstract class BaseServiceCommand<T> : RemoteCommand<T> where T : CallServiceCommandOptions {

	#region Fields: Private

	/// <summary>
	/// Upper bound for the error-page detection patterns so a hostile or oversized response body cannot
	/// stall the command through catastrophic backtracking.
	/// </summary>
	private static readonly TimeSpan ErrorDetectionRegexTimeout = TimeSpan.FromSeconds(1);

	private readonly IFileSystem _fileSystem;

	#endregion

	#region Fields: Protected

	protected readonly IServiceUrlBuilder ServiceUrlBuilderInstance;

	public bool IsSilent { get; private set; }

	#endregion

	#region Constructors: Protected

	protected BaseServiceCommand(IApplicationClient applicationClient,
		EnvironmentSettings settings,
		IServiceUrlBuilder serviceUrlBuilderInstance, IFileSystem fileSystem)
		: base(applicationClient, settings){
		ServiceUrlBuilderInstance = serviceUrlBuilderInstance;
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Private

	// Errors and the beautified body come out of ONE parse. call-service can return multi-megabyte
	// OData exports, and classifying with a throwaway tree and then re-parsing the same body to
	// indent it doubled the full-tree parse and its transient allocation on every successful call.
	// The document is handed back to the caller, which serializes that same document.
	private static bool TryClassifyResponse(string response, out JsonDocument parsed,
		out ServiceResponseClassification classification) {
		parsed = null;
		classification = default;
		if (string.IsNullOrWhiteSpace(response)) {
			return true;
		}

		if (CreatioResponseError.IsMarkup(response)) {
			//An HTML/XML body is never a successful service payload: the request did not reach the
			//service intact. Recognizing the markup is what decides failure - the status line, the known
			//page wording and the generic markers only sharpen the diagnostic. A marker-free page such as
			//"<html><body>Access denied</body></html>" carries none of them, and returning success for it
			//saved the page to --destination and exited 0, which is the very behaviour this contract
			//forbids. Fail closed instead.
			if (TryGetErrorStatus(response, out int statusCode)) {
				classification = new ServiceResponseClassification(
					ServiceResponseFailure.HttpErrorStatus, statusCode);
			}
			else if (CreatioResponseError.IsKnownErrorPage(response) || MatchesErrorPageMarkers(response)) {
				classification = new ServiceResponseClassification(ServiceResponseFailure.KnownErrorPage);
			}
			else {
				classification = new ServiceResponseClassification(ServiceResponseFailure.NotAServicePayload);
			}
			return false;
		}

		try {
			parsed = JsonDocument.Parse(response);
		}
		catch (JsonException) {
			//Not JSON and not markup - a plain-text body is passed through unchanged, as before.
			return true;
		}

		//The detected text is deliberately discarded: it is remote-authored prose and must not be
		//logged. Only the fact that the service reported a failure is kept.
		if (!CreatioResponseError.TryDetect(parsed.RootElement, CreatioResponseContext.Service, out string _)) {
			return true;
		}
		classification = new ServiceResponseClassification(ServiceResponseFailure.ReportedFailure);
		parsed.Dispose();
		parsed = null;
		return false;
	}

	/// <summary>
	/// Why a response was rejected, decided locally. The remote body is never part of it: a service or
	/// a proxy can put personal data, opaque tokens, newline/ANSI control sequences or prompt-like text
	/// into <c>Exception</c>, <c>errorInfo.message</c> or an OData <c>error.message</c>, and any of that
	/// would land verbatim in a terminal or a CI log.
	/// </summary>
	private enum ServiceResponseFailure {
		None,
		HttpErrorStatus,
		KnownErrorPage,
		NotAServicePayload,
		ReportedFailure
	}

	private readonly record struct ServiceResponseClassification(
		ServiceResponseFailure Failure, int StatusCode = 0);

	// Indents the already-parsed document. UnsafeRelaxedJsonEscaping keeps the output byte-identical
	// in intent to what Newtonsoft wrote before: without it System.Text.Json escapes `+`, `<`, `>`
	// and every non-ASCII character, which would mangle saved payloads containing them.
	private static string Beautify(JsonDocument parsed) =>
		JsonSerializer.Serialize(parsed.RootElement, BeautifyOptions);

	private static readonly JsonSerializerOptions BeautifyOptions = new() {
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	private static string ReplaceVariablesInJson(string json, IEnumerable<string> variables){
		if (variables == null) {
			return json;
		}
		foreach (string variable in variables) {
			string pattern = "{{" + variable.Split('=')[0] + "}}";
			Regex regex = new(pattern);
			Match match = regex.Match(json);
			if (match.Success) {
				json = regex.Replace(json, variable.Split('=')[1]);
			}
		}
		return json;
	}

	private static string NormalizeServicePath(string servicePath) {
		if (string.IsNullOrWhiteSpace(servicePath)) {
			return servicePath;
		}

		// Stripping has to loop: a single pass over "0/0/odata/Entity" leaves one "0/" layer behind,
		// which ServiceUrlBuilder.Build then double-adds on .NET Framework environments. The prefix is
		// purely numeric, so Ordinal is the correct comparison - digits have no case variants.
		//
		// The loop advances an INDEX rather than re-slicing the string. Each string range materializes the
		// whole remaining suffix, so a path built from repeated "0/" prefixes was quadratic: 2,000/4,000/
		// 8,000 prefixes allocated roughly 8/32/128 MB before the URL was even constructed. One string is
		// created at the end instead.
		ReadOnlySpan<char> normalized = servicePath.AsSpan().Trim();
		while (true) {
			if (normalized.StartsWith("/0/", StringComparison.Ordinal)) {
				normalized = normalized[3..];
				continue;
			}
			break;
		}
		while (normalized.StartsWith("0/", StringComparison.Ordinal)) {
			normalized = normalized[2..];
		}

		return normalized.TrimStart('/').ToString();
	}

	// A timeout-guarded regex throws RegexMatchTimeoutException when the bound fires, and nothing up the
	// call chain catches it - it would reach Main's top-level handler, report a cryptic regex error and
	// discard the service response. Treating a timeout as "no error status found" is the conservative
	// fallback and matches every other timeout-guarded regex site in the codebase.
	private static bool TryGetErrorStatus(string response, out int statusCode) {
		statusCode = 0;
		try {
			Match match = Regex.Match(response ?? string.Empty, @"(?:HTTP\s+Error\s+|<title>\s*)(?<status>[45]\d{2})\b",
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, ErrorDetectionRegexTimeout);
			return match.Success && int.TryParse(match.Groups["status"].Value, out statusCode);
		}
		catch (RegexMatchTimeoutException) {
			return false;
		}
	}

	private static bool MatchesErrorPageMarkers(string html) {
		try {
			return Regex.IsMatch(html, "server error|file or directory not found|error page",
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, ErrorDetectionRegexTimeout);
		}
		catch (RegexMatchTimeoutException) {
			// Conservative: the response is written normally rather than silently discarded.
			return false;
		}
	}

	private void WriteServiceError(ServiceResponseClassification classification) {
		string reason = classification.Failure switch {
			ServiceResponseFailure.HttpErrorStatus => $"HTTP status {classification.StatusCode}",
			ServiceResponseFailure.KnownErrorPage => "the response is an error page and carries no HTTP status",
			ServiceResponseFailure.NotAServicePayload => "the response is an HTML page, not a service payload",
			ServiceResponseFailure.ReportedFailure => "the service reported the request as failed",
			var _ => "the response could not be classified as a service payload"
		};
		//No response preview: see ServiceResponseFailure for why nothing remote-authored is logged.
		Logger.WriteError($"Service request failed ({reason}). Response was not saved.");
	}

	#endregion

	#region Methods: Protected

	protected virtual string BuildUrl(T options) => ServiceUrlBuilderInstance.Build(NormalizeServicePath(options.ServicePath));

	/// <summary>
	/// The outcome of one service call. A nullable body cannot carry this: a no-content GET, POST or
	/// DELETE legitimately answers with an empty body, and <see cref="TryClassifyResponse"/> accepts
	/// that as success - so returning the body alone made a successful empty response
	/// indistinguishable from a classified failure, and such a GET exited 1.
	/// </summary>
	protected readonly record struct ServiceRequestOutcome(bool Succeeded, string ResponseBody);

	protected ServiceRequestOutcome ExecuteServiceRequest(string url, string requestData,
		string resultFileName = null, string httpMethod = ""){
		string normalizedMethod = string.IsNullOrWhiteSpace(httpMethod)
			? "POST"
			: httpMethod.ToUpperInvariant();

		string jsonResult = normalizedMethod switch {
					"POST" => ApplicationClient.ExecutePostRequest(url, requestData),
					"GET" => ApplicationClient.ExecuteGetRequest(url),
					"DELETE" => ApplicationClient.ExecuteDeleteRequest(url, requestData),
					var _ => throw new ArgumentException($"Unsupported HTTP method '{httpMethod}'", nameof(httpMethod))
				};

		if (!TryClassifyResponse(jsonResult, out JsonDocument parsedResult,
			out ServiceResponseClassification classification)) {
			WriteServiceError(classification);
			return new ServiceRequestOutcome(Succeeded: false, ResponseBody: jsonResult);
		}

		using (parsedResult) {
			bool hasDestination = !string.IsNullOrWhiteSpace(resultFileName);
			//Nothing consumes the indented text when the command is silent and writes no file, so the
			//serialization is skipped entirely rather than produced and discarded.
			if (!hasDestination && IsSilent) {
				return new ServiceRequestOutcome(Succeeded: true, ResponseBody: jsonResult);
			}

			string beautifiedJson = parsedResult is null ? jsonResult : Beautify(parsedResult);
			if (!hasDestination) {
				// Print to console if no destination file specified
				Logger.WriteLine(beautifiedJson);
			}
			else {
				// Write to file if destination specified
				_fileSystem.WriteAllTextToFile(resultFileName, beautifiedJson);
				if (!IsSilent) {
					Logger.WriteInfo($"Result saved to {resultFileName}");
				}
			}
		}

		return new ServiceRequestOutcome(Succeeded: true, ResponseBody: jsonResult);
	}

	protected string GetRequestData(string requestFileName){
		IFileInfo fi = _fileSystem.GetFilesInfos(requestFileName);
		if (!fi.Exists) {
			throw new FileNotFoundException("File not found", requestFileName);
		}
		return _fileSystem.ReadAllText(requestFileName);
	}

	#endregion

	#region Methods: Public

	public override int Execute(T options){
		IsSilent = options.IsSilent;
		string requestData = string.Empty;
		if (!(string.IsNullOrWhiteSpace(options.RequestFileName) && string.IsNullOrWhiteSpace(options.RequestBody))) {
			requestData = string.IsNullOrWhiteSpace(options.RequestBody)
				? GetRequestData(options.RequestFileName)
				: options.RequestBody;
			if (options.Variables != null && options.Variables.Any()) {
				requestData = ReplaceVariablesInJson(requestData, options.Variables);
			}
		}
		ServiceRequestOutcome outcome = ExecuteServiceRequest(
			BuildUrl(options), requestData, options.ResultFileName, options.HttpMethodName);
		return outcome.Succeeded ? 0 : 1;
	}

	#endregion

}
