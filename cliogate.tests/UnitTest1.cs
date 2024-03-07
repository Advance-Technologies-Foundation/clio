using cliogate.Files.cs;

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

}