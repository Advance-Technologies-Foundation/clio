namespace Clio.Package
{
	using System.Collections.Generic;
	using System.Linq;
	using Clio.Common;

	#region Interface: IPackageLockManager

	public interface IPackageLockManager
	{

		#region Methods: Public

		void Unlock();
		void Lock();
		void Unlock(IEnumerable<string> packages);
		void Lock(IEnumerable<string> packages);


		#endregion

	}

	#endregion

	#region Class: PackageLockManager

	public class PackageLockManager : IPackageLockManager
	{

		#region Fields: Private

		private readonly EnvironmentSettings _environmentSettings;
		private readonly IApplicationClientFactory _applicationClientFactory;

		#endregion
		
		#region Constructors: Public

		public PackageLockManager(EnvironmentSettings environmentSettings, IApplicationClientFactory applicationClientFactory) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
		}

		#endregion

		#region Methods: Private

		private IApplicationClient CreateApplicationClient() =>
			_applicationClientFactory.CreateClient(_environmentSettings);

		private string GetRequestData(string argumentName, IEnumerable<string> packages) =>
			"{\"" + argumentName + "\":[" + string.Join(",", packages.Select(pkg => $"\"{pkg.Trim()}\"")) + "]}";


		#endregion

		#region Methods: Public

		public void Unlock(IEnumerable<string> packages) {
			IApplicationClient applicationClient = CreateApplicationClient();
			string requestData = GetRequestData("unlockPackages", packages);
			applicationClient.CallConfigurationService("CreatioApiGateway", "UnlockPackages", requestData) ;
		}

		public void Unlock() => Unlock(Enumerable.Empty<string>());

		public void Lock(IEnumerable<string> packages) {
			IApplicationClient applicationClient = CreateApplicationClient();
			string requestData = GetRequestData("lockPackages", packages);
			applicationClient.CallConfigurationService("CreatioApiGateway", "LockPackages", requestData) ;
		}

		public void Lock() => Lock(Enumerable.Empty<string>());

		#endregion

	}

	#endregion

}