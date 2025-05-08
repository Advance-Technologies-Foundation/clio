using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Json;
using System.Linq;
using System.Reflection;
using Clio.Common;
using Newtonsoft.Json;
using File = System.IO.File;

namespace Clio;

internal interface IPackageConverter
{

    #region Methods: Public

    int Convert(ConvertOptions options);

    #endregion

}

internal class PackageConverter : IPackageConverter
{

    #region Fields: Private

    private readonly string prefix = string.Empty;
    private readonly ILogger _logger;

    #endregion

    #region Constructors: Public

    public PackageConverter(ILogger logger)
    {
        _logger = logger;
    }

    #endregion

    #region Methods: Private

    private static void CreateFromTpl(string tplPath, string filePath, string packageName, List<string> fileNames,
        string maintainer, List<string> refs, List<string> deps)
    {
        string text = ReplaceMacro(File.ReadAllText(tplPath), packageName, fileNames, maintainer, refs, deps);
        FileInfo file = new(filePath);
        using (StreamWriter sw = file.CreateText())
        {
            sw.Write(text);
        }
    }

    private static string GetFilesPaths(List<string> fileNames)
    {
        string template = "<Compile Include=\"Files\\cs\\{0}\" />" + Environment.NewLine + "\t";
        string result = string.Empty;
        foreach (string fileName in fileNames)
        {
            result += string.Format(template, fileName);
        }
        return result;
    }

    private static string GetProjectsPath(List<string> deps)
    {
        string template = "\t<ProjectReference Include=\"..\\{0}\\{0}.csproj\"><Name>{0}</Name></ProjectReference>" +
            Environment.NewLine + "\t";
        string result = string.Empty;
        foreach (string dep in deps)
        {
            result += string.Format(template, dep);
        }
        return result;
    }

    private static List<string> GetRefFromFile(string path)
    {
        List<string> result = new();
        string line;
        try
        {
            StreamReader sr = new(path);
            line = sr.ReadLine();
            line = sr.ReadLine();
            line = sr.ReadLine();
            while (line != null)
            {
                if (line.Contains("=") || line.Contains(":"))
                {
                    line = sr.ReadLine();
                    continue;
                }
                if (line.Contains("{") || line.Contains("}") || line.Contains("[") || line.Contains("]"))
                {
                    break;
                }
                if (line.ToLower().Contains("using"))
                {
                    line = line.Replace("\t", string.Empty).Replace("using ", string.Empty).Replace(";", string.Empty)
                               .Replace(" ", string.Empty);
                }
                else
                {
                    line = sr.ReadLine();
                    continue;
                }
                result.Add(line);
                line = sr.ReadLine();
            }
            sr.Close();
        }
        catch (Exception)
        {
            return new List<string>();
        }
        return result;
    }

    private static string GetRefPaths(List<string> refs)
    {
        string template = "\t<Reference Include=\"{0}\" />" + Environment.NewLine;
        string result = string.Empty;
        foreach (string _ref in refs)
        {
            result += string.Format(template, _ref);
        }
        return result;
    }

    private static string GetTplPath(string tplPath)
    {
        string fullPath;
        if (File.Exists(tplPath))
        {
            fullPath = tplPath;
        }
        else
        {
            string executingPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            fullPath = Path.Combine(executingPath, tplPath);
        }
        return fullPath;
    }

    private static List<string> MoveFiles(string schemasPath, string filesPath, string extension)
    {
        List<string> fileNames = new();
        DirectoryInfo dir = new(schemasPath);
        foreach (DirectoryInfo schemaDirectory in dir.GetDirectories())
        {
            foreach (FileInfo file in schemaDirectory.GetFiles(extension))
            {
                string destFilePath = Path.Combine(filesPath, file.Name);
                if (File.Exists(destFilePath))
                {
                    File.Delete(destFilePath);
                }
                fileNames.Add(file.Name);
                file.MoveTo(destFilePath);
            }
        }
        return fileNames;
    }

    private static string ReplaceMacro(string text, string packageName, List<string> fileNames, string maintainer,
        List<string> refs, List<string> deps)
    {
        return text.Replace("$safeprojectname$", packageName)
                   .Replace("$userdomain$", maintainer)
                   .Replace("$guid1$", Guid.NewGuid().ToString())
                   .Replace("$year$", DateTime.Now.Year.ToString())
                   .Replace("$modifiedon$", ToJsonMsDate(DateTime.Now))
                   .Replace("$ref1$", GetRefPaths(refs))
                   .Replace("$files$", GetFilesPaths(fileNames))
                   .Replace("$projects$", GetProjectsPath(deps));
    }

    private static string ToJsonMsDate(DateTime date)
    {
        JsonSerializerSettings microsoftDateFormatSettings = new()
        {
            DateFormatHandling = DateFormatHandling.MicrosoftDateFormat
        };
        return JsonConvert.SerializeObject(date, microsoftDateFormatSettings).Replace("\"", "").Replace("\\", "");
    }

    private int ConvertPackage(ConvertOptions options)
    {
        try
        {
            string packageFolderPath = options.Path;
            DirectoryInfo packageDirectory = new(packageFolderPath);
            FileInfo[] existingProjects = packageDirectory.GetFiles("*.csproj");
            string packageName = packageDirectory.Name;
            if (existingProjects.Length > 0)
            {
                throw new Exception(
                    $"Package {packageName} contains existing .proj file. Remove existing project from package folder and try again.");
            }
            _logger.WriteInfo($"Start converting package '{packageName}'.");
            string packagePath = Path.Combine(options.Path, prefix);
            string backupPath = packageName + ".zip";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }
            ZipFile.CreateFromDirectory(packagePath, backupPath);
            _logger.WriteInfo($"Created backup package '{packageName}'.");
            List<string> fileNames = options.ConvertSourceCode ? MoveCsFiles(packagePath) : new List<string>();
            CorrectingFiles(packagePath);
            CreateProjectInfo(packagePath, packageName, fileNames);
            _logger.WriteInfo($"Package '{packageName}' was converted.");
            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.Message);
            return 1;
        }
    }

    private void CorrectingFiles(string path)
    {
        string csFilesPath = Path.Combine(path, "Files", "cs");
        string resourcePath = Path.Combine(path, "Resources");
        string schemasPath = Path.Combine(path, "Schemas");
        List<string> names = new();
        if (!Directory.Exists(csFilesPath))
        {
            Directory.CreateDirectory(csFilesPath);
        }
        DirectoryInfo csFilesDir = new(csFilesPath);
        foreach (FileInfo file in csFilesDir.GetFiles("*.cs"))
        {
            string name = file.Name.Split('.')[0];
            names.Add(name);
            DirectoryInfo currentResourcesDirectory = new(Path.Combine(resourcePath, name + ".SourceCode"));
            if (!currentResourcesDirectory.Exists)
            {
                break;
            }
            int countLines = 0;
            foreach (FileInfo resourceFile in currentResourcesDirectory.GetFiles("*.xml"))
            {
                int currentCount = File.ReadAllLines(resourceFile.FullName).Length;
                countLines = countLines > currentCount ? countLines : currentCount;
            }
            if (countLines < 9)
            {
                currentResourcesDirectory.Delete(true);
                Directory.Delete(Path.Combine(schemasPath, name), true);
            }
            else
            {
                File.WriteAllText(Path.Combine(schemasPath, name, file.Name), string.Empty);
            }
        }
    }

    private void CreateProjectInfo(string path, string name, List<string> fileNames)
    {
        string filePath = Path.Combine(path, name + "." + "csproj");
        string csFilesPath = Path.Combine(path, "Files", "cs");
        List<string> refs = GetRefs(csFilesPath, fileNames);
        string descriptorPath = Path.Combine(path, "descriptor.json");
        string descriptorContent = File.ReadAllText(descriptorPath);
        JsonObject jsonDoc = (JsonObject)JsonObject.Parse(descriptorContent);
        string maintainer = jsonDoc["Descriptor"]["Maintainer"];
        List<string> depends = new();
        foreach (object depend in jsonDoc["Descriptor"]["DependsOn"])
        {
            string curName = depend.ToString().Split("\": \"")[1].Split("\"")[0];
            depends.Add(curName);
        }
        CreateFromTpl(GetTplPath(CreatioPackage.EditProjTpl), filePath, name, fileNames, maintainer, refs, depends);
        string propertiesDirPath = Path.Combine(path, "Properties");
        Directory.CreateDirectory(propertiesDirPath);
        string propertiesFilePath = Path.Combine(propertiesDirPath, "AssemblyInfo.cs");
        CreateFromTpl(GetTplPath(CreatioPackage.AssemblyInfoTpl), propertiesFilePath, name, new List<string>(),
            maintainer, refs, depends);
        string packagesConfigFilePath = Path.Combine(path, "packages.config");
        CreateFromTpl(GetTplPath(CreatioPackage.PackageConfigTpl), packagesConfigFilePath, name, new List<string>(),
            maintainer, refs, depends);
    }

    private List<string> GetRefs(string path, List<string> files)
    {
        List<string> result = new();
        foreach (string fileName in files)
        {
            List<string> refs = GetRefFromFile(Path.Combine(path, fileName));
            foreach (string line in refs)
            {
                if (!result.Contains(line))
                {
                    result.Add(line);
                }
            }
        }

        return result;
    }

    private List<string> MoveCsFiles(string path)
    {
        string schemasPath = Path.Combine(path, "Schemas");
        string csFilesPath = Path.Combine(path, "Files", "cs");
        Directory.CreateDirectory(csFilesPath);
        return MoveFiles(schemasPath, csFilesPath, "*.cs");
    }

    #endregion

    #region Methods: Public

    public int Convert(ConvertOptions options)
    {
        try
        {
            List<string> packagePathes = new();
            if (options.Path == null)
            {
                options.Path = Environment.CurrentDirectory;
            }
            if (string.IsNullOrEmpty(options.Name))
            {
                DirectoryInfo info = new(options.Path);
                if (File.Exists(Path.Combine(info.FullName, prefix, "descriptor.json")))
                {
                    packagePathes.Add(info.FullName);
                }
                foreach (DirectoryInfo directory in info.GetDirectories())
                {
                    if (File.Exists(Path.Combine(directory.FullName, prefix, "descriptor.json")))
                    {
                        packagePathes.Add(directory.FullName);
                    }
                }
            }
            else
            {
                packagePathes = options.Name.Split(',').Select(a => a.Trim()).ToList();
            }
            foreach (string packagePath in packagePathes)
            {
                ConvertOptions convertOptions = new()
                {
                    Path = packagePath, ConvertSourceCode = options.ConvertSourceCode
                };
                if (ConvertPackage(convertOptions) == 1)
                {
                    return 1;
                }
            }
            return 0;
        }
        catch (Exception e)
        {
            _logger.WriteError(e.Message);
            return 1;
        }
    }

    #endregion

}
