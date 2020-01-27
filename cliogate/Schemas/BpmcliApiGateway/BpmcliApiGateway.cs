using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using ClioGate.Functions.SQL;
using Newtonsoft.Json;
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
			return "1.1.1.2";
		}

		[OperationContract]
		[WebGet(UriTemplate = "GetEntitySchemaModels/{entitySchema}", ResponseFormat = WebMessageFormat.Json, BodyStyle =WebMessageBodyStyle.Bare)]
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
		public List<Dictionary<string, string>> GetPackages() {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				var packages = new List<Dictionary<string, string>>();
				var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "SysPackage");
				esq.AddColumn("Name").OrderByAsc();
				esq.AddColumn("UId");
				esq.AddColumn("Maintainer");
				var packages = esq.GetEntityCollection(userConnection);
				foreach (var p in packages) {
					var packageName = p.PrimaryDisplayColumnValue;
					var package = new Dictionary<string, string>();
					package["Name"] = p.PrimaryDisplayColumnValue;
					package["UId"] = p.GetTypedColumnValue<string>("UId");
					package["Maintainer"] = p.GetTypedColumnValue<string>("Maintainer");
					packages.Add(package);
				}
				return packages;
			} else {
				throw new Exception("You don`n have permission for operation CanManageSolution");
			}
		}
	}
}
