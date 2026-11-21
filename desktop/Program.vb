Imports System.Diagnostics
Imports System.IO
Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Windows.Forms

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
    Private ReadOnly AssetInput As New TextBox With {.PlaceholderText = "https://c0.wp.com"}
    Private ReadOnly RunButton As New Button With {.Text = "Run test", .AutoSize = True, .Enabled = False}
    Private ReadOnly OpenButton As New Button With {.Text = "Open artifacts", .AutoSize = True, .Enabled = False}
    Private ReadOnly StatusText As New Label With {.AutoSize = True}
    Private ReadOnly Output As New TextBox With {
        .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical, .Dock = DockStyle.Fill
    }
    Private ArtifactFolder As String
    Private Running As Boolean

    Public Sub New()
        Text = "Exploratory QA Agent"
        ClientSize = New Drawing.Size(720, 540)
        MinimumSize = New Drawing.Size(580, 480)
        StartPosition = FormStartPosition.CenterScreen

        Dim layout As New TableLayoutPanel With {
            .Dock = DockStyle.Fill, .Padding = New Padding(12), .ColumnCount = 2
        }
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        Controls.Add(layout)
        AddField(layout, "URL", UrlInput)
        CharterInput.Height = 95
        AddField(layout, "Charter", CharterInput)
        Dim modelRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        ModelInput.Width = 220
        modelRow.Controls.Add(ModelInput)
        modelRow.Controls.Add(RefreshModels)
        AddField(layout, "Ollama model", modelRow)
        AddField(layout, "Allowed asset origins", AssetInput)
        AddField(layout, "", YouTube)

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True}
        buttons.Controls.Add(RunButton)
        buttons.Controls.Add(OpenButton)
        buttons.Controls.Add(StatusText)
        AddFullRow(layout, buttons, SizeType.AutoSize)
        AddFullRow(layout, Output, SizeType.Percent)

        AddHandler Shown, AddressOf LoadModels
        AddHandler RefreshModels.Click, AddressOf LoadModels
        AddHandler RunButton.Click, AddressOf RunTest
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
        Output.Clear()
        StatusText.Text = "Running…"
        Try
            Dim repo = FindRepository()
            Dim startInfo As New ProcessStartInfo("node") With {
                .UseShellExecute = False, .CreateNoWindow = True, .WorkingDirectory = repo,
                .RedirectStandardOutput = True, .RedirectStandardError = True,
                .StandardOutputEncoding = Encoding.UTF8, .StandardErrorEncoding = Encoding.UTF8
            }
            For Each argument In {"--import", "tsx", "src/qa.ts", "--url", address.AbsoluteUri,
                                  "--charter", CharterInput.Text.Trim(), "--model", CStr(ModelInput.SelectedItem)}
                startInfo.ArgumentList.Add(argument)
            Next
            If YouTube.Checked Then startInfo.ArgumentList.Add("--allow-youtube-embeds")
            For Each origin In AssetInput.Text.Split(","c, StringSplitOptions.RemoveEmptyEntries Or StringSplitOptions.TrimEntries)
                startInfo.ArgumentList.Add("--asset-origin")
                startInfo.ArgumentList.Add(origin)
            Next

            Using runner As New Process With {.StartInfo = startInfo}
                AddHandler runner.ErrorDataReceived, AddressOf ShowRunnerOutput
                runner.Start()
                runner.BeginErrorReadLine()
                Dim reportTask = runner.StandardOutput.ReadToEndAsync()
                Await runner.WaitForExitAsync()
                Dim report = (Await reportTask).Trim()
                If report <> "" Then
                    Output.AppendText(report & Environment.NewLine)
                    Dim reportPath = Path.GetFullPath(report, repo)
                    If File.Exists(reportPath) Then
                        ArtifactFolder = Path.GetDirectoryName(reportPath)
                        OpenButton.Enabled = True
                    End If
                End If
                StatusText.Text = If(runner.ExitCode = 0, "Run finished.", "Run incomplete or failed.")
            End Using
        Catch ex As Exception
            StatusText.Text = "Could not run the agent."
            Output.AppendText(ex.Message & Environment.NewLine)
        Finally
            Running = False
            RefreshModels.Enabled = True
            RunButton.Enabled = True
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
