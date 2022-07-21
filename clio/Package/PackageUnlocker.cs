namespace Clio.Package
{
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;

	#region Interface: IPackageDownloader

	public interface IPackageUnlocker
	{

		#region Methods: Public

		void Unlock(IEnumerable<string> packages);

		#endregion

	}

	#endregion

	#region Class: PackageUnlocker

	public class PackageUnlocker : IPackageUnlocker
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;

		#endregion
		
		#region Constructors: Public

		public PackageUnlocker(EnvironmentSettings environmentSettings, IApplicationClientFactory applicationClientFactory) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
		}

		#endregion

		#region Methods: Private

		private IApplicationClient CreateApplicationClient() =>
			_applicationClientFactory.CreateClient(_environmentSettings);

		private string GetRequestData(IEnumerable<string> packages) =>
			"{\"unlockPackages\":[" + string.Join(",", packages.Select(pkg => $"\"{pkg.Trim()}\"")) + "]}";


		#endregion

		#region Methods: Public

		public void Unlock(IEnumerable<string> packages) {
			IApplicationClient applicationClient = CreateApplicationClient();
			string requestData = GetRequestData(packages);
			applicationClient.CallConfigurationService("CreatioApiGateway", "UnlockPackages", requestData) ;
		}

		#endregion

	}

	#endregion

}