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
			.AppendLine()
			.AppendLine(GenerateStringForProperty(processModel, culture))
			.AppendLine("\t}")
			.AppendLine("}");
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
	
	private static string GenerateStringForProperty(ProcessModel processModel, string culture) {

		StringBuilder builder = new ();
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
				string prop = para.DataValueType == DataValueTypeMap.CompositeObjectListDataValueTypeUId
						? CreateCollectionModel(para)
						: IndentWithTab($"public {para.DataValueTypeResolved} {para.Name}" + "{ get; set; }", 2);
				
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
		return builder.ToString();
	}

	private static string CreateCollectionModel(ProcessParameter parameter) {
		
		
		
		parameter.ItemProperties?.ForEach(p => {
			
		});
		
		
		string collectionCass = $$"""
								  "
								   public class {{parameter.Name}} {
								  	
								  	[JsonProperty("ContactFirstName")]
								  	public string FirstName { get; set; }
								  	
								  	[JsonProperty("ContactLastName")]
								  	public string LastName { get; set; }

								   }
								  "
								  """;


		return collectionCass;
	}
	
	
}
