namespace Clio.Command {
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Autofac;
    using Clio.UserEnvironment;
    using Clio.YAML;
    using CommandLine;
    using MediatR;
    using OneOf.Types;
    using YamlDotNet.Serialization;

    [Verb("run", Aliases = new[] { "run-scenario" }, HelpText = "Run scenario")]
    public class ScenarioRunnerOptions : EnvironmentOptions {
        [Option("file-name", Required = true, HelpText = "Scenario file name")]
        public string FileName {
            get; set;
        }
    }

    public class ScenarioRunnerCommand : Command<ScenarioRunnerOptions> {
        
        private readonly IDeserializer _deserializer;
        private readonly IContainer _container;

        public ScenarioRunnerCommand(IDeserializer deserializer) {
            _deserializer = deserializer;
        }


        public override int Execute(ScenarioRunnerOptions options) {
            
            var fScenario = Scenario.CreateScenarioFromFile(options.FileName, _deserializer);
            var steps = Scenario.ParseSteps(fScenario.Sections);
            
            int result = 0;
            steps.ToList().ForEach(step=> {
                
                var s = Step.FindOptionTypeByName(Assembly.GetExecutingAssembly().GetTypes(), step.Action);
                var activatedOption = Step.ActivateOptions(s, step.Options);
                result += Program.MyMap(activatedOption);
            });
            return result >=1 ? 1: 0;
        }
    }
    
    public class  Scenario
    {
        public IReadOnlyDictionary<string, object> Sections {get; init;}
        
        
        
        
        
        public static readonly Func<string, IDeserializer, Scenario> CreateScenarioFromFile = (fileName, deserializer)=> (File.Exists(fileName)) 
            ? new Scenario {
                Sections = deserializer
                    .Deserialize<Dictionary<string, object>>(File.ReadAllText(fileName))
            }
            : new Scenario();
        
        
        public readonly Func<Scenario> ParseSecrets = ()=> {
            return default;
        };
    
        public readonly Func<Scenario> ParseSettings = ()=> {
            return default;
        };
        
       
        public static readonly Func<IReadOnlyDictionary<string, object>, IReadOnlyList<Step>> ParseSteps = (section)=> {
            
            List<Step> returnList = new ();
            
            section.ToList()
                .ForEach(sectionItem=> {
                    
                    if(sectionItem.Key == "steps") {
                        
                        if(sectionItem.Value is Dictionary<object, object> possiblyValuesFile) {
                            
                            
                            
                        }
                        
                        
                        
                        
                        if(sectionItem.Value is List<object> unparsedStep) {
                        
                            //Here add call to read more files
                        
                            unparsedStep.ForEach(stepItem=> {
                            
                                if(stepItem is Dictionary<object, object> innerDict) {
                                    Dictionary<string, object> convertedDict = innerDict.ToDictionary(
                                        kvp => kvp.Key.ToString(),
                                        kvp => kvp.Value
                                    );
                
                                    Step step = new Step {
                                        Action = convertedDict["action"].ToString(),
                                        Description = convertedDict["description"].ToString(),
                                        Options = convertedDict["options"] as Dictionary<object, object>
                                    };
                                    returnList.Add(step);
                                }
                            });
                        
                        }
                    }
                    
                    
                });
            return returnList;
        };
        
        
    }
            
}