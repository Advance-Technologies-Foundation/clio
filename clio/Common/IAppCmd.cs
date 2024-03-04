using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Clio.Requests;

namespace Clio.Common;

public interface IAppCmd
{ }

public class AppCmd
{

	#region Constants: Private

	private const string DirPath = @"C:\Windows\System32\inetsrv\";
	private const string ExeName = "appcmd.exe";

	#endregion

	#region Fields: Private

	/// <summary>
	/// Executes appcmd.exe with arguments and captures output
	/// </summary>
	internal static Func<string, string> Appcmd = args => Process.Start(new ProcessStartInfo {
		RedirectStandardError = true,
		RedirectStandardOutput = true,
		UseShellExecute = false,
		Arguments = args,
		FileName = Path.Join(DirPath, ExeName)
	}).StandardOutput.ReadToEnd();

	private readonly IFileSystem _fileSystem;

	#endregion

	#region Constructors: Public

	public AppCmd(IFileSystem fileSystem){
		_fileSystem = fileSystem;
	}

	#endregion

	#region Methods: Private

	/// <summary>
	/// Splits IIS Binding
	/// </summary>
	private List<Uri> ConvertBindingToUri(string binding){
		//List<string> internalStrings = new();
		List<Uri> result = new();

		//"http/*:7080:localhost,http/*:7080:kkrylovn
		SplitBinding(binding).ToList().ForEach(item => {
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
	}

	/// <summary>
	/// Detect Site Type
	/// </summary>
	private IISScannerHandler.SiteType DetectSiteType(string path){
		string webapp = Path.Join(path, "Terrasoft.WebApp");
		string configuration = Path.Join(path, "Terrasoft.Configuration");

		if (_fileSystem.ExistsDirectory(webapp)) {
			return IISScannerHandler.SiteType.NetFramework;
		}
		if (_fileSystem.ExistsDirectory(configuration)) {
			return IISScannerHandler.SiteType.Core;
		}
		return IISScannerHandler.SiteType.NotCreatioSite;
	}

	/// <summary>
	/// Gets data from appcmd.exe
	/// </summary>
	private IEnumerable<IISScannerHandler.SiteBinding> GetBindings() =>
		XElement.Parse(Appcmd("list sites /xml"))
			.Elements("SITE")
			.Select(GetSiteBindingFromXmlElement);

	/// <summary>
	/// Converts XElement to Sitebinding
	/// </summary>
	private IISScannerHandler.SiteBinding GetSiteBindingFromXmlElement(XElement xmlElement) =>
		new(
			xmlElement.Attribute("SITE.NAME")?.Value,
			xmlElement.Attribute("state")?.Value,
			xmlElement.Attribute("bindings")?.Value,
			Appcmd($"list VDIR {xmlElement.Attribute("SITE.NAME").Value}/ /text:physicalPath").Trim()
		);

	private IEnumerable<string> SplitBinding(string binding) =>
		binding.Contains(',')
			? binding.Split(',')
			: new List<string> {binding};

	internal CreatioDBType GetDbTypeFromConnectionStringFile(string cnPath){
		
		var xmlFileContent = _fileSystem.ReadAllText(cnPath);
		var doc = XDocument.Parse(xmlFileContent);
		
		var cs = doc
			.Elements("connectionStrings")
			.Elements("add")
			.Where(e=> e.Attribute("name").Value=="db")?.FirstOrDefault()
			.Attribute("connectionString")?.Value;

		Regex regexMSSQL = new Regex(@"Data Source=.*?;");
		Regex regexPostgres = new Regex(@"Database=.*?;");
		
		if(regexMSSQL.IsMatch(cs)) {
			return CreatioDBType.MSSQL;
		}
		
		if(regexPostgres.IsMatch(cs)) {
			return CreatioDBType.PostgreSQL;
		}
		
		throw new Exception("Could not determine DB type");
	}
	
	
	#endregion

	
	internal IEnumerable<ScannedSite> GetAllSites() =>
		GetBindings()
			.Where(i=> _fileSystem.ExistsDirectory(i.path))
			.Select(item=> new ScannedSite(
				item.name,
				ConvertBindingToUri(item.binding),
				DetectSiteType(item.path),
				GetDbTypeFromConnectionStringFile(Path.Join(item.path, "ConnectionStrings.config"))
			));

	internal record ScannedSite(string Name, IEnumerable<Uri> Urls, IISScannerHandler.SiteType SiteType, CreatioDBType DbType);
}