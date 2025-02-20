using Clio.Command;
using Clio.Common;
using CommandLine;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clio.Querry
{
	[Verb("dataservice", Aliases = new[] { "ds" }, HelpText = "DataService Request")]
	public class DataServiceQuerryOptions : RemoteCommandOptions
	{
		[Option('t', "type", Required = true, HelpText = "Operation type", Separator = ' ')]
		public string OperationType { get; set; }
		
		[Option('f', "input", Required = true, HelpText = "Request file", Separator = ' ')]
		public string ReqeustFileName { get; set; }
		
		[Option('d', "destination", Required = false, HelpText = "Result file", Separator = ' ')]
		public string ResultFileName { get; set; }
		
		[Option('v', "variables", Required = false, HelpText = "Result file", Separator = ';')]
		public IEnumerable<string> Variables { get; set; }
	}

	public class DataServiceQuery : RemoteCommand<DataServiceQuerryOptions>
	{

		private readonly IServiceUrlBuilder _serviceUrlBuilder;

		public DataServiceQuery(IApplicationClient applicationClient, EnvironmentSettings settings, IServiceUrlBuilder serviceUrlBuilder)
			: base(applicationClient, settings){
			_serviceUrlBuilder = serviceUrlBuilder;
		}
		
		public override int Execute(DataServiceQuerryOptions options){
			string requestData = GetRequestData(options);
			if (options.Variables != null && options.Variables.Any()){
				requestData = ReplaceVarInJson(requestData, options.Variables);
			}
			
			string jsonResult = ApplicationClient.ExecutePostRequest(BuildUrl(options.OperationType), requestData);
			
			if(string.IsNullOrWhiteSpace(options.ResultFileName)) {
				Logger.WriteInfo(jsonResult);
			}else {
				string outputFileName = SetOutputFileName(options.ResultFileName);
				File.WriteAllText(outputFileName, jsonResult);
			}
			return 0;
		}

		protected override string GetRequestData(DataServiceQuerryOptions options)
		{
			FileInfo fi = new FileInfo(options.ReqeustFileName);
			if(!fi.Exists){
				throw new FileNotFoundException("File not found", options.ReqeustFileName);
			}
			return File.ReadAllText(options.ReqeustFileName);
		}
		
		private string BuildUrl(string operationType) => operationType.ToUpperInvariant() switch {
															"SELECT" => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Select),
															"INSERT" => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Insert),
															"UPDATE" => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Update),
															"DELETE" => _serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.Delete),
															var _ => throw new System.Exception("Unknown operation type"),
														};

		private string SetOutputFileName(string resultFileName){
			FileInfo fi = new FileInfo(resultFileName);
			string fileName = Path.GetFileNameWithoutExtension(resultFileName);
			int count = fi.Directory.GetFiles($"{fileName}*{fi.Extension}").Count();
			if(count > 0){
				return Path.Combine(fi.Directory.ToString(), $"{fileName}-({count + 1}){fi.Extension}");
			}
			return Path.Combine(fi.Directory.ToString(), $"{fileName}{fi.Extension}");
		}
		private string ReplaceVarInJson(string json, IEnumerable<string> variables)
		{
			foreach(var variable in variables)
			{
				var pattern = "{{" + variable.Split('=')[0] + "}}";
				var regex = new Regex(pattern);
				var match = regex.Match(json);
				if ( match.Success){
					json = regex.Replace(json, variable.Split('=')[1]);
				}
			}
			return json;
		}
	}
}
