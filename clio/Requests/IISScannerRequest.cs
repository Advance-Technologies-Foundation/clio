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

public class IISScannerRequest : IExternalLink
{
    public string Content { get; set; }
}

internal class AllUnregisteredSitesRequest : IRequest
{
    public Action<IEnumerable<IISScannerHandler.UnregisteredSite>> Callback;
}

internal class AllRegisteredSitesRequest : IRequest
{
    public Action<IEnumerable<IISScannerHandler.RegisteredSite>> Callback;
}

internal class StopInstanceByNameRequest : IRequest
{
    public string SiteName { get; set; }
}

internal class DeleteInstanceByNameRequest : IRequest
{
    public string SiteName { get; set; }
}

/// <summary>
///  Finds path to appSetting.json.
/// </summary>
/// <remarks>
///  Handles extenral link requests.
///  <example>
///   <code>clio externalLink clio://IISScannerRequest</code>
///  </example>
/// </remarks>
/// <example>
/// </example>
internal class IISScannerHandler(ISettingsRepository settingsRepository, RegAppCommand regCommand,
    PowerShellFactory powerShellFactory, ILogger logger): BaseExternalLinkHandler, IRequestHandler<IISScannerRequest>,
    IRequestHandler<AllUnregisteredSitesRequest>, IRequestHandler<DeleteInstanceByNameRequest>,
    IRequestHandler<StopInstanceByNameRequest>, IRequestHandler<AllRegisteredSitesRequest>
{
    internal enum SiteType
    {
        NetFramework,
        Core,
        NotCreatioSite
    }



    /// <summary>
    ///  Finds Creatio Sites in IIS that are not registered with clio.
    /// </summary>
    private static readonly Func<ISettingsRepository, IEnumerable<UnregisteredSite>> FindUnregisteredCreatioSites =
        settingsRepository =>
        {
            return GetBindings().Where(site =>
                {
                    bool isRegisteredEnvironment = false;
                    ConvertBindingToUri(site.binding).ForEach(uri =>
                    {
                        string key = settingsRepository.FindEnvironmentNameByUri(uri.ToString());
                        if (!string.IsNullOrEmpty(key) && !isRegisteredEnvironment)
                        {
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
    ///  Executes appcmd.exe with arguments and captures output.
    /// </summary>
    private static readonly Func<string, string> _appcmd = args =>
    {
        const string dirPath = @"C:\Windows\System32\inetsrv\";
        const string exeName = "appcmd.exe";
        return Process.Start(new ProcessStartInfo
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            Arguments = args,
            FileName = Path.Join(dirPath, exeName)
        })?.StandardOutput.ReadToEnd();
    };

    private static readonly Action<string> StopAppPoolByName = name =>
    {
        _appcmd($"stop apppool /apppool.name:{name}");
    };

    private static readonly Action<string> StopSiteByName = name => { _appcmd($"stop site /site.name:{name}"); };

    private static readonly Action<string> RemoveSiteByName = name => { _appcmd($"delete site /site.name:{name}"); };

    private static readonly Action<string> RemoveAppPoolByName = name =>
    {
        _appcmd($"delete apppool /apppool.name:{name}");
    };

    /// <summary>
    ///  Finds Creatio Sites in IIS that are not registered with clio.
    /// </summary>
    internal static readonly Func<IEnumerable<UnregisteredSite>> FindAllCreatioSites = () =>
    {
        return GetBindings()
            .Where(site => DetectSiteType(site.path) != SiteType.NotCreatioSite)
            .Select(site => new UnregisteredSite(
                site,
                ConvertBindingToUri(site.binding),
                DetectSiteType(site.path)));
    };

    /// <summary>
    ///  Finds Creatio Sites in IIS that are not registered with clio.
    /// </summary>
    internal static readonly Func<IEnumerable<RegisteredSite>> FindAllRegisteredCreatioSites = () =>
    {
        return GetBindings()
            .Where(site => DetectSiteType(site.path) != SiteType.NotCreatioSite)
            .Select(site => new RegisteredSite(
                site,
                ConvertBindingToUri(site.binding),
                DetectSiteType(site.path)));
    };

    /// <summary>
    ///  Gets data from appcmd.exe.
    /// </summary>
    private static readonly Func<IEnumerable<SiteBinding>> GetBindings = () =>
    {
        return XElement.Parse(_appcmd("list sites /xml"))
            .Elements("SITE")
            .Select(site => GetSiteBindingFromXmlElement(site));
    };

    /// <summary>
    ///  Splits IIS Binding into list.
    /// </summary>
    private static readonly Func<string, List<string>> SplitBinding = binding =>
        binding.Contains(',') ? binding.Split(',').ToList() : [binding];

    /// <summary>
    ///  Splits IIS Binding.
    /// </summary>
    private static readonly Func<string, List<Uri>> ConvertBindingToUri = binding =>
    {
        List<Uri> result = [];

        // "http/*:7080:localhost,http/*:7080:kkrylovn
        SplitBinding(binding).ForEach(item =>
        {
            // http/*:7080:localhost
            // http/*:80:
            string[] items = item.Split(':');
            if (items.Length >= 3)
            {
                string hostName = string.IsNullOrEmpty(items[2]) ? "localhost" : items[2];
                string port = items[1];
                string other = items[0];
                string protocol = other.Replace("/*", string.Empty);
                string url = $"{protocol}://{hostName}:{port}";

                if (Uri.TryCreate(url, UriKind.Absolute, out Uri value))
                {
                    result.Add(value);
                }
            }
        });
        return result;
    };

    /// <summary>
    ///  Converts XElement to Sitebinding.
    /// </summary>
    private static readonly Func<XElement, SiteBinding> GetSiteBindingFromXmlElement = xmlElement => new SiteBinding(
        xmlElement.Attribute("SITE.NAME")?.Value,
        xmlElement.Attribute("state")?.Value,
        xmlElement.Attribute("bindings")?.Value,
        _appcmd($"list VDIR {xmlElement.Attribute("SITE.NAME").Value}/ /text:physicalPath").Trim());

    /// <summary>
    ///  Detect Site Type.
    /// </summary>
    private static readonly Func<string, SiteType> DetectSiteType = path =>
    {
        string webapp = Path.Join(path, "Terrasoft.WebApp");
        string configuration = Path.Join(path, "Terrasoft.Configuration");

        if (new DirectoryInfo(webapp).Exists)
        {
            return SiteType.NetFramework;
        }

        if (new DirectoryInfo(configuration).Exists)
        {
            return SiteType.Core;
        }

        return SiteType.NotCreatioSite;
    };

    private readonly ISettingsRepository _settingsRepository = settingsRepository;
    private readonly RegAppCommand _regCommand = regCommand;
    private readonly PowerShellFactory _powerShellFactory = powerShellFactory;
    private readonly ILogger _logger = logger;



    /// <summary>
    ///  Gets IIS Sites that are physically located in **/Terrasoft.WebApp folder from remote host.
    /// </summary>
    public static readonly Func<IPowerShellFactory, Dictionary<string, Uri>> GetSites = psf =>
    {
        Dictionary<string, Uri> result = [];

        Collection<Site> sites = psf.GetInstance().AddCommand("Get-WebSite").Invoke<Site>();
        Collection<WebApp> webApps = psf.GetInstance().AddCommand("Get-WebApplication").Invoke<WebApp>();

        webApps.Where(webApp => webApp.Path.EndsWith("/0") && webApp.PhysicalPath.EndsWith("Terrasoft.WebApp"))
            .ToList()
            .ForEach(webApp =>
            {
                ConsoleLogger.Instance.WriteInfo(webApp.SiteName);
                (string name, List<Uri> uris) = sites.Where(site =>
                        site.Name == webApp.SiteName && site.Id == webApp.SiteId)
                    .Select(site => (site.Name, site.Uris)).FirstOrDefault();

                string newPath = webApp.Path.Substring(0, webApp.Path.Length - 2);
                Uri rootUri = psf.ComputerName == "localhost"
                    ? uris.FirstOrDefault()
                    : uris.FirstOrDefault(u => u.Host != "localhost");
                if (Uri.TryCreate(rootUri, newPath, out Uri value))
                {
                    result.Add(name + newPath, value);
                }
            });
        return result;
    };

    public async Task Handle(AllUnregisteredSitesRequest request, CancellationToken cancellationToken)
    {
        IEnumerable<UnregisteredSite> sites = FindAllCreatioSites();
        request.Callback(sites);
    }

    public async Task Handle(AllRegisteredSitesRequest request, CancellationToken cancellationToken)
    {
        IEnumerable<RegisteredSite> sites = FindAllRegisteredCreatioSites();
        request.Callback(sites);
    }

    public async Task Handle(StopInstanceByNameRequest request, CancellationToken cancellationToken)
    {
        string name = request.SiteName;
        StopSiteByName(name);
        StopAppPoolByName(name);
    }

    public async Task Handle(DeleteInstanceByNameRequest request, CancellationToken cancellationToken)
    {
        string name = request.SiteName;
        RemoveSiteByName(name);
        RemoveAppPoolByName(name);
    }

    public async Task Handle(IISScannerRequest request, CancellationToken cancellationToken)
    {
        Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
        IEnumerable<UnregisteredSite> unregSites = FindUnregisteredCreatioSites(_settingsRepository);

        string r = ClioParams?["return"];
        if (r == "remote")
        {
            // clio://IISScannerRequest/?returnremote&host=localhost;
            string computerName = ClioParams?["host"];

            // clio externalLink clio://IISScannerRequest/?return=remote&host=localhost&username=1234&password=5678;
            string userName = ClioParams?["username"];
            string password = ClioParams?["password"];
            _powerShellFactory.Initialize(userName, password, computerName);

            int i = 1;

            GetSites(_powerShellFactory)?.ToList().ForEach(async site =>
            {
                _logger.WriteInfo($"({i++}) {site.Key} - {site.Value}");
            });

            // Here I would call regApp command but instead I will write total
            _logger.WriteInfo($"**** TOTAL: {i - 1} new sites ****");
        }

        if (r == "count")
        {
            _logger.WriteInfo(unregSites.Count().ToString());
        }

        if (r == "details")
        {
            string json = JsonSerializer.Serialize(unregSites);
            _logger.WriteInfo(json);
        }

        if (r == "registerAll")
        {
            unregSites.ToList().ForEach((Action<UnregisteredSite>)(site =>
            {
                _regCommand.Execute(new RegAppOptions
                {
                    IsNetCore = site.siteType == SiteType.Core,
                    Uri = Enumerable.FirstOrDefault<Uri>(site.Uris).ToString(),
                    EnvironmentName = site.siteBinding.name,
                    Login = "Supervisor",
                    Password = "Supervisor",
                    Maintainer = "Customer",
                    CheckLogin = false
                });
            }));
        }
    }


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
    internal sealed record SiteBinding(string name, string state, string binding, string path)
    {
    }

    internal sealed record UnregisteredSite(SiteBinding siteBinding, IList<Uri> uris, SiteType siteType)
    {
    }

    internal sealed record RegisteredSite(SiteBinding siteBinding, IList<Uri> uris, SiteType siteType)
    {
    }
}

public record Site
{

    /// <summary>
    ///  Converts PSObject to Site.
    /// </summary>
    private static readonly Func<PSObject, Site> _asSite = psObject =>
    {
        return new Site
        {
            Name = psObject.Properties["Name"].Value as string,
            PhysicalPath = psObject.Properties["PhysicalPath"].ToString(),
            Id = (long)psObject.Properties["Id"].Value,
            EnabledProtocols = psObject.Properties["EnabledProtocols"].ToString(),
            Binding = (psObject.Properties["Bindings"].Value as PSObject).Properties
                .FirstOrDefault(p => p.Name == "Collection")?.Value.ToString()
        };
    };

    /// <summary>
    ///  Splits IIS Binding.
    /// </summary>
    private static readonly Func<string, List<Uri>> ConvertBindingToUri = binding =>
    {
        binding = binding.Replace(" *", "/*").Replace(" ", ",");

        List<Uri> result = [];

        // "http/*:7080:localhost,http/*:7080:kkrylovn
        SplitBinding(binding).ForEach(item =>
        {
            // http/*:7080:localhost
            // http/*:80:
            string[] items = item.Split(':');
            if (items.Length >= 3)
            {
                string hostName = string.IsNullOrEmpty(items[2]) ? "localhost" : items[2];
                string port = items[1];
                string other = items[0];
                string protocol = other.Replace("/*", string.Empty);
                string url = $"{protocol}://{hostName}:{port}";

                if (Uri.TryCreate(url, UriKind.Absolute, out Uri value))
                {
                    result.Add(value);
                }
            }
        });
        return result;
    };

    /// <summary>
    ///  Splits IIS Binding into list.
    /// </summary>
    private static readonly Func<string, List<string>> SplitBinding = binding =>
        binding.Contains(',') ? binding.Split(',').ToList() : [binding];

    public string Binding { get; private set; }

    /// <summary>
    ///  Gets or sets iIS EnabledProtocols.
    /// </summary>
    public string EnabledProtocols { get; set; }

    /// <summary>
    ///  Gets iIS Site Id.
    /// </summary>
    public long Id { get; private set; }

    /// <summary>
    ///  Gets iIS Site name.
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    ///  Gets iIS physical path.
    /// </summary>
    public string PhysicalPath { get; private set; }

    public List<Uri> Uris => ConvertBindingToUri(Binding);

    public static implicit operator Site(PSObject obj) => _asSite(obj);
}

public partial record WebApp
{
    private const string Regex = "(@name=')(.*?)'\\sand\\s@id='(\\d*?)'";
    private static readonly Func<PSObject, WebApp> AsWebApp = psObject =>
    {
        string itemXPath = psObject.Properties["ItemXPath"]?.Value as string ?? string.Empty;
        GroupCollection groups = MyRegex().Match(itemXPath).Groups;

        return new WebApp
        {
            ElementTagName = psObject.Properties["ElementTagName"]?.Value as string ?? string.Empty,
            Path = psObject.Properties["Path"]?.Value as string ?? string.Empty,
            EnabledProtocols = psObject.Properties["EnabledProtocols"]?.Value as string ?? string.Empty,
            PhysicalPath = psObject.Properties["PhysicalPath"]?.Value as string ?? string.Empty,
            ItemXPath = itemXPath,
            SiteName = groups[2].Value.Trim(),
            SiteId = long.TryParse(groups[3].Value?.Trim(), out long v) ? v : -1
        };
    };

    public string ElementTagName { get; private set; }

    public string EnabledProtocols { get; private set; }

    public string ItemXPath { get; private set; }

    public string Path { get; private set; }

    public string PhysicalPath { get; private set; }

    public long SiteId { get; private set; }

    public string SiteName { get; private set; }

    public static implicit operator WebApp(PSObject obj) => AsWebApp(obj);

    [GeneratedRegex(Regex)]
    private static partial Regex MyRegex();
}
