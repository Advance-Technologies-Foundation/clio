using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using FluentValidation;

namespace Clio.Requests;

public enum SiteType {
	NetFramework,
	Core,
	NotCreatioSite
}

public sealed record SiteBinding(string name, string state, string binding, string path, string appPoolName = null) { }

public sealed record UnregisteredSite(SiteBinding siteBinding, IList<Uri> Uris, SiteType siteType) { }

public sealed record RegisteredSite(SiteBinding siteBinding, IList<Uri> Uris, SiteType siteType) { }

/// <summary>Provides discovery of Creatio sites registered in IIS.</summary>
public interface IIisScanner {

	/// <summary>
	///  Finds all Creatio sites in IIS, regardless of whether they are registered with clio.
	/// </summary>
	/// <returns>The Creatio sites discovered in IIS.</returns>
	IEnumerable<UnregisteredSite> FindAllCreatioSites();

	/// <summary>
	///  Finds all registered Creatio sites in IIS.
	/// </summary>
	/// <returns>The registered Creatio sites discovered in IIS.</returns>
	IEnumerable<RegisteredSite> FindAllRegisteredCreatioSites();

	/// <summary>
	///  Stops the IIS site and its application pool by site name.
	/// </summary>
	/// <param name="siteName">The IIS site name to stop.</param>
	void StopSiteByName(string siteName, string appPoolName = null);

	/// <summary>
	///  Deletes the IIS site and its application pool by site name.
	/// </summary>
	/// <param name="siteName">The IIS site name to delete.</param>
	void DeleteSiteByName(string siteName, string appPoolName = null);
}

public class IISScannerRequest : IExternalLink {

	#region Properties: Public

	public string Content { get; set; }

	#endregion

}

/// <summary>
///  Finds path to appSetting.json
/// </summary>
/// <remarks>
///  Handles extenral link requests
///  <example>
///   <code>clio externalLink clio://IISScannerRequest</code>
///  </example>
/// </remarks>
/// <example>
/// </example>
internal class IisScannerHandler : BaseExternalLinkHandler, IIisScanner, IExternalLinkHandler {

	#region Fields: Private

	/// <summary>
	///  Splits IIS Binding into list
	/// </summary>
	private static readonly Func<string, List<string>> SplitBinding = binding => binding.Contains(',') ? binding.Split(',').ToList() : [binding];

	/// <summary>
	///  Splits IIS Binding
	/// </summary>
	private static readonly Func<string, List<Uri>> ConvertBindingToUri = binding => {
		List<Uri> result = [];
		SplitBinding(binding).ForEach(item => {
			string[] items = item.Split(':');
			if (items.Length >= 3) {
				string hostName = string.IsNullOrEmpty(items[2]) ? "localhost" : items[2];
				string port = items[1];
				string other = items[0];
				string protocol = other.Replace("/*", "");
				string url = $"{protocol}://{hostName}:{port}";
				if (Uri.TryCreate(url, UriKind.Absolute, out Uri value)) {
					result.Add(value);
				}
			}
		});
		return result;
	};

	/// <summary>
	///  Detect Site Type
	/// </summary>
	private static readonly Func<string, SiteType> DetectSiteType = path => {
		string normalizedPath = (path ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar);
		bool isTerrasoftWebAppFolder = string.Equals(
			Path.GetFileName(normalizedPath),
			"Terrasoft.WebApp",
			StringComparison.OrdinalIgnoreCase);
		string webapp = Path.Join(path, "Terrasoft.WebApp");
		string configuration = Path.Join(path, "Terrasoft.Configuration");
		if (new DirectoryInfo(webapp).Exists) {
			return SiteType.NetFramework;
		}
		if (!isTerrasoftWebAppFolder && new DirectoryInfo(configuration).Exists) {
			return SiteType.Core;
		}
		return SiteType.NotCreatioSite;
	};

	private readonly IProcessExecutor _processExecutor;
	private readonly ISettingsRepository _settingsRepository;
	private readonly RegAppCommand _regCommand;
	private readonly PowerShellFactory _powerShellFactory;
	private readonly ILogger _logger;
	private readonly IValidator<IISScannerRequest> _validator;

	#endregion

	#region Fields: Public


	public record App(string PhysicalPath, Uri Url, SiteType SiteType);
	
	/// <summary>
	///  Gets IIS Sites that are physically located in **/Terrasoft.WebApp folder from remote host
	/// </summary>
	public static readonly Func<IPowerShellFactory, Dictionary<string, App>> GetSites = psf => {
		Dictionary<string, App> result = new();
		psf.GetInstance().AddCommand("Import-Module").AddArgument("WebAdministration").Invoke();
		
		Collection<Site> sites = psf.GetInstance().AddCommand("Get-WebSite").Invoke<Site>();
		Collection<WebApp> webApps = psf.GetInstance().AddCommand("Get-WebApplication").Invoke<WebApp>();

		webApps.Where(webApp => webApp.Path.EndsWith("/0") && webApp.PhysicalPath.EndsWith("Terrasoft.WebApp"))
			.ToList()
			.ForEach(webApp => {
				ConsoleLogger.Instance.WriteInfo(webApp.SiteName);
				(string Name, List<Uri> Uris, string physicalPath) rootSite = sites.Where(site =>
						site.Name == webApp.SiteName && site.Id == webApp.SiteId)
					.Select(site => (site.Name, site.Uris, site.PhysicalPath)).FirstOrDefault();

				string newPath = webApp.Path.Substring(0, webApp.Path.Length - 2);
				Uri rootUri = psf.ComputerName == "localhost" 
					? rootSite.Uris.FirstOrDefault()
					: rootSite.Uris.FirstOrDefault(u => u.Host != "localhost");
				
				if (Uri.TryCreate(rootUri, newPath, out Uri value)) {
					// For NetFramework apps, the environment path should be the parent of Terrasoft.WebApp
					// webApp.PhysicalPath points to "C:\...\Terrasoft.WebApp", we need its parent
					string environmentPath = Directory.GetParent(webApp.PhysicalPath)?.FullName ?? webApp.PhysicalPath;
					result.Add(rootSite.Name + newPath, new App(environmentPath, value, SiteType.NetFramework));
				}
			});

		// Only add top-level sites that are Creatio sites and haven't been added yet
		sites.Where(site => !result.ContainsKey(site.Name) && DetectSiteType(site.PhysicalPath) != SiteType.NotCreatioSite)
			.ToList()
			.ForEach(site => {
				result.Add(site.Name, new App(site.PhysicalPath, site.Uris.FirstOrDefault(), DetectSiteType(site.PhysicalPath)));
			});
		
		return result;
	};
	
	#endregion

	#region Constructors: Public

	public IisScannerHandler(ISettingsRepository settingsRepository, RegAppCommand regCommand,
		PowerShellFactory powerShellFactory, ILogger logger, IProcessExecutor processExecutor,
		IValidator<IISScannerRequest> validator) {
		_settingsRepository = settingsRepository;
		_regCommand = regCommand;
		_powerShellFactory = powerShellFactory;
		_logger = logger;
		_processExecutor = processExecutor;
		_validator = validator;
	}

	/// <inheritdoc />
	public Type RequestType => typeof(IISScannerRequest);

	#endregion

	#region Methods: Private

	private string ExecuteAppCmd(string args) {
		const string dirPath = @"C:\Windows\System32\inetsrv\";
		return _processExecutor.Execute(Path.Join(dirPath, "appcmd.exe"), args, waitForExit: true);
	}

	private static string QuoteAppCmdArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

	private void StopAppPool(string name) =>
		ExecuteAppCmd($"stop apppool {QuoteAppCmdArgument($"/apppool.name:{name}")}");
	private void StopSite(string name) => ExecuteAppCmd($"stop site {QuoteAppCmdArgument($"/site.name:{name}")}");
	private void RemoveSite(string name) => ExecuteAppCmd($"delete site {QuoteAppCmdArgument($"/site.name:{name}")}");
	private void RemoveAppPool(string name) =>
		ExecuteAppCmd($"delete apppool {QuoteAppCmdArgument($"/apppool.name:{name}")}");

	private SiteBinding GetSiteBinding(XElement xmlElement) => new SiteBinding(
		xmlElement.Attribute("SITE.NAME")?.Value,
		xmlElement.Attribute("state")?.Value,
		xmlElement.Attribute("bindings")?.Value,
		ExecuteAppCmd($"list VDIR {QuoteAppCmdArgument($"{xmlElement.Attribute("SITE.NAME").Value}/")} /text:physicalPath").Trim(),
		ExecuteAppCmd($"list APP {QuoteAppCmdArgument($"{xmlElement.Attribute("SITE.NAME").Value}/")} /text:applicationPool").Trim()
	);

	private SiteBinding GetApplicationBinding(XElement xmlElement) {
		string appName = xmlElement.Attribute("APP.NAME")?.Value ?? string.Empty;
		string[] parts = appName.Split('/', 2);
		string siteName = parts.Length > 0 ? parts[0] : string.Empty;
		string appPath = parts.Length > 1 ? ("/" + parts[1]) : string.Empty;
		string siteXml = ExecuteAppCmd($"list site \"{siteName}\" /xml");
		string siteBindings = string.Empty;
		string siteState = string.Empty;
		if (!string.IsNullOrWhiteSpace(siteXml)) {
			try {
				XElement siteElement = XElement.Parse(siteXml).Element("SITE");
				if (siteElement != null) {
					siteBindings = siteElement.Attribute("bindings")?.Value ?? string.Empty;
					siteState = siteElement.Attribute("state")?.Value ?? string.Empty;
				}
			} catch (Exception ex) {
				System.Diagnostics.Trace.TraceWarning(ex.Message);
			}
		}
		string vdirPath = string.IsNullOrEmpty(appPath) ? $"{siteName}/" : $"{siteName}{appPath}/";
		string physicalPath = ExecuteAppCmd($"list vdir \"{vdirPath}\" /text:physicalPath").Trim();
		if (string.IsNullOrWhiteSpace(physicalPath)) {
			physicalPath = ExecuteAppCmd($"list app \"{appName}\" /text:physicalPath").Trim();
		}
		string appPoolName = ExecuteAppCmd($"list APP {QuoteAppCmdArgument(appName)} /text:applicationPool").Trim();
		return new SiteBinding(appName, siteState, siteBindings, physicalPath, appPoolName);
	}

	private IEnumerable<SiteBinding> GetIISBindings() {
		List<SiteBinding> result = [];
		string sitesXml = ExecuteAppCmd("list sites /xml");
		if (!string.IsNullOrWhiteSpace(sitesXml)) {
			XElement sitesRoot = XElement.Parse(sitesXml);
			IEnumerable<SiteBinding> topLevelSites = sitesRoot.Elements("SITE")
				.Select(site => GetSiteBinding(site));
			result.AddRange(topLevelSites);
		}
		string appsXml = ExecuteAppCmd("list app /xml");
		if (!string.IsNullOrWhiteSpace(appsXml)) {
			XElement appsRoot = XElement.Parse(appsXml);
			IEnumerable<SiteBinding> nestedApps = appsRoot.Elements("APP")
				.Where(app => {
					string appName = app.Attribute("APP.NAME")?.Value ?? string.Empty;
					return !string.IsNullOrEmpty(appName) && !appName.EndsWith('/') && appName.Contains('/');
				})
				.Select(app => GetApplicationBinding(app));
			result.AddRange(nestedApps);
		}
		return result;
	}

	private IEnumerable<UnregisteredSite> FindUnregisteredSites() {
		return GetIISBindings().Where(site => {
			bool isRegisteredEnvironment = false;
			ConvertBindingToUri(site.binding).ForEach(uri => {
				string key = _settingsRepository.FindEnvironmentNameByUri(uri.ToString());
				if (!string.IsNullOrEmpty(key) && !isRegisteredEnvironment) {
					isRegisteredEnvironment = true;
				}
			});
			return !isRegisteredEnvironment;
		})
		.Where(site => DetectSiteType(site.path) != SiteType.NotCreatioSite)
		.Select(site => new UnregisteredSite(
			site,
			ConvertBindingToUri(site.binding),
			DetectSiteType(site.path)));
	}

	public IEnumerable<UnregisteredSite> FindAllCreatioSites() {
		return GetIISBindings()
			.Where(site => DetectSiteType(site.path) != SiteType.NotCreatioSite)
			.Select(site => new UnregisteredSite(
				site,
				ConvertBindingToUri(site.binding),
				DetectSiteType(site.path)));
	}

	public IEnumerable<RegisteredSite> FindAllRegisteredCreatioSites() {
		return GetIISBindings()
			.Where(site => DetectSiteType(site.path) != SiteType.NotCreatioSite)
			.Select(site => new RegisteredSite(
				site,
				ConvertBindingToUri(site.binding),
				DetectSiteType(site.path)));
	}

	#endregion

	#region Methods: Public

	/// <inheritdoc />
	public void StopSiteByName(string siteName, string appPoolName = null){
		StopSite(siteName);
		StopAppPool(string.IsNullOrWhiteSpace(appPoolName) ? siteName : appPoolName);
	}

	/// <inheritdoc />
	public void DeleteSiteByName(string siteName, string appPoolName = null){
		RemoveSite(siteName);
		RemoveAppPool(string.IsNullOrWhiteSpace(appPoolName) ? siteName : appPoolName);
	}

	public async Task Handle(IExternalLink request){
		_validator.ValidateAndThrow((IISScannerRequest)request);
		Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
		IEnumerable<UnregisteredSite> unregSites = FindUnregisteredSites();

		string r = ClioParams?["return"];
		if (r == "remote") {
			//clio://IISScannerRequest/?returnremote&host=localhost;
			string computerName = ClioParams?["host"];

			//clio externalLink clio://IISScannerRequest/?return=remote&host=localhost&username=1234&password=5678;
			string userName = ClioParams?["username"];
			string password = ClioParams?["password"];
			_powerShellFactory.Initialize(userName, password, computerName);

			int i = 1;

			GetSites(_powerShellFactory)?.ToList().ForEach(async site => {
				_logger.WriteInfo($"({i++}) {site.Key} - {site.Value.Url} at {site.Value.PhysicalPath}");
			});

			//Here I would call regApp command but instead I will write total
			_logger.WriteInfo($"**** TOTAL: {i - 1} new sites ****");
		}
		if (r == "count") {
			_logger.WriteInfo(unregSites.Count().ToString());
		}
		if (r == "details") {
			string json = JsonSerializer.Serialize(unregSites);
			_logger.WriteInfo(json);
		}
		if (r == "registerAll") {
			unregSites.ToList().ForEach(site => {
				_regCommand.Execute(new RegAppOptions {
					IsNetCore = site.siteType == SiteType.Core,
					Uri = site.Uris.FirstOrDefault().ToString(),
					EnvironmentName = site.siteBinding.name,
					Login = "Supervisor",
					Password = "Supervisor",
					Maintainer = "Customer",
					CheckLogin = false
				});
			});
		}
	}

	#endregion

}

public record Site {

	#region Fields: Private

	/// <summary>
	///  Converts PSObject to Site
	/// </summary>
	private static readonly Func<PSObject, Site> _asSite = psObject => {
		return new Site {
			Name = psObject.Properties["Name"].Value as string,
			PhysicalPath = psObject.Properties["PhysicalPath"]?.Value as string ?? string.Empty,
			Id = (long)psObject.Properties["Id"].Value,
			EnabledProtocols = psObject.Properties["EnabledProtocols"].ToString(),
			Binding = (psObject.Properties["Bindings"].Value as PSObject).Properties
				.FirstOrDefault(p => p.Name == "Collection")?.Value.ToString()
		};
	};

	/// <summary>
	///  Splits IIS Binding
	/// </summary>
	private static readonly Func<string, List<Uri>> ConvertBindingToUri = binding => {
		binding = binding.Replace(" *", "/*").Replace(" ", ",");

		List<Uri> result = [];

		//"http/*:7080:localhost,http/*:7080:kkrylovn
		SplitBinding(binding).ForEach(item => {
			//http/*:7080:localhost
			//http/*:80:
			string[] items = item.Split(':');
			if (items.Length >= 3) {
				string hostName = string.IsNullOrEmpty(items[2]) ? "localhost" : items[2];
				string port = items[1];
				string other = items[0];
				string protocol = other.Replace("/*", "");
				string url = $"{protocol}://{hostName}:{port}";

				if (Uri.TryCreate(url, UriKind.Absolute, out Uri value)) {
					result.Add(value);
				}
			}
		});
		return result;
	};

	/// <summary>
	///  Splits IIS Binding into list
	/// </summary>
	private static readonly Func<string, List<string>> SplitBinding = binding => 
		binding.Contains(',') ? binding.Split(',').ToList() : [binding];

	#endregion

	#region Properties: Public

	public string Binding { get; private set; }

	/// <summary>
	///  IIS EnabledProtocols
	/// </summary>
	public string EnabledProtocols { get; set; }

	/// <summary>
	///  IIS Site Id
	/// </summary>
	public long Id { get; private set; }

	/// <summary>
	///  IIS Site name
	/// </summary>
	public string Name { get; private set; }

	/// <summary>
	///  IIS physical path
	/// </summary>
	public string PhysicalPath { get; private set; }

	public List<Uri> Uris => ConvertBindingToUri(Binding);

	#endregion

	#region Methods: Public

	public static implicit operator Site(PSObject obj) => _asSite(obj);

	#endregion

}

public record WebApp {

	#region Constants: Private

	private const string Regex = "(@name=')(.*?)'\\sand\\s@id='(\\d*?)'";

	#endregion

	#region Fields: Private

	private static readonly Func<PSObject, WebApp> AsWebApp = psObject => {
		string itemXPath = psObject.Properties["ItemXPath"]?.Value as string ?? string.Empty;
		GroupCollection groups = System.Text.RegularExpressions.Regex.Match(itemXPath, Regex).Groups;

		return new WebApp {
			ElementTagName = psObject.Properties["ElementTagName"]?.Value as string ?? string.Empty,
			Path = psObject.Properties["Path"]?.Value as string ?? string.Empty,
			EnabledProtocols = psObject.Properties["EnabledProtocols"]?.Value as string ?? string.Empty,
			PhysicalPath = psObject.Properties["PhysicalPath"]?.Value as string ?? string.Empty,
			ItemXPath = itemXPath,
			SiteName = groups[2].Value.Trim(),
			SiteId = long.TryParse(groups[3].Value?.Trim(), out long v) ? v : -1
		};
	};

	#endregion

	#region Properties: Public

	public string ElementTagName { get; private set; }

	public string EnabledProtocols { get; private set; }

	public string ItemXPath { get; private set; }

	public string Path { get; private set; }

	public string PhysicalPath { get; private set; }

	public long SiteId { get; private set; }

	public string SiteName { get; private set; }

	#endregion

	#region Methods: Public

	public static implicit operator WebApp(PSObject obj) => AsWebApp(obj);

	#endregion

}
