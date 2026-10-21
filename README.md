# QuickStart Launcher

This is a simple Windows application to sequentially launch a list of actions (open apps, websites, run commands, etc.).

## How to Build

1.  **Prerequisites**:
    *   .NET 8 SDK

2.  **Build Command**:
    Open a terminal in the `QuickStartLauncher` directory and run the following command:
    ```sh
    dotnet build -c Release
    ```
    The output will be in `bin/Release/net8.0-windows/`.

## How to Run

1.  Run the `QuickStartLauncher.exe` file.
2.  Click the **⚙** icon to open the configuration window.
3.  The configuration is stored in `%LocalAppData%\QuickStart\config\config.json`. You can edit this file manually or use the configuration window (currently in a basic state).
4.  Click **Begin** to run the actions defined in the active profile. The application will exit automatically when the queue is finished.

## Project Structure

*   `/` - Main project files (`.csproj`, `App.xaml`, `MainWindow.xaml`)
*   `/Core/` - Core components like `ViewModelBase` and `RelayCommand`.
*   `/Models/` - Data models for actions, profiles, and configuration.
*   `/Services/` - Business logic services (Config, Action Executor, App Indexer, Logging).
*   `/ViewModels/` - ViewModels for the application's windows.
*   `/Views/` - XAML files for the application's windows and user controls.