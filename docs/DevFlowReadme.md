# Organize common development flow for No-Code and Proffesional developers


## Initilize environments

<Image how it look like>

## Proffesional developers

1. Install clio
```bash
dotnet tool install clio -g
```
> Recomened install plugin [clio explore](https://marketplace.visualstudio.com/items?itemName=AdvanceTechnologiesFoundation.clio-explorer) for [VS Code](https://code.visualstudio.com/download)

2. Deploy Creatio instance localy. [Creatio Academy](https://academy.creatio.com/docs/7-18/user/on_site_deployment/general_deployment_procedure/general_creatio_deployment_procedure) or using [clio automation](https://github.com/Advance-Technologies-Foundation/clio#installation-of-creatio-using-clio)

3. [Register environment in clio](https://github.com/Advance-Technologies-Foundation/clio#environment-settings) or via clio explorer
<IMAGE FROM CLIO ADD ENV IN CLIO EXPLORER>
4. Turn on [FSM mode](https://academy.creatio.com/docs/developer/development_tools/external_ides/overview#title-2098-3) r via clio explorer
<IMAGE FROM CLIO ADD ENV IN CLIO EXPLORER>

5. Create [workspace](https://github.com/Advance-Technologies-Foundation/clio#workspaces) for project
```bash
clio create-workspace
```

6. Create application in Creatio and link packages to workspace
```bash
clio l2r -r "Path to workspace folder" -e "{LOCAL_CREATIO_PATH}Terrasoft.WebApp\Terrasoft.Configuration\Pkg" -p "AppPackage1,AppPackage2"
``` 

7. Link workspace with environment
Add settings in file <workspace_path>/.clio/workspaceEnvironmentSettings.json your environment
```json
{
  "Environment": "<env_name>"
}
```
add settings of your environment in <workspace_path>/.clio/workspaceSettings.json
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

> You need configuration to organize CI\CD pipelines and unit tests

9. Create Git repository for workspace folder

## No-Code developer

Получает удаленную среду разработки и регистрирует его в clio explorer
Устанавливает в систему clio api через clio explorer
Выкачивает локально воркспейс из git
Ставит приложение через clio explorer на среду разработки


## Proffesional developers flow

Разработчик используя IDE разрабатывает исходный код, изменения в схемах делает через дизайнеры
Использует выгрузку в РФС и загрузку чрез clio
Использует работу с git через IDE или внешний тул


## No-Code developers flow

Разрабатывает через дизайнеры
При обмене изменениями выгружает пакеты себе локально через clio explorer
Использует работу с git через VS Code чтобы залить свои изименения и забрать изменения разработчика


## Unit testing
Добавление проектов с тестами

clio init-test-project


## CI\CD process
Работа с CI\CD сервером
 
пример YAML файла