using Clio.UserEnvironment;
using MediatR;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Clio.Requests
{
	public class IISScannerRequest : IExtenalLink
	{
		public string Content
		{
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

		public IISScannerHandler(ISettingsRepository settingsRepository)
		{
			_settingsRepository = settingsRepository;
		}

		public async Task<Unit> Handle(IISScannerRequest request, CancellationToken cancellationToken)
		{
			Uri.TryCreate(request.Content, UriKind.Absolute, out _clioUri);

			IList<UnregisteredSite> unregSites = await FindUnregisteredSites();

			var r = ClioParams?["return"];
			if (r == "count")
			{
				Console.WriteLine(unregSites.Count);
			}
			if (r == "details")
			{
				var json = JsonSerializer.Serialize(unregSites);
				Console.WriteLine(json);
			}
			return new Unit();
		}


		/// <summary>
		/// Look inside fileSystem to detect if IIS site is Creatio
		/// </summary>
		/// <param name="path">Path to IIS site</param>
		/// <returns></returns>
		private SiteType GetSiteType(string path)
		{
			//C:\inetpub\wwwroot\bundle806
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
		}

		/// <summary>
		/// Compares registed environments with IIS sites
		/// </summary>
		/// <returns>IIS sites that are not registered as clio environment</returns>
		private async Task<IList<UnregisteredSite>> FindUnregisteredSites()
		{
			var bindings = await GetBindings();
			IList<UnregisteredSite> result = new List<UnregisteredSite>();
			foreach (SiteBinding siteBinding in bindings)
			{
				IList<Uri> uris = ConvertBindingToUri(siteBinding.binding);
				bool isRegisteredEnvironment = false;

				foreach (Uri uri in uris)
				{
					var key = _settingsRepository.FindEnvironmentNameByUri(uri.ToString());
					if (!string.IsNullOrEmpty(key))
					{
						isRegisteredEnvironment = true;
						break;
					}
				}

				SiteType siteType = GetSiteType(siteBinding.path);
				if (!isRegisteredEnvironment && siteType != SiteType.NotCreatioSite)
				{
					result.Add(new UnregisteredSite(siteBinding, uris, siteType));
				}
			}
			return result;
		}

		/// <summary>
		/// Gets data from AppCmd.exe
		/// </summary>
		/// <returns>Parsed collection of SiteBinding</returns>
		private async Task<IList<SiteBinding>> GetBindings()
		{
			string consoleOutput = await ExecuteAppCmdCommand("list sites /xml");
			var xml = XElement.Parse(consoleOutput);
			var sites = xml.Elements("SITE").ToList();

			List<SiteBinding> sitesBindings = new List<SiteBinding>();
			foreach (var site in sites)
			{
				string name = site.Attribute("SITE.NAME")?.Value;
				string state = site.Attribute("state")?.Value;
				string binding = site.Attribute("bindings")?.Value;
				string path = await ExecuteAppCmdCommand($"list VDIR {name}/ /text:physicalPath");

				if (name is not null && state is not null && binding is not null && !string.IsNullOrWhiteSpace(path))
				{
					sitesBindings.Add(new SiteBinding(name, state, binding, path.Trim()));
				}
			}
			return sitesBindings;
		}

		/// <summary>
		/// Executes appcmd.exe with arguments and captures output
		/// </summary>
		/// <param name="args">commanda rguments</param>
		/// <returns></returns>
		/// <exception>
		/// </exception>
		/// <inheritdoc cref="Process.Start(ProcessStartInfo)" />
		/// <remarks>
		/// See <see href="https://learn.microsoft.com/en-us/iis/get-started/getting-started-with-iis/getting-started-with-appcmdexe">Getting Started with AppCmd.exe</see>
		/// </remarks>
		private Task<string> ExecuteAppCmdCommand(string args)
		{
			ProcessStartInfo psi = new()
			{
				RedirectStandardError = true,
				RedirectStandardOutput = true,
				UseShellExecute = false,
				Arguments = args,
				WorkingDirectory = "C:\\Windows\\System32\\inetsrv\\",
				FileName = "C:\\Windows\\System32\\inetsrv\\appcmd.exe",
				CreateNoWindow = false
			};

			Process process = Process.Start(psi);
			process.WaitForExit();
			return process.StandardOutput.ReadToEndAsync();
		}


		/// <summary>
		/// Convert IIS binding format to list of Uri
		/// </summary>
		/// <param name="binding">string to convert</param>
		/// <returns>Parsed collection of Uri</returns>
		private IList<Uri> ConvertBindingToUri(string binding)
		{
			List<string> internalStrings = new();

			//"http/*:7080:localhost,http/*:7080:kkrylovn
			if (binding.Contains(','))
			{
				string[] items = binding.Split(',');
				internalStrings.AddRange(items);
			}
			else
			{
				internalStrings.Add(binding);
			}


			IList<Uri> result = new List<Uri>();
			foreach (string item in internalStrings)
			{
				//http/*:7080:localhost
				//http/*:80:
				var items = item.Split(':');
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
			return result;
		}

		private sealed record SiteBinding(string name, string state, string binding, string path)
		{
		}

		private sealed record UnregisteredSite(SiteBinding siteBinding, IList<Uri> Uris, SiteType siteType)
		{
		}

		private enum SiteType
		{
			NetFramework,
			Core,
			NotCreatioSite
		}

	}
}
