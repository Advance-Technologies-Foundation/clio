using System;
using System.Collections.Generic;

namespace Clio.Common;

#region Class: ServiceUrlBuilder

public class ServiceUrlBuilder : IServiceUrlBuilder
{

	#region Enum: Public

	public enum KnownRoute
	{

		/// <summary>
		///     DataService Select Query
		/// </summary>
		Select = 1,

		/// <summary>
		///     DataService Insert Query
		/// </summary>
		Insert = 2,

		/// <summary>
		///     DataService Update Query
		/// </summary>
		Update = 3,

		/// <summary>
		///     DataService Delete Query
		/// </summary>
		Delete = 4,
		
		
		GetBusinessRules = 5,

		/// <summary>
		///     Start Business Process
		/// </summary>
		RunProcess = 6,

		/// <summary>
		///     Completes or continues execution of a business process
		/// </summary>
		CompleteExecuting = 7,
		
		/// <summary>
		/// Restores configuration from backup
		/// </summary>
		RestoreFromPackageBackup = 8,
		GetZipPackage = 9,
		BuildPackage = 10,
		RebuildPackage = 11,
		Compile = 12,
		CompileAll = 13,
		DownloadPackageDllFile = 14,
		ClearFeaturesCacheForAllUsers = 15,
		SendEventToUi = 17,
		
		/// <summary>
		/// Gets the schema of the process
		/// </summary>
		ProcessSchemaRequest = 18,
		StartLogBroadcast = 19,
		StopLogBroadcast = 20,
		ClearRedisDb = 21,
		EntitySchemaManagerRequest = 22,
		RuntimeEntitySchemaRequest = 23,
		GetWorkspaceItems = 24,
		DeleteWorkspaceItem = 25,
		GetUserTaskSchema = 26,
		CreateUserTaskSchema = 27,
		SaveUserTaskSchema = 28,
		GetAvailableEntitySchemas = 29,
		ServiceBasePath = 30,
		GetApplicationInfo = 31,
		SchemaDesignerRequest = 32,

		/// <summary>
		///     Saves or creates package data binding schema data via the schema designer service.
		/// </summary>
		SaveSchemaData = 33,

		/// <summary>
		///     Deletes package data binding schema data from the remote DB.
		/// </summary>
		DeletePackageSchemaData = 34,

		/// <summary>
		///     Retrieves bound schema data rows from the remote DB.
		/// </summary>
		GetBoundSchemaData = 35,

		/// <summary>
		///     Unlocks packages via ClioGate (bypasses DataService ESQ permission checks).
		/// </summary>
		UnlockPackages = 36,

		/// <summary>
		///     Locks packages via ClioGate (bypasses DataService ESQ permission checks).
		/// </summary>
		LockPackages = 37,

		/// <summary>
		///     Generates source code for all schemas.
		/// </summary>
		GenerateAllSchemasSources = 38,

		/// <summary>
		///     Generates source code only for modified schemas.
		/// </summary>
		GenerateModifiedSchemasSources = 39,

		/// <summary>
		///     Starts source code generation for all schemas as a background task.
		/// </summary>
		GenerateAllSchemasSourcesInBackground = 40,

		/// <summary>
		///     Generates source code for schemas that require it.
		/// </summary>
		GenerateRequiredSchemasSources = 41,

		/// <summary>
		///     Issues a short-lived signed identity assertion (JWT) for the current authorized user,
		///     used as input for the Identity Service V3 token exchange flow.
		/// </summary>
		IdentityAssertionCurrentUser = 42,

		/// <summary>
		///     Returns the instance public key as a JWK for registration with Identity Service V3.
		/// </summary>
		IdentityAssertionPublicJwk = 43,

		/// <summary>
		///     Regenerates the instance identity-assertion signing key pair (the private key never leaves Creatio).
		/// </summary>
		IdentityAssertionRegenerateSigningKey = 44,

		/// <summary>
		///     Reports whether the environment can use the OAuth authorization code flow.
		/// </summary>
		IdentityServiceInfoCanUseAuthorizationCodeFlow = 45,

		/// <summary>
		///     Reads the designer IdentityService client secret from Creatio.
		/// </summary>
		OAuthConfigGetIdentityServerClientSecret = 46,

		/// <summary>
		///     Creates a technical user for an OAuth client.
		/// </summary>
		OAuthConfigCreateTechnicalUser = 47,

		/// <summary>
		///     Adds an OAuth client through Creatio OAuth configuration service.
		/// </summary>
		OAuthConfigAddClient = 48,

		/// <summary>
		///     Builds a business process from a declarative descriptor via the ProcessDesignService package.
		/// </summary>
		BuildProcess = 49,

		/// <summary>
		///     Lists the available user-facing user tasks (designer palette) via the ProcessDesignService package.
		/// </summary>
		ListUserTasks = 50,

		/// <summary>
		///     Reads an existing process as a structured graph via the ProcessDesignService package.
		/// </summary>
		DescribeProcess = 51,

		/// <summary>
		///     Edits an existing business process (add/remove elements, flows, …) via the ProcessDesignService package.
		/// </summary>
		ModifyProcess = 52,

		/// <summary>
		///     Lists the custom themes available on the environment via the native ThemeService.
		/// </summary>
		GetAvailableThemes = 53,

		/// <summary>
		///     Refreshes the Creatio theme catalog cache via the native ThemeService.
		/// </summary>
		ClearThemesCache = 54,

		/// <summary>
		///     Creates a custom theme on the environment via the native ThemeService.
		/// </summary>
		CreateTheme = 55,

		/// <summary>
		///     Updates an existing custom theme via the native ThemeService.
		/// </summary>
		UpdateTheme = 56,

		/// <summary>
		///     Deletes a custom theme via the native ThemeService.
		/// </summary>
		DeleteTheme = 57,

		/// <summary>
		///     Checks whether the current user can execute a named system operation via the native RightsService.
		/// </summary>
		RightsGetCanExecuteOperation = 58,

		/// <summary>
		///     Reads the status of named license operations for the current user via the native LicenseService.
		/// </summary>
		LicenseGetLicOperationStatuses = 59

	}

	#endregion

	#region Constants: Private

	private const string WebAppAlias = "0/";

	#endregion

	#region Fields: Private

	public static readonly IReadOnlyDictionary<KnownRoute, string> KnownRoutes = new Dictionary<KnownRoute, string> {
		{KnownRoute.Select, "DataService/json/SyncReply/SelectQuery"},
		{KnownRoute.Insert, "DataService/json/SyncReply/InsertQuery"},
		{KnownRoute.Update, "DataService/json/SyncReply/UpdateQuery"},
		{KnownRoute.Delete, "DataService/json/SyncReply/DeleteQuery"},
		{KnownRoute.GetBusinessRules, "ServiceModel/BusinessRulesManagerService.svc/GetBusinessRules"},
		{KnownRoute.RunProcess, "ServiceModel/ProcessEngineService.svc/RunProcess"},
		{KnownRoute.CompleteExecuting, "ServiceModel/ProcessEngineService.svc/CompleteExecuting"},
		{KnownRoute.RestoreFromPackageBackup, "ServiceModel/PackageInstallerService.svc/RestoreFromPackageBackup"},
		{KnownRoute.GetZipPackage, "ServiceModel/PackageInstallerService.svc/GetZipPackages"},
		{KnownRoute.BuildPackage, "ServiceModel/WorkspaceExplorerService.svc/BuildPackage"},
		{KnownRoute.RebuildPackage, "ServiceModel/WorkspaceExplorerService.svc/RebuildPackage"},
		{KnownRoute.Compile, "ServiceModel/WorkspaceExplorerService.svc/Build"},
		{KnownRoute.CompileAll, "ServiceModel/WorkspaceExplorerService.svc/Rebuild"},
		{KnownRoute.DownloadPackageDllFile, "/rest/CreatioApiGateway/DownloadFile"},
		{KnownRoute.ClearFeaturesCacheForAllUsers, "/rest/FeatureService/ClearFeaturesCacheForAllUsers"},
		{KnownRoute.SendEventToUi, "/rest/CreatioApiGateway/SendEventToUI"},
		{KnownRoute.ProcessSchemaRequest, "DataService/json/SyncReply/ProcessSchemaRequest"},
		{KnownRoute.StartLogBroadcast, "/rest/ATFLogService/StartLogBroadcast"},
		{KnownRoute.StopLogBroadcast, "/rest/ATFLogService/ResetConfiguration"},
		{KnownRoute.ClearRedisDb, "ServiceModel/AppInstallerService.svc/ClearRedisDb"},
		{KnownRoute.EntitySchemaManagerRequest, "DataService/json/SyncReply/EntitySchemaManagerRequest"},
		{KnownRoute.RuntimeEntitySchemaRequest, "DataService/json/SyncReply/RuntimeEntitySchemaRequest"},
		{KnownRoute.GetWorkspaceItems, "ServiceModel/WorkspaceExplorerService.svc/GetWorkspaceItems"},
		{KnownRoute.DeleteWorkspaceItem, "ServiceModel/WorkspaceExplorerService.svc/Delete"},
		{KnownRoute.GetUserTaskSchema, "ServiceModel/ProcessUserTaskSchemaDesignerService.svc/GetSchema"},
		{KnownRoute.CreateUserTaskSchema, "ServiceModel/ProcessUserTaskSchemaDesignerService.svc/CreateNewSchema"},
		{KnownRoute.SaveUserTaskSchema, "ServiceModel/ProcessUserTaskSchemaDesignerService.svc/SaveSchema"},
		{KnownRoute.GetAvailableEntitySchemas, "ServiceModel/SchemaDataDesignerService.svc/GetAvailableEntitySchemas"},
		{KnownRoute.ServiceBasePath, "ServiceModel/EntitySchemaDesignerService.svc"},
		{KnownRoute.GetApplicationInfo, "ServiceModel/ApplicationInfoService.svc/GetApplicationInfo"},
		{KnownRoute.SchemaDesignerRequest, "DataService/json/SyncReply/SchemaDesignerRequest"},
		{KnownRoute.SaveSchemaData, "ServiceModel/SchemaDataDesignerService.svc/SaveSchema"},
		{KnownRoute.DeletePackageSchemaData, "DataService/json/SyncReply/DeletePackageSchemaDataRequest"},
		{KnownRoute.GetBoundSchemaData, "ServiceModel/SchemaDataDesignerService.svc/GetBoundSchemaData"},
		{KnownRoute.UnlockPackages, "/rest/CreatioApiGateway/UnlockPackages"},
		{KnownRoute.LockPackages, "/rest/CreatioApiGateway/LockPackages"},
		{KnownRoute.GenerateAllSchemasSources, "ServiceModel/WorkspaceExplorerService.svc/GenerateAllSchemasSources"},
		{KnownRoute.GenerateModifiedSchemasSources, "ServiceModel/WorkspaceExplorerService.svc/GenerateModifiedSchemasSources"},
		{KnownRoute.GenerateAllSchemasSourcesInBackground, "ServiceModel/WorkspaceExplorerService.svc/GenerateAllSchemasSourcesInBackground"},
		{KnownRoute.GenerateRequiredSchemasSources, "ServiceModel/WorkspaceExplorerService.svc/GenerateRequiredSchemasSources"},
		{KnownRoute.IdentityAssertionCurrentUser, "identityAssertion/currentUser"},
		{KnownRoute.IdentityAssertionPublicJwk, "identityAssertion/publicJwk"},
		{KnownRoute.IdentityAssertionRegenerateSigningKey, "identityAssertion/regenerateSigningKey"},
		{KnownRoute.IdentityServiceInfoCanUseAuthorizationCodeFlow, "identityServiceInfo/canUseAuthorizationCodeFlow"},
		{KnownRoute.OAuthConfigGetIdentityServerClientSecret, "/rest/OAuthConfigService/GetIdentityServerClientSecret"},
		{KnownRoute.OAuthConfigCreateTechnicalUser, "/rest/OAuthConfigService/CreateTechnicalUser"},
		{KnownRoute.OAuthConfigAddClient, "/rest/OAuthConfigService/AddClient"},
		{KnownRoute.BuildProcess, "/rest/ProcessDesignService/BuildProcess"},
		{KnownRoute.ListUserTasks, "/rest/ProcessDesignService/ListUserTasks"},
		{KnownRoute.DescribeProcess, "/rest/ProcessDesignService/DescribeProcess"},
		{KnownRoute.ModifyProcess, "/rest/ProcessDesignService/ModifyProcess"},
		{KnownRoute.GetAvailableThemes, "ServiceModel/ThemeService.svc/GetAvailableThemes"},
		{KnownRoute.ClearThemesCache, "ServiceModel/ThemeService.svc/ClearThemesCache"},
		{KnownRoute.CreateTheme, "ServiceModel/ThemeService.svc/CreateTheme"},
		{KnownRoute.UpdateTheme, "ServiceModel/ThemeService.svc/UpdateTheme"},
		{KnownRoute.DeleteTheme, "ServiceModel/ThemeService.svc/DeleteTheme"},
		{KnownRoute.RightsGetCanExecuteOperation, "/rest/RightsService/GetCanExecuteOperation"},
		{KnownRoute.LicenseGetLicOperationStatuses, "ServiceModel/LicenseService.svc/GetLicOperationStatuses"},
	};

	private EnvironmentSettings _environmentSettings;

	#endregion

	#region Constructors: Public

	public ServiceUrlBuilder(EnvironmentSettings environmentSettings){
		environmentSettings.CheckArgumentNull(nameof(environmentSettings));
		_environmentSettings = environmentSettings;
	}

	#endregion

	#region Methods: Private

	private string CreateUrl(string route){
		bool isBase = Uri.TryCreate(_environmentSettings.Uri, UriKind.Absolute, out Uri baseUri);
		if (!isBase) {
			throw new ArgumentException("Misconfigured Url, check settings and try again ", nameof(_environmentSettings.Uri));
		}
		
		return baseUri switch {
			_ when baseUri.ToString().EndsWith('/') && route.StartsWith('/') =>$"{baseUri}{route[1..]}",
			_ when (baseUri.ToString().EndsWith('/') && !route.StartsWith('/')) 
				|| (!baseUri.ToString().EndsWith('/') && route.StartsWith('/')) 
				=> $"{baseUri}{route}",
			_ => $"{baseUri}/{route}"
		};
	}

	#endregion

	#region Methods: Public

	public string Build(string serviceEndpoint){
		return _environmentSettings.IsNetCore switch {
			true => CreateUrl(serviceEndpoint),
			false => CreateUrl(
				$"{WebAppAlias}{(serviceEndpoint.StartsWith('/') ? serviceEndpoint[1..] : serviceEndpoint)}")
		};
	}

	public string Build(KnownRoute knownRoute) => Build(KnownRoutes[knownRoute]);

	public string Build(string serviceEndpoint, EnvironmentSettings environmentSettings){
		_environmentSettings = environmentSettings;
		return _environmentSettings.IsNetCore switch {
			true => CreateUrl(serviceEndpoint),
			false => CreateUrl(
				$"{WebAppAlias}{(serviceEndpoint.StartsWith('/') ? serviceEndpoint[1..] : serviceEndpoint)}")
		};
	}

	public string Build(KnownRoute knownRoute, EnvironmentSettings environmentSettings) =>
		Build(KnownRoutes[knownRoute], environmentSettings);

	#endregion

}

#endregion
