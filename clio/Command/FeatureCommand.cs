using System;
using System.Linq;
using System.Text;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;

namespace Clio.Command;

[Verb("set-feature", Aliases = new[] {"feature"}, HelpText = "Set feature state")]
public class FeatureOptions : RemoteCommandOptions {

	#region Properties: Public

	[Value(0, MetaName = "Code", Required = true, HelpText = "Feature code")]
	public string Code { get; set; }

	[Value(2, MetaName = "onlyCurrentUser", Required = false, Default = false, HelpText = "Only current user")]
	public bool OnlyCurrentUser { get; set; }

	[Value(1, MetaName = "State", Required = true, HelpText = "Feature state")]
	public int State { get; set; }

	[Option("SysAdminUnitName", Required = false, HelpText = "Name of the user for whom to set feature state for")]
	public string SysAdminUnitName { get; set; }

	[Option("UseFeatureWebService", Required = false,
		HelpText = "Use obsolete method to set feature state via feature webservice")]
	public bool UseFeatureWebService { get; set; }

	#endregion

}

public class FeatureCommand : RemoteCommand<FeatureOptions> {

	#region Fields: Private

	private readonly IDataProvider _dataProvider;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly ILogger _logger;

	#endregion

	#region Constructors: Public

	public FeatureCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IDataProvider dataProvider, IServiceUrlBuilder serviceUrlBuilder)
		: base(applicationClient, settings){
		_dataProvider = dataProvider;
		_serviceUrlBuilder = serviceUrlBuilder;
	}

	#endregion

	#region Properties: Protected

	protected override string ServicePath => @"/rest/FeatureStateService/SetFeatureState";

	#endregion

	#region Methods: Protected

	protected override string GetRequestData(FeatureOptions options){
		return "{" +
			$"\"code\":\"{options.Code}\",\"state\":\"{options.State}\",\"onlyCurrentUser\":{options.OnlyCurrentUser.ToString().ToLower()}" +
			"}";
	}

	#endregion

	#region Methods: Internal

	internal void ClearCache(string featureName){
		string base64FeatureName = Convert.ToBase64String(FileSystem.Utf8NoBom.GetBytes(featureName));
		string url
			= $"{_serviceUrlBuilder.Build(ServiceUrlBuilder.KnownRoute.ClearFeaturesCacheForAllUsers)}/{base64FeatureName}";
		string response = ApplicationClient.ExecuteGetRequest(url);
		Logger.WriteInfo($"{response}");
	}

	#endregion

	#region Methods: Public

	public override int Execute(FeatureOptions options){
		if (options.UseFeatureWebService) {
			Logger.WriteWarning("Use of UseFeatureWebService flag is not recommended");
			return base.Execute(options);
		}
		if (options.SysAdminUnitName is null) {
			SetFeatureStateDefValue(options);
		} else {
			SetFeatureStateForUser(options);
		}
		ClearCache(options.Code);
		return 0;
	}

	public AppFeature GetFeature(string featureCode){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		AppFeature feature = ctx.Models<AppFeature>().ToList().FirstOrDefault(f => f.Code == featureCode);

		if (feature is null || feature.Id == Guid.Empty) {
			feature = ctx.CreateModel<AppFeature>();
			feature.Code = featureCode;
			feature.Name = featureCode;
			ctx.Save();
		}
		return feature;
	}

	public void SaveFeatureState(AppFeature feature, Guid sysAdminUnitId, bool state){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);

		Guid? featureStateId = ctx.Models<AdminUnitFeatureState>()
								.FirstOrDefault(f => f.FeatureId == feature.Id && f.AdminUnitId == sysAdminUnitId)?.Id;

		if (featureStateId is null) {
			AppFeatureState featureState = ctx.CreateModel<AppFeatureState>();
			featureState.FeatureId = feature.Id;
			featureState.FeatureState = state;
			featureState.AdminUnitId = sysAdminUnitId;
			ctx.Save();
		} else {
			AppFeatureState featureState = ctx
											.Models<AppFeatureState>()
											.FirstOrDefault(f => f.Id == featureStateId);
			featureState.FeatureState = state;
			ctx.Save();
		}
	}

	public void SetFeatureStateDefValue(FeatureOptions options){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		AppFeature feature = ctx.Models<AppFeature>().ToList().FirstOrDefault(f => f.Code == options.Code);

		if (feature is null || feature.Id == Guid.Empty) {
			feature = ctx.CreateModel<AppFeature>();
			feature.Code = options.Code;
			feature.Name = options.Code;
		}
		feature.State = options.State == 1;
		feature.StateForCurrentUser = options.OnlyCurrentUser;
		ctx.Save();
	}

	public void SetFeatureStateForUser(FeatureOptions options){
		if (options.SysAdminUnitName is null) {
			return;
		}
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		SysAdminUnit user = ctx
							.Models<SysAdminUnit>()
							.FirstOrDefault(s => s.Name == options.SysAdminUnitName);
		if (user is null) {
			Logger.WriteWarning($"User with name {options.SysAdminUnitName} was not found");
			return;
		}
		Guid id = user.Id;
		AppFeature feature = GetFeature(options.Code);
		SaveFeatureState(feature, id, options.State == 1);
	}

	#endregion

}