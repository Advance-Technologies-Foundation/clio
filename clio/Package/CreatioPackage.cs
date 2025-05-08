using System;
using System.IO;
using Clio.Common;
using Newtonsoft.Json;

namespace Clio;

public class CreatioPackage
{

    #region Constants: Public

    public const string AssemblyInfoName = "AssemblyInfo.cs";
    public const string CsprojExtension = "csproj";
    public const string DescriptorName = "descriptor.json";
    public const string IgnoreFileName = "clioignore";
    public const string PackageConfigName = "packages.config";
    public const string PlaceholderFileName = "placeholder.txt";
    public const string PropertiesDirName = "Properties";
    public const string SlnExtension = "sln";

    #endregion

    #region Fields: Private

    private readonly string[] _pkgDirectories =
    {
        "Assemblies", "Data", "Schemas", "SqlScripts", "Resources", "Files", "Files\\cs"
    };

    private DateTime _createdOn;

    #endregion

    #region Constructors: Protected

    protected CreatioPackage(string packageName, string maintainer)
    {
        PackageName = packageName;
        Maintainer = maintainer;
        CreatedOn = DateTime.UtcNow;
        FullPath = Path.Combine(Environment.CurrentDirectory, packageName);
    }

    #endregion

    #region Properties: Private

    private static string DescriptorTpl => $"tpl{Path.DirectorySeparatorChar}{DescriptorName}.tpl";

    private static string ProjTpl => $"tpl{Path.DirectorySeparatorChar}Proj.{CsprojExtension}.tpl";

    private string ProjectFileName => $"{PackageName}.{CsprojExtension}";

    private string SolutionFileName => $"{SolutionName}.{SlnExtension}";

    private string SolutionName => PackageName;

    #endregion

    #region Properties: Public

    public static string AssemblyInfoTpl => $"tpl{Path.DirectorySeparatorChar}{AssemblyInfoName}.tpl";

    public static string EditProjTpl => $"tpl{Path.DirectorySeparatorChar}EditProj.{CsprojExtension}.tpl";

    public static string IgnoreFileTpl =>
        $"tpl{Path.DirectorySeparatorChar}package{Path.DirectorySeparatorChar}{IgnoreFileName}";

    public static string PackageConfigTpl => $"tpl{Path.DirectorySeparatorChar}{PackageConfigName}.tpl";

    public DateTime CreatedOn
    {
        get => _createdOn;
        protected set => _createdOn = GetDateTimeTillSeconds(value);
    }

    public string FullPath { get; protected set; }

    public string Maintainer { get; }

    public string PackageName { get; }

    public Guid ProjectId { get; protected set; }

    #endregion

    #region Methods: Private

    private static DateTime GetDateTimeTillSeconds(DateTime dateTime)
    {
        return dateTime.AddTicks(-(dateTime.Ticks % TimeSpan.TicksPerSecond));
    }

    private static string ToJsonMsDate(DateTime date)
    {
        JsonSerializerSettings microsoftDateFormatSettings = new()
        {
            DateFormatHandling = DateFormatHandling.MicrosoftDateFormat
        };
        return JsonConvert.SerializeObject(date, microsoftDateFormatSettings).Replace("\"", "").Replace("\\", "");
    }

    private void AddPlaceholderFile(string dirPath)
    {
        string placeholderPath = Path.Combine(dirPath, PlaceholderFileName);
        File.Create(placeholderPath).Dispose();
    }

    private bool CreateFromTpl(string tplPath, string filePath)
    {
        if (GetTplPath(tplPath, out string fullTplPath))
        {
            string text = ReplaceMacro(File.ReadAllText(fullTplPath));
            File.WriteAllText(filePath, text);
            return true;
        }
        return false;
    }

    private void ExecuteDotnetCommand(string command)
    {
        IProcessExecutor processExecutor = new ProcessExecutor();
        IDotnetExecutor dotnetExecutor = new DotnetExecutor(processExecutor);
        dotnetExecutor.Execute(command, true, FullPath);
    }

    private bool GetTplPath(string tplPath, out string fullPath)
    {
        if (File.Exists(tplPath))
        {
            fullPath = tplPath;
            return true;
        }
        string envPath = GetExecutingDirectorybyAppDomain();
        if (!string.IsNullOrEmpty(envPath))
        {
            fullPath = Path.Combine(envPath, tplPath);
            return true;
        }
        fullPath = null;
        return false;
    }

    private string ReplaceMacro(string text)
    {
        return text.Replace("$safeprojectname$", PackageName)
                   .Replace("$userdomain$", Maintainer)
                   .Replace("$guid1$", ProjectId.ToString())
                   .Replace("$year$", CreatedOn.Year.ToString())
                   .Replace("$modifiedon$", ToJsonMsDate(CreatedOn));
    }

    #endregion

    #region Methods: Protected

    protected CreatioPackage CreateAssemblyInfo()
    {
        Directory.CreateDirectory(Path.Combine(FullPath, PropertiesDirName));
        string filePath = Path.Combine(FullPath, PropertiesDirName, AssemblyInfoName);
        CreateFromTpl(AssemblyInfoTpl, filePath);
        return this;
    }

    protected CreatioPackage CreateEmptyClass()
    {
        string filePath = Path.Combine(FullPath, "Files\\cs", "EmptyClass.cs");
        File.Create(filePath).Dispose();
        return this;
    }

    protected CreatioPackage CreatePackageConfig()
    {
        string filePath = Path.Combine(FullPath, PackageConfigName);
        CreateFromTpl(PackageConfigTpl, filePath);
        return this;
    }

    protected CreatioPackage CreatePackageDirectories()
    {
        foreach (string directory in _pkgDirectories)
        {
            DirectoryInfo dInfo = Directory.CreateDirectory(Path.Combine(FullPath, directory));
            AddPlaceholderFile(dInfo.FullName);
        }
        return this;
    }

    protected CreatioPackage CreatePackageFiles()
    {
        CreatePkgDescriptor()
            .CreateProj()
            .CreateSolution()
            .CreatePackageConfig()
            .CreateAssemblyInfo()
            .CreateEmptyClass();
        return this;
    }

    protected CreatioPackage CreatePkgDescriptor()
    {
        string filePath = Path.Combine(FullPath, DescriptorName);
        CreateFromTpl(DescriptorTpl, filePath);
        return this;
    }

    protected CreatioPackage CreateProj()
    {
        string filePath = Path.Combine(FullPath, ProjectFileName);
        CreateFromTpl(ProjTpl, filePath);
        return this;
    }

    protected CreatioPackage CreateSolution()
    {
        ExecuteDotnetCommand($"new sln -n {SolutionName}");
        ExecuteDotnetCommand($"sln {SolutionFileName} add {ProjectFileName}");
        return this;
    }

    #endregion

    #region Methods: Internal

    internal void RemovePackageConfig()
    {
        string filePath = Path.Combine(FullPath, PackageConfigName);
        File.Delete(filePath);
    }

    #endregion

    #region Methods: Public

    public static CreatioPackage CreatePackage(string name, string maintainer)
    {
        return new CreatioPackage(name, maintainer)
        {
            ProjectId = Guid.NewGuid()
        };
    }

    public static string GetExecutingDirectorybyAppDomain()
    {
        string path = AppDomain.CurrentDomain.BaseDirectory;
        return path;
    }

    public void Create()
    {
        CreatePackageDirectories().CreatePackageFiles();
    }

    #endregion

}
