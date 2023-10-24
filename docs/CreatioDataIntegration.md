# Creatio Data Integration through [ATF repository][Atf Repository README]

To simply integration between Creatio and third party C# applications you can use ATF Repository. It allows you to work with Creatio data through C# classes and LINQ queries. [Clio][Clio README] generates models consumed by ATF Respository. This combination of tools significantly reduces time for integration development and testing. For further details please refer to [ATF repository documentation][ATF Repository README]

> WARNING: Required minimum [Clio][Clio README] version 6.0.2.4

1. Create new C# project or use existing one

```ps
dotnet new console -n CreatioDataIntegration
cd CreatioDataIntegration
```

2. Add NuGet package to use [ATF Repository][Atf Repository Nuget]

```ps
dotnet add package ATF.Repository
```

3. Generate models for named environment, omit -e key for default environment
```ps
clio add-item model -n "CreatioDataIntegration.Models" -d .\Models -e <ENVIRONMENT_NAME>
```

4. Add generated classes to your project if necessary

5. Modify Program.cs to use ATF Repository

```csharp
using ATF.Repository;
using ATF.Repository.Providers;
using CDI = CreatioDataIntegration.Models;
namespace CreatioDataIntegration;

public static class Program
{

	public static int Main(string[] args) {
		IDataProvider dataProvider =
			new RemoteDataProvider("<URL_TO_CREATIO_ENVIRONMENT>", "<USER_NAME>", "<USER_PASSWORD>");

		IAppDataContext? context = AppDataContextFactory.GetAppDataContext(dataProvider);
		
		List<CDI.Contact> allContacts = context
			.Models<CDI.Contact>()
			.Where(c=> c.Name !="")
			.ToList();
		
		allContacts.ForEach(c=> Console.WriteLine(c.Name));
		return 0;
	}
}
``````

Run project
```ps
dotnet run --project CreatioDataIntegration.csproj
```

You can use this type of project for integration needs or to test logic in Creatio environment, for example business process.

<!-- named links-->
[ATF Repository README]: https://github.com/Advance-Technologies-Foundation/repository/blob/master/README.md
[ATF Repository Nuget]: https://www.nuget.org/packages/ATF.Repository
[Clio README]: https://github.com/Advance-Technologies-Foundation/clio/blob/master/README.md