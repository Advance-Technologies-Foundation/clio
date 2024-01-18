using System;
using System.Linq;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Command;
using Clio.Common;
using CommandLine;
using CreatioModel;
using Newtonsoft.Json;

namespace Clio;

[Verb("set-webservice-url", Aliases = new string[] { "swu","webservice" }, HelpText = "Set base url for web service")]
public class SetWebServiceUrlOptions : EnvironmentOptions
{

	[Value(0, MetaName = "WebServiceName", Required = true, HelpText = "Web service name")]
	public string WebServiceName { get; set; }
	
	[Value(1, MetaName= "baseurl", Required = true, HelpText = "Base url of a web service")]
	public string WebServiceUrl { get; set; }

}

public class SetWebServiceUrlCommand : RemoteCommand<SetWebServiceUrlOptions>
{

	private readonly IDataProvider _provider;

	protected override string ServicePath => "/DataService/json/SyncReply/SetUserPropertyRequest";
	public SetWebServiceUrlCommand(IDataProvider provider, IApplicationClient applicationClient, EnvironmentSettings environmentSettings) : base(applicationClient, environmentSettings){
		_provider = provider;
	}

	public SetWebServiceUrlCommand(IDataProvider provider, EnvironmentSettings environmentSettings) : base(environmentSettings){
		_provider = provider;
	}

	
	protected override string GetRequestData(SetWebServiceUrlOptions options) {
		
		const string managerName = "ServiceSchemaManager";
		var payload = new SetWebServiceUrlPayload {
			contractName = "SetUserPropertyRequest",
			schemaId = GetSchemaIdBySchemaName(options.WebServiceName, managerName),
			propertyName = "BaseUri",
			propertyValue = options.WebServiceUrl,
			managerName = managerName
		};
		var str =  JsonConvert.SerializeObject(payload);
		return str;
	}

	private Guid GetSchemaIdBySchemaName(string optionsWebServiceName, string managerName){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_provider);
		return ctx
			.Models<SysSchema>()
			.FirstOrDefault(s => s.Name == optionsWebServiceName && s.ManagerName == managerName)?.Id ?? Guid.Empty;
	}

}
public record SetWebServiceUrlPayload
{
	public string contractName {get; init;}
    public Guid schemaId {get; init;}
    public string propertyName {get; init;}
    public string propertyValue {get; init;}
    public string managerName {get; init;}

}


