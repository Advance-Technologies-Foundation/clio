using Clio.Common;
using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Clio.Project.NuGet;
using Clio.Command.ModelBuilder;
using System.Xml.Linq;
using System.Threading.Tasks;
using static Clio.Command.ModelBuilder.ConsoleWriter;

namespace Clio.Command
{
	[Verb("generate-models", Aliases = new string[] { "models" }, HelpText = "Generate Creatio Entity models for use with Creatio.DataServise")]
    class GenerateModelsOptions : EnvironmentOptions
	{		
		[Option('d', "DestinationPath", Required = true, HelpText = "Full destination path")]
		public string DestinationPath { get; set; }
	}

	class GenerateModelsCommand : RemoteCommand<GenerateModelsOptions>
	{
		public GenerateModelsCommand(IApplicationClient applicationClient, EnvironmentSettings settings) :
			base(applicationClient, settings) {}


		public override int Execute(GenerateModelsOptions options)
		{
			try
			{
				if (!EnvironmentSettings.Uri.EndsWith('/'))
					EnvironmentSettings.Uri +="/";

				Uri metaData = new Uri($"{EnvironmentSettings.Uri}0/ServiceModel/EntityDataService.svc/$metadata");
				string response = ApplicationClient.ExecuteGetRequest(metaData.ToString(), -1);
				
				int count = CountXmlLines(response);

				ConsoleWriter.WriteMessage(ConsoleWriter.MessageType.OK, $"Obtained definition for {count} entities");
				Console.WriteLine();
				Console.WriteLine($"Would you like to create {count} models ? \nPress any key to continue, <Esc> to exit\n...\n");
				ConsoleKeyInfo keyInfo = Console.ReadKey();

				if (keyInfo.Key != ConsoleKey.Escape) 
				{
					Task t = Task.Run(async () => { 
						await BuildModelsAsync(response, options.DestinationPath);
					});
					t.Wait();
				}
				

				return 0;
			}
			catch (Exception)
			{
				return 1;
			}
		}

		private static int CountXmlLines(string input="")
		{
			if (string.IsNullOrEmpty(input)) return 0;

			XDocument xDoc = XDocument.Parse(input);
			int q = (from c in xDoc.Descendants()
					 where c.Name.LocalName == "EntityType"
					 select c).Count();
			return q;
		}

		private static async Task BuildModelsAsync(string input, string DestinationPath)
		{

			if (string.IsNullOrEmpty(input))
				return;

			XDocument xDoc = XDocument.Parse(input);
			var nSpace = from c in xDoc.Descendants()
						 where c.Name.LocalName == "Schema"
						 select c.Attribute("Namespace").Value;

			//Create Directory
			string dir = DestinationPath;
			DirectoryInfo dirInfo = Directory.CreateDirectory(dir);
			

			var associations = (from ent in xDoc.Descendants()
								where ent.Name.LocalName == "Association"
								select ent).ToList();


			var entities = from ent in xDoc.Descendants()
						   where ent.Name.LocalName == "EntityType"
						   select ent;

			foreach (XElement entity in entities)
			{

				IEnumerable<XElement> keys = from key in entities.Descendants()
											 where key.Name.LocalName == "PropertyRef"
											 select key;

				EntityBuilder eb = Factory.Create<EntityBuilder>();
				BaseModel bm = eb.Build(nSpace.FirstOrDefault(), entity, keys, associations); ;

				string fullPath = $"{dir}\\{bm.Class.Name}.cs";
				await CreateSourceFile(fullPath, bm.ToString()).ConfigureAwait(false);
			}
		}

		private static async Task CreateSourceFile(string fullPath, string content)
		{

			if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(fullPath))
				return;

			StreamWriter streamWriter = File.CreateText(fullPath);
			using (StreamWriter sw = streamWriter) { 
		
				try
				{
					await sw.WriteAsync(content).ConfigureAwait(false);
					ConsoleWriter.WriteMessage(MessageType.OK, $"Created: {fullPath}");
				}
				catch (IOException ex)
				{
					ConsoleWriter.WriteMessage(MessageType.Error, ex.Message);
				}
				finally
				{
					sw.Dispose();
				}
			}
		}



	}
}
