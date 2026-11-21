Imports System.IO

Friend Module DesktopPaths
    Friend ReadOnly AppDataDirectory As String = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExploratoryQaAgent")
    Friend ReadOnly HistoryPath As String = Path.Combine(AppDataDirectory, "history.json")
    Friend ReadOnly AssetSettingsPath As String = Path.Combine(AppDataDirectory, "asset-origins.json")
    Friend ReadOnly ProfilePath As String = Path.Combine(AppDataDirectory, "profiles.json")
End Module
