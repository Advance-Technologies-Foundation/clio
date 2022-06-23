namespace Clio.Project.NuGet
{
	using System;
	using System.Linq;

	#region Class: CreatioSdk

	public class CreatioSdk : ICreatioSdk
	{

		#region Fields: Private

		private readonly Version[] _versions = {
			new Version("8.0.3.1759"),
			new Version("8.0.2.2425"),
			new Version("8.0.1.1993"),
			new Version("8.0.0.5484"),
			new Version("7.18.5.1500"),
			new Version("7.18.4.1534"),
			new Version("7.18.4.1532"),
			new Version("7.18.3.1241"),
			new Version("7.18.3.1238"),
			new Version("7.18.2.1236"),
			new Version("7.18.2.1235"),
			new Version("7.18.1.2800"),
			new Version("7.18.0.1353"),
			new Version("7.17.4.2265"),
			new Version("7.17.3.1379"),
			new Version("7.17.3.1378"),
			new Version("7.17.3.1377"),
			new Version("7.17.3.1376"),
			new Version("7.17.2.1728"),
			new Version("7.17.2.1725"),
			new Version("7.17.1.1363"),
			new Version("7.17.0.2148"),
			new Version("7.17.0.2147"),
			new Version("7.16.4.1731"),
			new Version("7.16.3.1473"),
			new Version("7.16.3.1472"),
			new Version("7.16.2.1600"),
			new Version("7.16.2.1599"),
			new Version("7.16.1.2142"),
			new Version("7.16.1.2140"),
			new Version("7.16.1.2135"),
			new Version("7.16.0.4462"),
			new Version("7.16.0.4461"),
			new Version("7.16.0.4449"),
			new Version("7.15.4.3060"),
			new Version("7.15.4.3055"),
			new Version("7.15.3.1650"),
			new Version("7.15.3.1649"),
			new Version("7.15.2.501")
		};

		#endregion

		#region Properties: Public

		public Version LastVersion => _versions[0];

		#endregion

		#region Methods: Public

		public Version FindSdkVersion(Version applicationVersion) => _versions
			.LastOrDefault(sdkVersion => sdkVersion >= applicationVersion) ?? LastVersion;

		#endregion

	}

	#endregion

}