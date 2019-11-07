using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Text;
using Creatio.Client;
using CommandLine;
using ConsoleTables;
using Newtonsoft.Json;

namespace clio.Command.SqlScriptCommand
{
	[Verb("execute-sql-script", Aliases = new string[] { "sql" }, HelpText = "Execute script on web application")]
	internal class ExecuteSqlScriptOptions : EnvironmentOptions
	{
		[Value(0, MetaName = "Script", Required = false, HelpText = "Sql script")]
		public string Script { get; set; }

		[Option('f', "File", Required = false,
			HelpText = "Path to the sql script file.", Default = null)]
		public string File { get; set; }

		[Option('v', "View", Required = false, HelpText = "View type.", Default = "table")]
		public string ViewType { get; set; }

		[Option('d', "DestinationPath", Required = false, HelpText = "Path to results file.", Default = null)]
		public string DestPath { get; set; }
	}

	class SqlScriptCommand: BaseRemoteCommand
	{
		private static string ExecuteSqlScriptUrl => _appUrl + @"/rest/CreatioApiGateway/ExecuteSqlScript";

		public static string ExecuteSqlScript(string script, CreatioClient client = null) {
			var _client = CreatioClient;
			if (client != null) {
				_client = client;
			}
			var scriptData = "{ \"script\":\"" + script + "\"}";
			string responseFormServer = _client.ExecutePostRequest(ExecuteSqlScriptUrl, scriptData);
			return CorrectJson(responseFormServer);
		}

		public static int ExecuteSqlScript(ExecuteSqlScriptOptions opts) {
			try {
				Configure(opts);
				string result = string.Empty;
				if (!string.IsNullOrEmpty(opts.Script)) {
					result = ExecuteSqlScript(opts.Script);
				} else if (!string.IsNullOrEmpty(opts.File)) {
					var script = File.ReadAllText(opts.File);
					Console.WriteLine(script);
					script = script.Replace(Environment.NewLine, "|nl|");
					result = ExecuteSqlScript(script);
				} else {
					Console.WriteLine("Enter sql (Ctrl+C for exit): ");
					var sc = Console.ReadLine();
					result = ExecuteSqlScript(sc);
				}
				result = GetSqlScriptResult(result, opts.ViewType);
				Console.WriteLine(result);
				if (opts.DestPath != null) {
					File.WriteAllText(opts.DestPath, result);
				}
				Console.WriteLine("Done");
			} catch (Exception e) {
				Console.WriteLine(e);
			}
			return 0;
		}

		private static string GetSqlScriptResult(string result, string viewType) {
			viewType = viewType.ToLower();
			if (viewType == "table") {
				if (result == "[]") {
					return string.Empty;
				}
				if (int.TryParse(result, out var count)) {
					return $"({count} rows affected)";
				}
				var dataTable = JsonConvert.DeserializeObject<DataTable>(result);
				var table = CreateConsoleTable(dataTable);
				return table.ToString();
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

		private static ConsoleTable CreateConsoleTable(DataTable dataTable) {
			var table = new ConsoleTable();
			foreach (var column in dataTable.Columns) {
				table.AddColumn(new[] { column.ToString() });
			}
			for (var i = 0; i < dataTable.Rows.Count; i++) {
				table.AddRow(dataTable.Rows[i].ItemArray);
			}
			return table;
		}
	}
}
