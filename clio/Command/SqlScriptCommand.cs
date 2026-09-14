using System;
using System.Data;
using System.IO;
using Clio.Common;
using CommandLine;
using ConsoleTables;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Clio.Command.SqlScriptCommand
{
	[Verb("execute-sql-script", Aliases = ["sql"], HelpText = "Execute script on web application")]
	[RequiresPackage("cliogate", "2.0.0.41", Hint = "Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) to install/update cliogate.")]
	public class ExecuteSqlScriptOptions : RemoteCommandOptions
	{
		[Value(0, MetaName = "Script", Required = false, HelpText = "Sql script")]
		public string Script { get; set; }

		[Option('f', "File", Required = false,
			HelpText = "Path to the sql script file.", Default = null)]
		public string File { get; set; }

		[Option('v', "View", Required = false, HelpText = "View type.", Default = "table")]
		public string ViewType { get; set; }

		[Option('d', "destination-path", Required = false, HelpText = "Path to results file.", Default = null)]
		public string DestPath { get; set; }

		[Option("DestinationPath", Required = false, Hidden = true, HelpText = "Alias for --destination-path")]
		public string DestPathAlias {
			get => DestPath;
			set { if (!string.IsNullOrEmpty(value)) DestPath = value; }
		}

		[Option("silent", Required = false, HelpText = "Use default behavior without user interaction")]
		public bool IsSilent
		{
			get; set;
		}
	}

	public class SqlScriptCommand : RemoteCommand<ExecuteSqlScriptOptions>
	{
		private const string CsvDestinationRequired =
			"-d/--destination-path is required when -v csv is used.";
		private readonly ISqlScriptExecutor _sqlScriptExecutor;
		private readonly ILogger _logger;

		public SqlScriptCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
				ISqlScriptExecutor sqlScriptExecutor, ILogger logger)
			: base(applicationClient, settings) {
			_sqlScriptExecutor = sqlScriptExecutor;
			_logger = logger;
		}

		private static string GetSqlScriptResult(string serverResponse, string viewType, string filePath) {
			
			viewType = viewType.ToLowerInvariant();
			if (viewType == "json") {
				return GetJsonResult(serverResponse, filePath);
			}
			if (int.TryParse(serverResponse, out var count)) {
				if (filePath != null) {
					throw new InvalidOperationException("Use -v json to export an affected-row count.");
				}
				return $"({count} rows affected)";
			}
			var dataTable = JsonConvert.DeserializeObject<DataTable>(serverResponse);
			string formatResult = dataTable.Rows.Count == 0 ? string.Empty : CreateConsoleTable(dataTable).ToString();
			if (viewType == "table") {
				if (filePath != null) {
					File.WriteAllText(filePath, formatResult);
				}
			} else if (viewType.ToLower() == "csv") {
				SaveDataTableToCsv(dataTable, filePath);
			} else if (viewType.ToLower() == "xlsx") {
				SaveDataTableToXlsx(dataTable, filePath);
			} else {
				if (filePath != null) {
					File.WriteAllText(filePath, serverResponse);
				}
				formatResult = serverResponse;
			}
			return formatResult;
		}

		private static string GetJsonResult(string serverResponse, string filePath) {
			JToken jsonResult = JToken.Parse(serverResponse);
			if (jsonResult.Type is not (JTokenType.Array or JTokenType.Integer)) {
				throw new InvalidOperationException("SQL response must contain a JSON row array or an affected-row count.");
			}
			if (filePath != null) {
				File.WriteAllText(filePath, serverResponse);
			}
			return serverResponse;
		}

		private static bool TryGetError(string json, out string errorMessage) {
			if (json.StartsWith("ExecuteSQL ERROR: ")) {
				errorMessage = json["ExecuteSQL ERROR: ".Length..];
				return true;
			}
			errorMessage = string.Empty;
			return false;
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

		static void SaveDataTableToCsv(DataTable dataTable, string filePath, string delimiter = ";") {
			using (StreamWriter sw = new StreamWriter(filePath)) {
				for (int i = 0; i < dataTable.Columns.Count; i++) {
					sw.Write(dataTable.Columns[i]);
					if (i < dataTable.Columns.Count - 1) {
						sw.Write(delimiter);
					}
				}
				sw.WriteLine();
				foreach (DataRow row in dataTable.Rows) {
					for (int i = 0; i < dataTable.Columns.Count; i++) {
						sw.Write(row[i]);
						if (i < dataTable.Columns.Count - 1) {
							sw.Write(delimiter);
						}
					}
					sw.WriteLine();
				}
			}
		}

		static void SaveDataTableToXlsx(DataTable dataTable, string filePath) {
			using SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(filePath,
				SpreadsheetDocumentType.Workbook);
			WorkbookPart workbookPart = spreadsheetDocument.AddWorkbookPart();
			workbookPart.Workbook = new Workbook();
			WorksheetPart worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
			worksheetPart.Worksheet = new Worksheet(new SheetData());
			Sheets sheets = spreadsheetDocument.WorkbookPart.Workbook.AppendChild(new Sheets());
			string sheetName = "Sheet1";
			uint sheetId = 1;
			Sheet sheet = new Sheet() { Id = spreadsheetDocument.WorkbookPart.GetIdOfPart(worksheetPart),
				SheetId = sheetId, Name = sheetName };
			sheets.Append(sheet);
			SheetData sheetData = worksheetPart.Worksheet.GetFirstChild<SheetData>();
			Row headerRow = new Row();
			foreach (DataColumn column in dataTable.Columns) {
				Cell cell = new Cell();
				cell.DataType = CellValues.String;
				cell.CellValue = new CellValue(column.ColumnName);
				headerRow.AppendChild(cell);
			}
			sheetData.AppendChild(headerRow);
			foreach (DataRow row in dataTable.Rows) {
				Row dataRow = new Row();
				foreach (var item in row.ItemArray) {
					Cell cell = new Cell();
					cell.DataType = CellValues.String;
					cell.CellValue = new CellValue(item.ToString());
					dataRow.AppendChild(cell);
				}
				sheetData.AppendChild(dataRow);
			}
			workbookPart.Workbook.Save();
			spreadsheetDocument.Close();
		}

		/// <summary>
		/// Executes SQL and renders the result after validating the CSV destination.
		/// </summary>
		/// <param name="opts">SQL input, output format, and destination options.</param>
		/// <returns>Zero on success; one for a missing CSV destination, SQL error, or export failure.</returns>
		public override int Execute(ExecuteSqlScriptOptions opts) {
			if (string.Equals(opts.ViewType, "csv", StringComparison.OrdinalIgnoreCase)
				&& string.IsNullOrWhiteSpace(opts.DestPath)) {
				_logger.WriteError(CsvDestinationRequired);
				return 1;
			}
			try {
				string result = string.Empty;
				if (!string.IsNullOrEmpty(opts.Script)) {
					result = _sqlScriptExecutor.Execute(opts.Script, ApplicationClient, EnvironmentSettings);
				} else if (!string.IsNullOrEmpty(opts.File)) {
					var script = File.ReadAllText(opts.File);
					if (!opts.IsSilent) {
						_logger.WriteLine(script);
					}
					script = script.Replace(Environment.NewLine, "|nl|");
					result = _sqlScriptExecutor.Execute(script, ApplicationClient, EnvironmentSettings);
				} else {
					_logger.WriteLine("Enter sql (Ctrl+C for exit): ");
					var sc = Console.ReadLine();
					result = _sqlScriptExecutor.Execute(sc, ApplicationClient, EnvironmentSettings);
				}
				if (TryGetError(result, out string errorMessage)) {
					_logger.WriteError(errorMessage);
					return 1;
				}
				result = GetSqlScriptResult(result, opts.ViewType, opts.DestPath);
				if (opts.DestPath != null) {
					_logger.WriteInfo($"Results saved to: {Path.GetFullPath(opts.DestPath)}");
				}
#pragma warning disable CLIO002
				Console.OutputEncoding = System.Text.Encoding.UTF8;
#pragma warning restore CLIO002
				if (!opts.IsSilent) {
					_logger.WriteLine(result);
				}
				_logger.WriteInfo("Done");
			} catch (Exception e) {
				_logger.WriteError(e.Message);
				return 1;
			}
			return 0;
		}

	}
}
