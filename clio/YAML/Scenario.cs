namespace Clio.YAML;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OneOf;
using OneOf.Types;
using YamlDotNet.Serialization;

public interface IScenario
{

	#region Methods: Public

	/// <summary>
	/// Initializes scenario from YAML file.
	/// </summary>
	/// <param name="filename"></param>
	IExecutableScenario InitScript(string filename);

	#endregion

}

public interface IExecutableScenario
{

	#region Methods: Public

	IEnumerable<Tuple<object,string>> GetSteps(Type[] types);

	#endregion
	
	internal List<Step> Steps { get; set; }

}

public class Scenario : IScenario, IExecutableScenario
{

	#region Fields: Private

	private static readonly Func<string, IReadOnlyDictionary<string, object>, OneOf<object, None>>
		GetOptionByKey = (key, section) => {
			if (section.ContainsKey(key)) {
				return section[key];
			}
			string[] segments = key.Split('.');
			if (!segments.Any() || !section.ContainsKey(segments[0])) {
				return new None();
			}
			return section[segments[0]] switch {
				Dictionary<object, object> root => GetOptionByKey(string.Join('.', segments.AsSpan()[1..].ToArray()),
					root.ToDictionary(t => t.Key.ToString(), t => t.Value)),
				_ => new None()
			};
		};

	private static readonly Func<string, IDeserializer, Dictionary<object, object>>
		FileContentLoader = (fileName, deserializer) => {
			if (!File.Exists(fileName)) {
				return new Dictionary<object, object>();
			}
			using TextReader reader = new StreamReader(fileName);
			try {
				return deserializer.Deserialize<Dictionary<object, object>>(reader) ?? new Dictionary<object, object>();
			} catch (Exception e) {
				Console.WriteLine("Could not deserialize file: " + fileName);
				return new Dictionary<object, object>();
			}
		};

	private static readonly Func<Dictionary<object, object>, string, Func<string, Dictionary<object, object>>,
			Dictionary<string, object>>
		GetSectionFromDeserializedContent = (deserializedFileContent, sectionName, fileContentLoader) => {
			if (deserializedFileContent is null) {
				return new Dictionary<string, object>();
			}
			IEnumerable<Dictionary<string, object>> collection = deserializedFileContent
				.Where(section => section.Value is not null && section.Key as string == sectionName)
				.Select(s => s.Value switch {
					Dictionary<object, object> dict => ConvertDictionary(dict),
					List<object> list => new Dictionary<string, object> {
						{"steps", list}
					},
					_ => throw new Exception("I dunnot what happened")
				});

			Dictionary<string, object>[]
				enumerable = collection as Dictionary<string, object>[] ?? collection.ToArray();
			return enumerable.Any()
				? GetAdditionalContent(enumerable.First(), sectionName, fileContentLoader)
				: new Dictionary<string, object>();
		};

	private static readonly Func<List<object>, List<Step>>
		ParseSteps = rawList => {
			List<Step> resultList = new();
			rawList.ForEach(item => {
				if (item is Dictionary<object, object> dict && dict.Any()) {
					resultList.Add(new Step {
						Action = dict.TryGetValue("action", out object action) ? action as string : string.Empty,
						Description = dict.TryGetValue("description", out object description) ? description as string
							: string.Empty,
						Options = dict.TryGetValue("options", out object options)
							? options as Dictionary<object, object>
							: new Dictionary<object, object>()
					});
				}
			});
			return resultList;
		};

	private static readonly Func<Dictionary<object, object>, Dictionary<string, object>>
		ConvertDictionary = dict => dict.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);

	private static readonly Func<
			Dictionary<string, object>,
			string,
			Func<string, Dictionary<object, object>>,
			Dictionary<string, object>>
		GetAdditionalContent = (section, sectionName, fileContentLoader) => {
			if (section.TryGetValue("values", out object fileName)) {
				Dictionary<object, object> additionalValues = fileContentLoader(fileName as string);
				Dictionary<string, object> additionalSectionValues =
					GetSectionFromDeserializedContent(additionalValues, sectionName, fileContentLoader);
				return MergeDictionaries(section, additionalSectionValues);
			}
			return section;
		};

	private static readonly Func<Dictionary<string, object>, Dictionary<string, object>, Dictionary<string, object>>
		MergeDictionaries = (into, from) => {
			Dictionary<string, object> result = new();
			into.Where(kvp => kvp.Key != "values")
				.Select(kvp => kvp)
				.ToList()
				.ForEach(kvp => result.Add(kvp.Key, kvp.Value));

			from.Where(kvp => kvp.Key != "values")
				.Select(kvp => kvp)
				.ToList()
				.ForEach(kvp => result.Add(kvp.Key, kvp.Value));
			return result;
		};

	private readonly Func<string, Dictionary<object, object>> _initializedFileContentLoader;
	private readonly Func<string, OneOf<object, None>> _settingLookup;
	private readonly Func<string, OneOf<object, None>> _secretsLookup;

	#endregion

	#region Constructors: Public

	public Scenario(IDeserializer deserializer) {
		_initializedFileContentLoader = filename => FileContentLoader(filename, deserializer);
		_settingLookup = key => GetOptionByKey(key, Settings);
		_secretsLookup = key => GetOptionByKey(key, Secrets);
	}

	#endregion

	#region Properties: Private

	private IReadOnlyDictionary<string, object> Secrets { get; set; }

	private IReadOnlyDictionary<string, object> Settings { get; set; }

	public List<Step> Steps { get; set; }

	#endregion

	#region Methods: Private

	private void ReadFileContent(
		string fileName,
		Func<Dictionary<object, object>, string, Func<string, Dictionary<object, object>>, Dictionary<string, object>>
			sectionParser,
		Func<string, Dictionary<object, object>> fileContentLoader
	) {
		Dictionary<object, object> deserializedContent = fileContentLoader(fileName);

		Secrets = sectionParser(deserializedContent, "secrets", fileContentLoader);
		Settings = sectionParser(deserializedContent, "settings", fileContentLoader);
		Steps = sectionParser(deserializedContent, "steps", fileContentLoader)
			.TryGetValue("steps", out object stepsValue) ? ParseSteps(stepsValue as List<object>) : new List<Step>();
	}

	#endregion

	#region Methods: Public

	public IEnumerable<Tuple<object, string>> GetSteps(Type[] types) {
		return Steps
			.Select(step => step.Activate(types, _settingLookup, _secretsLookup))
			.Where(activeStep => activeStep.Item1.Value is not None)
			.Select(activeStep => new Tuple<object, string>(activeStep.Item1.Value, activeStep.Item2));
	}

	/// <summary>
	/// Initializes scenario from YAML file.
	/// </summary>
	/// <param name="filename"></param>
	public IExecutableScenario InitScript(string filename) {
		ReadFileContent(filename, GetSectionFromDeserializedContent, _initializedFileContentLoader);
		return this;
	}

	#endregion

}