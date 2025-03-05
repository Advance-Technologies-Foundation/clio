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

[Verb("call-service", Aliases = new[] {"cs"}, HelpText = "Call Service Request")]
public class CallServiceCommandOptions : RemoteCommandOptions {

	#region Properties: Public

	[Option('m', "method", Required = false, HelpText = "Result file", Separator = ';')]
	public string HttpMethodName { get; set; }

	[Option('f', "input", Required = false, HelpText = "Request file", Separator = ' ')]
	public string RequestFileName { get; set; }

	[Option('d', "destination", Required = false, HelpText = "Destination set")]
	public string ResultFileName { get; set; }

	[Option("service-path", Required = false, HelpText = "Route service path")]
	public string ServicePath { get; set; }

	[Option('v', "variables", Required = false, HelpText = "Result file", Separator = ';')]
	public IEnumerable<string> Variables { get; set; }

	#endregion

}

[Verb("dataservice", Aliases = new[] {"ds"}, HelpText = "DataService Request")]
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

	private readonly IFileSystem _fileSystem;

	#endregion

	#region Fields: Protected

	protected readonly IServiceUrlBuilder ServiceUrlBuilderInstance;

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

	#endregion

	#region Methods: Protected

	protected virtual string BuildUrl(T options) => ServiceUrlBuilderInstance.Build(options.ServicePath);

	protected string ExecuteServiceRequest(string url, string requestData, string resultFileName = null,
		string httpMethod = ""){
		string jsonResult = httpMethod switch {
								"POST" => ApplicationClient.ExecutePostRequest(url, requestData),
								"GET" => ApplicationClient.ExecuteGetRequest(url),
								var _ => ApplicationClient.ExecutePostRequest(url, requestData)
							};

		string beautifiedJson = BeautifyJsonIfPossible(jsonResult);
		if (string.IsNullOrWhiteSpace(resultFileName)) {
			Logger.WriteLine(beautifiedJson);
		}
		else {
			_fileSystem.WriteAllTextToFile(resultFileName, beautifiedJson);
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
		if (string.IsNullOrWhiteSpace(options.RequestFileName)) {
			ExecuteServiceRequest(BuildUrl(options), string.Empty, options.ResultFileName, options.HttpMethodName);
		}
		else {
			string requestData = GetRequestData(options.RequestFileName);
			if (options.Variables != null && options.Variables.Any()) {
				requestData = ReplaceVariablesInJson(requestData, options.Variables);
			}
			ExecuteServiceRequest(BuildUrl(options), requestData, options.ResultFileName);
		}
		return 0;
	}

	#endregion

}
