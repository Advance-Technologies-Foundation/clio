using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using ClioGate.Functions.SQL;
using Newtonsoft.Json;
using Terrasoft.Core.ConfigurationBuild;
using Terrasoft.Core.DB;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Factories;
using Terrasoft.Core.Packages;
using Terrasoft.Web.Common;

namespace cliogate.Files.cs
{
	[DataContract]
	public class CompilationResult
	{
		[JsonProperty("compilerErrors")]
		public CompilerErrorCollection CompilerErrors { get; set; }

		[JsonProperty("status")]
		public BuildResultType Status { get; set; }
	}

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
				throw new Exception("You don't have permission for operation CanManageSolution");
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
		[WebGet(UriTemplate = "GetEntitySchemaModels/{entitySchema}/{fields}", ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public string GetEntitySchemaModels(string entitySchema, string fields) {
if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				var generator = new EntitySchemaModelClassGenerator(UserConnection.EntitySchemaManager);
				var columns = new List<string>();
				if (!String.IsNullOrEmpty(fields)) {
					columns = new List<string>(fields.Split(','));
				}
				var models = generator.Generate(entitySchema, columns);
				return JsonConvert.SerializeObject(models, Formatting.Indented);
			} else {
				throw new Exception("You don't have permission for operation CanManageSolution");
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
						["Maintainer"] = p.GetTypedColumnValue<string>("Maintainer"),
						["Version"] = p.GetTypedColumnValue<string>("Version")
					};
					packageList.Add(package);
				}
				var json = JsonConvert.SerializeObject(packageList);
				return json;
			} else {
				throw new Exception("You don'nt have permission for operation CanManageSolution");
			}
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "ResetSchemaChangeState",
			BodyStyle = WebMessageBodyStyle.WrappedRequest, RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json)]
		public bool ResetSchemaChangeState(string packageName) {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				Update update = new Update(UserConnection, "SysSchema")
					.Set("IsChanged", Column.Parameter(false, "Boolean"))
					.Where("SysPackageId").In(new Select(UserConnection).Column("Id").From("SysPackage")
				.Where("SysPackage", "Name").IsEqual(Column.Parameter(packageName))) as Update;
				update.Execute();
				return true;
			}
			else {
				throw new Exception("You don'nt have permission for operation CanManageSolution");
			}
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "CompileWorkspace", BodyStyle = WebMessageBodyStyle.WrappedRequest,
		RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public CompilationResult CompileWorkspace(bool compileModified = false) {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				WorkspaceBuilder workspaceBuilder = WorkspaceBuilderUtility.CreateWorkspaceBuilder(AppConnection);
				CompilerErrorCollection compilerErrors = workspaceBuilder.Rebuild(AppConnection.Workspace,
					out var buildResultType);
				var configurationBuilder = ClassFactory.Get<IAppConfigurationBuilder>();
				if (compileModified) {
					configurationBuilder.BuildChanged();
				} else {
					configurationBuilder.BuildAll();
				}
				return new CompilationResult {
					Status = buildResultType,
					CompilerErrors = compilerErrors
				};
			} else {
				throw new Exception("You don't have permission for operation CanManageSolution");
			}
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "UpdateDBStructure", BodyStyle = WebMessageBodyStyle.WrappedRequest,
		RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public bool UpdateDBStructure() {
			if (UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				var invalidSchemas = GetEntitySchemasWithNeedUpdateStructure();
				return CreateInstallUtilities().SaveSchemaDBStructure(invalidSchemas, true);
			} else {
				throw new Exception("You don't have permission for operation CanManageSolution");
			}
		}

		private IEnumerable<Guid> GetEntitySchemasWithNeedUpdateStructure() {
			var result = new List<Guid>();
			var esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "VwSysEntitySchemaInWorkspace");
			esq.AddAllSchemaColumns();
			esq.Filters.LogicalOperation = Terrasoft.Common.LogicalOperationStrict.And;
			var needUpdateFilter = esq.CreateFilterWithParameters(FilterComparisonType.Equal, "NeedUpdateStructure", true);
			esq.Filters.Add(needUpdateFilter);
			var packages = esq.GetEntityCollection(UserConnection);
			foreach (var p in packages) {
				var schemaUId = p.GetTypedColumnValue<Guid>("UId");
				result.Add(schemaUId);
			}
			return result;
		}

		private PackageInstallUtilities CreateInstallUtilities() {
			return new PackageInstallUtilities(UserConnection);
		}
	}
}