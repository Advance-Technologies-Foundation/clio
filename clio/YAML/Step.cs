namespace Clio.YAML
{
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
		[YamlMember(Alias = "action")]
		public string Action { get; init; }

		[YamlMember(Alias = "description")]
		public string Description { get; init; }

		[YamlMember(Alias = "options")]
		public IReadOnlyDictionary<object, object> Options { get; init; }
		
		
		/// <summary>
		/// Finds Type from assembly that matches Step in YAML file
		/// </summary>
		internal static readonly Func<Type[], string,  OneOf<Type, None>> FindOptionTypeByName =  (allTypes, optionsOrAliasName)=> {
			IEnumerable<Type> enumerable= allTypes
				.Where(t=> {
					if (t.GetCustomAttribute<VerbAttribute>() is not {Name: not null} verb) {
						return false;
					}
					if(string.Equals(verb?.Name, optionsOrAliasName, StringComparison.CurrentCultureIgnoreCase)) {
						return true;
					}
					return verb.Aliases?.Contains(optionsOrAliasName.ToLower()) ?? false;
				});
			return enumerable.Any() ? enumerable.FirstOrDefault() : new None();
		};
		
		/// <summary>
		/// Creates a new instance of a command option type and sets the properties based on the settings and secrets
		/// </summary>
		public static readonly Func<OneOf<Type, None>, 
				IReadOnlyDictionary<object, object>, IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>,
				OneOf<None, object>> 
			ActivateOptions = (maybeType, commandOptions, settings, secrets) => {
			
			if(maybeType.Value is None) {
				return new None();
			}
			Type type = maybeType.Value as Type;
			object instance = Activator.CreateInstance(type!);
			type.GetProperties().ToList()
				.ForEach(property=> SetPropertyValue(property, instance, commandOptions, settings, secrets));
			return instance;
		};
		
		
		/// <summary>
		/// Converts macro to value, a macro is any string that start is enclosed between {{ and }}
		/// </summary>
		private static readonly Func<object, IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>,object> 
			ConvertMacroToValue = (maybeMacro, settings, secrets)=> {
			if(maybeMacro is not string) {
				return maybeMacro;
			}
			
			const string pattern = @"{{(.*?)}}";
			Match match = Regex.Match(maybeMacro.ToString() ?? "", pattern);

			if (!match.Success) {
				return maybeMacro;
			}
			string capturedValue = match.Groups[1].Value;
			
			if(capturedValue.StartsWith("secrets")) {
				string parsedCapturedValue = capturedValue.Replace("secrets.", "");
				OneOf<object, None> maybeValue = YAML.Options.GetOptionByKey(parsedCapturedValue,secrets);
				return maybeValue.Value is None ? maybeMacro : maybeValue.Value;
			}
			if(capturedValue.StartsWith("settings")) {
				string parsedCapturedValue = capturedValue.Replace("settings.", "");
				var maybeValue =YAML.Options.GetOptionByKey(parsedCapturedValue,settings);
				return maybeValue.Value is None ? maybeMacro : maybeValue.Value;
			}
			return maybeMacro;
		};
		
		/// <summary>
		/// Sets property value of instance based on the command line options
		/// </summary>
		private static readonly Action<PropertyInfo, object, 
				IReadOnlyDictionary<object, object>, IReadOnlyDictionary<string, object>, IReadOnlyDictionary<string, object>> 
			SetPropertyValue = (property, instance, commandOptions, settings, secrets)=> {
				if(property.GetCustomAttribute<ValueAttribute>() is {MetaName: not null} valueAttr ) {
					if(commandOptions.ContainsKey(valueAttr.MetaName)) {
						property.SetValue(
							instance, 
							Convert.ChangeType(ConvertMacroToValue(commandOptions[valueAttr.MetaName], settings, secrets), 
								Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
					}
				}

				if(property.GetCustomAttribute<OptionAttribute>() is {LongName: not null} longOptionsAttr) {
					if(commandOptions.ContainsKey(longOptionsAttr.LongName)) {
						property.SetValue(
							instance, 
							Convert.ChangeType(ConvertMacroToValue(commandOptions[longOptionsAttr.LongName],settings, secrets), 
								Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
					}
				}
				
				if(property.GetCustomAttribute<OptionAttribute>() is {ShortName: not null} shortOptionsAttr) {
					if(commandOptions.ContainsKey(shortOptionsAttr.ShortName)) {
						property.SetValue(
							instance, 
							Convert.ChangeType(ConvertMacroToValue(commandOptions[shortOptionsAttr.ShortName],settings, secrets), 
								Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
					}
				}
		};
	}
}