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

	public bool IsInReadme(Type T){
		
		PopulateListToCheck(T);
		return _namesToCheck
			.Select(name => _readmeContent.ToUpper()
				.Contains(name.ToUpper()))
			.Any(isInReadme => isInReadme);
	}

	private void PopulateListToCheck(Type T){
		string commandVerbName = T.GetAttribute<VerbAttribute>().Name;

		//Add Verb
		_namesToCheck.Add(_convertCommandNameToSection(commandVerbName));

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