using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using cliogate.Files.cs.Dto;

namespace cliogate.Files.cs
{
	public class ProductManager
	{

		
        private string[] SplitRawData()
		{
			string rawDatafilePath = Path.Combine(CreatioPathBuilder.GetPackageFilePath("cliogate"),"data", "product_info.txt");
			return File.ReadAllLines(rawDatafilePath);
		}
        public List<ProductInfo> GetProductInfoByVersion(Version coreVersion)
		{
			List<ProductInfo> productInfos = new List<ProductInfo>();
			foreach (string line in SplitRawData()) {
				
				var lineItems = line.Split(new []{'\t'}, StringSplitOptions.RemoveEmptyEntries);
				
				if(lineItems.Length == 3) {
					if(Version.TryParse(lineItems[1], out Version productVersion))
					{
						if(coreVersion.Major == productVersion.Major && coreVersion.Minor == productVersion.Minor && coreVersion.Build == productVersion.Build)
						{
							var product =  new ProductInfo() {
								Name = lineItems[0],
								Version = productVersion,
								Packages = lineItems[2].Split(new []{','}, StringSplitOptions.RemoveEmptyEntries)
							};
							productInfos.Add(product);
						}
					}
				}
			}
			return productInfos;
		}
		
		public string FindProductNameByPackages(IEnumerable<string> packages, Version coreVersion){
			
			var products = GetProductInfoByVersion(coreVersion)
				.OrderByDescending(p => p.Packages.Length)
				.ThenByDescending(p=> p.Name.Length);
			
			foreach (ProductInfo product in products) {
				if(product.Packages.Intersect(packages).Count() == product.Packages.Length) {
					return product.Name
						.Replace("linux","")
						.Replace("& customer360","")
						.Replace("customerCenter","customer center")
						.Replace("compatibility edition","")
						.TrimEnd();
				}
			}
			return "UNKNOWN PRODUCT";
		}
	}
}