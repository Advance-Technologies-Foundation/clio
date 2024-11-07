using System.Net;
using Clio.Common;
using FluentAssertions;
using NUnit.Framework;

namespace Clio.Tests.Common;

[TestFixture]
public class CompilationLogParserTestFixture {

	
	private CompilationLogParser _sut;
	
	
	[TestCase("Examples/CompilationLog/Pair1/pair1-creatio-compilation-log.json","Examples/CompilationLog/Pair1/pair1-desired-output.txt")]
	[TestCase("Examples/CompilationLog/Pair2/pair2-creatio-compilation-log.json","Examples/CompilationLog/Pair2/pair2-desired-output.txt")]
	public void ParseCreatioCompilationLog(string input, string expectedOutput){

		//Arrange
		_sut = new CompilationLogParser();
		
		string desiredOutputContent = System.IO.File.ReadAllText(expectedOutput);
		string inputContent = System.IO.File.ReadAllText(input);
		
		
		//Act
		string result = _sut.ParseCreatioCompilationLog(inputContent);

		//Assert
		result.Should().Be(desiredOutputContent);
		
	}

}