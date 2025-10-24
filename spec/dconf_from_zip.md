=== ЗАДАНИЕ ===

Добавить возможность в команде dconf использвать такую сигнатуру

clio dconf --build {PATH_TO_ZIP_FILE_NAME}.zip 

при выполенении этой команды нужно разорхивировать указаный файл во временную директорю (директорию необходимо удалить после выполенения команды вне зависимости от успешности) будем называть полученную директорию
Creatio

если в разорхивированной папке находится папка вложенная Terrasoft.WebApp то это NetFramework Creatio

для NetFramework необходимо скопировать
содержимое Creatio из вложенной директории "Terrasoft.WebApp\bin"
в директорию workspace для которого вызвана команда dconf ".application\net-framework\core-bin"
также из Creatio нужно из Terrasoft.WebApp\Terrasoft.Configuration\Lib скопировать в workspace .application/net-framework/bin
также из Creatio нужно из Terrasoft.WebApp\conf\bin\{NUMBER} (взять нужно последний номер) скопировать в workspace .application/net-framework/bin там должны быть файлы Terrasoft.Configuration.dll
Terrasoft.Configuration.ODataEntities.dll
также из Creatio нужно из Terrasoft.WebApp\Terrasoft.Configuration\Pkg для каждой вложенной папки (єто папки пакетов) если в ней есть Files\bin нужно скопировать папку конкретного пакета в .application/net-framework/packages/

если Creatio не NetFramework то значит єто сборка NET8 то нужно сделать следующее копирование которое отличается от Framework
содержимое Creatio корневой директории все dll и pdb в директорию workspace для которого вызвана команда dconf ".application\net-core\core-bin"
также из Creatio нужно из Terrasoft.Configuration\Lib скопировать в workspace .application/net-core/bin
также из Creatio нужно из conf\bin\{NUMBER} (взять нужно последний номер) скопировать в workspace .application/net-core/bin там должны быть файлы Terrasoft.Configuration.dll
Terrasoft.Configuration.ODataEntities.dll
также из Creatio нужно из Terrasoft.Configuration\Pkg для каждой вложенной папки (єто папки пакетов) если в ней есть Files\bin\ нужно скопировать папку конкретного пакета в .application/net-core/packages/

**ДОПОЛНИТЕЛЬНОЕ ТРЕБОВАНИЕ (добавлено позже):**

Команда должна поддерживать работу не только с ZIP архивами, но и с уже распакованными директориями.

При передаче пути к директории (вместо ZIP файла):
- Команда должна автоматически определить, это ZIP архив или директория
- Если это директория (путь без расширения .zip), обработать её содержимое напрямую
- Исходная директория должна остаться на месте (НЕ удаляться)
- Это позволяет переиспользовать одну распакованную папку несколько раз

Автоматическое определение типа входа:
- Если путь оканчивается на .zip → обработать как ZIP архив (распаковать → обработать → удалить временную папку)
- Если путь не имеет расширения .zip → обработать как распакованную директорию (обработать на месте → оставить директорию)


=== РЕАЛИЗАЦИЯ ===

✅ Задание выполнено полностью. Дата завершения: 24 октября 2025
✅ Обновление NET8: 24 октября 2025 - Добавлена полная поддержка NET8 requirements
✅ Поддержка директорий: 24 октября 2025 - Добавлена поддержка работы с распакованными директориями

Реализованная функциональность:
- Добавлена опция --build (-b) в команду dconf для извлечения конфигурации из ZIP-файлов И распакованных директорий
- Реализована автоматическая очистка временной директории через callback механизм (для ZIP режима)
- Реализовано автоматическое определение типа входа: ZIP файл vs распакованная директория
- Добавлена полная поддержка обработки директорий без их удаления (для использования в CI/CD пайплайнах)
- Добавлена автоматическая детекция NetFramework (Terrasoft.WebApp) и NET8 (NetCore)
- Реализовано копирование всех необходимых файлов для обоих типов Creatio

Созданные/изменённые файлы:
1. clio/Command/DownloadConfigurationCommand.cs
   - Добавлена опция [Option('b', "build")] BuildZipPath (переименована с BuildZipPath на более универсальное имя)
   - Обновлена валидация BuildZipPath (DownloadConfigurationCommandOptionsValidator) для принятия ZIP файлов И директорий
   - Реализована условная маршрутизация между environment download и build mode (ZIP vs directory)
   - Метод Execute использует DownloadFromPath() для автоматического определения типа входа

2. clio/Workspace/ZipBasedApplicationDownloader.cs (НОВЫЙ ФАЙЛ - ~450 строк)
   - Интерфейс: IZipBasedApplicationDownloader с тремя методами:
     * DownloadFromPath(string path) - автоматическое определение типа (ZIP vs directory)
     * DownloadFromZip(string zipFilePath) - обработка ZIP архивов
     * DownloadFromDirectory(string directoryPath) - обработка распакованных директорий
   
   - Детекция типа: IsNetFrameworkCreatio() проверяет наличие Terrasoft.WebApp
   - Автоматическое определение входа: проверка расширения .zip на DownloadFromPath()
   
   NetFramework обработка:
     * CopyCoreBinFiles(): Terrasoft.WebApp/bin -> .application/net-framework/core-bin
     * CopyLibFiles(): Terrasoft.WebApp/Terrasoft.Configuration/Lib -> .application/net-framework/bin
     * CopyConfigurationBinFiles(): Terrasoft.WebApp/conf/bin/{NUMBER} -> .application/net-framework/bin
       (выбирается последний номер через OrderByDescending + int.Parse)
     * CopyPackages(): Terrasoft.WebApp/Terrasoft.Configuration/Pkg -> .application/net-framework/packages
       (копируются только пакеты с Files/bin)
   
   NET8 (NetCore) обработка:
     * CopyRootAssemblies(): ROOT/*.dll и ROOT/*.pdb -> .application/net-core/core-bin
       (фильтрация по расширению .dll и .pdb, исключая другие файлы)
     * CopyLibFiles(): Terrasoft.Configuration/Lib -> .application/net-core/bin
     * CopyNetCoreConfigurationBinFiles(): conf/bin/{NUMBER} -> .application/net-core/bin
       (выбирается последний номер через LINQ OrderByDescending + int.Parse)
     * CopyNetCorePackages(): Terrasoft.Configuration/Pkg -> .application/net-core/packages
       (копируются только пакеты с Files/bin)
   
   ZIP режим: IWorkingDirectoriesProvider.CreateTempDirectory(Action<string> callback) с автоматической очисткой
   Directory режим: обработка на месте без удаления исходной папки

3. clio/BindingsModule.cs
   - Зарегистрированы ZipBasedApplicationDownloader и DownloadConfigurationCommandOptionsValidator

4. clio/Commands.md
   - Добавлена полная секция "Download Configuration" с примерами всех трёх режимов
   - Документация по автоматическому определению типа входа (ZIP vs directory)
   - Документация по детекции NetFramework/NET8
   - Полное описание файловой структуры для обоих типов Creatio
   - Use cases: offline development, version comparison, CI/CD с pre-extracted директориями
   - Debug mode документация с примерами вывода

5. clio.tests/Workspace/ZipBasedApplicationDownloaderTests.cs (НОВЫЙ ФАЙЛ - ~700 строк)
   - 18 comprehensive unit tests (13 original + 5 новых для directory mode и auto-detection)
   
   Покрытие тестами:
     * NetFramework ZIP mode: detection (1), core-bin (1), lib (1), conf/bin (1), packages (1), warnings (1)
     * NetCore ZIP mode: detection (1), files copy (1), latest folder (1), packages (1)
     * Directory mode NetFramework: processing (1), no deletion (1)
     * Directory mode NetCore: processing (1), no deletion (1)
     * Error handling: missing directory (1)
     * Auto-detection: ZIP vs directory (2 теста)
     * Cleanup: temp directory deletion (1)

Unit тесты для нового функционала:
   * DownloadFromDirectory_ProcessesNetFrameworkDirectory_WithoutDeletion()
     - Проверяет обработку NetFramework директории
     - Проверяет что исходная директория НЕ удалена
   
   * DownloadFromDirectory_ProcessesNetCoreDirectory_WithoutDeletion()
     - Проверяет обработку NET8 директории
     - Проверяет что исходная директория НЕ удалена
   
   * DownloadFromDirectory_ThrowsException_WhenDirectoryNotFound()
     - Проверяет правильную обработку ошибок при несуществующей директории
   
   * DownloadFromPath_DetectsZipFile_AndProcessesAsZip()
     - Проверяет что .zip файлы определяются и обрабатываются как ZIP
     - Проверяет вызов Unzip()
   
   * DownloadFromPath_DetectsDirectory_AndProcessesAsDirectory()
     - Проверяет что директории определяются и обрабатываются напрямую
     - Проверяет что Unzip() НЕ вызывается
     - Проверяет что исходная директория остаётся на месте

Использует: NUnit, NSubstitute, FluentAssertions, MockFileSystem

Ключевые особенности реализации:
- Валидация входных данных (для ZIP: файл существует, расширение .zip, не пустой; для DIR: директория существует, не пустая)
- Автоматическое определение типа входа на основе расширения файла
- Умный выбор последней версии из conf/bin/{NUMBER} через LINQ
- Фильтрация пакетов по наличию Files/bin для обоих типов Creatio
- NET8: Копирование DLL/PDB из корневой директории с фильтрацией по расширению
- NET8: Отдельная логика для conf/bin/{NUMBER} с автоматическим выбором последнего
- Логирование прогресса и предупреждений на английском
- ZIP режим: Автоматическая очистка временной директории через callback pattern
- Directory режим: Исходная директория НЕ удаляется (может переиспользоваться)
- Поддержка как NetFramework, так и NET8 (NetCore) Creatio
- Использование IFileSystem для тестируемости
- Все сообщения и комментарии на английском языке согласно code style проекта

Результаты тестирования:
- ✅ 18/18 unit тестов прошли успешно (13 original + 5 новых)
- ✅ Нет регрессий в существующем функционале
- ✅ Все requirements (ZIP mode, NET8, Directory mode, Auto-detection) полностью покрыты тестами

Примеры использования:

```bash
# Режим 1: Скачивание с живого окружения
clio dconf -e myenv

# Режим 2: Извлечение из ZIP-файла NetFramework
clio dconf --build C:\downloads\creatio-netframework.zip

# Режим 3: Извлечение из ZIP-файла NET8 (NetCore)
clio dconf --build /path/to/creatio-net8.zip

# Режим 4: Обработка уже распакованной директории NetFramework
clio dconf --build C:\extracted\creatio-netframework

# Режим 5: Обработка уже распакованной директории NET8 (NetCore)
clio dconf --build C:\extracted\creatio-net8

# CI/CD использование - переиспользование одной директории
clio dconf --build ${CI_BUILD_DIR}/creatio-extracted/  # Первый запуск
clio dconf --build ${CI_BUILD_DIR}/creatio-extracted/  # Второй запуск ✅ Работает

# С debug логированием
clio dconf --build /path/to/creatio.zip --debug
clio dconf --build /path/to/extracted --debug
```

=== DEBUG ЛОГИРОВАНИЕ ===

✅ Добавлено: 24 октября 2025 - Реализовано детальное debug логирование для всех режимов

Функциональность:
- Все debug сообщения выводятся только при использовании флага --debug
- Debug сообщения помечены префиксом [DEBUG] для легкой идентификации
- Детальная информация о путях копирования файлов и директорий
- Информация о количестве скопированных и пропущенных файлов
- Список найденных numbered folders в conf/bin с выбором последнего
- Полная трассировка стека при ошибках в debug режиме
- Определение типа входа (ZIP vs Directory)
- Определение типа Creatio (NetFramework vs NetCore)

Debug Output Examples:

**ZIP режим (NetFramework):**
```
[DEBUG] DownloadConfigurationCommand: Using build mode with path=C:\creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=C:\creatio.zip
[DEBUG]   Workspace root: C:\workspace
[DEBUG]   Temporary directory created: C:\Temp\clio_abc123
[DEBUG] Detected NetFramework Creatio
[DEBUG] CopyCoreBinFiles: Source=C:\Temp\clio_abc123\Terrasoft.WebApp\bin, Destination=C:\workspace\.application\net-framework\core-bin
[DEBUG]   CopyAllFiles: 142 files from C:\Temp\clio_abc123\Terrasoft.WebApp\bin
[DEBUG] CopyConfigurationBinFiles: ConfBinPath=C:\Temp\clio_abc123\Terrasoft.WebApp\conf\bin
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3, Destination=C:\workspace\.application\net-framework\bin
[DEBUG] CopyPackages: Source=C:\Temp\clio_abc123\Terrasoft.WebApp\Terrasoft.Configuration\Pkg
[DEBUG]   Destination: C:\workspace\.application\net-framework\packages
[DEBUG] NetFramework packages summary: Copied=15, Skipped=3
```

**ZIP режим (NET8):**
```
[DEBUG] DownloadConfigurationCommand: Using build mode with path=/path/to/creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=/path/to/creatio.zip
[DEBUG]   Workspace root: /workspace
[DEBUG]   Temporary directory created: /tmp/clio_xyz789
[DEBUG] Detected NetCore Creatio
[DEBUG] CopyRootAssemblies: Source=/tmp/clio_xyz789, Destination=/workspace/.application/net-core/core-bin
[DEBUG]   Copied: Terrasoft.Core.dll -> /workspace/.application/net-core/core-bin/Terrasoft.Core.dll
[DEBUG]   Copied: Terrasoft.Core.pdb -> /workspace/.application/net-core/core-bin/Terrasoft.Core.pdb
[DEBUG] CopyNetCoreConfigurationBinFiles: ConfBinPath=/tmp/clio_xyz789/conf/bin
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3
[DEBUG] CopyNetCorePackages: NetCore packages summary: Copied=12, Skipped=2
```

**Directory режим (без удаления):**
```
[DEBUG] DownloadFromDirectory started: Directory=C:\extracted\creatio
[DEBUG]   Workspace root: C:\workspace
[DEBUG] Processing Creatio configuration from directory: C:\extracted\creatio
[DEBUG] Detected NetFramework Creatio
[DEBUG] CopyCoreBinFiles: Source=C:\extracted\creatio\Terrasoft.WebApp\bin, Destination=C:\workspace\.application\net-framework\core-bin
[DEBUG] Configuration download from directory completed successfully
```

Использование Debug Mode:
- Запуск: `clio dconf --build path/to/file.zip --debug`
- Механизм: глобальный флаг Program.IsDebugMode устанавливается при наличии --debug в args
- Производительность: минимальное влияние (проверка if перед каждым debug логом)
- Все debug логи на английском языке согласно code style проекта



=== РЕАЛИЗАЦИЯ ===

✅ Задание выполнено полностью. Дата завершения: 24 октября 2025
✅ Обновление NET8: 24 октября 2025 - Добавлена полная поддержка NET8 requirements

Реализованная функциональность:
- Добавлена опция --build (-b) в команду dconf для извлечения конфигурации из ZIP-файлов
- Реализована автоматическая очистка временной директории через callback механизм
- Добавлена автоматическая детекция NetFramework (Terrasoft.WebApp) и NET8 (NetCore)
- Реализовано копирование всех необходимых файлов для обоих типов Creatio

Созданные/изменённые файлы:
1. clio/Command/DownloadConfigurationCommand.cs
   - Добавлена опция [Option('b', "build")] BuildZipPath
   - Добавлена валидация BuildZipPath (DownloadConfigurationCommandOptionsValidator)
   - Реализована условная маршрутизация между environment download и ZIP extraction

2. clio/Workspace/ZipBasedApplicationDownloader.cs (НОВЫЙ ФАЙЛ - ~420 строк)
   - Интерфейс: IZipBasedApplicationDownloader
   - Детекция типа: IsNetFrameworkCreatio() проверяет наличие Terrasoft.WebApp
   
   NetFramework:
     * CopyCoreBinFiles(): Terrasoft.WebApp/bin -> .application/net-framework/core-bin
     * CopyLibFiles(): Terrasoft.WebApp/Terrasoft.Configuration/Lib -> .application/net-framework/bin
     * CopyConfigurationBinFiles(): Terrasoft.WebApp/conf/bin/{NUMBER} -> .application/net-framework/bin
       (выбирается последний номер через OrderByDescending + int.Parse)
     * CopyPackages(): Terrasoft.WebApp/Terrasoft.Configuration/Pkg -> .application/net-framework/packages
       (копируются только пакеты с Files/bin)
   
   NET8 (NetCore) - ОБНОВЛЕНО:
     * CopyRootAssemblies(): ROOT/*.dll и ROOT/*.pdb -> .application/net-core/core-bin
       (фильтрация по расширению .dll и .pdb, исключая другие файлы)
     * CopyLibFiles(): Terrasoft.Configuration/Lib -> .application/net-core/bin
     * CopyNetCoreConfigurationBinFiles(): conf/bin/{NUMBER} -> .application/net-core/bin
       (выбирается последний номер через LINQ OrderByDescending + int.Parse)
       Копируются: Terrasoft.Configuration.dll, Terrasoft.Configuration.ODataEntities.dll
     * CopyNetCorePackages(): Terrasoft.Configuration/Pkg -> .application/net-core/packages
       (копируются только пакеты с Files/bin, используется EnumerateDirectories + filtering)
   
   - Cleanup: IWorkingDirectoriesProvider.CreateTempDirectory(Action<string> callback)

3. clio/BindingsModule.cs
   - Зарегистрированы ZipBasedApplicationDownloader и DownloadConfigurationCommandOptionsValidator

4. clio/Commands.md
   - Добавлена секция "Download Configuration" с примерами использования обоих режимов
   - Документация по детекции NetFramework/NET8
   - Mapping файловой структуры для обоих типов Creatio
   - Use cases: offline development, version comparison, CI/CD

5. clio.tests/Workspace/ZipBasedApplicationDownloaderTests.cs (НОВЫЙ ФАЙЛ - ~520 строк)
   - 13 comprehensive unit tests (11 original + 2 new NET8-specific)
   
   Покрытие тестами:
     * NetFramework: detection (1), core-bin copying (1), lib copying (1), conf/bin copying (1), 
       packages filtering (2)
     * NET8: main test with assertions (1), latest folder selection (1), package filtering (1), 
       full integration (2)
     * Error handling: missing ZIP (1), invalid ZIP (1)
     * Cleanup: temp directory (1)
   
   NET8 Test Structure (CreateNetCoreStructure):
     * Корневые файлы: Terrasoft.Core.dll/.pdb, Terrasoft.Common.dll/.pdb, SomeOther.dll
     * Негативный тест: config.json (НЕ должен копироваться)
     * Terrasoft.Configuration/Lib: Newtonsoft.Json.dll, AutoMapper.dll
     * conf/bin/1, conf/bin/2, conf/bin/3 (выбирается папка 3 как последняя)
     * Packages: NetCorePackage1 (с Files/bin ✓), NetCorePackage2 (без Files/bin ✗), 
       NetCorePackage3 (с Files/bin ✓)
   
   NET8 Specific Tests:
     * DownloadFromZip_CopiesFiles_ForNetCore - комплексный тест с 4 группами assertions:
       1. Root assemblies (DLLs и PDBs копируются, config.json НЕ копируется)
       2. Lib files (Newtonsoft.Json.dll, AutoMapper.dll)
       3. Configuration bin (Terrasoft.Configuration.dll, ODataEntities.dll из папки 3)
       4. Packages (NetCorePackage1 ✓, NetCorePackage2 ✗, NetCorePackage3 ✓)
     * DownloadFromZip_SelectsLatestNumberedFolder_ForNetCore - проверка выбора папки 3
     * DownloadFromZip_CopiesOnlyPackagesWithFilesBin_ForNetCore - проверка фильтрации пакетов
   
   - Использует: NUnit, NSubstitute, FluentAssertions, MockFileSystem

Ключевые особенности реализации:
- Валидация входных данных (файл существует, расширение .zip, не пустой)
- Умный выбор последней версии из conf/bin/{NUMBER} через LINQ
- Фильтрация пакетов по наличию Files/bin для обоих типов Creatio
- NET8: Копирование DLL/PDB из корневой директории с фильтрацией по расширению
- NET8: Отдельная логика для conf/bin/{NUMBER} с автоматическим выбором последнего
- Логирование прогресса и предупреждений на английском
- Автоматическая очистка временной директории через callback pattern
- Поддержка как NetFramework, так и NET8 (NetCore) Creatio
- Использование IFileSystem для тестируемости
- Все сообщения и комментарии на английском языке согласно code style проекта

Результаты тестирования:
- ✅ 13/13 новых unit тестов прошли успешно (11 original + 2 NET8-specific)
- ✅ 735/735 всех тестов проекта прошли успешно (+2 новых теста)
- ✅ Нет регрессий в существующем функционале
- ✅ Все NET8 requirements полностью покрыты тестами

Примеры использования:
```bash
# Извлечение конфигурации из ZIP-файла NetFramework
clio dconf --build C:\downloads\creatio-netframework.zip

# Извлечение конфигурации из ZIP-файла NET8 (NetCore)
clio dconf --build /path/to/creatio-net8.zip

# Стандартный режим (скачивание с environment) продолжает работать
clio dconf -e myenv

# Использование с debug режимом для детальной информации
clio dconf --build /path/to/creatio.zip --debug
```

=== DEBUG ЛОГИРОВАНИЕ ===

✅ Добавлено: 24 октября 2025 - Реализовано детальное debug логирование

Функциональность:
- Все debug сообщения выводятся только при использовании флага --debug
- Debug сообщения помечены префиксом [DEBUG] для легкой идентификации
- Детальная информация о путях копирования файлов и директорий
- Информация о количестве скопированных и пропущенных файлов
- Список найденных numbered folders в conf/bin с выбором последнего
- Полная трассировка стека при ошибках в debug режиме

Обновленные файлы:

1. clio/Command/DownloadConfigurationCommand.cs
   - Добавлено логирование режима работы (ZIP или environment)
   - Добавлен вывод полного stack trace при ошибках в debug режиме
   - Проверка Program.IsDebugMode для условного вывода

2. clio/Workspace/ZipBasedApplicationDownloader.cs
   Добавлено debug логирование во все key методы:
   
   * DownloadFromZip() - основной метод:
     - Входные параметры: ZIP file path, Workspace root
     - Созданная временная директория
   
   * NetFramework методы:
     - CopyCoreBinFiles(): source/destination paths
     - CopyLibFiles(): source/destination paths  
     - CopyConfigurationBinFiles(): conf/bin path, найденные numbered folders, выбранный последний
     - CopyPackages(): source/destination, списки скопированных и пропущенных пакетов
   
   * NET8 (NetCore) методы:
     - CopyRootAssemblies(): source/destination, каждый скопированный DLL/PDB файл
     - CopyNetCoreConfigurationBinFiles(): conf/bin path, найденные folders, выбранный последний
     - CopyNetCorePackages(): source/destination, скопированные и пропущенные пакеты
   
   * Utility методы:
     - CopyAllFiles(): количество файлов и список каждого
     - CopyFileIfExists(): source -> destination paths для каждого файла

Debug Output Example (NetFramework):
```
[DEBUG] DownloadConfigurationCommand: Using ZIP mode with file=C:\creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=C:\creatio.zip
[DEBUG]   Workspace root: C:\workspace
[DEBUG]   Temporary directory created: C:\Temp\clio_abc123
[DEBUG] CopyCoreBinFiles: Source=C:\Temp\clio_abc123\Terrasoft.WebApp\bin, Destination=C:\workspace\.application\net-framework\core-bin
[DEBUG]   CopyAllFiles: 142 files from C:\Temp\clio_abc123\Terrasoft.WebApp\bin
[DEBUG]     Terrasoft.Core.dll
[DEBUG]     Terrasoft.Common.dll
...
[DEBUG] CopyLibFiles: Source=C:\Temp\clio_abc123\Terrasoft.WebApp\Terrasoft.Configuration\Lib, Destination=C:\workspace\.application\net-framework\bin
[DEBUG] CopyConfigurationBinFiles: ConfBinPath=C:\Temp\clio_abc123\Terrasoft.WebApp\conf\bin
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3, Destination=C:\workspace\.application\net-framework\bin
[DEBUG] CopyPackages: Source=C:\Temp\clio_abc123\Terrasoft.WebApp\Terrasoft.Configuration\Pkg
[DEBUG]   Destination: C:\workspace\.application\net-framework\packages
[DEBUG]   CustomPackage1: ...\CustomPackage1 -> C:\workspace\.application\net-framework\packages\CustomPackage1
[DEBUG]   Skipped CustomPackage2 (no Files/bin folder)
[DEBUG] NetFramework packages summary: Copied=15, Skipped=3
```

Debug Output Example (NET8):
```
[DEBUG] DownloadConfigurationCommand: Using ZIP mode with file=/path/to/creatio.zip
[DEBUG] DownloadFromZip started: ZipFile=/path/to/creatio.zip
[DEBUG]   Workspace root: /workspace
[DEBUG]   Temporary directory created: /tmp/clio_xyz789
[DEBUG] CopyRootAssemblies: Source=/tmp/clio_xyz789, Destination=/workspace/.application/net-core/core-bin
[DEBUG]   Copied: Terrasoft.Core.dll -> /workspace/.application/net-core/core-bin/Terrasoft.Core.dll
[DEBUG]   Copied: Terrasoft.Core.pdb -> /workspace/.application/net-core/core-bin/Terrasoft.Core.pdb
[DEBUG]   Copied: Terrasoft.Common.dll -> /workspace/.application/net-core/core-bin/Terrasoft.Common.dll
...
[DEBUG] CopyNetCoreConfigurationBinFiles: ConfBinPath=/tmp/clio_xyz789/conf/bin, Destination=/workspace/.application/net-core/bin
[DEBUG]   Found 3 numbered folders: 3, 2, 1
[DEBUG]   Selected latest folder: 3
[DEBUG] CopyNetCorePackages: Source=/tmp/clio_xyz789/Terrasoft.Configuration/Pkg
[DEBUG]   Destination: /workspace/.application/net-core/packages
[DEBUG]   NetCorePackage1: /tmp/clio_xyz789/Terrasoft.Configuration/Pkg/NetCorePackage1 -> /workspace/.application/net-core/packages/NetCorePackage1
[DEBUG]   Skipped NetCorePackage2 (no Files/bin folder)
[DEBUG] NetCore packages summary: Copied=12, Skipped=2
```

Использование Debug Mode:
- Запуск: `clio dconf --build path/to/file.zip --debug`
- Механизм: глобальный флаг Program.IsDebugMode устанавливается при наличии --debug в args
- Производительность: минимальное влияние (проверка if перед каждым debug логом)
- Все debug логи на английском языке согласно code style проекта