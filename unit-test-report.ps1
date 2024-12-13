$token = "c685eefae5f747989c1c4803a8e7c14e88b13d0f";

dotnet-coverage collect "dotnet test .\clio.tests\clio.tests.csproj" -f xml -o "coverage.xml"
dotnet sonarscanner begin /k:"clio2" `
/d:sonar.host.url="https://sonarqube-rnd.creatio.com/" `
/d:sonar.login=$token `
/d:sonar.cs.vscoveragexml.reportsPaths=coverage.xml;


dotnet build .\clio\clio.csproj --no-incremental;
dotnet sonarscanner end /d:sonar.login=$token;

# This will run UnitTests an generate coverage report TestResults\coverage.opencover.xml 
#dotnet test .\clio.tests\clio.tests.csproj `
#  /p:CollectCoverage=true `
#  /p:CoverletOutputFormat=opencover `
#  /p:Exclude="[Terrasoft*]*" `
#  /p:CoverletOutput=".\..\TestResults\coverage.opencover.xml"

## Clean up HTML report directory
#Remove-Item -Recurse -Force .\TestResults\UnitTests\Html
#
## Generate report
## dotnet tool install --global dotnet-reportgenerator-globaltool
#reportgenerator -reports:TestResults\coverage.opencover.xml `
#  -targetdir:"D:\Projects\inetpub\wwwroot\Docs\clio\UnitTests\Html" `
#  -historydir:"D:\Projects\inetpub\wwwroot\Docs\clio\UnitTests\History" `
#  -assemblyfilters:"+Clio;-Terrasoft.*" `
#  -title:"Clio Unit Tests" `
#  -reporttypes:"Html;SonarQube;MarkdownSummaryGithub;Badges" `
#  -license:$LICENSE