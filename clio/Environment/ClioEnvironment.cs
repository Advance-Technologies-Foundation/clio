using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Clio.UserEnvironment;

internal class CreatioEnvironment : ICreatioEnvironment
{
    private const string PathVariableName = "PATH";

    public static bool IsNetCore => Settings.IsNetCore;
    public static string EnvironmentName { get; set; }
    public static EnvironmentSettings Settings { get; set; }


    private IResult RegisterPath(string path, EnvironmentVariableTarget target)
    {
        EnvironmentResult result = new();
        string pathValue = Environment.GetEnvironmentVariable(PathVariableName, target);
        if (string.IsNullOrEmpty(pathValue))
        {
            pathValue = string.Empty;
        }

        if (pathValue.Contains(path))
        {
            result.AppendMessage($"{PathVariableName} variable already registered!");
            return result;
        }

        result.AppendMessage($"register path {path} in {PathVariableName} variable.");
        string value = string.Concat(pathValue, Path.PathSeparator + path.Trim(Path.PathSeparator));
        Environment.SetEnvironmentVariable(PathVariableName, value, target);
        result.AppendMessage($"{PathVariableName} variable registered.");
        return result;
    }

    private IResult UnregisterPath(EnvironmentVariableTarget target)
    {
        EnvironmentResult result = new();
        string pathValue = Environment.GetEnvironmentVariable(PathVariableName, target);
        string[] paths = pathValue.Split(Path.PathSeparator);
        string clioPath = string.Empty;
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                DirectoryInfo dir = new(path);
                FileInfo[] files = dir.GetFiles("clio.cmd");
                if (files.Length > 0)
                {
                    clioPath = path;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(clioPath))
        {
            result.AppendMessage($"Application already unregistered!");
            return result;
        }

        result.AppendMessage($"Unregister path {clioPath} in {PathVariableName} variable.");
        string newValue = pathValue.Replace(clioPath, string.Empty)
            .Replace(string.Concat(Path.PathSeparator, Path.PathSeparator), Path.PathSeparator.ToString());
        Environment.SetEnvironmentVariable(PathVariableName, newValue, target);
        result.AppendMessage($"{PathVariableName} variable unregistered.");
        return result;
    }

    public string GetRegisteredPath()
    {
        string? environmentPath = Environment.GetEnvironmentVariable(PathVariableName);
        string[] cliPath = environmentPath?.Split(Path.PathSeparator);
        return cliPath?.FirstOrDefault(p => p.Contains("clio"));
    }

    public IResult UserRegisterPath(string path) => RegisterPath(path, EnvironmentVariableTarget.User);

    public IResult MachineRegisterPath(string path) => RegisterPath(path, EnvironmentVariableTarget.Machine);

    public IResult MachineUnregisterPath() => UnregisterPath(EnvironmentVariableTarget.Machine);

    public IResult UserUnregisterPath() => UnregisterPath(EnvironmentVariableTarget.User);

    public string GetAssemblyFolderPath() => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
}
