using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using IFileSystem = Clio.Common.IFileSystem;
using Path = System.IO.Path;

namespace Clio.Command.ProcessModel;

public interface IProcessModelWriter{
	void WriteFileFromModel(ProcessModel processModel, string nameSpace, string filePath, string culture);
}

public class ProcessModelWriter(IFileSystem fileSystem) : IProcessModelWriter{
	public void WriteFileFromModel(ProcessModel processModel, string nameSpace, string fileOrFolderPath, string culture) {
		string fileContent = CreateFileContent(processModel, nameSpace, culture);
		WriteFle(processModel, fileContent, fileOrFolderPath);
	}
	
	
	private static readonly Func<string, int, string> IndentWithTab = (s, i) => {
		string[] lines = s.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
		IEnumerable<string> indented = lines.Select(line => new string('\t', i) + line);
		return string.Join(Environment.NewLine, indented);
	};
	
	private static string CreateFileContent(ProcessModel processModel, string nameSpace, string culture) {
		StringBuilder builder = new ();
		
		builder
			.AppendLine("using ATF.Repository;")
			.AppendLine("using ATF.Repository.Attributes;")
			.AppendLine("using Newtonsoft.Json;")
			.Append($"namespace {nameSpace}").AppendLine("{")
			.AppendLine();
			
		if (!string.IsNullOrWhiteSpace(processModel.Description)) {
			string desc = $"""
					   /// <summary>
					   /// {processModel.Description} 
					   /// </summary>
					   """;
			builder.AppendLine(IndentWithTab(desc,1));
		}
		
		builder
			.AppendLine("\t[BusinessProcess(\""+processModel.Code+"\")]")
			.AppendLine("\tpublic class " + processModel.Code + " : IBusinessProcess {")
			.AppendLine();
			
		(string main, List<string> classes) x = GenerateStringForProperty(processModel, culture);
		
		builder
			.AppendLine(x.main)
			.AppendLine("\t}")
			.AppendLine();

		x.classes.ForEach(c => builder.AppendLine(c));
			
		builder.AppendLine("}");
		
		
		
		return builder.ToString();
	}

	private void WriteFle(ProcessModel processModel, string content, string fileOrFolderPath ) {
		string path = Path.Join(Environment.CurrentDirectory, fileOrFolderPath);
		string dirName = Path.GetDirectoryName(path);
		fileSystem.CreateDirectoryIfNotExists(dirName);
		string filePath = Path.Combine(dirName, $"{processModel.Code}.cs");
		if (fileSystem.ExistsFile(filePath)) {
			fileSystem.DeleteFile(filePath);
		}
		fileSystem.WriteAllTextToFile(filePath, content);
	}
	
	private static (string main, List<string> classes) GenerateStringForProperty(ProcessModel processModel, string culture) {

		StringBuilder builder = new ();
		List<string> classes = [];
		
		List<ProcessParameterDirection> directions = [ProcessParameterDirection.Input, ProcessParameterDirection.Output];
		processModel.Parameters.Where(parameter => directions.Contains(parameter.Direction))
			.ToList()
			.ForEach(para => {
				string attribute = para.Direction switch {
					ProcessParameterDirection.Input =>
						IndentWithTab($"[BusinessProcessParameter(\"{para.Name}\", BusinessProcessParameterDirection.Input)]",2)
					, ProcessParameterDirection.Output =>
						IndentWithTab($"[BusinessProcessParameter(\"{para.Name}\", BusinessProcessParameterDirection.Output)]",2)
					, var _ => string.Empty
				};
				
				//Here we may need to create a separate class for Collection parameter
				(string Property, string CollectionClass) t = (string.Empty, string.Empty);
				string prop = string.Empty;
				
				if (para.DataValueType == DataValueTypeMap.CompositeObjectListDataValueTypeUId) {
					t = CreateCollectionModel(para, culture);
					prop = IndentWithTab(t.Property,2);
					classes.Add(t.CollectionClass);
				}
				else {
					prop =IndentWithTab($"public {para.DataValueTypeResolved} {para.Name}" + "{ get; set; }", 2); 
				}
				
				if (string.IsNullOrWhiteSpace(attribute) || string.IsNullOrWhiteSpace(prop)) {
					return;
				}
				
				string caption = string.Empty;
				bool? isCaption = para.Captions?.TryGetValue(culture, out caption);
				if (isCaption.HasValue && isCaption.Value && !string.IsNullOrWhiteSpace(caption)) {
					builder
						.AppendLine(IndentWithTab("/// <summary>", 2))
						.Append(IndentWithTab("/// ",2)).AppendLine(caption)
						.AppendLine(IndentWithTab("/// </summary>", 2));
				}

				string description = string.Empty;
				bool? isDescription = para.Descriptions?.TryGetValue(culture, out description);
				
				if (isDescription.HasValue && isDescription.Value && !string.IsNullOrWhiteSpace(description)) {
					builder
						.AppendLine(IndentWithTab("/// <remarks>", 2))
						.Append(IndentWithTab("/// ",2)).AppendLine(description)
						.AppendLine(IndentWithTab("/// </remarks>", 2));
				}

				builder
					.AppendLine(attribute)
					.AppendLine(prop)
					.AppendLine();
			});
		return new ValueTuple<string, List<string>>(builder.ToString(), classes);
	}

	private static (string Property, string CollectionClass) CreateCollectionModel(ProcessParameter parameter, string culture) {
		
		StringBuilder sb = new ();
		// public Contact Contact { get; set; }
		string prop = $"public List<{parameter.Name}> {parameter.Name} {{ get; set; }}";

		string className = $"public class {parameter.Name} {{";
		sb
			.AppendLine(IndentWithTab(className,1))
			.AppendLine();
		
		parameter.ItemProperties?.ForEach(p => {
			
			string description = string.Empty;
			bool? isDescription = p.Descriptions?.TryGetValue(culture, out description);
			
			string captions = string.Empty;
			bool? isCaption = p.Captions?.TryGetValue(culture, out captions);


			if (isCaption.HasValue && isCaption.Value && !string.IsNullOrWhiteSpace(captions)) {
				sb.AppendLine(IndentWithTab("/// <summary>", 2))
				  .Append(IndentWithTab("/// ",2)).AppendLine(captions)
				  .AppendLine(IndentWithTab("/// </summary>", 2));
			}

			if (isDescription.HasValue && isDescription.Value && !string.IsNullOrWhiteSpace(description)) {
				sb.AppendLine(IndentWithTab("/// <remarks>", 2))
				  .Append(IndentWithTab("/// ",2)).AppendLine(description)
				  .AppendLine(IndentWithTab("/// </remarks>", 2));
			}
			
			string attr = $"[JsonProperty(\"{p.Name}\")]";
			string prop = $"public {p.DataValueTypeResolved.Name} {p.Name} {{get; set;}}";

			if (!string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.DataValueTypeResolved.Name)) {
				sb.AppendLine(IndentWithTab(attr,2))
				  .AppendLine(IndentWithTab(prop,2))
				  .AppendLine();
			}
		});
		
		sb.AppendLine(IndentWithTab("}",1));
		return new ValueTuple<string, string>(prop,sb.ToString());
	}
}
