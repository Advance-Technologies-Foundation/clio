using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Clio.Command
{
	[Verb("mock-data", Aliases = new string[] { "data-mock" }, HelpText = "Build package command")]
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
		}

		public MockDataCommand(IFileSystem fileSystem) {
			this._fileSystem = fileSystem;
		}

		public override int Execute(MockDataCommandOptions options) {
			return base.Execute(options);
		}

		internal List<string> FindModels(string models) {
			List<string> schemaNames = new List<string>();
			var files = _fileSystem.GetFiles(models, "*.*", System.IO.SearchOption.AllDirectories).ToList();
			foreach (var file in files) {
				var fileContent = _fileSystem.ReadAllText(file);
				schemaNames.AddRange(ExtractSchemaNames(fileContent));
			}
			return schemaNames.Distinct().ToList();
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
