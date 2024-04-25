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
		///     Completes or continues business process
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

	}

	#endregion

	#region Constants: Private

	private const string WebAppAlias = "0/";

	#endregion

	#region Fields: Private

	internal readonly IReadOnlyDictionary<KnownRoute, string> KnownRoutes = new Dictionary<KnownRoute, string> {
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