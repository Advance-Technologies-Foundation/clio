using System;
using System.Collections.Generic;
using System.Linq;
using CommandLine;
using Creatio.Client;
using Newtonsoft.Json;

namespace Clio.Command
{
	[Verb("get-pkg-list", Aliases = new[] { "packages" }, HelpText = "Get environments packages")]
	public class PkgListOptions : EnvironmentNameOptions
	{

		[Option('f', "Filter", Required = false, HelpText = "Contains name filter",
		Default = null)]
		public string SearchPattern { get; set; } = string.Empty;

	}

	public class GetPkgListCommand : BaseRemoteCommand
	{
		private static string ServiceUrl => AppUrl + @"/rest/CreatioApiGateway/GetPackages";

		public static int GetPkgList(PkgListOptions options) {
			Configure(options);
			var scriptData = "{}";
			string responseFormServer = CreatioClient.ExecutePostRequest(ServiceUrl, scriptData);
			var json = CorrectJson(responseFormServer);
			var packages = JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(json);
			var selectedPackages = packages.Where(p => p["Name"].ToLower().Contains(options.SearchPattern.ToLower())).OrderBy(p => p["Name"]);
			if (selectedPackages.Count() > 0) {
				var row = GetFormatedString("Name", "Maintainer");
				Console.WriteLine();
				Console.WriteLine(row);
				Console.WriteLine();
			}
			foreach (var p in selectedPackages) {
				var row = GetFormatedString(p["Name"], p["Maintainer"]);
				Console.WriteLine(row);
			}
			Console.WriteLine();
			Console.WriteLine($"Find {selectedPackages.Count()} packages in {Settings.Uri}");
			return 0;
		}

		private static string GetFormatedString(params string[] args) {
			int columnSize = 30;
			string result = "  ";
			for (int i = 0; i < args.Length; i++) {
				int tabSize = columnSize * i - result.Length;
				for (int j = 0; j < tabSize; j++) {
					result += " ";
				}
				result += args[i];
			}
			return result;
		}

		private static string CorrectJson(string body) {
			body = body.Replace("\\\\r\\\\n", Environment.NewLine);
			body = body.Replace("\\\\n", Environment.NewLine);
			body = body.Replace("\\r\\n", Environment.NewLine);
			body = body.Replace("\\n", Environment.NewLine);
			body = body.Replace("\\\\t", Convert.ToChar(9).ToString());
			body = body.Replace("\\\"", "\"");
			body = body.Replace("\\\\", "\\");
			body = body.Trim(new Char[] { '\"' });
			return body;
		}

	}
}