using Autofac;
using Clio.Common;
using Clio.Common.ScenarioHandlers;
using Clio.UserEnvironment;
using CommandLine;
using MediatR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
            _container = new PrivateBindingModule().Register();
        }


        public override int Execute(ScenarioRunnerOptions options) {

            if (!TryFindScenarioFile(options.FileName, out string scenarioFileName)) {
                Console.WriteLine("Scenario not found");
                return 1;
            }
            return ParseYaml(scenarioFileName);
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


        private int ParseYaml(string scenarioFileName) {

            var text = File.OpenText(scenarioFileName);
            var yaml = _deserializer.Deserialize<Scenario2>(text);

            yaml.Steps.ForEach(step => {
                var commandOption = MatchStepToCommandOption(step);
                Program.MyMap(commandOption);
            });
           return 0;
        }

        private object MatchStepToCommandOption(Step2 step) {

            var type = FindType(step.Action);
            var instance = InitInstance(type, step);

            if (step.Options is not null) {
                instance = InitOptions(instance, step);
                instance = InitValues(instance, step);
            }
            //var args = CreateArgsFromInstace(instance);

            return instance;
        }

        private static Type FindType(string action) {
            var allTypes = Assembly.GetExecutingAssembly().GetTypes();
            foreach (var type in allTypes) {
                var attributes = type.GetCustomAttributes<VerbAttribute>();
                if (attributes.Any()) {
                    foreach (var attribute in attributes) {
                        if (attribute is VerbAttribute attr) {
                            var isAlias = (attr.Aliases is object) ? attr.Aliases.Contains(action) : false;
                            var isName = attr.Name == action;
                            if (isAlias || isName) {
                                return type;
                            }
                        }
                    }
                }
            }
            return null;
        }

        private static object InitInstance(Type type, Step2 step) {
            var instance = Activator.CreateInstance(type);
            
            if (instance is null) {
                return null;
            }
            return instance;
        }
        private static object InitOptions(object instance, Step2 step) {
            var type = instance.GetType();
            var props = type.GetProperties();
            foreach (var prop in props) {
                var attributes = prop.GetCustomAttributes<OptionAttribute>();
                if (attributes.Any()) {
                    foreach (var attribute in attributes) {
                        if (attribute is OptionAttribute attr) {
                            var isLongName = step.Options.ContainsKey(attr.LongName);
                            var isShortName = step.Options.ContainsKey(attr.ShortName);
                            if (isLongName || isShortName) {

                                var value = isShortName ? step.Options[attr.ShortName] : step.Options[attr.LongName];
                                var pValue = Convert.ChangeType(value, prop.PropertyType);
                                instance.GetType().GetProperty(prop.Name).SetValue(instance, pValue);
                            }
                        }
                    }
                }
            }
            return instance;
        }
        private static object InitValues(object instance, Step2 step) {
            var type = instance.GetType();
            var props = type.GetProperties();
            foreach (var prop in props) {
                var attributes = prop.GetCustomAttributes<ValueAttribute>();
                if (attributes.Any()) {
                    foreach (var attribute in attributes) {
                        if (attribute is ValueAttribute attr) {
                            if (step.Options.ContainsKey(attr.MetaName)) {
                                var value = step.Options[attr.MetaName];
                                var pValue = Convert.ChangeType(value, prop.PropertyType);
                                instance.GetType().GetProperty(prop.Name).SetValue(instance, pValue);
                            }
                        }
                    }
                }
            }
            return instance;
        }

        /// <summary>
        /// Creates arguments from instace.
        /// </summary>
        /// <param name="instance">The instance.</param>
        /// <returns></returns>
        private static string[] CreateArgsFromInstace(object instance) {

            var properties = instance.GetType().GetProperties();
            var verbAttribute = instance.GetType().GetCustomAttribute<VerbAttribute>();
            var valueDictionary = new Dictionary<int, string>();
            var optionDictionary = new Dictionary<string, string>();
            foreach(var property in properties) {

                var propertyValue = property.GetValue(instance);
                if(propertyValue is not null) {
                    var valueAttirbute = property.GetCustomAttribute<ValueAttribute>();
                    if(valueAttirbute is not null) {
                        valueDictionary.Add(valueAttirbute.Index, propertyValue.ToString());
                    }
                    var optionAttirbute = property.GetCustomAttribute<OptionAttribute>();
                    if (optionAttirbute is not null) {

                        if(!string.IsNullOrWhiteSpace(optionAttirbute.ShortName)) {
                            var value = $"{optionAttirbute.ShortName} {propertyValue}"; //-
                            optionDictionary.Add(optionAttirbute.ShortName, value);
                        }
                        else {
                            var value = $"{optionAttirbute.LongName} {propertyValue}"; //--
                            optionDictionary.Add(optionAttirbute.LongName, value);
                        }                        
                    }
                }
            }

            var orderedvalueDictionary = valueDictionary.OrderBy(x => x.Key);
            StringBuilder sb = new StringBuilder();
            List<string> args = new List<string>();
            args.Add(verbAttribute.Name);

            foreach (var kvp in orderedvalueDictionary) {
                args.Add(kvp.Value);
            }

            foreach(var kvp in optionDictionary) {
                args.Add(kvp.Value);
            }

            
            return args.ToArray();
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

        private sealed class PrivateBindingModule {
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


    public class Scenario2 {
        [YamlMember(Alias = "settings")]
        public Dictionary<string, string> CommonSettings { get; set; }

        [YamlMember(Alias = "steps")]
        public List<Step2> Steps { get; set; }

    }
    public class Step2 {
        [YamlMember(Alias = "action")]
        public string Action { get; set; }

        [YamlMember(Alias = "description")]
        public string Description { get; set; }

        [YamlMember(Alias = "options")]
        public Dictionary<object, object> Options { get; set; }
    }
}