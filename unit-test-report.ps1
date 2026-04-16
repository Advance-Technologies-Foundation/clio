param(
    [string]$ReportDir   = $env:CLIO_REPORT_DIR,
    [string]$HistoryDir  = $env:CLIO_REPORT_HISTORY_DIR
)

if (-not $ReportDir)  { $ReportDir  = "D:\Projects\inetpub\wwwroot\Docs\clio\UnitTests\Html" }
if (-not $HistoryDir) { $HistoryDir = "D:\Projects\inetpub\wwwroot\Docs\clio\UnitTests\History" }

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
  -targetdir:"$ReportDir" `
  -historydir:"$HistoryDir" `
  -assemblyfilters:"+Clio;-Terrasoft.*" `
  -title:"Clio Unit Tests" `
  -reporttypes:"Html;SonarQube;MarkdownSummaryGithub;Badges" `
  -license:$LICENSE