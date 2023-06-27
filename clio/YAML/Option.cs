namespace Clio.YAML;

using System;
using System.Collections.Generic;
using System.Linq;
using OneOf;
using OneOf.Types;

public static class Options
{
	
	public static readonly Func<string, IReadOnlyDictionary<string, object>,  OneOf<object, None>> GetOptionByKey = (key, section) => {

		if(section.ContainsKey(key)) {
			return section[key];
		}
		string[] segments = key.Split('.');
		if(!segments.Any() || !section.ContainsKey(segments[0])) return new None();
		return section[segments[0]] switch 
		{
			Dictionary<object, object> root => GetOptionByKey(string.Join('.', segments.AsSpan()[1..].ToArray()), 
				root.ToDictionary(t=> t.Key.ToString(), t=>t.Value)),
			_=> new None()
		};
	};
}