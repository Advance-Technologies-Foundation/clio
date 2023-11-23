# Using backend unit tests for Creatio development

Prerequisites:
- Initialized workspace with one or more packages in it

## Creating unit test project

To create a new UnitTest project for every package in the workspace, run the following command in the workspace folder:

```bash
clio new-test-project 
```

To specify a package for which a UnitTest will be created, run the following command in the workspace folder:

```bash
clio new-test-project --package <PACKAGE_NAME>
```

To open UnitTest solution use task open-test-project:

```bash
<WORKSPACE_FOLDER>\tasks\open-test-project.cmd
```


Add TestFixture class to the test project

```csharp
[TestFixture]
[MockSettings(RequireMock.All)]
public class TestClass {

	[Test]
	public void TestMethod(){
		//Arrange
		var sut = new SystemUnderTest();
		var expected = "OK";

		//Act
		var result = sut.DoSomething();
		
		//Assert
		Assert.AreEqual(result, expected);
	}
}
```
