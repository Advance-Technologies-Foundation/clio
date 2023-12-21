pipeline{
  agent any
  stages {
    stage ('Setup parameters'){
            steps {
                script { 
                    properties([
                        parameters([
                            string(
                                defaultValue: 'empty', 
                                name: 'CREATIO_URL', 
                                trim: true
                            ),
                            string(
                                defaultValue: 'empty', 
                                name: 'CREATIO_LOGIN', 
                                trim: true
                            ),
                            string(
                                defaultValue: 'empty', 
                                name: 'CREATIO_PASSWORD', 
                                trim: true
                            )
                        ])
                    ])
                }
            }
    }   
    stage ('unit tests'){
      steps {
                powershell 'dotnet test "./tests/UnitTests.sln"  --filter TestCategory=UnitTest'
            }
    }
    stage ('build'){
      steps {
                powershell 'dotnet clean ./.solution/CreatioPackages.sln'
                powershell '$env:CoreTargetFramework = "net472"'
                powershell 'dotnet build ./.solution/CreatioPackages.sln -c Release '
            }
    } 
    stage ('deploy-pkg'){
      steps {
                powershell 'clio pushw -u '+ params.CREATIO_URL +' -l params.CREATIO_LOGIN -p params.CREATIO_PASSWORD'
            }
    }
  }
  
}