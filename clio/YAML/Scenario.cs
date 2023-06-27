namespace Clio.YAML;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Serialization;

public class  Scenario
{
	public IReadOnlyDictionary<string, object> Sections {get; private init;}
	public static readonly Func<string, IDeserializer, Scenario> CreateScenarioFromFile = (fileName, deserializer)=> (File.Exists(fileName)) 
		? new Scenario {
			Sections = deserializer
				.Deserialize<Dictionary<string, object>>(File.ReadAllText(fileName))
		}
		: new Scenario();
	public readonly Func<Scenario> ParseSecrets = ()=> default;
	public readonly Func<Scenario> ParseSettings = ()=> default;
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