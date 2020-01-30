using System;

namespace Clio.Common
{
	public class SqlScriptExecutor : ISqlScriptExecutor
	{
		private static string ExecuteSqlScriptUrl => @"/rest/CreatioApiGateway/ExecuteSqlScript";

		private string CorrectJson(string body) {
			body = body.Replace("\\\\r\\\\n", Environment.NewLine);
			body = body.Replace("\\\\n", Environment.NewLine);
			body = body.Replace("\\r\\n", Environment.NewLine);
			body = body.Replace("\\n", Environment.NewLine);
			body = body.Replace("\\\\t", Convert.ToChar(9).ToString());
			body = body.Replace("\\\"", "\"");
			body = body.Replace("\\\\", "\\");
			body = body.Trim(new char[] { '\"' });
			return body;
		}

		public string Execute(string sql, IApplicationClient applicationClient, EnvironmentSettings settings) {
			var scriptData = "{ \"script\":\"" + sql + "\"}";
			string responseFormServer = applicationClient.ExecutePostRequest(
				settings.IsNetCore ? settings.Uri + ExecuteSqlScriptUrl : settings.Uri + "/0" + ExecuteSqlScriptUrl, scriptData);
			return CorrectJson(responseFormServer);
		}
	}
}
