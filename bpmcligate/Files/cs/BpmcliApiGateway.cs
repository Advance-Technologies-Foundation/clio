using BpmcliGate.Functions.SQL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using Terrasoft.Web.Common;

namespace bpmcligate.Files.cs
{
	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	class BpmcliApiGateway : BaseService
	{
		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "ExecuteSqlScript", BodyStyle = WebMessageBodyStyle.WrappedRequest,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public string ExecuteSqlScript(string script) {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				return SQLFunctions.ExecuteSQL(script, UserConnection);
			} else {
				throw new Exception("You don`n have permission for operation CanManageSolution");
			}
		}

		[OperationContract]
		[WebInvoke(Method = "GET", UriTemplate = "GetApiVersion", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.WrappedRequest)]
		public string GetApiVersion() {
			var version = typeof(BpmcliApiGateway).Assembly.GetName().Version;
			return version.ToString();
		}

		[OperationContract]
		[WebInvoke(Method = "GET", UriTemplate = "GetEntitySchemaModels", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.WrappedRequest)]
		public Dictionary<string, string> GetEntitySchemaModels(string entitySchema) {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				var generator = new EntitySchemaModelClassGenerator(UserConnection.EntitySchemaManager);
				return generator.Generate(entitySchema);
			} else {
				throw new Exception("You don`n have permission for operation CanManageSolution");
			}
		}
	}
}


