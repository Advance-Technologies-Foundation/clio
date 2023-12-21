﻿using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using ATF.Repository;
using ClioGate.Functions.SQL;
using Common.Logging;
using Newtonsoft.Json;
using Terrasoft.Common;
using Terrasoft.Core;
using Terrasoft.Core.Configuration;
using Terrasoft.Core.ConfigurationBuild;
using Terrasoft.Core.DB;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Factories;
using Terrasoft.Core.Packages;
using Terrasoft.Core.ServiceModelContract;
using Terrasoft.Web.Common;
using Terrasoft.Web.Http.Abstractions;
#if NETSTANDARD2_0
using System.Globalization;
using Terrasoft.Web.Http.Abstractions;
#endif

namespace cliogate.Files.cs
{
	#region Class: CompilationResult

	[DataContract]
	public class CompilationResult
	{

		#region Properties: Public

		[JsonProperty("compilerErrors")]
		public CompilerErrorCollection CompilerErrors { get; set; }

		[JsonProperty("status")]
		public BuildResultType Status { get; set; }

		#endregion

	}

	#endregion

	#region Class: CreatioApiGateway

	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public class CreatioApiGateway : BaseService
	{

		#region Fields: Private

		private readonly ILog _log = LogManager.GetLogger(typeof(CreatioApiGateway));
		private readonly string splitName = "#OriginalMaintainer:";

		#endregion

		#region Methods: Private

		private void AssignFileResponseContent(string contentType, long contentLength,
			string contentDisposition){
#if !NETSTANDARD2_0
			WebOperationContext.Current.OutgoingResponse.ContentType = contentType;
			WebOperationContext.Current.OutgoingResponse.ContentLength = contentLength;
			WebOperationContext.Current.OutgoingResponse.Headers.Add("Content-Disposition", contentDisposition);
#else
			HttpContext httpContext = HttpContextAccessor.GetInstance();
			HttpResponse response = httpContext.Response;
			response.ContentType = contentType;
			response.Headers["Content-Length"] = contentLength.ToString(CultureInfo.InvariantCulture);
			response.Headers["Content-Disposition"] = contentDisposition;
#endif
		}

		private void AssignFileResponseContent(long contentLength, string fileName) =>
			AssignFileResponseContent("application/octet-stream", contentLength,
				$"attachment; filename=\"{fileName}\"");

		private void CheckCanManageSolution(){
			if (!UserConnection.DBSecurityEngine.GetCanExecuteOperation("CanManageSolution")) {
				throw new Exception("You don't have permission for operation CanManageSolution");
			}
		}

		private PackageInstallUtilities CreateInstallUtilities(){
			return new PackageInstallUtilities(UserConnection);
		}

		private Stream GetCompressedFolder(string rootRelativePath, string fileName,
			IEnumerable<string> ignoreDirectoriesName = null){
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string rootDirectoryPath = Path.Combine(baseDirectory, rootRelativePath);
			if (!Directory.Exists(rootDirectoryPath)) {
				return null;
			}
			MemoryStream compressedAutogeneratedFolder
				= CompressionUtilities.GetCompressedFolder(rootDirectoryPath, ignoreDirectoriesName);
			AssignFileResponseContent(compressedAutogeneratedFolder?.Length ?? 0, fileName);
			return compressedAutogeneratedFolder;
		}

		private IEnumerable<Guid> GetEntitySchemasWithNeedUpdateStructure(){
			List<Guid> result = new List<Guid>();
			EntitySchemaQuery esq
				= new EntitySchemaQuery(UserConnection.EntitySchemaManager, "VwSysEntitySchemaInWorkspace");
			esq.AddAllSchemaColumns();
			esq.Filters.LogicalOperation = LogicalOperationStrict.And;
			IEntitySchemaQueryFilterItem needUpdateFilter
				= esq.CreateFilterWithParameters(FilterComparisonType.Equal, "NeedUpdateStructure", true);
			esq.Filters.Add(needUpdateFilter);
			EntityCollection packages = esq.GetEntityCollection(UserConnection);
			foreach (Entity p in packages) {
				Guid schemaUId = p.GetTypedColumnValue<Guid>("UId");
				result.Add(schemaUId);
			}
			return result;
		}

		private T GetPackageAttributeValue<T>(string key, string packageName){
			Select query = new Select(UserConnection)
				.From("SysPackage")
				.Column(key)
				.Where("Name")
				.IsEqual(Column.Parameter(packageName)) as Select;
			T result = query.ExecuteScalar<T>();
			return result;
		}

		#endregion

		#region Methods: Public

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "CompileWorkspace", BodyStyle = WebMessageBodyStyle.WrappedRequest,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public CompilationResult CompileWorkspace(bool compileModified = false){
			CheckCanManageSolution();
			WorkspaceBuilder workspaceBuilder = WorkspaceBuilderUtility.CreateWorkspaceBuilder(AppConnection);
			CompilerErrorCollection compilerErrors = workspaceBuilder.Rebuild(AppConnection.Workspace,
				out BuildResultType buildResultType);
			IAppConfigurationBuilder configurationBuilder = ClassFactory.Get<IAppConfigurationBuilder>();
			if (compileModified) {
				configurationBuilder.BuildChanged();
			} else {
				configurationBuilder.BuildAll();
			}
			return new CompilationResult {
				Status = buildResultType,
				CompilerErrors = compilerErrors
			};
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "ExecuteSqlScript", BodyStyle = WebMessageBodyStyle.WrappedRequest,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public string ExecuteSqlScript(string script){
			CheckCanManageSolution();
			return SQLFunctions.ExecuteSQL(script, UserConnection);
		}

		[OperationContract]
		[WebInvoke(Method = "GET", UriTemplate = "GetApiVersion", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.WrappedRequest)]
		public string GetApiVersion(){
			Version version = typeof(CreatioApiGateway).Assembly.GetName().Version;
			return version.ToString();
		}

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public Stream GetAutogeneratedFolder(string packageName){
			CheckCanManageSolution();
			string rootRelativePath = Path.Combine("Terrasoft.Configuration", "Pkg",
				packageName, "Autogenerated");
			return GetCompressedFolder(rootRelativePath, $"Autogenerated.{packageName}.gz");
		}

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public Stream GetConfigurationBinFolder(){
			CheckCanManageSolution();
			string rootRelativePath = Path.Combine("Terrasoft.Configuration", "bin");
			return GetCompressedFolder(rootRelativePath, "Bin.gz");
		}

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public Stream GetConfigurationLibFolder(){
			CheckCanManageSolution();
			string rootRelativePath = Path.Combine("Terrasoft.Configuration", "Lib");
			return GetCompressedFolder(rootRelativePath, "Lib.gz");
		}

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public Stream GetCoreBinFolder(){
			CheckCanManageSolution();
			string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
			string rootRelativePath = "bin";

			string[] ignoreDirectoriesName = null;
			if (!Directory.Exists(Path.Combine(baseDirectory, "bin"))) {
				ignoreDirectoriesName = new[] {
					"AppTemplates", "conf", "Packages", "Resources", "runtimes", "Terrasoft.Configuration", "WebFarm",
					"WorkspaceConsole"
				};
				rootRelativePath = string.Empty;
			}
			return GetCompressedFolder(rootRelativePath, "CoreBin.gz", ignoreDirectoriesName);
		}

		[OperationContract]
		[WebGet(UriTemplate = "GetEntitySchemaModels/{entitySchema}/{fields}", ResponseFormat = WebMessageFormat.Json,
			BodyStyle = WebMessageBodyStyle.Bare)]
		public string GetEntitySchemaModels(string entitySchema, string fields){
			CheckCanManageSolution();
			EntitySchemaModelClassGenerator generator
				= new EntitySchemaModelClassGenerator(UserConnection.EntitySchemaManager);
			List<string> columns = new List<string>();
			if (!string.IsNullOrEmpty(fields)) {
				columns = new List<string>(fields.Split(','));
			}
			Dictionary<string, string> models = generator.Generate(entitySchema, columns);
			return JsonConvert.SerializeObject(models, Formatting.Indented);
		}

		// /rest/CreatioApiGateway/GetPackageFileContent?packageName=CrtBase&filePath=descriptor.json
		[OperationContract]
		[WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public string GetPackageFileContent(string packageName, string filePath){
			CheckCanManageSolution();

			PackageExplorer packageExplorer = new PackageExplorer(packageName);
			return packageExplorer.GetPackageFileContent(filePath);
		}

		// /rest/CreatioApiGateway/GetPackageFilesDirectoryContent?packageName=CrtBase
		[OperationContract]
		[WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public IEnumerable<string> GetPackageFilesDirectoryContent(string packageName){
			CheckCanManageSolution();

			PackageExplorer packageExplorer = new PackageExplorer(packageName);
			return packageExplorer.GetPackageFilesDirectoryContent();
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "GetPackages", BodyStyle = WebMessageBodyStyle.WrappedRequest,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public string GetPackages(bool isCustomer = false){
			CheckCanManageSolution();
			List<Dictionary<string, string>> packageList = new List<Dictionary<string, string>>();
			EntitySchemaQuery esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "SysPackage");
			esq.AddAllSchemaColumns();
			if (isCustomer) {
				string maintainerName = SysSettings.GetValue(UserConnection, "Maintainer", string.Empty);
				Guid customPackageUId = SysSettings.GetValue(UserConnection, "CustomPackageUId", Guid.Empty);
				IEntitySchemaQueryFilterItem maintainerNameFilter =
					esq.CreateFilterWithParameters(FilterComparisonType.Equal, "Maintainer", maintainerName);
				IEntitySchemaQueryFilterItem nameNotEqualCustomFilter =
					esq.CreateFilterWithParameters(FilterComparisonType.NotEqual, "UId", customPackageUId);
				IEntitySchemaQueryFilterItem installTypeFilter =
					esq.CreateFilterWithParameters(FilterComparisonType.Equal, "InstallType", 0);
				esq.Filters.Add(maintainerNameFilter);
				esq.Filters.Add(nameNotEqualCustomFilter);
				esq.Filters.Add(installTypeFilter);
			}
			EntityCollection packages = esq.GetEntityCollection(UserConnection);
			foreach (Entity p in packages) {
				Dictionary<string, string> package = new Dictionary<string, string> {
					["Name"] = p.PrimaryDisplayColumnValue,
					["UId"] = p.GetTypedColumnValue<string>("UId"),
					["Maintainer"] = p.GetTypedColumnValue<string>("Maintainer"),
					["Version"] = p.GetTypedColumnValue<string>("Version")
				};
				packageList.Add(package);
			}
			string json = JsonConvert.SerializeObject(packageList);
			return json;
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "LockPackages",
			BodyStyle = WebMessageBodyStyle.WrappedRequest, RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json)]
		public bool LockPackages(string[] lockPackages = null){
			CheckCanManageSolution();
			_log.WarnFormat("Start LockPackages, packages: {0}", string.Join(", ", lockPackages));
			string maintainerCode = SysSettings.GetValue(UserConnection, "Maintainer", "NonImplemented");
			if (lockPackages != null && lockPackages.Any()) {
				foreach (string lockPackage in lockPackages) {
					string originalMaintainer = GetPackageAttributeValue<string>("Maintainer", lockPackage);
					string[] description = GetPackageAttributeValue<string>("Description", lockPackage)
						.Split(new[] {splitName}, StringSplitOptions.None);
					string maintainer = description.Length > 1 ? description.Last() : originalMaintainer;
					Query query = new Update(UserConnection, "SysPackage")
						.Set("InstallType", Column.Parameter(1))
						.Set("Maintainer", Column.Parameter(maintainer))
						.Set("Description", Column.Parameter(description[0]))
						.Where("Name").IsEqual(Column.Parameter(lockPackage));
					Update update = query as Update;
					update.BuildParametersAsValue = true;
					update.Execute();
				}
			} else {
				Query query = new Update(UserConnection, "SysPackage")
					.Set("InstallType", Column.Parameter(1, "Integer"))
					.Where("Maintainer").IsEqual(Column.Parameter(maintainerCode))
					.And("Name").IsNotEqual(Column.Parameter("Custom")) as Update;
				Update update = query as Update;
				update.Execute();
			}
			return true;
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "ResetSchemaChangeState",
			BodyStyle = WebMessageBodyStyle.WrappedRequest, RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json)]
		public bool ResetSchemaChangeState(string packageName){
			CheckCanManageSolution();
			Update update = new Update(UserConnection, "SysSchema")
				.Set("IsChanged", Column.Parameter(false, "Boolean"))
				.Where("SysPackageId").In(new Select(UserConnection).Column("Id").From("SysPackage")
					.Where("SysPackage", "Name").IsEqual(Column.Parameter(packageName))) as Update;
			update.Execute();
			return true;
		}

		// /rest/CreatioApiGateway/SavePackageFileContent?packageName=CrtBase&filePath=descriptor12345.json&fileContent=123
		[OperationContract]
		[WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.WrappedRequest, RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json)]
		public BaseResponse SavePackageFileContent(string packageName, string filePath, string fileContent){
			CheckCanManageSolution();

			PackageExplorer packageExplorer = new PackageExplorer(packageName);
			var result = packageExplorer.SaveFileContent(filePath, fileContent);
			return new BaseResponse {
				Success = result.isSuccess,
				ErrorInfo = new ErrorInfo() {
					Message = result.ex?.Message,
					StackTrace = result.ex?.StackTrace
				}
			};
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "UnlockPackages",
			BodyStyle = WebMessageBodyStyle.WrappedRequest, RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json)]
		public bool UnlockPackages(string[] unlockPackages = null){
			CheckCanManageSolution();
			_log.WarnFormat("Start UnlockPackages, packages: {0}", string.Join(", ", unlockPackages));
			string maintainerCode = SysSettings.GetValue(UserConnection, "Maintainer", "NonImplemented");
			if (unlockPackages != null && unlockPackages.Any()) {
				foreach (string unlockPackage in unlockPackages) {
					string originalMaintainer = GetPackageAttributeValue<string>("Maintainer", unlockPackage);
					string originalDescription = GetPackageAttributeValue<string>("Description", unlockPackage);
					string description = originalDescription.Contains(splitName) ? originalDescription
						: originalDescription + splitName + originalMaintainer;
					Query query = new Update(UserConnection, "SysPackage")
						.Set("InstallType", Column.Parameter(0))
						.Set("Maintainer", Column.Parameter(maintainerCode))
						.Set("Description", Column.Parameter(description))
						.Where("Name").IsEqual(Column.Parameter(unlockPackage));
					Update update = query as Update;
					update.BuildParametersAsValue = true;
					update.Execute();
				}
			} else {
				Query query = new Update(UserConnection, "SysPackage")
					.Set("InstallType", Column.Parameter(0, "Integer"))
					.Where("Maintainer").IsEqual(Column.Parameter(maintainerCode));
				Update update = query as Update;
				update.Execute();
			}
			return true;
		}

		[OperationContract]
		[WebInvoke(Method = "POST", UriTemplate = "UpdateDBStructure", BodyStyle = WebMessageBodyStyle.WrappedRequest,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public bool UpdateDBStructure(){
			CheckCanManageSolution();
			IEnumerable<Guid> invalidSchemas = GetEntitySchemasWithNeedUpdateStructure();
			return CreateInstallUtilities().SaveSchemaDBStructure(invalidSchemas, true);
		}

		[OperationContract]
		[WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.WrappedRequest,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public BaseResponse UploadFile(Stream stream){
			CheckCanManageSolution();
			HttpContext contextAccessor = HttpContextAccessor.GetInstance();
			HeaderCollection headers = contextAccessor.Request.Headers;
			const string fileNameHeader = "X-File-Name";
			const string packageNameHeader = "X-Package-Name";
			if(!headers.AllKeys.Contains(fileNameHeader)) {
				return new BaseResponse {
					Success = false,
					ErrorInfo = new ErrorInfo() {
						Message = $"Error: {fileNameHeader} header missing",
					}
				};
			}
			if(!headers.AllKeys.Contains(packageNameHeader)) {
				return new BaseResponse {
					Success = false,
					ErrorInfo = new ErrorInfo() {
						Message = $"Error: {packageNameHeader} header missing",
					}
				};
			}
			string filename = headers[fileNameHeader];
			string packageName = headers[packageNameHeader];
			
			PackageExplorer packageExplorer = new PackageExplorer(packageName);
			packageExplorer.SaveFileContent(filename, stream);
			
			return new BaseResponse {
				Success = true
			};
			
		}
		[OperationContract]
		[WebInvoke(Method = "POST", BodyStyle = WebMessageBodyStyle.WrappedRequest,
			RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public BaseResponse DeleteFile(string packageName, string filePath){
			CheckCanManageSolution();
			PackageExplorer packageExplorer = new PackageExplorer(packageName);
			(bool isSuccess, Exception ex) = packageExplorer.DeleteFile(filePath);
			
			if(!isSuccess) {
				return new BaseResponse {
					Success = isSuccess,
					ErrorInfo = {
						Message = ex.Message,
						StackTrace = ex.StackTrace
					}
				};
			}
			return new BaseResponse {
				Success = true
			};
			
		}

		// /rest/CreatioApiGateway/GetApplicationIdByName?appName=ttt1
		[OperationContract]
		[WebInvoke(Method = "GET", RequestFormat = WebMessageFormat.Json, ResponseFormat = WebMessageFormat.Json)]
		public string GetApplicationIdByName(string appName) {
			CheckCanManageSolution();
			EntitySchemaQuery esq = new EntitySchemaQuery(UserConnection.EntitySchemaManager, "SysInstalledApp");
			var columndId = esq.AddColumn("Id");
			
			esq.Filters.Add(
				esq.CreateFilterWithParameters(FilterComparisonType.Equal,"Name", appName));
			esq.Filters.Add(
				esq.CreateFilterWithParameters(FilterComparisonType.Equal, "Code", appName));
			esq.Filters.LogicalOperation = LogicalOperationStrict.Or;
			EntityCollection entities = esq.GetEntityCollection(UserConnection);
			if (entities.Count == 0) {
				return $"Application {appName} not found.";
			}
			if (entities.Count > 1) {
				return $"More then one application found.";
			}
			return entities[0].GetTypedColumnValue<string>(columndId.Name);
		}

		#endregion

	}

	
	public class PackageExplorer
	{

		#region Fields: Private

		private readonly string _packageName;
		private readonly string _baseDir = AppDomain.CurrentDomain.BaseDirectory;

		#endregion

		#region Constructors: Public

		public PackageExplorer(string packageName){
			CheckNameForDeniedSymbols(packageName);
			_packageName = packageName;
		}

		#endregion

		#region Methods: Private

		private void CheckNameForDeniedSymbols(string name){
			string[] invalidArgs = {
				"%2e%2e%2f", "%2e%2e/", "..%2f", "%2e%2e%5c", "2e%2e\\", "..%5c",
				"%252e%252e%255c", "..%255c", "../", "..\\"
			};
			foreach (string invalidArg in invalidArgs) {
				if (name.Contains(invalidArg)) {
					throw new Exception("Invalid character");
				}
			}
		}

		private string PackageDirectoryPath(){
			return Path.Combine(_baseDir, "Terrasoft.Configuration", "Pkg", _packageName, "Files");
		}

		private bool IsPackageUnlocked(string packageName){
			var userConnection = ClassFactory.Get<UserConnection>();
			string maintainerCode = SysSettings.GetValue(userConnection, "Maintainer", "NonImplemented");
			Select select = new Select(userConnection)
				.From("SysPackage").WithHints(new NoLockHint())
				.Column(Func.Count("Id")).As("Count")
				.Where("Name").IsEqual(Column.Parameter(packageName)) 
				.And("InstallType").IsEqual(Column.Parameter(0))
				.And("Maintainer").IsEqual(Column.Parameter(maintainerCode))
				as Select;
			return select.ExecuteScalar<int>() == 1;
		}
		
		#endregion

		#region Methods: Public

		public string GetPackageFileContent(string filePath){
			CheckNameForDeniedSymbols(filePath);
			return File.ReadAllText(Path.Combine(PackageDirectoryPath(), filePath));
		}

		public IEnumerable<string> GetPackageFilesDirectoryContent() =>
			Directory
				.GetFiles(PackageDirectoryPath(), "*.*", SearchOption.AllDirectories)
				.Select(f => f.Replace(PackageDirectoryPath(), string.Empty));

		public (bool isSuccess, Exception ex) SaveFileContent(string filePath, string fileContent){
			CheckNameForDeniedSymbols(filePath);
			if(!IsPackageUnlocked(_packageName)) {
				return (false, new Exception("Cannot save file in a locked package"));
			}
			string fullPath = Path.Combine(PackageDirectoryPath(), filePath);
			string directoryPath = Path.GetDirectoryName(fullPath);
			if (!Directory.Exists(directoryPath)) {
				Directory.CreateDirectory(directoryPath);
			}
			File.WriteAllText(fullPath, fileContent);
			return (true, null);
		}
		public (bool isSuccess, Exception ex) SaveFileContent(string filePath, Stream fileContent){
			CheckNameForDeniedSymbols(filePath);
			if(!IsPackageUnlocked(_packageName)) {
				return (false, new Exception("Cannot save file in a locked package"));
			}
			string fullPath = Path.Combine(PackageDirectoryPath(), filePath);
			string directoryPath = Path.GetDirectoryName(fullPath);
			if (!Directory.Exists(directoryPath)) {
				Directory.CreateDirectory(directoryPath);
			}
			File.WriteAllBytes(fullPath, fileContent.GetAllBytes());
			return (true, null);
		}

		#endregion

		public (bool isSuccess, Exception ex) DeleteFile(string filePath){
			CheckNameForDeniedSymbols(filePath);
			if(!IsPackageUnlocked(_packageName)) {
				return (false, new Exception("Cannot save file in a locked package"));
			}
			string fullPath = Path.Combine(PackageDirectoryPath(), filePath);
			string directoryPath = Path.GetDirectoryName(fullPath);
			if (!Directory.Exists(directoryPath)) {
				return (false, new DirectoryNotFoundException(directoryPath));
			}
			try {
				File.Delete(fullPath);
				return (true, null);
			} catch (Exception ex) {
				return (false, ex);
			}
		}

	}

	#endregion
}