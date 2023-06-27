namespace Clio.YAML
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Reflection;
	using CommandLine;
	using YamlDotNet.Serialization;

	public class Step
	{
		[YamlMember(Alias = "action")]
		public string Action { get; init; }

		[YamlMember(Alias = "description")]
		public string Description { get; init; }

		[YamlMember(Alias = "options")]
		public IReadOnlyDictionary<object, object> Options { get; init; }
		
		internal static readonly Func<Type[], string,  OneOf.OneOf<Type, NotType>> FindOptionTypeByName =  (allTypes, optionsOrAliasName)=> {
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
				return enumerable.Any() ? enumerable.FirstOrDefault() : new NotType();
		};
		
		internal static readonly Func<OneOf.OneOf<Type, NotType>, IReadOnlyDictionary<object, object>, OneOf.OneOf<NotOption, object>> ActivateOptions = (maybeType, options) => {
			
			if(maybeType.Value is NotType) {
				return new NotOption();
			}
			Type type = maybeType.Value as Type;
			object instance = Activator.CreateInstance(type!);
			type.GetProperties().ToList().ForEach(property=> {

				if(property.GetCustomAttribute<ValueAttribute>() is {MetaName: not null} valueAttr ) {
					if(options.ContainsKey(valueAttr.MetaName)) {
						property.SetValue(
							instance, 
							Convert.ChangeType(options[valueAttr.MetaName], 
								Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
					}
				}
				
				if(property.GetCustomAttribute<OptionAttribute>() is {LongName: not null} longOptionsAttr) {
					if(options.ContainsKey(longOptionsAttr.LongName)) {
						property.SetValue(
							instance, 
							Convert.ChangeType(options[longOptionsAttr.LongName], 
								Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
					}
				}
				
				if(property.GetCustomAttribute<OptionAttribute>() is {ShortName: not null} shortOptionsAttr) {
					if(options.ContainsKey(shortOptionsAttr.ShortName)) {
						property.SetValue(
							instance, 
							Convert.ChangeType(options[shortOptionsAttr.ShortName], 
								Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType));
					}
				}
			});
			return instance;
		};
	}
}

public class NotType{}
public class NotOption{}