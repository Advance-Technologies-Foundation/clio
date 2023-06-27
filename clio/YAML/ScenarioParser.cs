using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization;

namespace Clio.YAML
{
	public interface IScenarioParser
	{
		Task<IList<string>> SeparateSegmentsAsync(string scenarioFileName);
	}

	public class ScenarioParser
	{
		private readonly IDeserializer _deserializer;

		public ScenarioParser(IDeserializer deserializer)
		{
			_deserializer = deserializer;
		}

		internal string FindValuesFileByName(string scenarioFileName, string valuesFileName)
		{
			var fi = new FileInfo(scenarioFileName);
			return fi.Directory.GetFiles(valuesFileName, SearchOption.AllDirectories).FirstOrDefault().ToString();
		}

		internal async Task<IList<string>> SeparateSegmentsAsync(string scenarioFileName)
	{
			IList<string> segments = new List<string>();
			using var text = File.OpenText(scenarioFileName);
			StringBuilder sb = new();
			do
			{
				string line = await text.ReadLineAsync();
				bool isEmpty = string.IsNullOrWhiteSpace(line);
				bool isSeparator = !isEmpty && line.All(c => c == '-');

				if (isSeparator)
				{
					segments.Add(sb.ToString());
					sb.Clear();
				}
				else if (!isEmpty)
				{
					sb.AppendLine(line);
				}
			} while (!text.EndOfStream);

			var segment = sb.ToString();
			if (!string.IsNullOrEmpty(segment))
			{
				segments.Add(sb.ToString());
			}
			return segments;
		}
	}
}
