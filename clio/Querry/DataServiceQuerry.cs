using Clio.Command;
using Clio.Common;
using CommandLine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Clio.Querry
{
	[Verb("dataservice", Aliases = new[] { "ds" }, HelpText = "DataService Request")]
	public class DataServiceQuerryOptions : EnvironmentNameOptions
	{
		[Option('t', "type", Required = true, HelpText = "Operation type", Separator = ' ')]
		public string OperationType { get; set; }
		
		[Option('f', "input", Required = true, HelpText = "Request file", Separator = ' ')]
		public string ReqeustFileName { get; set; }
		
		[Option('d', "destination", Required = true, HelpText = "Result file", Separator = ' ')]
		public string ResultFileName { get; set; }
		
		[Option('v', "variables", Required = false, HelpText = "Result file", Separator = ';')]
		public IEnumerable<string> Variables { get; set; }
	}

	public class DataServiceQuerry : RemoteCommand<DataServiceQuerryOptions>
	{
		public DataServiceQuerry(IApplicationClient applicationClient, EnvironmentSettings settings)
			: base(applicationClient, settings){}

		const string _selectServicePath = @"/DataService/json/SyncReply/SelectQuery";
		const string _updateServicePath = @"/DataService/json/SyncReply/UpdateQuery";
		const string _insertServicePath = @"/DataService/json/SyncReply/InsertQuery";
		const string _deleteServicePath = @"/DataService/json/SyncReply/DeleteQuery";
		
		public override int Execute(DataServiceQuerryOptions options)
		{
			string requestData = GetRequestData(options);
			if (options.Variables is object && options.Variables.Any()){
				requestData = ReplaceVarInJson(requestData, options.Variables);
			}

			SetServicePath(options.OperationType);
			string jsonResult = ApplicationClient.ExecutePostRequest(ServiceUri, requestData);
			string outputFileName = SetOutputFileName(options.ResultFileName);
			File.WriteAllText(outputFileName, jsonResult);
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

		private void SetServicePath(string operationType)
		{
			ServicePath = operationType.ToLower() switch
			{
				"select" => _selectServicePath,
				"insert" => _insertServicePath,
				"update" => _updateServicePath,
				"delete" => _deleteServicePath,
				_ => _selectServicePath,
			};
		}

		private string SetOutputFileName(string resultFileName)
		{
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