using System;
using System.Collections.Generic;
using CommandLine;

namespace Clio.Command.CreatioInstallCommand;

[Verb("deploy-creatio", Aliases = ["dc", "ic", "install-creation"], HelpText = "Deploy Creatio from zip file")]
public class PfInstallerOptions : EnvironmentNameOptions
{

	#region Fields: Private

	private readonly Dictionary<string, string> _productList = new() {
		{"s", "Studio"},
		{"semse", "SalesEnterprise_Marketing_ServiceEnterprise"},
		{"bcj", "BankSales_BankCustomerJourney_Lending_Marketing"}
	};

	private CreatioDBType _dbType;
	private CreatioRuntimePlatform _platform;

	#endregion

	#region Properties: Internal

	internal CreatioDBType DBType {
		get {
			if (DB.ToLower() == "pg") {
				return CreatioDBType.PostgreSQL;
			}
			if (DB.ToLower() == "mssql") {
				return CreatioDBType.MSSQL;
			}
			return _dbType;
		}
		set { _dbType = value; }
	}

	internal CreatioRuntimePlatform RuntimePlatform {
		get {
			if (Platform.ToLower() == "net6") {
				return CreatioRuntimePlatform.NET6;
			}
			if (Platform.ToLower() == "netframework" || Platform.ToLower() == "nf") {
				return CreatioRuntimePlatform.NETFramework;
			}
			return _platform;
		}
		set { _platform = value; }
	}

	#endregion

	#region Properties: Public

	[Option("db", Required = false, HelpText = "DB type: pg|mssql")]
	public string DB { get; set; }

	[Option("platform", Required = false, HelpText = "Runtime platform: net6|netframework")]
	public string Platform { get; set; }

	public string Product {
		get {
			if (_productList.ContainsKey(ProductKey ?? string.Empty)) {
				return _productList[ProductKey];
			}
			return ProductKey;
		}
		set { ProductKey = value; }
	}

	[Option("product", Required = false, HelpText = "Product name")]
	public string ProductKey { get; set; }

	[Option("SiteName", Required = false, HelpText = "SiteName")]
	public string SiteName { get; set; }

	[Option("SitePort", Required = false, HelpText = "Site port")]
	public int SitePort { get; set; }

	[Option("ZipFile", Required = false, HelpText = "Sets Zip File path")]
	public string ZipFile { get; set; }

	[Option("deployment", Required = false, Default = "auto", HelpText = "Deployment method: auto|iis|dotnet")]
	public string DeploymentMethod { get; set; }

	[Option("no-iis", Required = false, Default = false, HelpText = "Don't use IIS on Windows (use dotnet run instead)")]
	public bool NoIIS { get; set; }

	[Option("app-path", Required = false, HelpText = "Application installation path")]
	public string AppPath { get; set; }

	[Option("use-https", Required = false, Default = false, HelpText = "Use HTTPS (requires certificate for dotnet)")]
	public bool UseHttps { get; set; }

	[Option("cert-path", Required = false, HelpText = "Path to SSL certificate file (.pem or .pfx)")]
	public string CertificatePath { get; set; }

	[Option("cert-password", Required = false, HelpText = "Password for SSL certificate")]
	public string CertificatePassword { get; set; }

	[Option("auto-run", Required = false, Default = true, HelpText = "Automatically run application after deployment")]
	public bool AutoRun { get; set; }

	#endregion

}

public class InstallerCommand : Command<PfInstallerOptions>
{

	#region Fields: Private

	private readonly ICreatioInstallerService _creatioInstallerService;

	#endregion

	#region Constructors: Public

	public InstallerCommand(ICreatioInstallerService creatioInstallerService){
		_creatioInstallerService = creatioInstallerService;
	}

	#endregion

	#region Methods: Public

	public override int Execute(PfInstallerOptions options){
		int result = _creatioInstallerService.Execute(options);
		if (!options.IsSilent) {
			//_creatioInstallerService.StartWebBrowser(options);
			Console.WriteLine("Press enter to exit...");
			Console.ReadLine();
		}
		return result;
	}

	#endregion

}
