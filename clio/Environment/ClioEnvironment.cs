﻿using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Clio.UserEnvironment;

internal class CreatioEnvironment : ICreatioEnvironment
{

    #region Constants: Private

    private const string PathVariableName = "PATH";

    #endregion

    #region Properties: Public

    public static string EnvironmentName { get; set; }

    public static bool IsNetCore => Settings.IsNetCore;

    public static EnvironmentSettings Settings { get; set; }

    #endregion

    #region Methods: Private

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
            result.AppendMessage("Application already unregistered!");
            return result;
        }
        result.AppendMessage($"Unregister path {clioPath} in {PathVariableName} variable.");
        string newValue = pathValue.Replace(clioPath, string.Empty)
                                   .Replace(string.Concat(Path.PathSeparator, Path.PathSeparator),
                                       Path.PathSeparator.ToString());
        Environment.SetEnvironmentVariable(PathVariableName, newValue, target);
        result.AppendMessage($"{PathVariableName} variable unregistered.");
        return result;
    }

    #endregion

    #region Methods: Public

    public string GetAssemblyFolderPath()
    {
        return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    }

    public string GetRegisteredPath()
    {
        string environmentPath = Environment.GetEnvironmentVariable(PathVariableName);
        string[] cliPath = environmentPath?.Split(Path.PathSeparator);
        return cliPath?.FirstOrDefault(p => p.Contains("clio"));
    }

    public IResult MachineRegisterPath(string path)
    {
        return RegisterPath(path, EnvironmentVariableTarget.Machine);
    }

    public IResult MachineUnregisterPath()
    {
        return UnregisterPath(EnvironmentVariableTarget.Machine);
    }

    public IResult UserRegisterPath(string path)
    {
        return RegisterPath(path, EnvironmentVariableTarget.User);
    }

    public IResult UserUnregisterPath()
    {
        return UnregisterPath(EnvironmentVariableTarget.User);
    }

    #endregion

}
