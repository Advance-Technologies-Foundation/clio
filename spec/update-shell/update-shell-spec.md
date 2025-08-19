 У меня есть структура монорепозитиория

# Project Tree

```
├── .angular
│   └── cache
│       └── 17.3.11
├── .dockerignore
├── .editorconfig
├── .env.development
├── .eslintignore
├── .eslintrc.json
├── .gitattributes
├── .gitignore
├── .husky
│   ├── .gitignore
│   ├── _
│   │   ├── .gitignore
│   │   └── husky.sh
│   ├── commit-msg
│   └── pre-commit
├── .huskyrc.js
├── .lintstaged-commitmsg-rc
├── .lintstaged-precommit-rc
├── .ncurc
├── .ncurc.json
├── .npmrc
├── .nx
│   └── cache│ 
├── .nxignore
├── .pipeline
│   ├── Jenkinsfile
│   ├── Jenkinsfile.Night
│   ├── Jenkinsfile.Release
│   └── node_modules_caching
│       └── Jenkinsfile
├── .prettierignore
├── .prettierrc
├── .puppeteerrc.cjs
├── .storybook
│   ├── .pipeline
│   │   └── Jenkinsfile
│   ├── README.md
│   ├── addons-settings.ts
│   ├── assets
│   │   ├── actions-menu
│   │   ├── assets
│   │   ├── carousel
│   │   ├── connection-indicator
│   │   ├── diagram
│   │   ├── file-drop
│   │   ├── fonts
│   │   ├── gallery
│   │   ├── image-input
│   │   ├── list-selection-dialog
│   │   ├── nav-panel
│   │   ├── next-best-offer
│   │   ├── select-menu
│   │   ├── themes
│   │   └── toolbar-navigation
│   ├── install
│   │   ├── Dockerfile
│   │   └── helm
│   ├── main.ts
│   ├── manager.js
│   ├── preview-head.html
│   ├── preview.ts
│   ├── styles
│   │   ├── entrypoint.scss
│   │   ├── fonts.scss
│   │   ├── main.scss
│   │   ├── studio-enterprise.theme.scss
│   │   └── studio-free.theme.scss
│   ├── test-runner-jest.config.js
│   ├── test-runner.ts
│   └── typings.d.ts
├── .vs
│   └── slnx.sqlite
├── README.NX.update.md
├── README.md
├── all_build.js
├── all_test.js
├── apps
│   ├── cdk
│   │   └── shell
│   ├── pkgs
│   │   ├── finserv-product-catalog
│   │   └── pages-runtime-tests
│   ├── social-network-integration
│   │   └── connector
│   ├── studio-enterprise
│   │   ├── analytics-dashboard
│   │   ├── bank-product-selection
│   │   ├── campaign-designer
│   │   ├── campaign-gallery
│   │   ├── confidence-level-widget
│   │   ├── duplicates-widget
│   │   ├── error-list-dialog
│   │   ├── forecast
│   │   ├── login
│   │   ├── marketing-campaign
│   │   ├── omnichannel-messaging
│   │   ├── page-wizard
│   │   ├── pivot-table
│   │   ├── process-designer
│   │   ├── relationship-diagram
│   │   ├── schema-view
│   │   ├── service-model-network
│   │   ├── shell
│   │   ├── structure-explorer
│   │   ├── system-designer
│   │   ├── term-calculation
│   │   ├── two-fa-app
│   │   ├── voice-to-text
│   │   └── web-service
│   └── studio-free
│       ├── bpmn-diagram-service
│       ├── client-log-service
│       ├── login
│       ├── process-catalogue-service
│       ├── process-designer
│       ├── registration
│       └── reset-password
├── blackduckignore.json
├── build-with-local-update.bat
├── build-with-local-update.sh
├── creatio.json
├── generate-safari-favicon.js
├── git
├── jest-migrations.json
├── jest.config.ts
├── jest.preset.js
├── jest.preset.legacy.js
├── jsdom-environment.ts
├── karma.conf.diagram.js
├── karma.conf.js
├── karma.process.env.js
├── libs
│   ├── devkit
│   │   ├── base
│   │   ├── common
│   │   ├── interface-designer
│   │   └── scripts
│   ├── sdk
│   │   ├── data-access
│   │   ├── feature
│   │   ├── styling
│   │   ├── ui
│   │   └── util
│   ├── social-network-integration
│   │   └── feature
│   ├── studio-enterprise
│   │   ├── data-access
│   │   ├── feature
│   │   ├── styling
│   │   ├── ui
│   │   └── util
│   └── studio-free
│       ├── data-access
│       ├── feature
│       ├── styling
│       ├── ui
│       └── util
├── lint_all_projects.js
├── mf-build-utils.js
├── nx.json
├── package-lock.json
├── package.json
├── page-start-load.js
├── patches
│   ├── @angular+cdk+16.2.14.patch
│   ├── @angular-architects+module-federation+15.0.3.patch
│   ├── @log4js-node+rabbitmq+2.0.0.patch
│   ├── @types+lodash-es+4.17.4.patch
│   ├── amqplib+0.10.3.patch
│   ├── browserchannel+2.1.0.patch
│   ├── drag-mock+1.4.0.patch
│   ├── eslint-plugin-import+2.27.5-1.patch
│   └── jest-raw-loader+1.0.1.patch
├── run-studio-free-dev.bat
├── run_component_tests.bat
├── run_page_tests.bat
├── setup-jest.ts
├── shared-mappings.js
├── storybook-migrations.json
├── tmp
│   └── libs
│       ├── devkit
│       └── sdk
├── tools
│   ├── cmd
│   │   ├── InstallNpmPackages.cmd
│   │   ├── UpdateNpmPackages.cmd
│   │   ├── commit.cmd
│   │   ├── macos
│   │   └── run_affected_tests.cmd
│   ├── eslint
│   │   ├── .babelrc
│   │   ├── .gitignore
│   │   ├── class-validator-import-rule
│   │   ├── devkit-internal-import-rule
│   │   ├── index.js
│   │   ├── package-lock.json
│   │   ├── package.json
│   │   ├── type-prefix-rule
│   │   └── utils
│   ├── generators
│   │   ├── .gitkeep
│   │   ├── .npmrc
│   │   └── terrasoft
│   ├── get-local-update-path.js
│   ├── i18n
│   │   ├── README.md
│   │   ├── export.command.ts
│   │   ├── import.command.ts
│   │   ├── index.ts
│   │   ├── types
│   │   └── utils
│   ├── jest-transformer
│   │   ├── package-lock.json
│   │   ├── package.json
│   │   └── src
│   ├── local-update.js
│   ├── performance
│   │   ├── analyze.js
│   │   └── source-map.service.js
│   ├── pipeline.groovy
│   ├── project-json-path.js
│   ├── proxy.conf.json
│   ├── snippets
│   │   ├── .gitkeep
│   │   └── vscode
│   ├── studio-free
│   │   ├── README.md
│   │   ├── consts.ps1
│   │   ├── deploy.ps1
│   │   └── menu.ps1
│   ├── tsconfig.tools.json
│   ├── web.config
│   └── workspace-plugin
│       ├── .eslintrc.json
│       ├── generators.json
│       ├── jest.config.ts
│       ├── package.json
│       ├── project.json
│       ├── src
│       ├── tsconfig.json
│       ├── tsconfig.lib.json
│       └── tsconfig.spec.json
├── tsconfig.base.json
├── tsconfig.eslint.json
└── tsconfig.spec.json
```
 
 Я хочу создать командлу update-shell котороую вызвать  в любом месте этой структуры
 взял все файлы из папки относительно корня по пути
 \dist\apps\studio-enterprise\shell
 запакуй содержимое в gzip
 убедись что системная настройка в Creatio с кодом MaxFileSize
 больше чем размер файла (значение системной настройки соответсвует макс имальному размеру файла в MB) если нет увеличь до размера gzip файла + 5 MB
и вызови Endpoint из clio gate CreatioApiGateway/UploadStaticFile передав туда полученный gzip архив и путь для распаковки Shell

в команде должен быть опциональный флаг build и поддержан параметр -e для указания среды куда нужно загрузить контент

```
clio update-shell -e ENV_NAME --build
```

при указании параметра build необходимо перед командой запустить скрипт
npm run build:shell
в корневой директории монорепозитория