using cliogate.Files.cs;
using cliogate.Files.cs.Dto;

namespace cliogate.tests;

public class Tests
{

	[SetUp]
	public void Setup(){ }

	// [Test]
	// public void Test1(){
	// 	
	// 	var pm = new ProductManager();
	// 	pm.FindProductNameByPackages(new []{"Base"}, new Version("1.0.0"));
	// 	
	// 	Assert.Pass();
	// }
	
	[Test]
	public void SplitRawData_ReturnsFileContent(){

		//Arrange
		CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
		var pm = new ProductManager();

		//Act
		var items = pm.GetProductInfoByVersion(new Version("9.0.0"));

		//Assert
		Assert.AreEqual(0, items.Count);
		
	}
	
	[Test]
	public void SplitRawData_ReturnsProduct(){

		//Arrange
		CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
		var sut = new ProductManager();

		//Act
		var actual = sut.FindProductNameByPackages(new []{"CrtBase"},new Version("8.1.2"));

		//Assert
		Assert.AreEqual("product base", actual);
		
	}
	
	[Test]
	public void SplitRawData_ReturnsProduct_1(){

		//Arrange
		CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
		var sut = new ProductManager();

		//Act
		var actual = sut.FindProductNameByPackages(new []{"Unknown_package"},new Version("8.1.2"));

		//Assert
		Assert.AreEqual("UNKNOWN PRODUCT", actual);
		
	}
	
	
	[Test]
	public void SplitRawData_ReturnsProduct_2(){

		//Arrange
		CreatioPathBuilder.RootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","..","..");
		var sut = new ProductManager();

		var expectedProductInfos = GetExpectedProductInfos(); 
		
		//Act
		foreach (var expectedProductInfo in expectedProductInfos) {
			
			var actual = sut.FindProductNameByPackages(expectedProductInfo.Packages,expectedProductInfo.Version);
			//Assert
			Assert.AreEqual(expectedProductInfo.Name, actual);
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