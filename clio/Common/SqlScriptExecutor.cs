using System;
using Newtonsoft.Json;

namespace Clio.Common
{
	/// <inheritdoc />
	public class SqlScriptExecutor : ISqlScriptExecutor
	{
		private static string ExecuteSqlScriptUrl => @"/rest/CreatioApiGateway/ExecuteSqlScript";

		/// <inheritdoc />
		public string Execute(string sql, IApplicationClient applicationClient, EnvironmentSettings settings) {
			var scriptData = new {
				script = sql
			};
			string serializedRequestPayload = JsonConvert.SerializeObject(scriptData);
			string endpointUri = settings.IsNetCore
				? settings.Uri + ExecuteSqlScriptUrl
				: settings.Uri + "/0" + ExecuteSqlScriptUrl;
			string responseFormServer = applicationClient.ExecutePostRequest(endpointUri,
				serializedRequestPayload);
			return responseFormServer.TrimStart().StartsWith("\"", StringComparison.Ordinal)
				? JsonConvert.DeserializeObject<string>(responseFormServer)
				: responseFormServer;
		}
	}
}
