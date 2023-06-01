using Autofac;
using Clio.Common.ScenarioHandlers;
using Clio.UserEnvironment;
using CommandLine;
using DocumentFormat.OpenXml.Spreadsheet;
using MediatR;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Clio.Command {
    [Verb("run", Aliases = new[] { "run-scenario" }, HelpText = "Run scenario")]
    public class ScenarioRunnerOptions : EnvironmentOptions {
        [Option("file-name", Required = true, HelpText = "Scenario file name")]
        public string FileName {
            get; set;
        }
    }

    public class ScenarioRunnerCommand : Command<ScenarioRunnerOptions> {

        private readonly ISettingsRepository _settingsRepository;
        private readonly IDeserializer _deserializer;
        private readonly IMediator _mediator;
        private readonly IContainer _container;

        public ScenarioRunnerCommand(ISettingsRepository settingsRepository, IDeserializer deserializer, IMediator mediator) {
            _settingsRepository = settingsRepository;
            _deserializer = deserializer;
            _mediator = mediator;
            _container = new MyBindingModule().Register();
        }


        public override int Execute(ScenarioRunnerOptions options) {

            if (!TryFindScenarioFile(options.FileName, out string scenarioFileName)) {
                Console.WriteLine("Scenario not found");
                return 1;
            }

            var yaml = ReadYaml(scenarioFileName);
            
            int counter = 0;
            Task.Run(async () => {

                while(yaml.Count>0 && yaml.Peek() is not null) {
                    counter++;
                    var step = yaml.Peek();

                    await Console.Out.WriteLineAsync($"{counter}: {step.Action} started");
                    var request = Step.GetRequest(step, _container);
                    var mresult = await _mediator.Send(request);

                    if (mresult.Value is HandlerError e) {
                        await Console.Out.WriteLineAsync(  $"${step.Action} step failed with:{e.ErrorDescription}");
                        await Console.Out.WriteLineAsync("!!! TERMINATING SCENARIO !!!");
                    }
                    else {
                        var response = mresult.Value as BaseHandlerResponse;
                        Console.SetCursorPosition(0, Console.CursorTop);

                        await Console.Out.WriteLineAsync(response.Description);
                        await Console.Out.WriteLineAsync($"{step.Action} step completed");
                        await Console.Out.WriteLineAsync("-------------------------");
                        await Console.Out.WriteLineAsync();
                        _ = yaml.Dequeue();
                    }
                }
            }).Wait();
            return 0;
        }

        private bool TryFindScenarioFile(string fileName, out string scenarioFileName) {
            scenarioFileName = string.Empty;
            var defaultDirectorty = _settingsRepository.AppSettingsFilePath;
            var dir = (new FileInfo(defaultDirectorty)).Directory;
            var scenarioFolderPath = Path.Combine(dir.FullName, "Scenarios");
            var scenarioDirectory = new DirectoryInfo(scenarioFolderPath);

            if (scenarioDirectory.Exists) {
                var file = scenarioDirectory.GetFiles(fileName).FirstOrDefault();

                if (file is null) {
                    return false;
                }
                scenarioFileName = file.FullName;
                return true;
            }
            return false;
        }

        private Queue<Step> ReadYaml(string scenarioFileName) {
            var queue = new Queue<Step>();
            var text = File.OpenText(scenarioFileName);
            var yaml = _deserializer.Deserialize<Scenario>(text);
            yaml.Steps.ForEach(step => {
                step.CommonSettings = yaml.CommonSettings; //TODO: refactor
                queue.Enqueue(step);
            });
            return queue;
        }
    }


    public class Scenario {
        [YamlMember(Alias = "settings")]
        public Dictionary<string, string> CommonSettings { get; set; }

        [YamlMember(Alias = "steps")]
        public List<Step> Steps { get; set; }

    }

    public class Step {
        [YamlMember(Alias = "action")]
        public string Action { get; set; }

        [YamlMember(Alias = "description")]
        public string Description { get; set; }

        [YamlMember(Alias = "options")]
        public Dictionary<string, string> Options { get; set; }


        public Dictionary<string, string> CommonSettings { get; set; }
        public static BaseHandlerRequest GetRequest(Step step, IContainer container) {

            var types = Assembly.GetExecutingAssembly().GetTypes();
            var typeName = step.Action[0].ToString().ToUpper() + step.Action.Substring(1) + "Request";
            var requestType = types.FirstOrDefault(t => t.Name == typeName);
            var handler = container.Resolve(requestType) as BaseHandlerRequest;
            handler.Arguments = step.Options;

            var keys = handler.Arguments.Keys;
            foreach (var key in keys)
            {
                var value = handler.Arguments[key];
                if(value.StartsWith('#') && value.EndsWith('#')) {
                    
                    var macroName = value.Trim('#');
                    if (step.CommonSettings.ContainsKey(macroName)) {
                        var macroValue = step.CommonSettings[macroName];
                        handler.Arguments[key] = macroValue;
                    }
                }
            }
            return handler;
        }
    }

    public class MyBindingModule {
        public IContainer Register() {
            var containerBuilder = new ContainerBuilder();
            containerBuilder.RegisterType<UnzipRequest>();
            containerBuilder.RegisterType<TestRequest>();
            containerBuilder.RegisterType<CreateIISSiteRequest>();
            containerBuilder.RegisterType<ConfigureConnectionStringRequest>();
            containerBuilder.RegisterType<RestoreBdRequest>();
            return containerBuilder.Build();
        }
    }

}