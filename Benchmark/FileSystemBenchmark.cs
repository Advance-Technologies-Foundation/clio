using BenchmarkDotNet.Attributes;

namespace Benchmark;



public class FileSystemBenchmark
{
	
	private const string InputFileName = "";
	private const string OutputPathFileName = "";
	
	
	[GlobalSetup]
	public void GlobalSetup()
	{
		string baseDirectory  = AppDomain.CurrentDomain.BaseDirectory;
		File.WriteAllText(Path.Combine(baseDirectory, "input.json"), "Hello World");
		//Write your initialization code here
	}

	[Benchmark]
	public void MyFirstBenchmarkMethod()
	{
		var tplContect = File.ReadAllText(InputFileName);
		File.WriteAllText(OutputPathFileName, tplContect);
	}
	
	[Benchmark]
	public void MySecondBenchmarkMethod()
	{
		File.Copy(InputFileName,OutputPathFileName);
	}
}