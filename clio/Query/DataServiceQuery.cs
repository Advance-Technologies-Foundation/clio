using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using Clio.Command;
using Clio.Common;
using CommandLine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

	[Option("service-path", Required = false, HelpText = "Route service path")]
	public string ServicePath { get; set; }

	[Option('v', "variables", Required = false, HelpText = "Result file", Separator = ';')]
	public IEnumerable<string> Variables { get; set; }

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

	private static string BeautifyJsonIfPossible(string input){
		try {
			JToken parsedJson = JToken.Parse(input);
			return JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
		}
		catch (JsonReaderException) {
			return input;
		}
	}

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

		string normalized = servicePath.Trim();
		if (normalized.StartsWith("/0/", StringComparison.OrdinalIgnoreCase)) {
			normalized = normalized[3..];
		}
		else if (normalized.StartsWith("0/", StringComparison.OrdinalIgnoreCase)) {
			normalized = normalized[2..];
		}

		return normalized.TrimStart('/');
	}

	private static bool TryGetErrorStatus(string response, out int statusCode) {
		statusCode = 0;
		Match match = Regex.Match(response ?? string.Empty, @"(?:HTTP\s+Error\s+|<title>\s*)(?<status>[45]\d{2})\b",
			RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, ErrorDetectionRegexTimeout);
		return match.Success && int.TryParse(match.Groups["status"].Value, out statusCode);
	}

	private static bool IsErrorResponse(string response) {
		if (string.IsNullOrWhiteSpace(response)) {
			return false;
		}

		string trimmed = response.TrimStart();
		if (trimmed.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
			|| trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) {
			return TryGetErrorStatus(trimmed, out _)
				|| Regex.IsMatch(trimmed, "server error|file or directory not found|error page",
					RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, ErrorDetectionRegexTimeout);
		}

		try {
			if (JToken.Parse(trimmed) is not JObject parsed) {
				return false;
			}
			JToken code = parsed["Code"] ?? parsed["code"];
			JToken exception = parsed["Exception"] ?? parsed["exception"];
			return code?.Type == JTokenType.Integer && code.Value<int>() != 0
				&& exception?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(exception.Value<string>());
		}
		catch (JsonReaderException) {
			return false;
		}
	}

	private void WriteServiceError(string response) {
		string status = TryGetErrorStatus(response, out int statusCode)
			? $"HTTP status {statusCode}"
			: "HTTP status unavailable from the response body";
		string detail = response?.Trim();
		if (detail?.Length > 500) {
			detail = detail[..500] + "...";
		}
		Logger.WriteError($"Service request failed ({status}). Response was not saved. {detail}");
	}

	#endregion

	#region Methods: Protected

	protected virtual string BuildUrl(T options) => ServiceUrlBuilderInstance.Build(NormalizeServicePath(options.ServicePath));

	protected string ExecuteServiceRequest(string url, string requestData, string resultFileName = null,
		string httpMethod = ""){
		string normalizedMethod = string.IsNullOrWhiteSpace(httpMethod)
			? "POST"
			: httpMethod.ToUpperInvariant();

		string jsonResult = normalizedMethod switch {
					"POST" => ApplicationClient.ExecutePostRequest(url, requestData),
					"GET" => ApplicationClient.ExecuteGetRequest(url),
					"DELETE" => ApplicationClient.ExecuteDeleteRequest(url, requestData),
					var _ => throw new ArgumentException($"Unsupported HTTP method '{httpMethod}'", nameof(httpMethod))
				};

		if (IsErrorResponse(jsonResult)) {
			WriteServiceError(jsonResult);
			return null;
		}

		string beautifiedJson = BeautifyJsonIfPossible(jsonResult);
		
		if (string.IsNullOrWhiteSpace(resultFileName)) {
			// Print to console if no destination file specified
			if (!IsSilent) { 
				Logger.WriteLine(beautifiedJson); 
			}
		}
		else {
			// Write to file if destination specified
			_fileSystem.WriteAllTextToFile(resultFileName, beautifiedJson);
			if (!IsSilent) {
				Logger.WriteInfo($"Result saved to {resultFileName}");
			}
		}

		return jsonResult;
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
		if (string.IsNullOrWhiteSpace(options.RequestFileName) && string.IsNullOrWhiteSpace(options.RequestBody)) {
			if (ExecuteServiceRequest(BuildUrl(options), string.Empty, options.ResultFileName, options.HttpMethodName) is null) {
				return 1;
			}
		}
		else {
			string requestData = string.IsNullOrWhiteSpace(options.RequestBody) ? GetRequestData(options.RequestFileName) : options.RequestBody;
			if (options.Variables != null && options.Variables.Any()) {
				requestData = ReplaceVariablesInJson(requestData, options.Variables);
			}
			if (ExecuteServiceRequest(BuildUrl(options), requestData, options.ResultFileName, options.HttpMethodName) is null) {
				return 1;
			}
		}
		return 0;
	}

	#endregion

}
