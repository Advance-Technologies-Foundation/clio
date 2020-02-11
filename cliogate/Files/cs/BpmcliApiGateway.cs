using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using ClioGate.Functions.SQL;
using Newtonsoft.Json;
using Terrasoft.Core.Entities;
using Terrasoft.Web.Common;

namespace cliogate.Files.cs
{
	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public class CreatioApiGateway : BaseService
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
			var version = typeof(CreatioApiGateway).Assembly.GetName().Version;
			return version.ToString();
		}

		[OperationContract]
		[WebGet(UriTemplate = "GetEntitySchemaModels/{entitySchema}", ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public string GetEntitySchemaModels(string entitySchema) {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				var generator = new EntitySchemaModelClassGenerator(UserConnection.EntitySchemaManager);
				var models = generator.Generate(entitySchema);
				return JsonConvert.SerializeObject(models, Formatting.Indented);
			} else {
				throw new Exception("You don`n have permission for operation CanManageSolution");
			}
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "GetPackages", BodyStyle = WebMessageBodyStyle.WrappedRequest,
		RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public string GetPackages() {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				var packageList = new List<Dictionary<string, string>>();
				var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "SysPackage");
				esq.AddAllSchemaColumns();
				var packages = esq.GetEntityCollection(UserConnection);
				foreach (var p in packages) {
					var package = new Dictionary<string, string> {
						["Name"] = p.PrimaryDisplayColumnValue,
						["UId"] = p.GetTypedColumnValue<string>("UId"),
						["Maintainer"] = p.GetTypedColumnValue<string>("Maintainer")
					};
					packageList.Add(package);
				}
				var json = JsonConvert.SerializeObject(packageList);
				return json;
			} else {
				throw new Exception("You don`n have permission for operation CanManageSolution");
			}
		}
	}
}


