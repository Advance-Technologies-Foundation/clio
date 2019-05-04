namespace BpmcliGate.Functions.SQL
{
	using Newtonsoft.Json;
	using System;
	using System.Data;
	using Terrasoft.Core;
	using Terrasoft.Core.DB;
	public static class SQLFunctions
	{
		public static string ExecuteSQL(string script, UserConnection userConnection) {
			script = script.Replace("|nl|", Environment.NewLine);
			var query = new CustomQuery(userConnection, script);
			var records = query.ExecuteReader(userConnection.EnsureDBConnection());
			var dataTable = new DataTable {
				TableName = "bpmcliTable"
			};
			dataTable.Load(records);
			return JsonConvert.SerializeObject(dataTable, Formatting.Indented);
		}
	}


}