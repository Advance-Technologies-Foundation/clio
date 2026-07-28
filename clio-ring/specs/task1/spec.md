This application will help users discore and start clio workspaces.

User may or may not provide additional arguments.

For example user may start ClioRing.Desktop.exe from any directory.
`ClioRing.Desktop`, must scan a folder defined in the `app-settings.json` file.
For instance `app-settings.json` file may define workspace folder as `C:\Projects\Workspaces`,
in this case I want the application to scan all workspaces in this folder and display them in a grid system with two buttons, Open NetFramework and Open .NetCore.

When a relevant button is cliecked then either a `open-test-solution-netcore.cmd` or `open-test-solution-framework.cmd` file must be executed from `Workspace\tasks` directory.

You can investigate directory structure in `C:\Projects\Workspaces\TIDE\` folder.

If workspace contains icons directory then icons must be displayed in the grid.
