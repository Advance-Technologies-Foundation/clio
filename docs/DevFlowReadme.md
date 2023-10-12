# Organize common development flow for No-Code and Professional developers


## Initialize environment

<img src="img/general_environment.png" alt="General environment">


## Professional developers

1. Install clio
    ```bash
    dotnet tool install clio -g
    ```
    > Recommended plugin [clio explorer](https://marketplace.visualstudio.com/items?itemName=AdvanceTechnologiesFoundation.clio-explorer) for [VS Code](https://code.visualstudio.com/download)
2. Deploy Creatio instance locally. [Creatio Academy](https://academy.creatio.com/docs/7-18/user/on_site_deployment/general_deployment_procedure/general_creatio_deployment_procedure) or using [clio automation](https://github.com/Advance-Technologies-Foundation/clio#installation-of-creatio-using-clio)
3. [Register environment in clio](https://github.com/Advance-Technologies-Foundation/clio#environment-settings) or via clio explorer. 
If you deployed Creatio locally, with clio, you can skip this step
   <img src="img/clio_explorer_new_environment.png" width="50%" alt="register new environment">
4. Turn on [FSM mode](https://academy.creatio.com/docs/developer/development_tools/external_ides/overview#title-2098-3) r via clio explorer
   <img src="img/clio_explorer_turn_fsm_mode.png" width="50%" alt="turn file system on">
5. Create a [workspace](https://github.com/Advance-Technologies-Foundation/clio#workspaces) for the project
   ```bash
    clio create-workspace
    ```
6. Create application in Creatio and link packages to workspace
   ```bash
    clio l2r -r "<PATH_TO_WORKSPACE_FOLDER>" \
    -e "<LOCAL_CREATIO_PATH>\Terrasoft.WebApp\Terrasoft.Configuration\Pkg" \
    -p "<AppPackage1>,<AppPackage2>,<...>"
    ``` 
7. Link workspace with environment, add settings in file `<workspace_path>/.clio/workspaceEnvironmentSettings.json` your environment
    ```json
    {
      "Environment": "<env_name>"
    }
    ```
    add settings of your environment in `<workspace_path>/.clio/workspaceSettings.json`
    ```json
    {
      "Packages": ["AppPackage1","AppPackage2"],
      "ApplicationVersion": "8.1.0"
    }
    ```
8. Download configuration to workspace
    ```bash
    clio dconf -e <env_name>
    ```
    > Configuration files are required to organize CI\CD pipelines and unit tests
9. Create Git repository for workspace folder

## No-Code developer

1. Gets access to Creatio instance and register it with clio explorer

   <img src="img/clio_explorer_new_environment.png" width="50%" alt="Initialize new environment">

2. Install `clio api` on the Creatio instance, use command line in clio or clio explorer
   ```bash
   clio install-gate -e <env_nname>
   ```
   IMAGE_HERE

3. Download previously initialized workspace from git repository
   
   ```bash
   git clone <git_repo_url>
   ```

4. Install application on creatio instance with clio explorer

    IMAGE_HERE


## Professional developers flow

Разработчик, используя IDE, разрабатывает исходный код, изменения в схемах делает через дизайнеров.
Использует выгрузку в РФС и загрузку чрез clio.
Использует работу с git через IDE или внешний тул


## No-Code developers flow

Разрабатывает через дизайнеров.
При обмене изменениями выгружает пакеты себе локально через clio explorer
Использует работу с git через "VS Code", чтобы залить свои изменения и забрать c изменениями разработчика


## Unit testing
Добавление проектов с тестами

clio init-test-project


## CI\CD process
Работа с CI\CD сервером
 
пример YAML файла


