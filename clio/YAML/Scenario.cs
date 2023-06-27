namespace Clio.YAML;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OneOf.Types;
using YamlDotNet.Serialization;

public class Scenario
{
	public IReadOnlyDictionary<string, object> Sections {get; private init;}
	public static readonly Func<string, IDeserializer, OneOf.OneOf<Scenario, None>> CreateScenarioFromFile = (fileName, deserializer)=> (File.Exists(fileName)) 
		? new Scenario {Sections = GetSectionsFromFile(fileName, deserializer)}
		: new None();
	public static readonly Func<IReadOnlyDictionary<string, object>, Dictionary<string, object>> ParseSecrets = (section)=> {
		var secrets = (section
				.Where(s=> s.Key=="secrets")
				.Select(s=> s.Value as Dictionary<object, object>)
				.FirstOrDefault() ?? new Dictionary<object, object>())
			.ToDictionary(v=> v.Key.ToString(), v=> v.Value);
		
		if(secrets.ContainsKey("values")) {
			string filePath  = secrets["values"].ToString();
			GetSectionsFromFile(filePath, new DeserializerBuilder().Build())
				.Where(s=> s.Key=="secrets")
				.ToList()
				.ForEach(sectionItem=> {
					ConvertDictionary(sectionItem.Value as Dictionary<object, object>)
					.ToList().ForEach(item=> {
						secrets[item.Key] = item.Value;
					});
				});
			secrets.Remove("values");
		}
		
		return secrets;
	};
	public static readonly Func<IReadOnlyDictionary<string, object>, Dictionary<string, object>> ParseSettings = (section)=> {
		var settings = (section
				.Where(s=> s.Key=="settings")
				.Select(s=> s.Value as Dictionary<object, object>)
				.FirstOrDefault() ?? new Dictionary<object, object>())
			.ToDictionary(v=> v.Key.ToString(), v=> v.Value);
		
		if(settings.ContainsKey("values")) {
			string filePath  = settings["values"].ToString();
			GetSectionsFromFile(filePath, new DeserializerBuilder().Build())
				.Where(s=> s.Key=="secrets")
				.ToList()
				.ForEach(sectionItem=> {
					ConvertDictionary(sectionItem.Value as Dictionary<object, object>)
						.ToList().ForEach(item=> {
							settings[item.Key] = item.Value;
						});
				});
			settings.Remove("values");
		}
		return settings;
	};
	public static readonly Func<IReadOnlyDictionary<string, object>, IReadOnlyList<Step>> ParseSteps = (section)=> {
		List<Step> returnList = new ();
		section.Where(s=> s.Key=="steps").ToList()
			.ForEach(sectionItem=> {
				returnList.AddRange(sectionItem.Value switch {
					List<object> o => HandleSteps(o),
					Dictionary<object,object> s => HandleValues(s),
					_=> new List<Step>()
				});
			});
		return returnList;
	};
	
	private static readonly Func<object, IEnumerable<Step>> HandleSteps = (stepsObj)=> {
		return (stepsObj as List<object> ?? new List<object>())
			.Where(step=> step is Dictionary<object, object> innerDict)
			.Select(step=> ConvertDictionary(step as Dictionary<object, object>))
			.Select(converted=> new Step () {
				Action = converted["action"].ToString(),
				Description = converted["description"].ToString(),
				Options = converted["options"] as Dictionary<object, object>
			});
	};
	private static readonly Func<object, IEnumerable<Step>> HandleValues = (stepsObj)=> {
		
		Dictionary<string, object> xx = ConvertDictionary(stepsObj as Dictionary<object, object>);
		string filename = xx.FirstOrDefault().Value as string;

		Dictionary<string, object> x = GetSectionsFromFile(filename, new DeserializerBuilder().Build());
		return HandleSteps(x.FirstOrDefault().Value);
	};
	
	internal static readonly Func<Dictionary<object, object>, Dictionary<string, object>> 
		ConvertDictionary = (dict)=> dict.ToDictionary(
		kvp => kvp.Key.ToString(),
		kvp => kvp.Value
	);
	
	private static readonly Func<string, IDeserializer, Dictionary<string, object>> 
		GetSectionsFromFile = (fileName, deserializer)=> deserializer
		.Deserialize<Dictionary<string, object>>(File.ReadAllText(fileName));

}