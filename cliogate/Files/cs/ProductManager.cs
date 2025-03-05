using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using cliogate.Files.cs.Dto;
using Common.Logging;

namespace cliogate.Files.cs
{
	public class ProductManager {

		private static readonly Lazy<ILog> _log = new Lazy<ILog>(()=>LogManager.GetLogger(typeof(CreatioApiGateway)));
		
		#region Methods: Private

		private static IEnumerable<string> SplitRawData(bool isNetCore){
			string packageName = isNetCore ? "cliogate_netcore" : "cliogate";
			string pkgDir = CreatioPathBuilder.GetPackageFilePath(packageName);
			if(!Directory.Exists(pkgDir)) {
				_log.Value.ErrorFormat(CultureInfo.InvariantCulture, "Could not find directory: {0}", pkgDir);
				return Array.Empty<string>();
			}
			string 	rawDatafilePath = Path.Combine(pkgDir, "data", "product_info.txt");
			return File.ReadAllLines(rawDatafilePath);
		}

		#endregion

		#region Methods: Public

		public string FindProductNameByPackages(IEnumerable<string> packages, Version coreVersion, bool isNetCore){
			IOrderedEnumerable<ProductInfo> products = GetProductInfoByVersion(coreVersion, isNetCore)
				.OrderByDescending(p => p.Packages.Length)
				.ThenByDescending(p => p.Name.Length);

			IEnumerable<string> enumerable = packages as string[] ?? packages.ToArray();
			foreach (ProductInfo product in products) {
				if (product.Packages.Intersect(enumerable).Count() == product.Packages.Length) {
					return product.Name
						.Replace("linux", "")
						.Replace("net6", "")
						.Replace("net8", "")
						.Replace("& customer360", "")
						.Replace("customerCenter", "customer center")
						.Replace("compatibility edition", "")
						.TrimEnd();
				}
			}
			return "UNKNOWN PRODUCT";
		}

		public List<ProductInfo> GetProductInfoByVersion(Version coreVersion, bool isNetCore){
			List<ProductInfo> productInfos = new List<ProductInfo>();
			
			
			foreach (string line in SplitRawData(isNetCore)) {
				string[] lineItems = line.Split(new[] {'\t'}, StringSplitOptions.RemoveEmptyEntries);

				if (lineItems.Length == 3 
					&& Version.TryParse(lineItems[1], out Version productVersion) 
					&& coreVersion.Major == productVersion.Major 
					&& coreVersion.Minor == productVersion.Minor 
					&& coreVersion.Build == productVersion.Build) {
					ProductInfo product = new ProductInfo {
						Name = lineItems[0],
						Version = productVersion,
						Packages = lineItems[2].Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries),
					};
					productInfos.Add(product);
				}
			}
			return productInfos;
		}

		#endregion

	}
}