
using System;
using System.Data;
using Newtonsoft.Json;
using Terrasoft.Core;
using Terrasoft.Core.DB;

namespace ClioGate.Functions.SQL;
public static class SQLFunctions
{
    private static bool GetIsChangeStateScript(string script)
    {
        bool result = script.StartsWith("update") || script.StartsWith("insert") ||
                      script.StartsWith("delete");
        return result;
    }

    public static string ExecuteSQL(string script, UserConnection userConnection)
    {
        script = script.Replace("|nl|", Environment.NewLine);
        CustomQuery query = new(userConnection, script);
        bool isChangeStateScript = GetIsChangeStateScript(script);
        if (isChangeStateScript)
        {
            int count = query.Execute();
            return count.ToString();
        }

        IDataReader? records = query.ExecuteReader(userConnection.EnsureDBConnection());
        DataTable dataTable = new() { TableName = "clioTable" };
        dataTable.Load(records);
        return JsonConvert.SerializeObject(dataTable, Formatting.Indented);
    }
}
