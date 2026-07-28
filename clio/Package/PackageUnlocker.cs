namespace Clio.Package
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text.Json;
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
		private readonly IServiceUrlBuilder _serviceUrlBuilder;

		private static readonly JsonSerializerOptions JsonOptions = new() {
			PropertyNameCaseInsensitive = true
		};

		#endregion

		#region Constructors: Public

		public PackageLockManager(EnvironmentSettings environmentSettings,
			IApplicationClientFactory applicationClientFactory,
			IServiceUrlBuilder serviceUrlBuilder) {
			environmentSettings.CheckArgumentNull(nameof(environmentSettings));
			applicationClientFactory.CheckArgumentNull(nameof(applicationClientFactory));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			_environmentSettings = environmentSettings;
			_applicationClientFactory = applicationClientFactory;
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		#endregion

		#region Methods: Private

		private IApplicationClient CreateApplicationClient() =>
			_applicationClientFactory.CreateClient(_environmentSettings);

		private void CallGate(ServiceUrlBuilder.KnownRoute route, string paramName, string[] packages) {
			string url = _serviceUrlBuilder.Build(route);
			string[] payload = packages.Length > 0 ? packages : null;
			string body = JsonSerializer.Serialize(
				new Dictionary<string, object> { [paramName] = payload },
				JsonOptions);
			string response = CreateApplicationClient().ExecutePostRequest(url, body);
			if (string.IsNullOrEmpty(response)) {
				throw new InvalidOperationException(
					$"ClioGate {route} returned an empty response. " +
					"Check the Creatio application logs (Error.log) and verify the installed cliogate version is current.");
			}
			bool success;
			try {
				success = JsonSerializer.Deserialize<bool>(response, JsonOptions);
			} catch (JsonException ex) {
				throw new InvalidOperationException(
					$"ClioGate {route} returned a non-JSON response (likely an HTTP error page). " +
					"Check the Creatio application logs (Error.log) and verify the installed cliogate version is current.", ex);
			}
			if (!success) {
				throw new InvalidOperationException(
					$"ClioGate {route} returned false. Check the Creatio application logs for details.");
			}
		}

		#endregion

		#region Methods: Public

		public void Unlock(IEnumerable<string> packages) =>
			CallGate(ServiceUrlBuilder.KnownRoute.UnlockPackages, "unlockPackages", packages.ToArray());

		public void Unlock() => Unlock(Enumerable.Empty<string>());

		public void Lock(IEnumerable<string> packages) =>
			CallGate(ServiceUrlBuilder.KnownRoute.LockPackages, "lockPackages", packages.ToArray());

		public void Lock() => Lock(Enumerable.Empty<string>());

		#endregion

	}

	#endregion

}
