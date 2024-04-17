#$env:URL = "http://kkrylovn.tscrm.com:40015";
#$env:URL = "http://ts1-mrkt-web01.tscrm.com:88/clio-f";
$env:URL = "http://10.48.14.198:88/clio-f";
$env:LOGIN = "Supervisor";
$env:PASSWORD = "Supervisor";
$env:IS_NETCORE = "false";

# This will generate exe in bin\Debug\net6.0\clio.exe
# this exe is used instead of clio-dev
dotnet build clio\clio.csproj;


# Can specify the test category to run
# dotnet test --filter Category=SetWebServiceUrlCommand  clio.TestsAPI\clio.TestsAPI.csproj;
# dotnet test --filter Category=PublishWorkspaceCommand  clio.TestsAPI\clio.TestsAPI.csproj;
# dotnet test --filter Category=SysSetting  clio.TestsAPI\clio.TestsAPI.csproj;

# when filter is omited then all tests are run
dotnet test clio.TestsAPI\clio.TestsAPI.csproj;


# ****** IMPORTANT ******
# The following command will install the SpecFlow+ LivingDoc CLI tool globally on your machine.
# SpecFlow CLI is required to generate the LivingDoc report.
# dotnet tool install --global SpecFlow.Plus.LivingDoc.CLI

livingdoc test-assembly .\clio.TestsAPI\bin\Debug\net8.0\clio.TestsAPI.dll `
-t .\clio.TestsAPI\bin\Debug\net8.0\TestExecution.json `
-o .\TestResults\Api-Test-Results;

# Open the generated LivingDoc report in Edge browser
$currentDir = Get-Location
Start-Process -FilePath "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" -ArgumentList "$currentDir\TestResults\Api-Test-Results\LivingDoc.html" 