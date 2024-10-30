using clioAgent.PgLogParser;
using FluentAssertions;

namespace clioAgent.Tests.PgLogParser;

[TestFixture]
public class PgObjectInstanceTests {

	private PgParser _sut;
	
	[SetUp]
	public void Setup(){
		_sut = new PgParser();
	}

	public record DataForParseReturnsCorrectCollection(List<string> Given, List<PgObjectInstance> Expected);
	private static IEnumerable<TestCaseData> TestCases() {
		
		yield return new TestCaseData(new DataForParseReturnsCorrectCollection(
			Given:[
				"some random line",
				"some random; line",
				";",
				"; Archive created at 2024-10-07 21:16:59",
				";     dbname: mnet6enu_8091415_1007",
				";     TOC Entries: 31375",
				";     Compression: gzip",
				";     Dump Version: 1.15-0",
				";     Format: CUSTOM",
				";     Integer: 4 bytes",
				";     Offset: 8 bytes",
				";     Dumped from database version: 16.1",
				";     Dumped by pg_dump version: 16.1",
				";",
				";",
				"; Selected TOC Entries:",
				";",
				"2; 3079 40184408 EXTENSION - uuid-ossp", 
				 "21906; 1259 40216586 INDEX public I7VNHUrNUlH9ULwOa4XFJS8Gm7Ws puser", 
			],
			Expected:[
			 	new PgObjectInstance(2, "3079 40184408 EXTENSION - uuid-ossp"),
				new PgObjectInstance(21906, "1259 40216586 INDEX public I7VNHUrNUlH9ULwOa4XFJS8Gm7Ws puser"),
			 ]
			));
	}
	
	[TestCaseSource(nameof(TestCases))]
	public void Parse_ReturnsCorrectCollection(DataForParseReturnsCorrectCollection objectList){
		//Arrange
		
		//Act
		List<PgObjectInstance> result = _sut.Init(objectList.Given);
		
		
		//Assert
		result.Should().BeEquivalentTo(objectList.Expected);
	}
	
	
	[Test]
	public void T2(){
		IEnumerable<string> objectList = File.ReadLines("PgLogParser/object_list.txt");
		IEnumerable<string> restoreLog = File.ReadLines("PgLogParser/restoredb_log.txt");
		
		int myProgress = 0;
		_sut.ProgressChanged += (_, progress) => {
			myProgress = progress;
		};
		
		const string marker = "pg_restore: finished main parallel loop";
		_sut.Init(objectList.ToList());
		foreach (string logLine in restoreLog) {
			_sut.Process(logLine);
		}
		myProgress.Should().BeGreaterThan(95);
	}
	
	
	private string GetTypeFromLine(string line){
		if(string.IsNullOrWhiteSpace(line) || !line.Contains(';')) {
			return string.Empty;
		}
		string[] parts = line.Split(';');
		bool isNumber = int.TryParse(parts[0].Trim(), out _);
		if(!isNumber){
			return string.Empty;
		}
		var key = parts[1].Split(' ')[3];
		return key;
	}
}