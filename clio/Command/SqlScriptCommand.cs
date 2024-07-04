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

namespace Clio.Command.SqlScriptCommand
{
	[Verb("execute-sql-script", Aliases = new string[] { "sql" }, HelpText = "Execute script on web application")]
	public class ExecuteSqlScriptOptions : RemoteCommandOptions
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

		[Option("silent", Required = false, HelpText = "Use default behavior without user interaction")]
		public bool IsSilent
		{
			get; set;
		}
	}

	public class SqlScriptCommand : RemoteCommand<ExecuteSqlScriptOptions>
	{
		private readonly ISqlScriptExecutor _sqlScriptExecutor;

		public SqlScriptCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
				ISqlScriptExecutor sqlScriptExecutor)
			: base(applicationClient, settings) {
			_sqlScriptExecutor = sqlScriptExecutor;
		}

		private static string GetSqlScriptResult(string serverResponse, string viewType, string filePath) {
			if (serverResponse == "[]") {
				return string.Empty;
			}
			if (int.TryParse(serverResponse, out var count)) {
				return $"({count} rows affected)";
			}
			viewType = viewType.ToLower();
			var dataTable = JsonConvert.DeserializeObject<DataTable>(serverResponse);
			var table = CreateConsoleTable(dataTable);
			string formatResult = table.ToString();
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
			using (SpreadsheetDocument spreadsheetDocument = SpreadsheetDocument.Create(filePath,
					SpreadsheetDocumentType.Workbook)) {
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
				result = GetSqlScriptResult(result, opts.ViewType, opts.DestPath);
				Console.OutputEncoding = System.Text.Encoding.UTF8;
				if (!opts.IsSilent) {
					Console.WriteLine(result);
				}
				Console.WriteLine("Done");
			} catch (Exception e) {
				Console.WriteLine(e);
			}
			return 0;
		}

	}
}
