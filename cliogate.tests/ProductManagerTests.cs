using System;
using System.Collections.Generic;
using System.IO;
using cliogate.Files.cs;
using cliogate.Files.cs.Dto;
using FluentAssertions;
using NUnit.Framework;

namespace cliogate.tests
{
	[TestFixture]
	[Category("ClioGate")]
	public class Tests
	{
	
		[Test]
		public void SplitRawData_ReturnsFileContent(){

			//Arrange
			CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
			ProductManager sut = new ProductManager();

			//Act
			List<ProductInfo> items = sut.GetProductInfoByVersion(new Version("9.0.0"), false);

			//Assert
			items.Should().HaveCount(0);
		
		}
	
		[Test]
		public void SplitRawData_ReturnsProduct(){

			//Arrange
			CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
			ProductManager sut = new ProductManager();

			//Act
			var actual = sut.FindProductNameByPackages(new []{"CrtBase"},new Version("8.1.2"), false);

			//Assert
			const string expected = "product base";
			actual.Should().Be(expected);
		
		}
	
		[Test]
		public void SplitRawData_ReturnsProduct_1(){

			//Arrange
			CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
			ProductManager sut = new ProductManager();

			//Act
			string actual = sut.FindProductNameByPackages(new []{"Unknown_package"},new Version("8.1.2"), false);

			//Assert
			const string expected = "UNKNOWN PRODUCT";
			actual.Should().Be(expected);
	
		}
	
	
		[Test]
		public void SplitRawData_ReturnsProduct_2(){

			//Arrange
			CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
			var sut = new ProductManager();
			var expectedProductInfos = GetExpectedProductInfos(); 
		
			foreach (var expectedProductInfo in expectedProductInfos) {
			
				//Act
				var actual = sut.FindProductNameByPackages(expectedProductInfo.Packages,expectedProductInfo.Version, false);
			
				//Assert
				actual.Should().Be(expectedProductInfo.Name);
			}
		}
	
	

		private IEnumerable<ProductInfo> GetExpectedProductInfos(){
			var lines = File.ReadLines("data/8.1.2.3842.txt");
			List<ProductInfo> result = new List<ProductInfo>();
			foreach (string line in lines) {
				var items = line.Split(new[]{'\t'}, StringSplitOptions.RemoveEmptyEntries);
				var pInfo = new ProductInfo() {
					Name = items[0],
					Packages = items[1].Split(new[]{','}, StringSplitOptions.RemoveEmptyEntries),
					Version = Version.Parse("8.1.2.3842")
				};
				result.Add(pInfo);
			}
			return result;
		}
	}
}