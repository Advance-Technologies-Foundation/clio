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
		private readonly string _packagesListServiceUrl;
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
			_packagesListServiceUrl = serviceUrlBuilder.Build("/rest/CreatioApiGateway/GetPackages");
		}

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
			string responseFormServer = _applicationClient.ExecutePostRequest(_packagesListServiceUrl, scriptData);
			var json = _jsonConverter.CorrectJson(responseFormServer);
			var packages = _jsonConverter.DeserializeObject<List<Dictionary<string, string>>>(json);
			return packages.Select(CreatePackageInfo);
		}

		#endregion

	}

	#endregion

}