using System;
using System.IO;
using System.IO.Abstractions.TestingHelpers;

namespace Clio.Tests.Extensions;

public static class FileSystemExtension
{
    public static string OriginFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
    public static string ExamplesFolderPath = Path.Combine(OriginFolderPath, "Examples");

    internal static void MockExamplesFolder(this MockFileSystem mockFileSystem, string exampleFolderName,
        string destinationFolderName = null)
    {
        string examplesTestFolder = Path.Combine(ExamplesFolderPath, exampleFolderName);
        mockFileSystem.MockFolder(examplesTestFolder, destinationFolderName);
    }

    public static void MockFolder(this MockFileSystem mockFileSystem, string folderName,
        string destinationFolderName = null)
    {
        string[] exampleFiles = Directory.GetFiles(folderName);
        if (!string.IsNullOrEmpty(destinationFolderName))
        {
            mockFileSystem.AddDirectory(destinationFolderName);
        }

        foreach (string exampleFile in exampleFiles)
        {
            FileInfo fileInfo = new (exampleFile);
            string destinationFilePath = string.IsNullOrEmpty(destinationFolderName)
                ? fileInfo.Name
                : Path.Combine(destinationFolderName, fileInfo.Name);
            mockFileSystem.AddFile(destinationFilePath, new MockFileData(File.ReadAllBytes(fileInfo.FullName)));
        }

        string[] subDirs = Directory.GetDirectories(folderName);
        foreach (string subFolder in subDirs)
        {
            string subFolderName = new DirectoryInfo(subFolder).Name;
            string subFolderPath = Path.Combine(destinationFolderName, subFolderName);
            MockFolder(mockFileSystem, subFolder, subFolderPath);
        }
    }

    public static void MockFile(this MockFileSystem mockFileSystem, string filePath)
    {
        string[] filePathBlocks = filePath.Split(Path.DirectorySeparatorChar);
        string currentPath = string.Empty;
        for (int i = 0; i < filePathBlocks.Length - 2; i++)
        {
            currentPath = Path.Combine(currentPath, filePathBlocks[i]);
            if (!string.IsNullOrEmpty(currentPath))
            {
                mockFileSystem.AddDirectory(currentPath);
            }
        }

        mockFileSystem.AddFile(filePath, new MockFileData("{empty file}"));
    }

    public static void MockFolderWithDir(this MockFileSystem mockFileSystem, string folderPath)
    {
        string[] exampleFiles = Directory.GetFiles(folderPath);
        foreach (string exampleFile in exampleFiles)
        {
            FileInfo fileInfo = new (exampleFile);
            mockFileSystem.AddFile(fileInfo.FullName, new MockFileData(File.ReadAllBytes(fileInfo.FullName)));
        }

        string[] subDirs = Directory.GetDirectories(folderPath);
        foreach (string subFolder in subDirs)
        {
            mockFileSystem.AddDirectory(subFolder);
            MockFolderWithDir(mockFileSystem, subFolder);
        }
    }
}
