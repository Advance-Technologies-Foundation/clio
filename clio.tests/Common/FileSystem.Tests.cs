using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text;
using Clio.Common;
using FluentAssertions;
using FluentAssertions.Specialized;
using NSubstitute;
using NUnit.Framework;
using Ms = System.IO.Abstractions;
namespace Clio.Tests.Common;

[TestFixture]
[Category("Unit")]
public class FileSystemTests
{

	private readonly Ms.IFileSystem _msFileSystem;
	private readonly Ms.IFileSystem _mockFileSystem;

	private readonly Dictionary<string, MockFileData> _mockFileData = new() {
		{"first.cs","some cs content"},
		{"second.cs","other content"},
		{"readonly.cs","some content"},
		{"path/to/file.cs","some content"}
	};
	public FileSystemTests(){
		_msFileSystem = new Ms.FileSystem();
		_mockFileSystem = new MockFileSystem(_mockFileData);
	}

	#region HashFile
	
	[TestCase("first.cs", "_first.cs", FileSystem.Algorithm.SHA1)]
	[TestCase("first.cs", "_first.cs", FileSystem.Algorithm.SHA256)]
	[TestCase("first.cs", "_first.cs", FileSystem.Algorithm.SHA384)]
	[TestCase("first.cs", "_first.cs", FileSystem.Algorithm.SHA512)]
	[TestCase("first.cs", "_first.cs", FileSystem.Algorithm.MD5)]
	public void CompareFiles_Returns_False_WhenFilesDoesNotExist(string first, string second,
		FileSystem.Algorithm algorithm){
		new FileSystem(_mockFileSystem)
			.CompareFiles(algorithm, first, second).Should().BeFalse();
	}

	[TestCase("first.cs", "second.cs", FileSystem.Algorithm.SHA1)]
	[TestCase("first.cs", "second.cs", FileSystem.Algorithm.SHA256)]
	[TestCase("first.cs", "second.cs", FileSystem.Algorithm.SHA384)]
	[TestCase("first.cs", "second.cs", FileSystem.Algorithm.SHA512)]
	[TestCase("first.cs", "second.cs", FileSystem.Algorithm.MD5)]
	public void CompareFiles_Returns_False_WhenFilesNotIdentical(string first, string second,
		FileSystem.Algorithm algorithm){
		new FileSystem(_msFileSystem)
			.CompareFiles(algorithm, first, second).Should().BeFalse();
	}

	[TestCase("first.cs", FileSystem.Algorithm.SHA1)]
	[TestCase("first.cs", FileSystem.Algorithm.SHA256)]
	[TestCase("first.cs", FileSystem.Algorithm.SHA384)]
	[TestCase("first.cs", FileSystem.Algorithm.SHA512)]
	[TestCase("first.cs", FileSystem.Algorithm.MD5)]
	public void CompareFiles_Returns_True_WhenFilesIdentical(string file, FileSystem.Algorithm algorithm){
		new FileSystem(_mockFileSystem)
			.CompareFiles(algorithm, file, file).Should().BeTrue();
	}
	
	[TestCase("first.cs")]
	public void CompareFiles_Returns_True_WhenFilesIdentical(string file){
		new FileSystem(_mockFileSystem)
			.CompareFiles(file, file).Should().BeTrue();
	}

	[TestCase("samplefiles/sample.txt", FileSystem.Algorithm.SHA1)]
	[TestCase("samplefiles/sample.txt", FileSystem.Algorithm.SHA256)]
	[TestCase("samplefiles/sample.txt", FileSystem.Algorithm.SHA384)]
	[TestCase("samplefiles/sample.txt", FileSystem.Algorithm.SHA512)]
	[TestCase("samplefiles/sample.txt", FileSystem.Algorithm.MD5)]
	public void ComputesCorrectHash(string sampleFile, FileSystem.Algorithm algorithm)
	{
		// Arrange
		string psHash;
		if (OperatingSystem.IsWindows())
		{
			psHash = new ProcessExecutor().Execute("pwsh.exe",
				$"-c \"Get-FileHash {sampleFile} -Algorithm {algorithm.ToString()} | Select-Object -ExpandProperty Hash\"",
				true, null, true);
		}
		else
		{
			string algo = algorithm.ToString().ToLowerInvariant();
			string hashCmd = algo switch
			{
				"md5" => $"md5sum {sampleFile} | awk '{{print $1}}'",
				"sha1" => $"sha1sum {sampleFile} | awk '{{print $1}}'",
				"sha256" => $"sha256sum {sampleFile} | awk '{{print $1}}'",
				"sha384" => $"sha384sum {sampleFile} | awk '{{print $1}}'",
				"sha512" => $"sha512sum {sampleFile} | awk '{{print $1}}'",
				_ => throw new NotSupportedException($"Algorithm {algorithm} not supported on this platform")
			};
			psHash = new ProcessExecutor().Execute("/bin/bash", $"-c \"{hashCmd}\"", true, null, true).Trim();
		}
	
		// Assert
		new FileSystem(_msFileSystem).GetFileHash(algorithm, sampleFile).ToUpper().Should().Be(psHash.ToUpper());
	}
	

	[Test]
	public void GetFileHash_ThrowsException_WhenFileDoesNotExist(){
		
		//Arrange
		const FileSystem.Algorithm algorithm = FileSystem.Algorithm.SHA1;
		const string nonExistentFile = "AssemblyInfo.css";

		//Assert
		Assert.Throws<FileNotFoundException>(() => new FileSystem(_mockFileSystem).GetFileHash(algorithm, nonExistentFile));
	}
	
	#endregion

	#region SymLink
	
	[Test]
	public void CreateFileSymLink_CreatesSymbolicLink_WhenPathAndTargetPathAreValid()
	{
		// Arrange
		const string path = "path/to/link";
		const string targetPath = "path/to/target";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(path).Returns(true);
		fs.File.Exists(targetPath).Returns(true);

		// Act
		var result = new FileSystem(fs).CreateFileSymLink(path, targetPath);

		// Assert
		result.Should().NotBeNull();
		fs.File.Received(1).CreateSymbolicLink(path, targetPath);
	}

	[Test]
	public void CreateFileSymLink_ThrowsException_WhenPathDoesNotExist()
	{
		// Arrange
		const string path = "path/to/nonexistent/link";
		const string targetPath = "path/to/target";
		Ms.IFileSystem fs = new MockFileSystem(_mockFileData);
		//fs.Directory.CreateDirectory(targetPath);
		
		// Act & Assert
		Action act = () => new FileSystem(fs).CreateFileSymLink(path, targetPath);
		act.Invoking(a => a()).Should()
			.Throw<DirectoryNotFoundException>()
			.WithMessage($"Could not find a part of the path '{path}'.");
	}

	[Test]
	public void CreateFileSymLink_ThrowsException_WhenTargetPathDoesNotExist()
	{
		// Arrange
		const string path = "path/to/link";
		const string targetPath = "path/to/nonexistent/target";
		Ms.IFileSystem fs = new MockFileSystem(_mockFileData);
		fs.Directory.CreateDirectory(path);
		// Act & Assert
		Action act = () => new FileSystem(fs).CreateFileSymLink(path, targetPath);
		act.Invoking(a => a()).Should()
			.Throw<FileNotFoundException>()
			.WithMessage($"Could not find file '{targetPath}'.");
	}

	[Test]
	public void CreateDirectorySymLink_CreatesSymbolicLink_WhenPathAndTargetPathAreValid()
	{
		// Arrange
		const string path = "path/to/link";
		const string targetPath = "path/to/target";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(path).Returns(true);
		fs.File.Exists(targetPath).Returns(true);

		// Act
		var result = new FileSystem(fs).CreateDirectorySymLink(path, targetPath);

		// Assert
		result.Should().NotBeNull();
		fs.Directory.Received(1).CreateSymbolicLink(path, targetPath);
	}

	[Test]
	public void CreateDirectorySymLink_ThrowsException_WhenPathDoesNotExist()
	{
		// Arrange
		const string path = "path2/to/nonexistent/link";
		const string targetPath = "path2/to/target";
		var mfd = new Dictionary<string, MockFileData>();
		Ms.IFileSystem fs = new MockFileSystem(mfd);
		fs.Directory.CreateDirectory(path);
		fs.Directory.CreateDirectory(targetPath);
		
		// Act & Assert
		Action act = () => new FileSystem(fs).CreateDirectorySymLink(path, targetPath);
		act.Invoking(a => a()).Should()
			.Throw<IOException>()
			.WithMessage($"The file 'path' already exists.");
	}
	
	#endregion

	#region ExtractFileNameFromPath

	[Test]
	public void ExtractFileNameFromPath_ReturnsFileName_WhenPathIsValid(){
		//Arrange
		const string fileName = "file.cs";
		const string expected = "file";
		MockFileSystem mockFs = new ();
		mockFs.Directory.CreateDirectory(Path.Join("path","to"));
		mockFs.File.Create(Path.Join("path","to", fileName));
		
		//Act
		string actual = new FileSystem(mockFs)
			.ExtractFileNameFromPath(Path.Join("path", "to", expected));
		
		//Assert
		actual.Should().Be(expected);
	}

	#endregion

	#region ExtractFileExtensionFromPath

	[Test]
	public void ExtractFileExtensionFromPath_ReturnsFileExtension_WhenPathIsValid(){
		//Arrange
		const string fileName = "file.cs";
		const string expected = ".cs";
		MockFileSystem mockFs = new ();
		mockFs.Directory.CreateDirectory(Path.Join("path","to"));
		mockFs.File.Create(Path.Join("path","to", fileName));
		
		//Act
		string actual = new FileSystem(mockFs)
			.ExtractFileExtensionFromPath(Path.Join("path", "to", fileName));
		
		//Assert
		actual.Should().Be(expected);
	}

	#endregion

	#region GetFiles

	[Test]
	public void GetFiles_ReturnsFiles_WhenDirectoryIsValid(){
		//Arrange
		MockFileSystem mockFs = new ();
		mockFs.Directory.CreateDirectory(Path.Join("path","to"));
		mockFs.File.Create(Path.Join("path","to", "first.cs"));
		mockFs.File.Create(Path.Join("path","to", "second.cs"));
		mockFs.File.Create(Path.Join("path","to", "third.cs"));
		
		const string directoryPath = "path/to";
		//Act
		string[] actual = new FileSystem(mockFs)
			.GetFiles(directoryPath);
		
		Ms.IDriveInfo drive = mockFs.DriveInfo.GetDrives().FirstOrDefault();
		//Assert
		actual.Should().BeEquivalentTo(new [] {
			Path.Join(drive!.Name, "path","to","first.cs"),
			Path.Join(drive.Name, "path","to","second.cs"),
			Path.Join(drive.Name, "path","to","third.cs")
		});
	}

	[Test]
	public void GetFiles_WithPattern_ReturnsFiles_WhenDirectoryIsValid(){
		//Arrange
		MockFileSystem mockFs = new ();
		mockFs.Directory.CreateDirectory(Path.Join("path","to"));
		mockFs.File.Create(Path.Join("path","to", "first.cs"));
		mockFs.File.Create(Path.Join("path","to", "second.cs"));
		mockFs.File.Create(Path.Join("path","to", "third.cs"));
		
		const string directoryPath = "path/to";
		//Act
		string[] actual = new FileSystem(mockFs)
			.GetFiles(directoryPath,"*.txt", SearchOption.AllDirectories);
		
		//Assert
		actual.Should().BeEmpty();
	}
	
	[Test]
	public void GetFiles_WithPattern2_ReturnsFiles_WhenDirectoryIsValid(){
		//Arrange
		MockFileSystem mockFs = new ();
		mockFs.Directory.CreateDirectory(Path.Join("path","to"));
		mockFs.File.Create(Path.Join("path","to", "first.cs"));
		mockFs.File.Create(Path.Join("path","to", "second.cs"));
		mockFs.File.Create(Path.Join("path","to", "third.cs"));
		
		const string directoryPath = "path/to";
		//Act
		string[] actual = new FileSystem(mockFs)
			.GetFiles(directoryPath,"*.cs", SearchOption.AllDirectories);
		
		Ms.IDriveInfo drive = mockFs.DriveInfo.GetDrives().FirstOrDefault();
		//Assert
		actual.Should().BeEquivalentTo(new [] {
			Path.Join(drive!.Name, "path","to","first.cs"),
			Path.Join(drive.Name, "path","to","second.cs"),
			Path.Join(drive.Name, "path","to","third.cs")
		});
	}
	
	#endregion
	
	#region CheckOrDeleteExistsFile
	
	[Test]
	public void CheckOrDeleteExistsFile_Returns_WhenFileDoesNotExist(){ 
		const string fileName = "first.cs";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(Arg.Is(fileName)).Returns(false);
		
		new FileSystem(fs)
			.CheckOrDeleteExistsFile("first.cs", false);
		fs.File.Received(1).Exists(fileName);
	}
	
	[Test]
	public void CheckOrDeleteExistsFile_DeletesFile(){ 
		//Arrange
		const string fileName = "first.cs";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(Arg.Is(fileName)).Returns(true);
		
		//Act
		new FileSystem(fs)
			.CheckOrDeleteExistsFile("first.cs", true);
		
		//Assert
		fs.File.Received(1).Delete(fileName);
		
		//Once in DeleteFile, amd once in CheckOrDeleteExistsFile
		fs.File.Received(2).Exists(fileName);
	}
	
	[Test]
	public void CheckOrDeleteExistsFile_ThrowsException_WhenFileExists(){ 
		//Arrange
		const string fileName = "first.cs";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(Arg.Is(fileName)).Returns(true);
		
		Action act = () => new FileSystem(fs)
			.CheckOrDeleteExistsFile(fileName, false);
		
		//Assert
		
		ExceptionAssertions<Exception> ex = act.Should().Throw<Exception>();
		ex.WithMessage($"The file {fileName} already exist");
		
		fs.File.Received(1).Exists(fileName);
	}
	
	#endregion

	#region CopyFiles

	[Test]
	public void CopyFiles_Throws_When_FilesPathsEmpty(){
		//Arrange
		const string destinationDirectory = "/dest";
		const bool overwrite = true;
		Ms.IFileSystem mockFs = Substitute.For<Ms.IFileSystem>();
		
		Action act = () => new FileSystem(mockFs)
			.CopyFiles(null, destinationDirectory, overwrite);
		
		//Act && Assert
		act.Should().Throw<Exception>()
			.WithMessage("Value cannot be null. (Parameter 'filesPaths')");
	}
	
	[Test]
	public void CopyFiles_Throws_When_DestinationDirectoryEmpty(){
		//Arrange
		List<string> filesPaths = new() {
			"file1.txt","file2.txt","file3.txt"
		};
		const string destinationDirectory = null;
		const bool overwrite = true;
		Ms.IFileSystem mockFs = Substitute.For<Ms.IFileSystem>();
		
		Action act = () => new FileSystem(mockFs)
			.CopyFiles(filesPaths, destinationDirectory, overwrite);
		
		//Act && Assert
		act.Should().Throw<Exception>()
			.WithMessage("Value cannot be null. (Parameter 'destinationDirectory')");
	}
	
	[Test]
	public void CopyFiles_CopiesEveryFile(){
		//Arrange
		const string destinationDirectory = "dest";
		const bool overwrite = true;
		Ms.IFileInfoFactory mockFileInfoFactory = Substitute.For<Ms.IFileInfoFactory>();
		
		IMockFileDataAccessor mockFileDataAccessor = new MockFileSystem(
			new Dictionary<string, MockFileData>{
			{"file1.txt","file1 content"},
			{"file2.txt","file2 content"},
			{"file3.txt","file3 content"}
		});
		mockFileDataAccessor.FileSystem.Directory.CreateDirectory(destinationDirectory);
		
		foreach (string filesPath in mockFileDataAccessor.AllFiles) {
			MockFileInfo mfi = new (mockFileDataAccessor, filesPath);
			mockFileInfoFactory.New(filesPath).Returns(mfi);
		}
		
		// Act
		 new FileSystem(mockFileDataAccessor)
		 	.CopyFiles(mockFileDataAccessor.AllFiles, destinationDirectory, overwrite);
		
		// Assert
		foreach (string filesPath in mockFileDataAccessor.AllFiles) {
			mockFileDataAccessor
				.FileExists(Path.Combine(destinationDirectory,filesPath))
				.Should().BeTrue();
		}
	}

	#endregion

	#region MoveFile

	[TestCase("from.txt","to.txt")]
	public void MoveFile_Calls_Move(string sourceFileName, string destinationFileName){
		// Arrange
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		
		// Act
		new FileSystem(fs).MoveFile(sourceFileName, destinationFileName);

		// Assert
		fs.File.Received(1).Move(sourceFileName, destinationFileName);
	}
	

	#endregion

	#region ResetFileReadOnlyAttribute

	[TestCase(FileAttributes.ReadOnly)]
	[TestCase(FileAttributes.ReadOnly | FileAttributes.Hidden)]
	[TestCase(FileAttributes.ReadOnly | FileAttributes.Encrypted)]
	[TestCase(FileAttributes.ReadOnly | FileAttributes.Compressed)]
	public void ResetFileReadOnlyAttribute_UnsetsReadOnlyAttribute_WhenFileIsReadonly(FileAttributes attributes){
		//Arrange
		const string fileName = "readonly.cs";
		_mockFileData[fileName].Attributes = attributes;
		
		//Act
		new FileSystem(_mockFileSystem).ResetFileReadOnlyAttribute(fileName);
		FileAttributes actual = _mockFileData[fileName].Attributes;
		
		actual.Should()
			.Be(attributes & ~FileAttributes.ReadOnly);
		
	}

	[TestCase(FileAttributes.Hidden)]
	[TestCase(FileAttributes.Compressed | FileAttributes.Hidden)]
	[TestCase(FileAttributes.Compressed | FileAttributes.Encrypted | FileAttributes.Archive)]
	public void ResetFileReadOnlyAttribute_DoesNotModifyAttributes_WhenFileIsNotReadonly(FileAttributes attributes){
		//Arrange
		const string fileName = "first.cs";
		_mockFileData[fileName].Attributes = attributes;
		
		//Act
		new FileSystem(_mockFileSystem).ResetFileReadOnlyAttribute(fileName);
		FileAttributes actual = _mockFileData[fileName].Attributes;
		
		actual.Should()
			.Be(attributes);
	}
	
	[Test]
	public void ResetFileReadOnlyAttribute_DoesNothing_WhenFileDoesNotExist(){
		//Arrange
		const string filePath = "first.cs";
		Ms.IFileSystem mockFs = Substitute.For<Ms.IFileSystem>();
		mockFs.File.Exists(filePath).Returns(false);
		
		//Act
		new FileSystem(mockFs).ResetFileReadOnlyAttribute(filePath);
		
		//Assert
		mockFs.File.Received(1).Exists(filePath);
	}
	
	#endregion

	#region ReadAllText

	[Test]
	public void ReadAllText_Calls_ReadAllText(){
		//Arrange
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>(); 
		const string fileName = "first.cs";
		
		//Act
		new FileSystem(fs)
			.ReadAllText(fileName);
			
		//Assert
		fs.File.Received(1).ReadAllText(fileName, FileSystem.Utf8NoBom);
		
	}

	#endregion

	#region WriteAllTextToFile

	[Test]
	public void WriteAllTextToFile_Calls_WriteAllTextToFile(){
		//Arrange
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>(); 
		const string fileName = "first.cs";
		const string content = "some content";
		
		//Act
		new FileSystem(fs)
			.WriteAllTextToFile(fileName, content);
			
		//Assert
		fs.File.Received(1).WriteAllText(fileName, content, FileSystem.Utf8NoBom);
	}

	#endregion

	#region ClearOrCreateDirectory

	[Test]
	public void ClearOrCreateDirectory_CreatesDirectory_WhenDirectoryDoesNotExist(){
		//Arrange
		const string directoryPath = "path/to";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.Directory.Exists(directoryPath).Returns(false);
		
		//Act
		new FileSystem(fs)
			.ClearOrCreateDirectory(directoryPath);
		
		//Assert
		fs.Directory.Received(1).CreateDirectory(directoryPath);
	}
	
	[Test]
	public void ClearOrCreateDirectory_ClearsDirectory_WhenDirectoryExist(){
		//Arrange
		const string directoryPath = "path_to";
		Dictionary<string, MockFileData> mockFileData = new() {
			{"first.cs","some cs content"},
			{"second.cs","other content"},
			{"readonly.cs","some content"},
		};
		Ms.IFileSystem fs = new MockFileSystem(mockFileData);
		var targetDir = fs.Directory.CreateDirectory(directoryPath);
		fs.File.Create(Path.Join(directoryPath, "file.txt"));
		
		//Act
		targetDir.GetFiles().Should().NotBeEmpty();
		
		new FileSystem(fs)
			.ClearOrCreateDirectory(directoryPath);
		
		//Assert
		targetDir.GetFiles().Should().BeEmpty();
	}
	
	#endregion

	#region ClearDirectory

	[Test]
	public void ClearDirectory_ClearsDirectory(){
		//Arrange
		const string directoryName = "dir";
		const string subDirectoryName = "subDir";
		Dictionary<string, MockFileData> mockFileData = new() {
			{"first.cs","some cs content"},
			{"second.cs","other content"},
			{"readonly.cs","some content"},
		};
		Ms.IFileSystem fs = new MockFileSystem(mockFileData);
		var targetDir = fs.Directory.CreateDirectory(directoryName);
		var subDir = targetDir.CreateSubdirectory(subDirectoryName);
		fs.File.Create(Path.Join(subDir.FullName, "file.txt"));
		
		//Act
		subDir.GetFiles().Should().NotBeEmpty();
		
		var sut = new FileSystem(fs);
			sut.ClearDirectory(subDir.FullName);
		
		//Assert
		subDir.GetFiles().Should().BeEmpty();
		
		//Act
		sut.ClearDirectory(targetDir.FullName);
		targetDir.GetFiles().Should().BeEmpty();
		targetDir.GetDirectories().Should().BeEmpty();
	}

	#endregion

	#region CopyDirectory

	[Test]
	public void CopyDirectory_CopiesDirectory(){
		//Arrange
		const string directoryName = "dir";
		const string subDirectoryName = "subDir";
		Dictionary<string, MockFileData> mockFileData = new();
		Ms.IFileSystem fs = new MockFileSystem(mockFileData);
		
		var targetDir = fs.Directory.CreateDirectory(directoryName);
		var subDir = targetDir.CreateSubdirectory(subDirectoryName);
		
		fs.File.Create(Path.Join(targetDir.FullName, "file.txt"));
		fs.File.Create(Path.Join(subDir.FullName, "file.txt"));
		FileSystem sut = new FileSystem(fs);
		Ms.IDriveInfo cDrive = fs.DriveInfo.GetDrives().First();
		
		//Act
		sut.CopyDirectory(targetDir.FullName, "newDir", true);
		
		// Assert
		fs.Directory.Exists("newDir").Should().BeTrue();
		string[] newDir = fs.Directory.GetDirectories("newDir");
		newDir.Should().HaveCount(1);
		string[] copiedFiles = fs.Directory.GetFiles(newDir[0]);
		copiedFiles.Should().HaveCount(1);
		copiedFiles.First().Should().Be(Path.Join(cDrive.Name,newDir[0], "file.txt"));
	}

	#endregion

	#region CreateDirectory

	[Test]
	public void CreateDirectory_Calls_CreateDirectory(){
		//Arrange
		var fs = new MockFileSystem();
		var cDrive = fs.DriveInfo.GetDrives().First();
		
		//Act
		var newDirInfo = new FileSystem(fs).CreateDirectory(Path.Join(cDrive.Name,"newDir"));
		
		//Assert
		var dirs = fs.Directory.GetDirectories(cDrive.Name);
		dirs.Where(d=> d== Path.Join(cDrive.Name, "newDir")).Should().NotBeEmpty();
		
	}

	#endregion
	
	#region DeleteFile

	[Test]
	public void DeleteFile_Returns_False_WhenFileDoesNotExist(){
		//This tests nothing
		new FileSystem(_mockFileSystem)
			.DeleteFile("first._cs").Should().BeTrue();
		
	} 
	
	[TestCase("")]
	[TestCase(null)]
	public void DeleteFile_Throws_When_FilePathNullOrEmpty(string filePath){
		//Arrange
		Action act = () => new FileSystem(_mockFileSystem)
			.DeleteFile(filePath);
		
		//Act && Assert
		act.Should().Throw<ArgumentNullException>();
	} 

	[Test]
	public void DeleteFile_Calls_Delete_WhenFileIsNotReadonly(){
		//Arrange
		const string fileName = "readonly.cs";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(fileName).Returns(true);
		fs.File.GetAttributes(fileName).Returns(FileAttributes.Normal);
		
		//Act
		bool actual = new FileSystem(fs).DeleteFile(fileName);
		
		//Assert
		actual.Should().BeTrue();
		fs.File.Received(1).Delete(fileName);
	} 
	
	[Test]
	public void DeleteFile_ResetsReadonlyAttribute_And_Calls_Delete_WhenFileIsReadonly(){
		//Arrange
		const string fileName = "readonly.cs";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(fileName).Returns(true);
		fs.File.GetAttributes(fileName).Returns(FileAttributes.ReadOnly);
		
		//Act
		bool actual = new FileSystem(fs).DeleteFile(fileName);
		
		//Assert
		actual.Should().BeTrue();
		fs.File.Received(1).Delete(fileName);
		
		// TODO: DeleteFile checks is file exists 3 times,
		// 1: IsReadOnlyFile
		// 2: ResetFileReadOnlyAttribute 
		// 3: ResetFileReadOnlyAttribute -> IsReadOnlyFile
		// Should we consider passing state/context around ?
		fs.File.Received(3).Exists(fileName);
		fs.File.Received(1).SetAttributes(fileName, Arg.Any<FileAttributes>());
		
	} 
	#endregion

	#region DeleteFileIfExists

	[Test]
	public void DeleteFileIfExists_CallsDelete_WhenFileExists() {
		const string fileName = "mockFile.cs";
		_mockFileData.Add(fileName, new MockFileData("some content"));
		var fs = new MockFileSystem(_mockFileData);
		new FileSystem(fs).DeleteFileIfExists(fileName);
		_mockFileSystem.File.Exists(fileName).Should().BeFalse();
	}
	
	[Test]
	public void DeleteFileIfExists_returns_WhenFileDoesNotExists() {
		//Arrange
		const string fileName = "mockFile.cs";
		Ms.IFileSystem fs = Substitute.For<Ms.IFileSystem>();
		fs.File.Exists(fileName).Returns(false);
		
		//Act
		new FileSystem(fs).DeleteFileIfExists(fileName);
		
		//Assert
		fs.File.Received(1).Exists(fileName);
		
		
	}

	#endregion
	
	#region ExistsFile
	
	[Test]
	public void Exists_Returns_True_WhenFileExists(){
		new FileSystem(_mockFileSystem)
			.ExistsFile("first.cs").Should().BeTrue();
	}
	
	[Test]
	public void Exists_Returns_True_WhenFileDoesNotExists(){
		new FileSystem(_mockFileSystem)
			.ExistsFile("first._cs").Should().BeFalse();
	}
	#endregion
	
	#region IsReadonlyFile

	[TestCase(FileAttributes.ReadOnly)]
	[TestCase(FileAttributes.ReadOnly | FileAttributes.Hidden)]
	[TestCase(FileAttributes.ReadOnly | FileAttributes.Encrypted)]
	[TestCase(FileAttributes.ReadOnly | FileAttributes.Compressed)]
	[TestCase(FileAttributes.Directory)]
	public void IsReadonlyFile_ReturnsTrue_When_FileIsReadonly(FileAttributes attributes){
		//Arrange
		const string fileName = "readonly.cs";
		_mockFileData[fileName].Attributes = attributes;
		
		//Act
		bool actual = new FileSystem(_mockFileSystem).IsReadOnlyFile(fileName);
		
		//Assert
		_ = (attributes & FileAttributes.ReadOnly) switch {
			0 => actual.Should().BeFalse(),
			var _ => actual.Should().BeTrue()
		};
	}

	#endregion

	#region GetDirectoryHash
	
	[Test]
	public void GetDirectoryHash_ThrowsException_WhenDirectoryDoesNotExist() {
		// Arrange
		const string nonExistentDirectory = "nonexistent/directory";
		
		// Act & Assert
		Assert.Throws<DirectoryNotFoundException>(() => 
			new FileSystem(_mockFileSystem).GetDirectoryHash(nonExistentDirectory, FileSystem.Algorithm.SHA256));
	}
	
	[Test]
	public void GetDirectoryHash_ReturnsEmptyString_WhenDirectoryIsEmpty() {
		// Arrange
		const string emptyDirectory = "empty/directory";
		var fs = new MockFileSystem();
		fs.Directory.CreateDirectory(emptyDirectory);
		
		// Act
		string hash = new FileSystem(fs).GetDirectoryHash(emptyDirectory, FileSystem.Algorithm.SHA256);
		
		// Assert
		hash.Should().BeEmpty();
	}
	
	[Test]
	public void GetDirectoryHash_ReturnsSameHash_ForIdenticalDirectories() {
		// Arrange
		var fs = new MockFileSystem();
		
		// Create two identical directories with the same files
		fs.Directory.CreateDirectory("/dir1/subdir");
		fs.Directory.CreateDirectory("/dir2/subdir");
		
		fs.File.WriteAllText("/dir1/file1.txt", "content1");
		fs.File.WriteAllText("/dir1/file2.txt", "content2");
		fs.File.WriteAllText("/dir1/subdir/file3.txt", "content3");
		                      
		fs.File.WriteAllText("/dir2/file1.txt", "content1");
		fs.File.WriteAllText("/dir2/file2.txt", "content2");
		fs.File.WriteAllText("/dir2/subdir/file3.txt", "content3");
		
		var fileSystem = new FileSystem(fs);
		
		// Act
		string hash1 = Environment.OSVersion.Platform == PlatformID.Win32NT 
			? fileSystem.GetDirectoryHash(@"C:\dir1", FileSystem.Algorithm.SHA256) 
			: fileSystem.GetDirectoryHash("/dir1", FileSystem.Algorithm.SHA256);
		string hash2 = Environment.OSVersion.Platform == PlatformID.Win32NT 
			? fileSystem.GetDirectoryHash(@"C:\dir2", FileSystem.Algorithm.SHA256) 
			: fileSystem.GetDirectoryHash("/dir2", FileSystem.Algorithm.SHA256);
		
		// Assert
		hash1.Should().NotBeEmpty();
		hash1.Should().Be(hash2,"the same content should produce same hash");
	}
	
	[Test]
	public void GetDirectoryHash_ReturnsDifferentHash_WhenFileContentsChange() {
		// Arrange
		var fs = new MockFileSystem();
		
		// Create directory structure
		fs.Directory.CreateDirectory("dir1");
		fs.File.WriteAllText("dir1/file1.txt", "content1");
		fs.File.WriteAllText("dir1/file2.txt", "content2");
		
		fs.Directory.CreateDirectory("dir2");
		fs.File.WriteAllText("dir2/file1.txt", "content1");
		fs.File.WriteAllText("dir2/file2.txt", "changed content"); // Different content
		
		var fileSystem = new FileSystem(fs);
		
		// Act
		string hash1 = fileSystem.GetDirectoryHash(@"C:\dir1", FileSystem.Algorithm.SHA256);
		string hash2 = fileSystem.GetDirectoryHash(@"C:\dir2", FileSystem.Algorithm.SHA256);
		
		// Assert
		hash1.Should().NotBe(hash2);
	}
	
	[Test]
	public void GetDirectoryHash_ReturnsDifferentHash_WhenFilePathsChange() {
		// Arrange
		var fs = new MockFileSystem();
		
		// Create directory structure
		fs.Directory.CreateDirectory("dir1");
		fs.File.WriteAllText("dir1/file1.txt", "content1");
		fs.File.WriteAllText("dir1/file2.txt", "content2");
		
		fs.Directory.CreateDirectory("dir2");
		fs.File.WriteAllText("dir2/file1.txt", "content1");
		fs.File.WriteAllText("dir2/different_name.txt", "content2"); // Same content, different name
		
		var fileSystem = new FileSystem(fs);
		
		// Act
		string hash1 = fileSystem.GetDirectoryHash(@"C:\dir1", FileSystem.Algorithm.SHA256);
		string hash2 = fileSystem.GetDirectoryHash(@"C:\dir2", FileSystem.Algorithm.SHA256);
		
		// Assert
		hash1.Should().NotBe(hash2);
	}
	
	[Test]
	public void GetDirectoryHash_ReturnsDifferentHash_WhenFilePathsChanges() {
		// Arrange
		var fs = new MockFileSystem();
		
		// Create directory structure
		fs.Directory.CreateDirectory("dir1");
		fs.File.WriteAllText("dir1/file1.txt", "content1");
		fs.File.WriteAllText("dir1/file2.txt", "content2");
		
		fs.Directory.CreateDirectory("dir2");
		fs.Directory.CreateDirectory("dir2/subdir");
		fs.File.WriteAllText("dir2/file1.txt", "content1");
		fs.File.WriteAllText("dir2/subdir/file2.txt", "content2"); // Same content, different name
		
		var fileSystem = new FileSystem(fs);
		
		// Act
		string hash1 = fileSystem.GetDirectoryHash(@"C:\dir1", FileSystem.Algorithm.SHA256);
		string hash2 = fileSystem.GetDirectoryHash(@"C:\dir2", FileSystem.Algorithm.SHA256);
		
		// Assert
		hash1.Should().NotBe(hash2);
	}
	
	[TestCase(FileSystem.Algorithm.SHA1)]
	[TestCase(FileSystem.Algorithm.SHA256)]
	[TestCase(FileSystem.Algorithm.SHA384)]
	[TestCase(FileSystem.Algorithm.SHA512)]
	[TestCase(FileSystem.Algorithm.MD5)]
	public void GetDirectoryHash_WorksWithAllAlgorithms(FileSystem.Algorithm algorithm) {
		// Arrange
		var fs = new MockFileSystem();
		fs.Directory.CreateDirectory("dir");
		fs.File.WriteAllText("dir/file1.txt", "content1");
		fs.File.WriteAllText("dir/file2.txt", "content2");
		
		// Act
		string hash = new FileSystem(fs).GetDirectoryHash(@"C:\dir", algorithm);
		
		// Assert
		hash.Should().NotBeEmpty();
	}
	
	#endregion
	
}
