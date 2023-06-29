namespace Clio.YAML;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CommandLine;
using OneOf;
using OneOf.Types;
using YamlDotNet.Serialization;

public class Step
{

	#region Fields: Private

	/// <summary>
	/// Finds Type from assembly that matches Step in YAML file
	/// </summary>
	private static readonly Func<Type[], string, OneOf<Type, None>> FindOptionTypeByName =
		(allTypes, optionsOrAliasName) => {
			IEnumerable<Type> enumerable = allTypes
				.Where(t => {
					if (t.GetCustomAttribute<VerbAttribute>() is not {Name: not null} verb) {
						return false;
					}
					if (string.Equals(verb?.Name, optionsOrAliasName, StringComparison.CurrentCultureIgnoreCase)) {
						return true;
					}
					return verb.Aliases?.Contains(optionsOrAliasName.ToLower()) ?? false;
				});
			IEnumerable<Type> types = enumerable as Type[] ?? enumerable.ToArray();
			return types.Any() ? types.FirstOrDefault() : new None();
		};

	/// <summary>
	/// Creates a new instance of a command option type and sets the properties based on the settings and secrets
	/// </summary>
	private static readonly Func<
			OneOf<Type, None>,
			IReadOnlyDictionary<object, object>,
			Func<string, OneOf<object, None>>,
			Func<string, OneOf<object, None>>,
			OneOf<None, object>>
		ActivateOptions = (maybeType, commandOptions, settingsLookup, secretsLookup) => {
			if (maybeType.Value is None) {
				return new None();
			}
			Type type = maybeType.Value as Type;
			object instance = Activator.CreateInstance(type!);
			type.GetProperties().ToList()
				.ForEach(property =>
					SetPropertyValue(property, instance, commandOptions, settingsLookup, secretsLookup));
			return instance;
		};

	/// <summary>
	/// Converts macro to value, a macro is any string that start is enclosed between {{ and }}
	/// </summary>
	private static readonly Func<object, Func<string, OneOf<object, None>>, Func<string, OneOf<object, None>>, object>
		ConvertMacroToValue = (maybeMacro, settingsLookup, secretsLookup) => {
			if (maybeMacro is not string) {
				return maybeMacro;
			}
			const string pattern = @"{{(.*?)}}";
			Match match = Regex.Match(maybeMacro.ToString() ?? "", pattern);

			if (!match.Success) {
				return maybeMacro;
			}
			string capturedValue = match.Groups[1].Value;

			if (capturedValue.StartsWith("secrets.")) {
				string parsedCapturedValue = capturedValue.Replace("secrets.", "");
				OneOf<object, None> maybeValue = secretsLookup(parsedCapturedValue);
				return maybeValue.Value is None ? maybeMacro : maybeValue.Value;
			}
			if (capturedValue.StartsWith("settings.")) {
				string parsedCapturedValue = capturedValue.Replace("settings.", "");
				OneOf<object, None> maybeValue = settingsLookup(parsedCapturedValue);
				return maybeValue.Value is None ? maybeMacro : maybeValue.Value;
			}

			return maybeMacro;
		};

	/// <summary>
	/// Sets the property value based on the command options
	/// </summary>
	private static readonly Action<PropertyInfo, object,
			IReadOnlyDictionary<object, object>, Func<string, OneOf<object, None>>, Func<string, OneOf<object, None>>>
		SetPropertyValue = (property, instance, commandOptions, settings, secrets) => {
			if (property.GetCustomAttribute<ValueAttribute>() is {MetaName: not null} valueAttr) {
				if (commandOptions.ContainsKey(valueAttr.MetaName)) {
					property.SetValue(
						instance,
						Convert.ChangeType(
							ConvertMacroToValue(commandOptions[valueAttr.MetaName], settings,
								secrets), 
							Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
				}
			}
			if (property.GetCustomAttribute<OptionAttribute>() is {LongName: not null} longOptionsAttr) {
				if (commandOptions.ContainsKey(longOptionsAttr.LongName)) {
					property.SetValue(
						instance,
						Convert.ChangeType(
							ConvertMacroToValue(commandOptions[longOptionsAttr.LongName], settings,
								secrets),
							Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
				}
			}
			if (property.GetCustomAttribute<OptionAttribute>() is {ShortName: not null} shortOptionsAttr) {
				if (commandOptions.ContainsKey(shortOptionsAttr.ShortName)) {
					property.SetValue(
						instance,
						Convert.ChangeType(
							ConvertMacroToValue(commandOptions[shortOptionsAttr.ShortName], settings,
								secrets),
							Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
				}
			}
		};

	#endregion

	#region Properties: Public

	[YamlMember(Alias = "action")]
	public string Action { get; init; }

	[YamlMember(Alias = "description")]
	public string Description { get; init; }

	[YamlMember(Alias = "options")]
	public IReadOnlyDictionary<object, object> Options { get; init; }

	#endregion

	#region Methods: Public

	/// <summary>
	/// Activates step by finding the type in the assembly and setting the properties based on the settings and secrets
	/// </summary>
	/// <param name="allTypes">Array of types to search for command option</param>
	/// <param name="settingsLookup">Settings lookup function</param>
	/// <param name="secretsLookup">Secrets lookup function</param>
	/// <returns>Executable CommandOption</returns>
	public Tuple<OneOf<None, object>, string> Activate(
		Type[] allTypes, Func<string, OneOf<object, None>> settingsLookup,
		Func<string, OneOf<object, None>> secretsLookup) {
		OneOf<Type, None> maybeType = FindOptionTypeByName(allTypes, Action);
		OneOf<None, object> maybeOptions = ActivateOptions(maybeType, Options, settingsLookup, secretsLookup);
		return new Tuple<OneOf<None, object>, string>( maybeOptions, Description ?? Action);
	}

	#endregion
	
}