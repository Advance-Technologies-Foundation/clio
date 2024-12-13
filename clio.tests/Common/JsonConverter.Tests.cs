using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using Clio.Common;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using IFileSystem = Clio.Common.IFileSystem;

namespace Clio.Tests.Common;

[TestFixture]
public class JsonConverterTests {

	private readonly MockFileSystem _fileSystemMock = new MockFileSystem();
	private JsonConverter _systemUnderTest;
	private Clio.Common.FileSystem fileSystem;
	
	[SetUp]
	public void SetUp() {
		fileSystem = new Clio.Common.FileSystem(_fileSystemMock);
		_systemUnderTest = new JsonConverter(fileSystem);
	}
	
    [TestCase(@"\\r\\n", ExpectedResult = "\r\n")]
    [TestCase(@"\\n", ExpectedResult = "\r\n")]
    [TestCase(@"\r\n", ExpectedResult = "\r\n")]
    [TestCase(@"\n", ExpectedResult = "\r\n")]
    [TestCase(@"\\t", ExpectedResult = "\t")]
    [TestCase(@"\\", ExpectedResult = "\\")]
    [TestCase("test", ExpectedResult = "test")]
    [TestCase(null, ExpectedResult = null)] //This should probably return empty string, not null
    public string SanitizeJsonString_ShouldCorrectlyReplaceEscapeSequences(string input){
        return _systemUnderTest.SanitizeJsonString(input);
    }
	
	
	[Test] 
    public void SerializeObjectToFile_WhenCalledWithValidObject_ShouldWriteToFile()
    {
        // Arrange
        var testObject = new { Name = "Test", Value = 123 };
        var filePath = "test.json";
		
		
		
		//FileSystemStream fileSystemStream = new MockFileStream(fs, filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileOptions.None);
		
		//var fileSystemStream = new MockFileStream(fs, filePath, FileMode.OpenOrCreate);
		
		
		//var fileSystemStream = Substitute.For<MockFileStream>(fs, filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileOptions.None);
		//fileSystemStream.CanWrite.Returns(true);
		
		//_fileSystemMock.CreateFile(filePath).Returns(fileSystemStream);
		
        // Act
        _systemUnderTest.SerializeObjectToFile(testObject, filePath);

        // Assert
        // _fileSystemMock.Received(1).WriteAllTextToFile(
        //     Arg.Is<string>(filePath),
        //     Arg.Is<string>(s => s.Contains("\"Name\":\"Test\"") && s.Contains("\"Value\":123"))
        // );
		
		//fileSystemStream.Received(1).Close();
		
		var files = _fileSystemMock.AllFiles;
		files.Select(Path.GetFileName).Should().Contain(filePath);
		
		var content = fileSystem.ReadAllText(_fileSystemMock.AllFiles.FirstOrDefault());
		
		//closed.Should().Be(1); //using FileSystemStream will call Flush and CLose on Dispose
		//flushed.Should().Be(1); // Using StreamWriter will call Flush and Close on Dispose
    }
	
	
	public class MyMockFileStream : MockFileStream {

		public MyMockFileStream(IMockFileDataAccessor mockFileDataAccessor, string path, FileMode mode, FileAccess access = FileAccess.ReadWrite, FileOptions options = FileOptions.None)
			: base(mockFileDataAccessor, path, mode, access, options){ }

		
		public event EventHandler Closed;
		public override void Close(){
			base.Close();
			Closed?.Invoke(this, EventArgs.Empty);
		}

		public event EventHandler Flushed;
		public override void Flush(){
			base.Flush();
			Flushed?.Invoke(this, EventArgs.Empty);
		}

	}
}