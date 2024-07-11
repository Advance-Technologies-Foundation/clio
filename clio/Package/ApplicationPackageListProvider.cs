using System;
using System.Collections.Generic;
using System.Linq;
using Clio.Common;

namespace Clio.Package
{

	#region Class: ApplicationPackageListProvider

	public class ApplicationPackageListProvider : IApplicationPackageListProvider
	{

		#region Fields: Private

		private readonly IJsonConverter _jsonConverter;
		private readonly IServiceUrlBuilder _serviceUrlBuilder;
		private readonly IApplicationClient _applicationClient;

		#endregion

		#region Constructors: Public

		public ApplicationPackageListProvider(IApplicationClient applicationClient, IJsonConverter jsonConverter, 
				IServiceUrlBuilder serviceUrlBuilder) {
			applicationClient.CheckArgumentNull(nameof(applicationClient));
			jsonConverter.CheckArgumentNull(nameof(jsonConverter));
			serviceUrlBuilder.CheckArgumentNull(nameof(serviceUrlBuilder));
			_applicationClient = applicationClient;
			_jsonConverter = jsonConverter;
			_serviceUrlBuilder = serviceUrlBuilder;
		}

		#endregion

		#region Properties: Private
		private string PackagesListServiceUrl => _serviceUrlBuilder.Build("/rest/CreatioApiGateway/GetPackages");

		#endregion

		#region Methods: Private

		private PackageInfo CreatePackageInfo(Dictionary<string, string> package) {
			var descriptor = new PackageDescriptor {
				Name = package["Name"],
				Maintainer = package["Maintainer"],
				UId = Guid.Parse(package["UId"]),
				PackageVersion = package["Version"]
			};
			return new PackageInfo(descriptor,string.Empty, Enumerable.Empty<string>());
		}

		#endregion

		#region Methods: Public

		public IEnumerable<PackageInfo> GetPackages() => GetPackages("{}");
	

		public IEnumerable<PackageInfo> GetPackages(string scriptData) {
			try {
				string responseFormServer = _applicationClient.ExecutePostRequest(PackagesListServiceUrl, scriptData);
				var json = _jsonConverter.CorrectJson(responseFormServer);
				var packages = _jsonConverter.DeserializeObject<List<Dictionary<string, string>>>(json);
				return packages.Select(CreatePackageInfo);
				
			} catch (Exception e) {
				return Array.Empty<PackageInfo>();
			}
		}

		#endregion

	}

	#endregion

}