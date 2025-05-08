using System;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Clio.Tests.Extensions;

namespace Clio.Tests.Infrastructure;

internal class TestFileSystem
{
    internal static IFileSystem MockExamplesFolder(string exampleFolderName)
    {
        MockFileSystem mockFileSystem = new();
        mockFileSystem.MockExamplesFolder(exampleFolderName);
        return mockFileSystem;
    }


    internal static MockFileSystem MockFileSystem()
    {
        MockFileSystem mockFileSystem = new();
        return mockFileSystem;
    }


    public static string ReadExamplesFile(string folderName, string fileName)
    {
        string filePath = Path.Combine(FileSystemExtension.ExamplesFolderPath, folderName, fileName);
        return File.ReadAllText(filePath);
    }
}
