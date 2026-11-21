Imports System.Diagnostics
Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms
Imports Microsoft.Web.WebView2.Core
Imports Microsoft.Web.WebView2.WinForms

Module Program
    <STAThread>
    Sub Main()
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)
        Application.Run(New QaForm())
    End Sub
End Module

Friend Class QaForm
    Inherits Form

    Private Shared ReadOnly Ollama As New HttpClient With {.Timeout = TimeSpan.FromSeconds(5)}
    Private ReadOnly UrlInput As New TextBox With {.PlaceholderText = "https://dustinsison.com"}
    Private ReadOnly CharterInput As New TextBox With {.Multiline = True, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly ModelInput As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly RefreshModels As New Button With {.Text = "Refresh models", .AutoSize = True}
    Private ReadOnly YouTube As New CheckBox With {.Text = "Allow YouTube embeds", .AutoSize = True}
    Private ReadOnly ShowBrowser As New CheckBox With {.Text = "Show live browser", .AutoSize = True, .Checked = True}
    Private ReadOnly HistoryInput As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
    Private ReadOnly History As New List(Of RunInputs)
    Private Shared ReadOnly HistoryPath As String = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ExploratoryQaAgent", "history.json")
    Private ReadOnly EvidenceTabs As New TabControl With {.Dock = DockStyle.Fill}
    Private ReadOnly RecordingTab As New TabPage("Recording")
    Private ReadOnly Player As New WebView2 With {.Dock = DockStyle.Fill, .Visible = False}
    Private ReadOnly PlayerStatus As New Label With {.Text = "No recording available.", .Dock = DockStyle.Fill}
    Private ReadOnly AssetInput As New TextBox With {.PlaceholderText = "https://c0.wp.com"}
    Private ReadOnly RunButton As New Button With {.Text = "Run test", .AutoSize = True, .Enabled = False}
    Private ReadOnly StopButton As New Button With {.Text = "Stop run", .AutoSize = True, .Enabled = False}
    Private ActiveRunner As Process
    Private ReadOnly OpenButton As New Button With {.Text = "Open artifacts", .AutoSize = True, .Enabled = False}
    Private ReadOnly StatusText As New Label With {.AutoSize = True}
    Private ReadOnly Output As New TextBox With {
        .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Fill
    }
    Private ArtifactFolder As String
    Private Running As Boolean

    Public Sub New()
        Text = "Exploratory QA Agent"
        ClientSize = New Drawing.Size(850, 700)
        MinimumSize = New Drawing.Size(620, 560)
        StartPosition = FormStartPosition.CenterScreen

        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Padding = New Padding(12), .ColumnCount = 2
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Controls.Add(layout)
        AddField(layout, "Recent runs", HistoryInput)
        AddField(layout, "URL", UrlInput)
        CharterInput.Height = 95
        AddField(layout, "Charter", CharterInput)
        Dim modelRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        ModelInput.Width = 220
        modelRow.Controls.Add(ModelInput)
        modelRow.Controls.Add(RefreshModels)
        AddField(layout, "Ollama model", modelRow)
        AddField(layout, "Allowed asset origins", AssetInput)
        Dim options As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        options.Controls.Add(YouTube)
        options.Controls.Add(ShowBrowser)
        AddField(layout, "", options)

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True}
        buttons.Controls.Add(RunButton)
        buttons.Controls.Add(StopButton)
        buttons.Controls.Add(OpenButton)
        buttons.Controls.Add(StatusText)
        AddFullRow(layout, buttons, SizeType.AutoSize)
        Dim logTab As New TabPage("Activity")
        logTab.Controls.Add(Output)
        RecordingTab.Controls.Add(Player)
        RecordingTab.Controls.Add(PlayerStatus)
        EvidenceTabs.TabPages.Add(logTab)
        EvidenceTabs.TabPages.Add(RecordingTab)
        AddFullRow(layout, EvidenceTabs, SizeType.Percent)
        ReadHistory()
        AddHandler HistoryInput.SelectionChangeCommitted, AddressOf RestoreInputs

        AddHandler Shown, AddressOf LoadModels
        AddHandler RefreshModels.Click, AddressOf LoadModels
        AddHandler ModelInput.SelectionChangeCommitted, Sub(sender, args)
            RunButton.Enabled = Not Running AndAlso RefreshModels.Enabled AndAlso ModelInput.SelectedIndex >= 0
        End Sub
        AddHandler RunButton.Click, AddressOf RunTest
        AddHandler StopButton.Click, AddressOf StopRun
        AddHandler OpenButton.Click, AddressOf OpenArtifacts
        AddHandler FormClosing, Sub(sender, args)
                                    If Running Then args.Cancel = True
                                End Sub
    End Sub

    Private Shared Sub AddField(layout As TableLayoutPanel, caption As String, field As Control)
        Dim row = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New Label With {.Text = caption, .AutoSize = True, .Margin = New Padding(3, 8, 12, 0)}, 0, row)
        field.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        layout.Controls.Add(field, 1, row)
    End Sub

    Private Shared Sub AddFullRow(layout As TableLayoutPanel, content As Control, size As SizeType)
        Dim row = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(size, 100))
        layout.Controls.Add(content, 0, row)
        layout.SetColumnSpan(content, 2)
    End Sub

    Private Async Sub LoadModels(sender As Object, args As EventArgs)
        RefreshModels.Enabled = False
        RunButton.Enabled = False
        Try
            StatusText.Text = "Loading models…"
            Dim previous = TryCast(ModelInput.SelectedItem, String)
            Using response = Await Ollama.GetAsync("http://127.0.0.1:11434/api/tags")
                response.EnsureSuccessStatusCode()
                Using payload = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                    ModelInput.Items.Clear()
                    For Each entry In payload.RootElement.GetProperty("models").EnumerateArray()
                        Dim name = entry.GetProperty("name").GetString()
                        If Not String.IsNullOrWhiteSpace(name) Then ModelInput.Items.Add(name)
                    Next
                End Using
            End Using
            If ModelInput.Items.Count > 0 Then
                Dim preferred = ModelInput.Items.IndexOf(If(previous, "qwen2.5:7b"))
                ModelInput.SelectedIndex = Math.Max(preferred, 0)
                StatusText.Text = "Ready"
            Else
                StatusText.Text = "No Ollama models installed."
            End If
        Catch ex As Exception
            StatusText.Text = "Could not load Ollama models."
            Output.AppendText(ex.Message & Environment.NewLine)
        Finally
            RefreshModels.Enabled = True
            RunButton.Enabled = ModelInput.SelectedIndex >= 0
        End Try
    End Sub

    Private Async Sub RunTest(sender As Object, args As EventArgs)
        Dim address As Uri = Nothing
        If Not Uri.TryCreate(UrlInput.Text.Trim(), UriKind.Absolute, address) OrElse
            (address.Scheme <> Uri.UriSchemeHttp AndAlso address.Scheme <> Uri.UriSchemeHttps) OrElse
            address.UserInfo <> "" OrElse String.IsNullOrWhiteSpace(CharterInput.Text) Then
            StatusText.Text = "Enter an HTTP(S) URL and a charter."
            Return
        End If

        Running = True
        RunButton.Enabled = False
        RefreshModels.Enabled = False
        OpenButton.Enabled = False
        HistoryInput.Enabled = False
        Output.Clear()
        EvidenceTabs.SelectedIndex = 0
        ResetRecording()
        StatusText.Text = "Running…"
        Dim inputs As New RunInputs With {
            .Url = address.AbsoluteUri, .Charter = CharterInput.Text.Trim(), .Model = CStr(ModelInput.SelectedItem),
            .YouTube = YouTube.Checked, .ShowBrowser = ShowBrowser.Checked, .AssetOrigins = AssetInput.Text.Trim()
        }
        Try
            Dim repo = FindRepository()
            Dim startInfo As New ProcessStartInfo("node") With {
                .UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = repo,
                .RedirectStandardOutput = True, .RedirectStandardError = True, .RedirectStandardInput = True,
                .StandardOutputEncoding = Encoding.UTF8, .StandardErrorEncoding = Encoding.UTF8
            }
            For Each argument In {"--import", "tsx", "src/qa.ts", "--url", address.AbsoluteUri,
                                  "--charter", inputs.Charter, "--model", inputs.Model, "--desktop-control"}
                startInfo.ArgumentList.Add(argument)
            Next
            If inputs.YouTube Then startInfo.ArgumentList.Add("--allow-youtube-embeds")
            If inputs.ShowBrowser Then startInfo.ArgumentList.Add("--headed")
            For Each origin In inputs.AssetOrigins.Split(","c, StringSplitOptions.RemoveEmptyEntries Or StringSplitOptions.TrimEntries)
                startInfo.ArgumentList.Add("--asset-origin")
                startInfo.ArgumentList.Add(origin)
            Next

            Using runner As New Process With {.StartInfo = startInfo}
                AddHandler runner.ErrorDataReceived, AddressOf ShowRunnerOutput
                runner.Start()
                ActiveRunner = runner
                StopButton.Enabled = True
                runner.BeginErrorReadLine()
                SaveInputs(inputs)
                Dim reportTask = runner.StandardOutput.ReadToEndAsync()
                Await runner.WaitForExitAsync()
                Dim report = (Await reportTask).Trim()
                If report <> "" Then
                    Output.AppendText(report & Environment.NewLine)
                    Dim reportPath = Path.GetFullPath(report, repo)
                    If File.Exists(reportPath) Then
                        ArtifactFolder = Path.GetDirectoryName(reportPath)
                        OpenButton.Enabled = True
                        Await LoadRecording(ArtifactFolder)
                    End If
                End If
                Select Case runner.ExitCode
                    Case 0 : StatusText.Text = "Run finished."
                    Case 130 : StatusText.Text = "Run stopped."
                    Case Else : StatusText.Text = "Run incomplete or failed."
                End Select
            End Using
        Catch ex As Exception
            StatusText.Text = "Could not run the agent."
            Output.AppendText(ex.Message & Environment.NewLine)
        Finally
            ActiveRunner = Nothing
            StopButton.Enabled = False
            Running = False
            RefreshModels.Enabled = True
            HistoryInput.Enabled = History.Count > 0
            RunButton.Enabled = True
        End Try
    End Sub

    Private Sub ReadHistory()
        Try
            If File.Exists(HistoryPath) Then
                Dim saved = JsonSerializer.Deserialize(Of List(Of RunInputs))(File.ReadAllText(HistoryPath))
                If saved IsNot Nothing Then History.AddRange(saved)
            End If
        Catch ex As Exception
            Output.AppendText("Could not read run history: " & ex.Message & Environment.NewLine)
        End Try
        UpdateHistory()
    End Sub

    Private Sub UpdateHistory()
        HistoryInput.Items.Clear()
        For Each entry In History
            Dim firstLine = entry.Charter.Split(ChrW(10))(0).Trim()
            If firstLine.Length > 75 Then firstLine = firstLine.Substring(0, 75) & "…"
            HistoryInput.Items.Add(entry.Url & " — " & firstLine & " [" & entry.Model & "]")
        Next
        HistoryInput.Enabled = Not Running AndAlso History.Count > 0
    End Sub

    Private Sub SaveInputs(inputs As RunInputs)
        History.RemoveAll(Function(entry) entry.Url = inputs.Url AndAlso entry.Charter = inputs.Charter AndAlso
            entry.Model = inputs.Model AndAlso entry.YouTube = inputs.YouTube AndAlso
            entry.ShowBrowser = inputs.ShowBrowser AndAlso entry.AssetOrigins = inputs.AssetOrigins)
        History.Insert(0, inputs)
        If History.Count > 25 Then History.RemoveRange(25, History.Count - 25)
        UpdateHistory()
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath))
            Dim pending = HistoryPath & ".tmp"
            File.WriteAllText(pending, JsonSerializer.Serialize(History))
            File.Move(pending, HistoryPath, overwrite:=True)
        Catch ex As Exception
            Output.AppendText("Could not save run history: " & ex.Message & Environment.NewLine)
        End Try
    End Sub

    Private Sub RestoreInputs(sender As Object, args As EventArgs)
        If HistoryInput.SelectedIndex < 0 Then Return
        Dim entry = History(HistoryInput.SelectedIndex)
        UrlInput.Text = entry.Url
        CharterInput.Text = entry.Charter
        YouTube.Checked = entry.YouTube
        ShowBrowser.Checked = entry.ShowBrowser
        AssetInput.Text = entry.AssetOrigins
        ModelInput.SelectedIndex = ModelInput.Items.IndexOf(entry.Model)
        RunButton.Enabled = ModelInput.SelectedIndex >= 0 AndAlso RefreshModels.Enabled
        StatusText.Text = If(ModelInput.SelectedIndex >= 0, "Inputs restored.", "Install the saved model or select another.")
    End Sub

    Private Sub ResetRecording()
        If Player.CoreWebView2 IsNot Nothing Then Player.CoreWebView2.Navigate("about:blank")
        Player.Visible = False
        PlayerStatus.Text = "No recording available."
        PlayerStatus.Visible = True
    End Sub

    Private Async Function LoadRecording(folder As String) As Task
        Dim videoFolder = Path.Combine(folder, "video")
        If Not Directory.Exists(videoFolder) Then Return
        Dim video = Directory.EnumerateFiles(videoFolder, "*.webm").OrderBy(Function(videoPath) File.GetCreationTimeUtc(videoPath)).FirstOrDefault()
        If video Is Nothing Then Return

        Try
            If Player.CoreWebView2 Is Nothing Then
                Dim environment = Await CoreWebView2Environment.CreateAsync(
                    userDataFolder:=Path.Combine(Path.GetDirectoryName(HistoryPath), "WebView2"))
                Await Player.EnsureCoreWebView2Async(environment)
            End If
            Player.CoreWebView2.SetVirtualHostNameToFolderMapping("qa-recording.invalid", videoFolder,
                                                                 CoreWebView2HostResourceAccessKind.Allow)
            Dim source = "https://qa-recording.invalid/" & Uri.EscapeDataString(Path.GetFileName(video))
            Player.CoreWebView2.NavigateToString(
                "<!doctype html><html lang='en'><meta charset='utf-8'><title>QA recording</title>" &
                "<body style='margin:0;background:#171717'><video controls preload='metadata' " &
                "style='width:100vw;height:100vh' src='" & source & "'></video></body></html>")
            PlayerStatus.Visible = False
            Player.Visible = True
            EvidenceTabs.SelectedTab = RecordingTab
        Catch ex As Exception
            PlayerStatus.Text = "Could not load the embedded player. Use Open artifacts to play the WebM file."
            Output.AppendText(ex.Message & Environment.NewLine)
        End Try
    End Function

    Private Sub StopRun(sender As Object, args As EventArgs)
        If ActiveRunner Is Nothing OrElse ActiveRunner.HasExited Then Return
        StopButton.Enabled = False
        StatusText.Text = "Stopping and saving evidence…"
        Try
            ActiveRunner.StandardInput.Close()
        Catch ex As IOException
            Output.AppendText(ex.Message & Environment.NewLine)
        End Try
    End Sub

    Private Sub ShowRunnerOutput(sender As Object, args As DataReceivedEventArgs)
        Dim line = args.Data
        If line IsNot Nothing Then
            BeginInvoke(Sub() Output.AppendText(line & Environment.NewLine))
        End If
    End Sub

    Private Sub OpenArtifacts(sender As Object, args As EventArgs)
        Try
            Process.Start(New ProcessStartInfo(ArtifactFolder) With {.UseShellExecute = True})
        Catch ex As Exception
            StatusText.Text = "Could not open the artifact folder."
            Output.AppendText(ex.Message & Environment.NewLine)
        End Try
    End Sub

    Private Shared Function FindRepository() As String
        Dim directory As New DirectoryInfo(AppContext.BaseDirectory)
        While directory IsNot Nothing
            If File.Exists(Path.Combine(directory.FullName, "src", "qa.ts")) Then Return directory.FullName
            directory = directory.Parent
        End While
        Throw New DirectoryNotFoundException("Run the desktop app from a build inside the cloned repository.")
    End Function
End Class

Friend Class RunInputs
    Public Property Url As String = ""
    Public Property Charter As String = ""
    Public Property Model As String = ""
    Public Property YouTube As Boolean
    Public Property ShowBrowser As Boolean = True
    Public Property AssetOrigins As String = ""
End Class
