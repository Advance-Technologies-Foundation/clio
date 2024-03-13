using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cliogate.Files.cs.Dto;

namespace cliogate.Files.cs
{
	public class ProductManager
	{

		#region Methods: Private

		private static IEnumerable<string> SplitRawData(){
			string rawDatafilePath = Path.Combine(CreatioPathBuilder.GetPackageFilePath("cliogate"), "data",
				"product_info.txt");
			return File.ReadAllLines(rawDatafilePath);
		}

		#endregion

		#region Methods: Public

		public string FindProductNameByPackages(IEnumerable<string> packages, Version coreVersion){
			IOrderedEnumerable<ProductInfo> products = GetProductInfoByVersion(coreVersion)
				.OrderByDescending(p => p.Packages.Length)
				.ThenByDescending(p => p.Name.Length);

			IEnumerable<string> enumerable = packages as string[] ?? packages.ToArray();
			foreach (ProductInfo product in products) {
				if (product.Packages.Intersect(enumerable).Count() == product.Packages.Length) {
					return product.Name
						.Replace("linux", "")
						.Replace("& customer360", "")
						.Replace("customerCenter", "customer center")
						.Replace("compatibility edition", "")
						.TrimEnd();
				}
			}
			return "UNKNOWN PRODUCT";
		}

		public List<ProductInfo> GetProductInfoByVersion(Version coreVersion){
			List<ProductInfo> productInfos = new List<ProductInfo>();
			foreach (string line in SplitRawData()) {
				string[] lineItems = line.Split(new[] {'\t'}, StringSplitOptions.RemoveEmptyEntries);

				if (lineItems.Length == 3 
					&& Version.TryParse(lineItems[1], out Version productVersion) 
					&& coreVersion.Major == productVersion.Major 
					&& coreVersion.Minor == productVersion.Minor 
					&& coreVersion.Build == productVersion.Build) {
					ProductInfo product = new ProductInfo {
						Name = lineItems[0],
						Version = productVersion,
						Packages = lineItems[2].Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries)
					};
					productInfos.Add(product);
				}
			}
			return productInfos;
		}

		#endregion

	}
}