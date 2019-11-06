namespace ClioGate.Functions.SQL
{
	using System;
	using System.Data;
	using Newtonsoft.Json;
	using Terrasoft.Core;
	using Terrasoft.Core.DB;

	public static class SQLFunctions
	{
		private static bool GetIsChangeStateScript(string script) {
			script = script.ToLower();
			var result = script.StartsWith("update") || script.StartsWith("insert") ||
				script.StartsWith("delete");
			return result;
		}

		public static string ExecuteSQL(string script, UserConnection userConnection) {
			script = script.Replace("|nl|", Environment.NewLine);
			var query = new CustomQuery(userConnection, script);
			var isChangeStateScript = GetIsChangeStateScript(script);
			if (isChangeStateScript) {
				var count = query.Execute();
				return count.ToString();
			}
			var records = query.ExecuteReader(userConnection.EnsureDBConnection());
			var dataTable = new DataTable {
				TableName = "clioTable"
			};
			dataTable.Load(records);
			return JsonConvert.SerializeObject(dataTable, Formatting.Indented);
		}
	}
}