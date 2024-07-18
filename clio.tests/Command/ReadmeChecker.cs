using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Castle.Core.Internal;
using CommandLine;

namespace Clio.Tests.Command;

public class ReadmeChecker
{

	private readonly string _readmeContent = File.ReadAllText(ReadmeFilePath);
	private readonly IEnumerable<string> _wikiAnchorsContent = File.ReadAllLines(WikiAnchorsFilePath);
	private const string ReadmeFilePath = @"..\..\..\..\README.md";
	private const string WikiAnchorsFilePath = @"..\..\..\..\clio\Wiki\WikiAnchors.txt";

	private readonly Func<string, string> _convertCommandNameToSection = (commandName) => {
		List<string> commandWords = commandName
			.Replace("-", " ")
			.Split(' ')
			.ToList();
		string expectedSectionName = "## " + string.Join(' ', commandWords);
		return expectedSectionName;
	};
	private readonly IList<string> _namesToCheck = new List<string>();

	
	/// <summary>
	/// Determines whether the specified command option type is represented in the README file.
	/// </summary>
	/// <remarks>
	/// This method first checks if the command opt ion typeis marked as hidden by the VerbAttribute.
	/// If it is not hidden, the method then checks if any of the associated names (derived from the command option type)
	/// are present in the README content, using a case-insensitive comparison.
	/// </remarks>
	/// <param name="commandOptionType">The Type of the command option to check. This should be a class type where VerbAttribute might be applied.</param>
	/// <returns>
	/// True if the command option type is either marked as hidden by VerbAttribute,
	/// or if any associated names are found in the README content; otherwise, false.
	/// </returns>
	/// <example>
	/// <code>
	/// Type commandType = typeof(MyCommandOption);
	/// bool isInReadme = IsInReadme(commandType);
	/// </code>
	/// </example>
	public bool IsInReadme(Type commandOptionType){
		// Check if the type has the VerbAttribute and is hidden
		bool isCommandHidden = commandOptionType
			.GetCustomAttributes(typeof(VerbAttribute), true)
			.OfType<VerbAttribute>()
			.Any(attr => attr.Hidden);
		
		if(isCommandHidden) {
			return true;
		}
		
		PopulateListToCheck(commandOptionType);
		
		// Check if names are present in readme content
		return _namesToCheck.Any(name => _readmeContent
			.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
		
	}

	private void PopulateListToCheck(Type T){
		string commandVerbName = T.GetAttribute<VerbAttribute>().Name;
		List<string> aliases = T.GetAttribute<VerbAttribute>().Aliases?.ToList()  ?? [];
		aliases.Add(commandVerbName);
		//Add Verb
		foreach(var alias in aliases) {
			_namesToCheck.Add(_convertCommandNameToSection(alias));
		}
		foreach (string anchorLine in _wikiAnchorsContent) {
			var commandName = anchorLine.Split(':');
			if (commandName[0] == commandVerbName) {
				string[] possibleSectionNames = commandName[1].Split(',');
				foreach (string possibleSectionName in possibleSectionNames) {
					string mayBeSectionNae = _convertCommandNameToSection(possibleSectionName);
					if (!_namesToCheck.Contains(mayBeSectionNae)) {
						_namesToCheck.Add(mayBeSectionNae);
					}
				}
			}
		}
	}
}