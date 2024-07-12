using Clio.Common;
using CommandLine;
using DocumentFormat.OpenXml.Math;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Clio.Command
{
	[Verb("mock-dataFolderPath", Aliases = new string[] { "dataFolderPath-mock" }, HelpText = "Build package command")]
	public class MockDataCommandOptions : RemoteCommandOptions
	{
		public string Models { get; internal set; }
		public string Data { get; internal set; }
	}

	internal class MockDataCommand : RemoteCommand<MockDataCommandOptions>
	{
		private readonly IFileSystem _fileSystem;

		public MockDataCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings, FileSystem fileSystem) : base(applicationClient, environmentSettings) {
			this._fileSystem = fileSystem;
			this.EnvironmentSettings = environmentSettings;
			
		}

		public MockDataCommand(IFileSystem fileSystem, IApplicationClient applicationClient) {
			this._fileSystem = fileSystem;
			this.ApplicationClient = applicationClient;
		}

		public MockDataCommand(FileSystem clioFileSystem) {
			this._fileSystem = clioFileSystem;
		}

		public override int Execute(MockDataCommandOptions options) {
			
			try {
				LoadODataData(options.Models, options.Data);
				string commandName = typeof(MockDataCommandOptions).GetCustomAttribute<VerbAttribute>()?.Name;
				Logger.WriteInfo($"Done {commandName}");
				return 0;
			} catch (SilentException ex) {
				return 1;
			} catch (Exception e) {
				Logger.WriteError(e.Message);
				return 1;
			}
		}

		private void LoadODataData(string models, string dataFolderPath) {
			var findedModels = FindModels(models);
			foreach (var findedModel in findedModels) {
				Logger.WriteInfo($"Recieved data for  {findedModel} ...");
				var modelODataDataFilePath = Path.Combine(dataFolderPath,$"{findedModel}.json");
				var modelOdataData = GetModelDataData(findedModel);
				_fileSystem.WriteAllTextToFile(modelODataDataFilePath, modelOdataData);
			}
		}

		internal List<string> FindModels(string models) {
			var schemaNames = new List<string>();
			var files = _fileSystem.GetFiles(models, "*.*", SearchOption.AllDirectories).ToList();
			foreach (var file in files) {
				var fileContent = _fileSystem.ReadAllText(file);
				schemaNames.AddRange(ExtractSchemaNames(fileContent));
			}
			var result = schemaNames.Distinct().ToList();
			Logger.WriteInfo($"Found {result} models");
			return result;
		}

		private string GetModelDataData(string findedModel) {
			string ODataModelUrl = $"{RootPath}/odata/{findedModel}";
			return ApplicationClient.ExecuteGetRequest(ODataModelUrl, RequestTimeout, RetryCount, DelaySec);	
		}

		public static List<string> ExtractSchemaNames(string sourceCode) {
			List<string> schemaNames = new List<string>();
			string pattern = @"\[Schema\(""([^""]+)""\)\]";
			MatchCollection matches = Regex.Matches(sourceCode, pattern);

			foreach (Match match in matches) {
				if (match.Groups.Count > 1) {
					schemaNames.Add(match.Groups[1].Value);
				}
			}

			return schemaNames;
		}
	}
}
