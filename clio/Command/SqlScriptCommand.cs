using System;
using System.Data;
using System.IO;
using Clio.Common;
using CommandLine;
using ConsoleTables;
using Newtonsoft.Json;

namespace Clio.Command.SqlScriptCommand
{
	[Verb("execute-sql-script", Aliases = new string[] { "sql" }, HelpText = "Execute script on web application")]
	public class ExecuteSqlScriptOptions : EnvironmentOptions
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

	public class SqlScriptCommand : RemoteCommand<ExecuteSqlScriptOptions>
	{
		private readonly ISqlScriptExecutor _sqlScriptExecutor;

		public SqlScriptCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
				ISqlScriptExecutor sqlScriptExecutor)
			: base(applicationClient, settings) {
			_sqlScriptExecutor = sqlScriptExecutor;
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

		public override int Execute(ExecuteSqlScriptOptions opts) {
			try {
				string result = string.Empty;
				if (!string.IsNullOrEmpty(opts.Script)) {
					result = _sqlScriptExecutor.Execute(opts.Script, ApplicationClient, EnvironmentSettings);
				} else if (!string.IsNullOrEmpty(opts.File)) {
					var script = File.ReadAllText(opts.File);
					Console.WriteLine(script);
					script = script.Replace(Environment.NewLine, "|nl|");
					result = _sqlScriptExecutor.Execute(script, ApplicationClient, EnvironmentSettings);
				} else {
					Console.WriteLine("Enter sql (Ctrl+C for exit): ");
					var sc = Console.ReadLine();
					result = _sqlScriptExecutor.Execute(sc, ApplicationClient, EnvironmentSettings);
				}
				result = GetSqlScriptResult(result, opts.ViewType);
				Console.OutputEncoding = System.Text.Encoding.UTF8;

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

	}
}
