using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Clio.Command;
using Clio.Common;
using Clio.UserEnvironment;
using Clio.Utilities;
using MediatR;

namespace Clio.Requests;

public class IISScannerRequest : IExternalLink {

	#region Properties: Public

	public string Content { get; set; }

	#endregion

}

internal class AllUnregisteredSitesRequest : IRequest {

	#region Fields: Public

	public Action<IEnumerable<IISScannerHandler.UnregisteredSite>> Callback;

	#endregion

}
internal class AllRegisteredSitesRequest : IRequest {

	#region Fields: Public

	public Action<IEnumerable<IISScannerHandler.RegisteredSite>> Callback;

	#endregion

}

internal class StopInstanceByNameRequest : IRequest {

	#region Properties: Public

	public string SiteName { get; set; }

	#endregion

}

internal class DeleteInstanceByNameRequest : IRequest {

	#region Properties: Public

	public string SiteName { get; set; }

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
internal class IISScannerHandler : BaseExternalLinkHandler, IRequestHandler<IISScannerRequest>,
	IRequestHandler<AllUnregisteredSitesRequest>, IRequestHandler<DeleteInstanceByNameRequest>,
	IRequestHandler<StopInstanceByNameRequest>,IRequestHandler<AllRegisteredSitesRequest> {

	#region Enum: Internal

	internal enum SiteType {

		NetFramework,
		Core,
		NotCreatioSite

	}

	#endregion

	#region Fields: Private

	/// <summary>
	///  Finds Creatio Sites in IIS that are not registered with clio
	/// </summary>
	private static readonly Func<ISettingsRepository, IEnumerable<UnregisteredSite>> FindUnregisteredCreatioSites = settingsRepository => {
			return GetBindings().Where(site => {
					bool isRegisteredEnvironment = false;
					ConvertBindingToUri(site.binding).ForEach(uri => {
						string key = settingsRepository.FindEnvironmentNameByUri(uri.ToString());
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
		};

	/// <summary>
	///  Executes appcmd.exe with arguments and captures output
	/// </summary>
	private static readonly Func<string, string> _appcmd = args => {
		const string dirPath = @"C:\Windows\System32\inetsrv\";
		const string exeName = "appcmd.exe";
		return Process.Start(new ProcessStartInfo {
			RedirectStandardError = true,
			RedirectStandardOutput = true,
			UseShellExecute = false,
			Arguments = args,
			FileName = Path.Join(dirPath, exeName)
		})?.StandardOutput.ReadToEnd();
	};

	private static readonly Action<string> StopAppPoolByName = name => { _appcmd($"stop apppool /apppool.name:{name}"); };

	private static readonly Action<string> StopSiteByName = name => { _appcmd($"stop site /site.name:{name}"); };

	private static readonly Action<string> RemoveSiteByName = name => { _appcmd($"delete site /site.name:{name}"); };

	private static readonly Action<string> RemoveAppPoolByName = name => { _appcmd($"delete apppool /apppool.name:{name}"); };

	
	/// <summary>
	///  Finds Creatio Sites in IIS that are not registered with clio
	/// </summary>
	internal static Func<IEnumerable<UnregisteredSite>> FindAllCreatioSites = () => {
		return GetBindings()
			.Where(site => DetectSiteType(site.path) != SiteType.NotCreatioSite)
			.Select(site => new UnregisteredSite(
				site,
				ConvertBindingToUri(site.binding),
				DetectSiteType(site.path)));
	};
	
	/// <summary>
	///  Finds Creatio Sites in IIS that are not registered with clio
	/// </summary>
	internal static readonly Func<IEnumerable<RegisteredSite>> FindAllRegisteredCreatioSites = () => {
		return GetBindings()
				.Where(site => DetectSiteType(site.path) != SiteType.NotCreatioSite)
				.Select(site => new RegisteredSite(
					site,
					ConvertBindingToUri(site.binding),
					DetectSiteType(site.path)));
	};

	/// <summary>
	///  Gets data from appcmd.exe
	/// </summary>
	private static readonly Func<IEnumerable<SiteBinding>> GetBindings = () => {
		List<SiteBinding> result = [];
		
		// Get all top-level sites
		string sitesXml = _appcmd("list sites /xml");
		if (!string.IsNullOrWhiteSpace(sitesXml)) {
			XElement sitesRoot = XElement.Parse(sitesXml);
			IEnumerable<SiteBinding> topLevelSites = sitesRoot.Elements("SITE")
				.Select(site => GetSiteBindingFromXmlElement(site));
			result.AddRange(topLevelSites);
		}
		
		// Get all applications to discover nested sites
		string appsXml = _appcmd("list app /xml");
		if (!string.IsNullOrWhiteSpace(appsXml)) {
			XElement appsRoot = XElement.Parse(appsXml);
			IEnumerable<SiteBinding> nestedApps = appsRoot.Elements("APP")
				.Where(app => {
					string appName = app.Attribute("APP.NAME")?.Value ?? string.Empty;
					// APP.NAME format is "SiteName/AppPath" or "SiteName/" for root
					// Skip root applications (ending with "/"), as they're already covered by top-level sites
					// We only want nested applications like "Default Web Site/MyApp"
					return !string.IsNullOrEmpty(appName) && 
					       !appName.EndsWith("/") && 
					       appName.Contains("/");
				})
				.Select(app => GetApplicationBindingFromXmlElement(app));
			result.AddRange(nestedApps);
		}
		
		return result;
	};

	/// <summary>
	///  Splits IIS Binding into list
	/// </summary>
	private static readonly Func<string, List<string>> SplitBinding = binding => binding.Contains(',') ? binding.Split(',').ToList() : [binding];

	/// <summary>
	///  Splits IIS Binding
	/// </summary>
	private static readonly Func<string, List<Uri>> ConvertBindingToUri = binding => {
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
	///  Converts XElement to Sitebinding
	/// </summary>
	private static readonly Func<XElement, SiteBinding> GetSiteBindingFromXmlElement = xmlElement => new SiteBinding(
		xmlElement.Attribute("SITE.NAME")?.Value,
		xmlElement.Attribute("state")?.Value,
		xmlElement.Attribute("bindings")?.Value,
		_appcmd($"list VDIR {xmlElement.Attribute("SITE.NAME").Value}/ /text:physicalPath").Trim()
	);

	/// <summary>
	///  Converts Application XElement to SiteBinding for nested applications
	/// </summary>
	private static readonly Func<XElement, SiteBinding> GetApplicationBindingFromXmlElement = xmlElement => {
		string appName = xmlElement.Attribute("APP.NAME")?.Value ?? string.Empty;
		// APP.NAME format is "SiteName/AppPath" e.g., "Default Web Site/MyApp"
		string[] parts = appName.Split('/', 2);
		string siteName = parts.Length > 0 ? parts[0] : string.Empty;
		string appPath = parts.Length > 1 ? ("/" + parts[1]) : string.Empty;
		
		// Get the site's bindings to construct the full URL
		string siteXml = _appcmd($"list site \"{siteName}\" /xml");
		string siteBindings = string.Empty;
		string siteState = string.Empty;
		
		if (!string.IsNullOrWhiteSpace(siteXml)) {
			try {
				XElement siteElement = XElement.Parse(siteXml).Element("SITE");
				if (siteElement != null) {
					siteBindings = siteElement.Attribute("bindings")?.Value ?? string.Empty;
					siteState = siteElement.Attribute("state")?.Value ?? string.Empty;
				}
			} catch {
				// If parsing fails, continue with empty bindings
			}
		}
		
		// Get the physical path for this application via vdir query
		// For nested apps, the vdir path is "SiteName/AppPath/"
		string vdirPath = string.IsNullOrEmpty(appPath) ? $"{siteName}/" : $"{siteName}{appPath}/";
		string physicalPath = _appcmd($"list vdir \"{vdirPath}\" /text:physicalPath").Trim();
		
		// If vdir query didn't work, try getting it from the app directly
		if (string.IsNullOrWhiteSpace(physicalPath)) {
			physicalPath = _appcmd($"list app \"{appName}\" /text:physicalPath").Trim();
		}
		
		// Use the full APP.NAME as the combined name (e.g., "Default Web Site/MyApp")
		return new SiteBinding(
			appName,
			siteState,
			siteBindings,
			physicalPath
		);
	};

	/// <summary>
	///  Detect Site Type
	/// </summary>
	private static readonly Func<string, SiteType> DetectSiteType = path => {
		string webapp = Path.Join(path, "Terrasoft.WebApp");
		string configuration = Path.Join(path, "Terrasoft.Configuration");

		if (new DirectoryInfo(webapp).Exists) {
			return SiteType.NetFramework;
		}

		if (new DirectoryInfo(configuration).Exists) {
			return SiteType.Core;
		}

		return SiteType.NotCreatioSite;
	};

	private readonly ISettingsRepository _settingsRepository;
	private readonly RegAppCommand _regCommand;
	private readonly PowerShellFactory _powerShellFactory;
	private readonly ILogger _logger;

	#endregion

	#region Fields: Public


	public record App(string PhysicalPath, Uri Url);
	
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
					result.Add(rootSite.Name + newPath, new App(environmentPath, value));
				}
			});

		// Only add top-level sites that are Creatio sites and haven't been added yet
		sites.Where(site => !result.ContainsKey(site.Name) && DetectSiteType(site.PhysicalPath) != SiteType.NotCreatioSite)
			.ToList()
			.ForEach(site => {
				result.Add(site.Name, new App(site.PhysicalPath, site.Uris.FirstOrDefault()));
			});
		
		return result;
	};
	
	#endregion

	#region Constructors: Public

	public IISScannerHandler(ISettingsRepository settingsRepository, RegAppCommand regCommand,
		PowerShellFactory powerShellFactory, ILogger logger){
		_settingsRepository = settingsRepository;
		_regCommand = regCommand;
		_powerShellFactory = powerShellFactory;
		_logger = logger;
	}

	#endregion

	#region Methods: Public

	public async Task Handle(AllUnregisteredSitesRequest request, CancellationToken cancellationToken){
		IEnumerable<UnregisteredSite> sites = FindAllCreatioSites();
		request.Callback(sites);
	}

	public async Task Handle(AllRegisteredSitesRequest request, CancellationToken cancellationToken){
		IEnumerable<RegisteredSite> sites = FindAllRegisteredCreatioSites();
		request.Callback(sites);
	}

	public async Task Handle(StopInstanceByNameRequest request, CancellationToken cancellationToken){
		string name = request.SiteName;
		StopSiteByName(name);
		StopAppPoolByName(name);
	}

	public async Task Handle(DeleteInstanceByNameRequest request, CancellationToken cancellationToken){
		string name = request.SiteName;
		RemoveSiteByName(name);
		RemoveAppPoolByName(name);
	}

	public async Task Handle(IISScannerRequest request, CancellationToken cancellationToken){
		Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
		IEnumerable<UnregisteredSite> unregSites = FindUnregisteredCreatioSites(_settingsRepository);

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

	/// <summary>
	/// </summary>
	/// <param name="name">Site name in IIS</param>
	/// <param name="state">
	///  State of IIS site
	///  <list type="bullet">
	///   <item>Started: when IIS site Started</item>
	///   <item>Stopped: when IIS site Started</item>
	///  </list>
	/// </param>
	/// <param name="binding"></param>
	/// <param name="path">Site directory path</param>
	internal sealed record SiteBinding(string name, string state, string binding, string path) { }

	internal sealed record UnregisteredSite(SiteBinding siteBinding, IList<Uri> Uris, SiteType siteType) { }
	internal sealed record RegisteredSite(SiteBinding siteBinding, IList<Uri> Uris, SiteType siteType) { }

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
