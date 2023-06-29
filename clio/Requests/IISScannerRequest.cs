using Clio.Command;
using Clio.UserEnvironment;
using Clio.Utilities;
using MediatR;
using Microsoft.CodeAnalysis;
using OneOf;
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

namespace Clio.Requests
{
	public class IISScannerRequest : IExtenalLink
	{
		public string Content {
			get; set;
		}
	}

	/// <summary>
	/// Finds path to appSetting.json
	/// </summary>
	/// <remarks>
	/// Handles extenral link requests
	/// <example><code>clio externalLink clio://IISScannerRequest</code></example>
	/// </remarks>
	/// <example>
	/// </example>
	internal class IISScannerHandler : BaseExternalLinkHandler, IRequestHandler<IISScannerRequest>
	{
		private readonly ISettingsRepository _settingsRepository;
		private readonly RegAppCommand _regCommand;
		private readonly PowerShellFactory _powerShellFactory;


		public IISScannerHandler(ISettingsRepository settingsRepository, RegAppCommand regCommand, PowerShellFactory powerShellFactory)
		{
			_settingsRepository = settingsRepository;
			_regCommand = regCommand;
			_powerShellFactory = powerShellFactory;
		}

		public async Task Handle(IISScannerRequest request, CancellationToken cancellationToken)
		{
			Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);
			IEnumerable<UnregisteredSite> unregSites = _findUnregisteredCreatioSites(_settingsRepository);

			var r = ClioParams?["return"];
			if (r == "remote")
			{
				//clio://IISScannerRequest/?returnremote&host=localhost;
				string computername = ClioParams?["host"];

				//clio externalLink clio://IISScannerRequest/?return=remote&host=localhost&username=1234&password=5678;
				string userName = ClioParams?["username"];
				string password = ClioParams?["password"];
				_powerShellFactory.Initialize(userName, password, computername);

				int i = 1;

				getSites(_powerShellFactory)?.ToList().ForEach(async site =>
				{
					Console.WriteLine($"({i++}) {site.Key} - {site.Value}");
				});

				//Here I would call regApp command but instead I will write total
				Console.WriteLine($"**** TOTAL: {i - 1} new sites ****");
			}
			if (r == "count")
			{
				Console.WriteLine(unregSites.Count());
			}
			if (r == "details")
			{
				var json = JsonSerializer.Serialize(unregSites);
				Console.WriteLine(json);
			}
			if (r == "registerAll")
			{
				unregSites.ToList().ForEach(site =>
				{
					_regCommand.Execute(new RegAppOptions
					{
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

		/// <summary>
		/// Finds Creatio Sites in IIS that are not registered with clio
		/// </summary>
		private static readonly Func<ISettingsRepository, IEnumerable<UnregisteredSite>> _findUnregisteredCreatioSites = (_settingsRepository) =>
		{
			return _getBindings().Where(site =>
			{
				bool isRegisteredEnvironment = false;
				_convertBindingToUri(site.binding).ForEach(uri =>
				{
					var key = _settingsRepository.FindEnvironmentNameByUri(uri.ToString());
					if (!string.IsNullOrEmpty(key) && !isRegisteredEnvironment)
					{
						isRegisteredEnvironment = true;
					}
				});
				return !isRegisteredEnvironment;
			})
			.Where(site => _detectSiteType(site.path) != SiteType.NotCreatioSite)
			.Select(site =>
			{
				return new UnregisteredSite(
					siteBinding: site,
					Uris: _convertBindingToUri(site.binding),
					siteType: _detectSiteType(site.path));
			});
		};

		/// <summary>
		/// Finds Creatio Sites in IIS that are not registered with clio
		/// </summary>
		internal static readonly Func<IEnumerable<UnregisteredSite>> _findAllCreatioSites = () =>
		{
			return _getBindings()
			.Where(site => _detectSiteType(site.path) != SiteType.NotCreatioSite)
			.Select(site =>
			{
				return new UnregisteredSite(
					siteBinding: site,
					Uris: _convertBindingToUri(site.binding),
					siteType: _detectSiteType(site.path));
			});
		};

		/// <summary>
		/// Executes appcmd.exe with arguments and captures output
		/// </summary>
		private static readonly Func<string, string> _appcmd = (args) =>
		{
			const string dirPath = "C:\\Windows\\System32\\inetsrv\\";
			const string exeName = "appcmd.exe";
			return Process.Start(new ProcessStartInfo
			{
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				Arguments = args,
				FileName = Path.Join(dirPath, exeName),
			}).StandardOutput.ReadToEnd();
		};

		/// <summary>
		/// Gets IIS Sites that are physically located in **/Terrasoft.WebApp folder from remote host
		/// </summary>

		public static readonly Func<IPowerShellFactory, Dictionary<string, Uri>> getSites = (psf) =>
		{
			Dictionary<string, Uri> result = new();

			var sites = psf.GetInstance().AddCommand("Get-WebSite").Invoke<Site>();
			var webApps = psf.GetInstance().AddCommand("Get-WebApplication").Invoke<WebApp>();

			webApps.Where(webApp => webApp.Path.EndsWith("/0") && webApp.PhysicalPath.EndsWith("Terrasoft.WebApp"))
			.ToList()
			.ForEach(webApp =>
			{

				Console.WriteLine(webApp.SiteName);
				var rootSite = sites.Where(site =>
					site.Name == webApp.SiteName && site.Id == webApp.SiteId)
				.Select(site => (site.Name, site.Uris)).FirstOrDefault();

				string newPath = webApp.Path.Substring(0, webApp.Path.Length - 2);
				var rootUri = (psf.ComputerName == "localhost") ? rootSite.Uris.FirstOrDefault() : rootSite.Uris.FirstOrDefault(u => u.Host != "localhost");
				if (Uri.TryCreate(rootUri, newPath, out Uri value))
				{
					result.Add(rootSite.Name + newPath, value);
				}
			});
			return result;
		};



		private static Func<OneOf<Collection<PSObject>, Exception>, Collection<PSObject>> getValue = (oneOf) =>
		{
			if (oneOf.Value is Exception ex)
			{
				Console.Write(ex.Message);
				return default;
			}
			return (oneOf.Value as Collection<PSObject>);
		};

		/// <summary>
		/// Gets data from appcmd.exe
		/// </summary>
		private static readonly Func<IEnumerable<SiteBinding>> _getBindings = () =>
		{
			return XElement.Parse(_appcmd("list sites /xml"))
				.Elements("SITE")
				.Select(site => _getSiteBindingFromXmlElement(site));
		};

		/// <summary>
		/// Splits IIS Binding into list
		/// </summary>
		private static readonly Func<string, List<string>> _splitBinding = (binding) =>
		{
			if (binding.Contains(','))
			{
				return binding.Split(',').ToList();
			}
			else
			{
				return new List<string> { binding };
			};
		};

		/// <summary>
		/// Splits IIS Binding
		/// </summary>
		private static readonly Func<string, List<Uri>> _convertBindingToUri = (binding) =>
		{
			//List<string> internalStrings = new();
			List<Uri> result = new();

			//"http/*:7080:localhost,http/*:7080:kkrylovn
			_splitBinding(binding).ForEach(item =>
			{
				//http/*:7080:localhost
				//http/*:80:
				var items = item.Split(':');
				if (items.Length >= 3)
				{
					string hostName = (string.IsNullOrEmpty(items[2])) ? "localhost" : items[2];
					string port = items[1];
					string other = items[0];
					string protocol = other.Replace("/*", "");
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
		/// Converts XElement to Sitebinding
		/// </summary>
		private static readonly Func<XElement, SiteBinding> _getSiteBindingFromXmlElement = (xmlElement) =>
		{
			return new SiteBinding(
				name: xmlElement.Attribute("SITE.NAME")?.Value,
				state: xmlElement.Attribute("state")?.Value,
				binding: xmlElement.Attribute("bindings")?.Value,
				path: _appcmd($"list VDIR {xmlElement.Attribute("SITE.NAME").Value}/ /text:physicalPath").Trim()
			);
		};

		/// <summary>
		/// Detect Site Type
		/// </summary>
		private static Func<string, SiteType> _detectSiteType = (path) =>
		{
			var webapp = Path.Join(path, "Terrasoft.WebApp");
			var configuration = Path.Join(path, "Terrasoft.Configuration");

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

		internal sealed record SiteBinding(string name, string state, string binding, string path)
		{
		}

		internal sealed record UnregisteredSite(SiteBinding siteBinding, IList<Uri> Uris, SiteType siteType)
		{
		}

		internal enum SiteType
		{
			NetFramework,
			Core,
			NotCreatioSite
		}

		private sealed record WebAppDto(string ElementTagName, string path, string enabledProtocols, string PhysicalPath, string ItemXPath);

		private sealed record WebSiteDto(string name, int id, Bindings bindings);

		private sealed record Bindings(string Collection);

	}

	public record Site
	{
		/// <summary>
		/// IIS Site name
		/// </summary>
		public string Name { get; private set; }


		/// <summary>
		/// IIS physical path
		/// </summary>
		public string PhysicalPath { get; private set; }

		/// <summary>
		/// IIS Site Id
		/// </summary>
		public long Id { get; private set; }

		/// <summary>
		/// IIS EnabledProtocols
		/// </summary>
		public string EnabledProtocols { get; set; }

		public string Binding { get; private set; }

		public List<Uri> Uris => _convertBindingToUri(Binding);

		public static implicit operator Site(PSObject obj) => _asSite(obj);


		/// <summary>
		/// Converts PSObject to Site
		/// </summary>
		private static Func<PSObject, Site> _asSite = (psObject) =>
		{
			return new Site()
			{
				Name = psObject.Properties["Name"].Value as string,
				PhysicalPath = psObject.Properties["PhysicalPath"].ToString(),
				Id = (long)psObject.Properties["Id"].Value,
				EnabledProtocols = psObject.Properties["EnabledProtocols"].ToString(),
				Binding = (psObject.Properties["Bindings"].Value as PSObject).
						Properties.FirstOrDefault(p => p.Name == "Collection")?.Value.ToString()
			};
		};

		/// <summary>
		/// Splits IIS Binding
		/// </summary>
		private static readonly Func<string, List<Uri>> _convertBindingToUri = (binding) =>
		{

			binding = binding.Replace(" *", "/*").Replace(" ", ",");

			//List<string> internalStrings = new();
			List<Uri> result = new();

			//"http/*:7080:localhost,http/*:7080:kkrylovn
			_splitBinding(binding).ForEach(item =>
			{
				//http/*:7080:localhost
				//http/*:80:
				var items = item.Split(':');
				if (items.Length >= 3)
				{
					string hostName = (string.IsNullOrEmpty(items[2])) ? "localhost" : items[2];
					string port = items[1];
					string other = items[0];
					string protocol = other.Replace("/*", "");
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
		/// Splits IIS Binding into list
		/// </summary>
		private static readonly Func<string, List<string>> _splitBinding = (binding) =>
		{
			if (binding.Contains(','))
			{
				return binding.Split(',').ToList();
			}
			else
			{
				return new List<string> { binding };
			};
		};
	}

	public record WebApp
	{

		private const string _regex = "(@name=')(.*?)'\\sand\\s@id='(\\d*?)'";
		public string ElementTagName { get; private set; }
		public string Path { get; private set; }
		public string EnabledProtocols { get; private set; }
		public string PhysicalPath { get; private set; }
		public string ItemXPath { get; private set; }
		public string SiteName { get; private set; }
		public long SiteId { get; private set; }

		public static implicit operator WebApp(PSObject obj) => _asWebApp(obj);
		private static Func<PSObject, WebApp> _asWebApp = (psObject) =>
		{
			var itemXPath = psObject.Properties["ItemXPath"]?.Value as string ?? string.Empty;
			var groups = Regex.Match(itemXPath, _regex).Groups;

			return new WebApp()
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
	}
}
