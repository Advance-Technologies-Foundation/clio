﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Clio.Common;
using CommandLine;

namespace Clio.Command;

[Verb("mock-dataFolderPath", Aliases = new[] { "dataFolderPath-mock" }, HelpText = "Setup mock data path")]
public class MockDataCommandOptions : RemoteCommandOptions
{
    [Option('d', "data", Required = true, HelpText = "path to save data")]
    public string Data { get; internal set; }

    [Option('m', "model", Required = true, HelpText = "Models folder path")]
    public string Models { get; internal set; }

    [Option('x', "exclude-models", Default = "VwSys", Required = false, HelpText = "Exclude models pattern")]
    public string ExcludeModel { get; internal set; }
}

internal class MockDataCommand : RemoteCommand<MockDataCommandOptions>
{
    private readonly IFileSystem _fileSystem;

    public MockDataCommand(IApplicationClient applicationClient, EnvironmentSettings environmentSettings,
        IFileSystem fileSystem)
        : base(applicationClient, environmentSettings)
    {
        _fileSystem = fileSystem;
        EnvironmentSettings = environmentSettings;
    }

    private string GetModelDataData(string findedModel)
    {
        string oDataModelUrl = $"{RootPath}/odata/{findedModel}";
        string x = ApplicationClient.ExecuteGetRequest(oDataModelUrl, 10_000, 3);
        return x;
    }

    private void LoadODataData(string models, string dataFolderPath, string excludeModel)
    {
        List<string> foundModels = string.IsNullOrWhiteSpace(excludeModel)
            ? FindModels(models)
            : FindModels(models).Where(m => !m.Contains(excludeModel)).ToList();

        int totalCount = foundModels.Count;
        Logger.WriteInfo($"Found {totalCount} models");
        int i = 0;

        Parallel.ForEach(foundModels, new ParallelOptions { MaxDegreeOfParallelism = 8 },
            foundModel =>
            {
                i++;
                try
                {
                    string modelODataDataFilePath = Path.Combine(dataFolderPath, $"{foundModel}.json");
                    string modelOdataData = GetModelDataData(foundModel);
                    _fileSystem.WriteAllTextToFile(modelODataDataFilePath, modelOdataData);
                    Logger.WriteInfo(
                        $"[{i} from {totalCount} ] Data for model {foundModel} saved to {modelODataDataFilePath}");
                }
                catch (Exception)
                {
                    Logger.WriteWarning($"Data for model {foundModel} not saved");
                }
            });
    }

    internal List<string> FindModels(string models)
    {
        List<string> schemaNames = [];
        List<string> files = [.. _fileSystem.GetFiles(models, "*.*", SearchOption.AllDirectories)];
        foreach (string file in files)
        {
            string fileContent = _fileSystem.ReadAllText(file);
            schemaNames.AddRange(ExtractSchemaNames(fileContent));
        }

        return schemaNames.Distinct().ToList();
    }

    public static List<string> ExtractSchemaNames(string sourceCode)
    {
        List<string> schemaNames = [];
        string pattern = @"\[Schema\(""([^""]+)""\)\]";
        MatchCollection matches = Regex.Matches(sourceCode, pattern);

        foreach (Match match in matches.Cast<Match>())
        {
            if (match.Groups.Count > 1)
            {
                schemaNames.Add(match.Groups[1].Value);
            }
        }

        return schemaNames;
    }

    // public MockDataCommand(IFileSystem fileSystem, IApplicationClient applicationClient) {
    //  this._fileSystem = fileSystem;
    //  this.ApplicationClient = applicationClient;
    // }

    // public MockDataCommand(FileSystem clioFileSystem) {
    //  this._fileSystem = clioFileSystem;
    // }
    public override int Execute(MockDataCommandOptions options)
    {
        try
        {
            LoadODataData(options.Models, options.Data, options.ExcludeModel);
            string commandName = typeof(MockDataCommandOptions).GetCustomAttribute<VerbAttribute>()?.Name;
            Logger.WriteInfo($"Done {commandName}");
            return 0;
        }
        catch (SilentException)
        {
            return 1;
        }
        catch (Exception e)
        {
            Logger.WriteError(e.Message);
            return 1;
        }
    }
}
