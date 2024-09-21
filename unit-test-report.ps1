
# This will run UnitTests an generate coverage report TestResults\coverage.opencover.xml 
dotnet test .\clio.tests\clio.tests.csproj `
  /p:CollectCoverage=true `
  /p:CoverletOutputFormat=opencover `
  /p:Exclude="[Terrasoft*]*" `
  /p:CoverletOutput=".\..\TestResults\coverage.opencover.xml"

# Clean up HTML report directory
Remove-Item -Recurse -Force .\TestResults\UnitTests\Html

# Generate report
# dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:TestResults\coverage.opencover.xml `
  -targetdir:"D:\Projects\inetpub\wwwroot\Docs\clio\UnitTests\Html" `
  -historydir:"D:\Projects\inetpub\wwwroot\Docs\clio\UnitTests\History" `
  -assemblyfilters:"+Clio;-Terrasoft.*" `
  -title:"Clio Unit Tests" `
  -reporttypes:"Html;SonarQube;MarkdownSummaryGithub;Badges" `
  -license:$LICENSE