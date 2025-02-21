using System;
using Clio.Command;
using Clio.Common;
using CommandLine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Clio;
using Clio.Querry;

namespace Clio.Querry
{
    [Verb("call-service", Aliases = new[] { "cs" }, HelpText = "Call Service Request")]
    public class CallServiceCommandOptions : RemoteCommandOptions
    {
        [Option('f', "input", Required = true, HelpText = "Request file", Separator = ' ')]
        public string ReqeustFileName { get; set; }

        [Option('d', "destination", Required = true, HelpText = "Destination set")]
        public string ResultFileName { get; set; }

        [Option('v', "variables", Required = false, HelpText = "Result file", Separator = ';')]
        public IEnumerable<string> Variables { get; set; }

        [Option("service-path", Required = false, HelpText = "Route service path")]
        public string ServicePath { get; set; }
    }

    [Verb("dataservice", Aliases = new[] { "ds" }, HelpText = "DataService Request")]
    public class DataServiceQuerryOptions : CallServiceCommandOptions
    {
        [Option('t', "type", Required = true, HelpText = "Operation type", Separator = ' ')]
        public string OperationType { get; set; }
    }
    
    public class CallServiceCommand : BaseServiceCommand<CallServiceCommandOptions>
    {
        public CallServiceCommand(IApplicationClient applicationClient,
            EnvironmentSettings settings,
            IServiceUrlBuilder serviceUrlBuilder)
            : base(applicationClient, settings, serviceUrlBuilder)
        {
        }
    }

    public abstract class BaseServiceCommand<T> : RemoteCommand<T> where T : CallServiceCommandOptions
    {
        protected readonly IServiceUrlBuilder ServiceUrlBuilderInstance;

        protected BaseServiceCommand(IApplicationClient applicationClient,
            EnvironmentSettings settings,
            IServiceUrlBuilder serviceUrlBuilderInstance)
            : base(applicationClient, settings)
        {
            ServiceUrlBuilderInstance = serviceUrlBuilderInstance;
        }

        protected virtual string BuildUrl(T options)
        {
            return ServiceUrlBuilderInstance.Build(options.ServicePath);
        }

        protected string GetRequestData(string requestFileName)
        {
            FileInfo fi = new FileInfo(requestFileName);
            if (!fi.Exists)
            {
                throw new FileNotFoundException("File not found", requestFileName);
            }
            return File.ReadAllText(requestFileName);
        }

        public override int Execute(T options)
        {
            string requestData = GetRequestData(options.ReqeustFileName);
            if (options.Variables != null && options.Variables.Any())
            {
                requestData = ReplaceVariablesInJson(requestData, options.Variables);
            }
            ExecuteServiceRequest(BuildUrl(options), requestData, options.ResultFileName);
            return 0;
        }

        protected string ExecuteServiceRequest(string url, string requestData, string resultFileName = null)
        {
            string jsonResult = ApplicationClient.ExecutePostRequest(url, requestData);

            if (string.IsNullOrWhiteSpace(resultFileName))
            {
                Logger.WriteInfo(jsonResult);
            }
            else
            {
                string outputFileName = SetOutputFileName(resultFileName);
                File.WriteAllText(outputFileName, jsonResult);
            }

            return jsonResult;
        }

        protected string ReplaceVariablesInJson(string json, IEnumerable<string> variables)
        {
            if (variables == null)
                return json;

            foreach (var variable in variables) {
                var pattern = "{{" + variable.Split('=')[0] + "}}";
                var regex = new Regex(pattern);
                var match = regex.Match(json);
                if (match.Success)
                {
                    json = regex.Replace(json, variable.Split('=')[1]);
                }
            }
            return json;
        }

        protected string SetOutputFileName(string resultFileName)
        {
            FileInfo fi = new FileInfo(resultFileName);
            string fileName = Path.GetFileNameWithoutExtension(resultFileName);
            int count = fi.Directory.GetFiles($"{fileName}*{fi.Extension}").Count();
            if (count > 0)
            {
                return Path.Combine(fi.Directory.ToString(), $"{fileName}-({count + 1}){fi.Extension}");
            }
            return Path.Combine(fi.Directory.ToString(), $"{fileName}{fi.Extension}");
        }
    }

    public class DataServiceQuery : BaseServiceCommand<DataServiceQuerryOptions>
    {
        public DataServiceQuery(IApplicationClient applicationClient,
            EnvironmentSettings settings,
            IServiceUrlBuilder serviceUrlBuilder)
            : base(applicationClient, settings, serviceUrlBuilder)
        {
        }

        protected override string BuildUrl(DataServiceQuerryOptions options)
        {
            return options.OperationType.ToUpperInvariant() switch
            {
                "SELECT" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Select),
                "INSERT" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Insert),
                "UPDATE" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Update),
                "DELETE" => ServiceUrlBuilderInstance.Build(ServiceUrlBuilder.KnownRoute.Delete),
                var _ => throw new Exception("Unknown operation type"),
            };
        }
    }
}