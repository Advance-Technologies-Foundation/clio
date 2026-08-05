using System;
using System.Linq;
using System.Text;
using ATF.Repository;
using ATF.Repository.Providers;
using Clio.Common;
using CommandLine;
using CreatioModel;

namespace Clio.Command;

[Verb("set-feature", Aliases = ["feature"], HelpText = "Set feature state")]
public class FeatureOptions : RemoteCommandOptions {

	#region Properties: Public

	[Value(0, MetaName = "Code", Required = true, HelpText = "Feature code")]
	public string Code { get; set; }

	[Value(2, MetaName = "onlyCurrentUser", Required = false, Default = false, HelpText = "Only current user")]
	public bool OnlyCurrentUser { get; set; }

	[Value(1, MetaName = "State", Required = true, HelpText = "Feature state")]
	public int State { get; set; }

	[Option("sys-admin-unit-name", Required = false, HelpText = "Name of the user for whom to set feature state for")]
	public string SysAdminUnitName { get; set; }

	[Option("SysAdminUnitName", Required = false, Hidden = true, HelpText = "Alias for --sys-admin-unit-name")]
	public string SysAdminUnitNameAlias {
		get => SysAdminUnitName;
		set { if (!string.IsNullOrEmpty(value)) SysAdminUnitName = value; }
	}

	[Option("use-feature-web-service", Required = false,
		HelpText = "Use obsolete method to set feature state via feature webservice")]
	[RequiresPackage("cliogate", Hint = "Run 'clio install-gate -e <environment>' (or call the install-gate MCP tool) to install/update cliogate.")]
	public bool UseFeatureWebService { get; set; }

	[Option("UseFeatureWebService", Required = false, Hidden = true, HelpText = "Alias for --use-feature-web-service")]
	public bool UseFeatureWebServiceAlias {
		get => UseFeatureWebService;
		set { if (value) UseFeatureWebService = value; }
	}

	#endregion

}

public class FeatureCommand : RemoteCommand<FeatureOptions> {

	#region Fields: Private

	private readonly IDataProvider _dataProvider;
	private readonly IServiceUrlBuilder _serviceUrlBuilder;
	private readonly IFeatureStateService _featureState;

	#endregion

	#region Constructors: Public

	public FeatureCommand(IApplicationClient applicationClient, EnvironmentSettings settings,
		IDataProvider dataProvider, IServiceUrlBuilder serviceUrlBuilder, IFeatureStateService featureState)
		: base(applicationClient, settings){
		_dataProvider = dataProvider;
		_serviceUrlBuilder = serviceUrlBuilder;
		_featureState = featureState;
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
		bool applied = options.SysAdminUnitName is null
			? SetFeatureStateDefValue(options)
			: SetFeatureStateForUser(options);
		if (!applied) {
			Logger.WriteError(options.SysAdminUnitName is null
				? $"Feature '{options.Code}' state was not written."
				: $"Feature '{options.Code}' state was not written for '{options.SysAdminUnitName}'.");
		}
		ClearCache(options.Code);
		return applied ? 0 : 1;
	}

	/// <summary>
	/// Writes the feature's own default state, and for the current user when the options ask for it.
	/// </summary>
	/// <param name="options">The feature code, the state, and whether it applies to the current user only.</param>
	/// <returns>
	/// <see langword="false"/> when the platform rejects the write. A caller that must not report a state
	/// it never wrote reads this back.
	/// </returns>
	public bool SetFeatureStateDefValue(FeatureOptions options){
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		AppFeature feature = ctx.Models<AppFeature>().ToList().FirstOrDefault(f => f.Code == options.Code);

		if (feature is null || feature.Id == Guid.Empty) {
			feature = ctx.CreateModel<AppFeature>();
			feature.Code = options.Code;
			feature.Name = options.Code;
		}
		feature.State = options.State == 1;
		feature.StateForCurrentUser = options.OnlyCurrentUser;
		return ctx.Save()?.Success == true;
	}

	/// <summary>
	/// Writes the feature state for the role named in <paramref name="options"/>.
	/// </summary>
	/// <param name="options">The feature code, the state, and the role name to write it for.</param>
	/// <returns>
	/// <see langword="false"/> when no role carries that name, in which case nothing is written and the
	/// reason is logged as a warning. A caller that must not report a state it never wrote reads this back.
	/// </returns>
	public bool SetFeatureStateForUser(FeatureOptions options){
		if (options.SysAdminUnitName is null) {
			return false;
		}
		IAppDataContext ctx = AppDataContextFactory.GetAppDataContext(_dataProvider);
		SysAdminUnit user = ctx
							.Models<SysAdminUnit>()
							.FirstOrDefault(s => s.Name == options.SysAdminUnitName);
		if (user is null) {
			Logger.WriteWarning($"User with name {options.SysAdminUnitName} was not found");
			return false;
		}
		_featureState.SetFeatureState(options.Code, user.Id, options.State == 1, defineIfMissing: true);
		return true;
	}

	#endregion

}
