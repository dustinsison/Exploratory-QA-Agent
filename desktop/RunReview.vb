Imports System.Globalization
Imports System.IO
Imports System.Net
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

Partial Friend Class QaForm
    Private Shared ReadOnly MetadataOptions As New JsonSerializerOptions With {.PropertyNameCaseInsensitive = True}
    Private ReadOnly TimelineTab As New TabPage("Timeline")
    Private ReadOnly TimelineList As New ListBox With {.Dock = DockStyle.Fill, .DisplayMember = NameOf(RunTimelineEntry.DisplayText)}
    Private ReadOnly CompareTab As New TabPage("Comparison")
    Private ReadOnly CompareText As New TextBox With {
        .Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = ScrollBars.Vertical
    }
    Private ReadOnly ExportButton As New Button With {.Text = "Export HTML…", .AutoSize = True, .Enabled = False}
    Private DisplayedReport As String
    Private DisplayedRun As RunMetadata

    Private Sub InitializeRunReview()
        Dim reportActions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
        reportActions.Controls.Add(ExportButton)
        ReportTab.Controls.Add(ReportText)
        ReportTab.Controls.Add(reportActions)
        Dim timelineActions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Top}
        timelineActions.Controls.Add(ProfileButton("Jump to recording", AddressOf JumpToRecording))
        TimelineTab.Controls.Add(TimelineList)
        TimelineTab.Controls.Add(timelineActions)
        CompareTab.Controls.Add(CompareText)
        EvidenceTabs.TabPages.Add(TimelineTab)
        EvidenceTabs.TabPages.Add(CompareTab)
        AddHandler TimelineList.DoubleClick, AddressOf JumpToRecording
        AddHandler ExportButton.Click, AddressOf ExportReport
    End Sub

    Private Shared Function ReadRunMetadata(reportPath As String) As RunMetadata
        If String.IsNullOrEmpty(reportPath) Then Return Nothing
        Dim metadataPath = Path.Combine(Path.GetDirectoryName(reportPath), "run.json")
        Try
            If File.Exists(metadataPath) Then
                Return JsonSerializer.Deserialize(Of RunMetadata)(File.ReadAllText(metadataPath), MetadataOptions)
            End If
        Catch ex As IOException
            Return Nothing
        Catch ex As JsonException
            Return Nothing
        End Try
        Return Nothing
    End Function

    Private Async Sub JumpToRecording(sender As Object, args As EventArgs)
        Dim entry = TryCast(TimelineList.SelectedItem, RunTimelineEntry)
        If entry Is Nothing Then Return
        If Not RecordingAvailable OrElse Player.CoreWebView2 Is Nothing Then
            StatusText.Text = "No recording is available for this event."
            Return
        End If
        EvidenceTabs.SelectedTab = RecordingTab
        Try
            Dim seconds = (entry.AtMs / 1000).ToString("0.000", CultureInfo.InvariantCulture)
            Await Player.CoreWebView2.ExecuteScriptAsync(
                "(()=>{const v=document.querySelector('video');if(!v)return;" &
                "const seek=()=>{v.currentTime=" & seconds & ";};" &
                "if(v.readyState)seek();else v.addEventListener('loadedmetadata',seek,{once:true});})()")
        Catch ex As Exception
            StatusText.Text = "Could not seek in this recording."
        End Try
    End Sub

    Private Sub ReuseRunInputs(sender As Object, args As EventArgs)
        Dim entry = TryCast(SavedRuns.SelectedItem, ProfileRun)
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If entry Is Nothing OrElse origin Is Nothing Then Return
        Dim inputs = If(entry.Inputs, Profiles(origin).Charters.FirstOrDefault(Function(item) item.Charter = entry.Charter))
        If inputs Is Nothing Then
            StatusText.Text = "This older run has no saved inputs."
            Return
        End If
        ApplyInputs(inputs)
        EvidenceTabs.SelectedIndex = 0
    End Sub

    Private Sub CompareSelectedRun(sender As Object, args As EventArgs)
        Dim current = TryCast(SavedRuns.SelectedItem, ProfileRun)
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If current Is Nothing OrElse origin Is Nothing Then Return
        Dim currentData = ReadRunMetadata(current.ReportPath)
        If currentData Is Nothing Then
            StatusText.Text = "This run predates checklist and timing capture."
            Return
        End If
        Dim previous = Profiles(origin).Runs.Where(Function(item) item.RecordedAt < current.RecordedAt AndAlso item.Charter = current.Charter).
            OrderByDescending(Function(item) item.RecordedAt).FirstOrDefault(Function(item) ReadRunMetadata(item.ReportPath) IsNot Nothing)
        If previous Is Nothing Then
            StatusText.Text = "Run the same charter again to compare it with an earlier run."
            Return
        End If
        Dim previousData = ReadRunMetadata(previous.ReportPath)
        If previousData Is Nothing Then Return

        Dim comparison As New StringBuilder
        comparison.AppendLine(current.Charter).AppendLine()
        comparison.AppendLine("Previous run  " & previous.RecordedAt.ToLocalTime().ToString("g") & "  •  " & previousData.Model)
        comparison.AppendLine("Selected run   " & current.RecordedAt.ToLocalTime().ToString("g") & "  •  " & currentData.Model)
        comparison.AppendLine().AppendLine("Verdict  " & previousData.Verdict & " → " & currentData.Verdict)
        comparison.AppendLine("Total time  " & Seconds(previousData.ElapsedMs) & " → " & Seconds(currentData.ElapsedMs))
        comparison.AppendLine("Ollama waits  " & Seconds(previousData.ModelMs) & " → " & Seconds(currentData.ModelMs))
        comparison.AppendLine("Browser actions and waits  " & Seconds(previousData.BrowserMs) & " → " & Seconds(currentData.BrowserMs))
        comparison.AppendLine().AppendLine("Checklist changes")
        Dim checks = previousData.CheckResults.Select(Function(item) item.Check).
            Union(currentData.CheckResults.Select(Function(item) item.Check), StringComparer.OrdinalIgnoreCase)
        For Each check In checks
            Dim before = previousData.CheckResults.FirstOrDefault(Function(item) String.Equals(item.Check, check, StringComparison.OrdinalIgnoreCase))
            Dim after = currentData.CheckResults.FirstOrDefault(Function(item) String.Equals(item.Check, check, StringComparison.OrdinalIgnoreCase))
            comparison.AppendLine("• " & check & "  |  " & If(before?.Status, "No check") & " → " & If(after?.Status, "No check"))
            If after IsNot Nothing Then comparison.AppendLine("  " & after.Evidence)
        Next
        CompareText.Text = comparison.ToString()
        EvidenceTabs.SelectedTab = CompareTab
    End Sub

    Private Shared Function Seconds(milliseconds As Long) As String
        Return (milliseconds / 1000).ToString("0.0", CultureInfo.InvariantCulture) & "s"
    End Function

    Private Async Sub ExportReport(sender As Object, args As EventArgs)
        Dim reportPath = DisplayedReport
        If reportPath Is Nothing Then Return
        Dim folder = Path.GetDirectoryName(reportPath)
        Dim screenshotPath = Path.Combine(folder, "final.png")
        Dim videoFolder = Path.Combine(folder, "video")
        Dim videoPath = If(Directory.Exists(videoFolder), Directory.EnumerateFiles(videoFolder, "*.webm").FirstOrDefault(), Nothing)

        Dim includeImage As Boolean
        Dim includeVideo As Boolean
        Using options As New Form With {.Text = "Export report", .StartPosition = FormStartPosition.CenterParent,
                                         .ClientSize = New Drawing.Size(390, 145), .FormBorderStyle = FormBorderStyle.FixedDialog}
            Dim fields As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(10), .FlowDirection = FlowDirection.TopDown}
            fields.Controls.Add(New Label With {.Text = "Choose which evidence to embed in the HTML file.", .AutoSize = True})
            Dim image As New CheckBox With {.Text = "Include final screenshot", .Enabled = File.Exists(screenshotPath), .AutoSize = True}
            Dim recording As New CheckBox With {.Text = "Include recording", .Enabled = videoPath IsNot Nothing, .AutoSize = True}
            fields.Controls.Add(image)
            fields.Controls.Add(recording)
            Dim accept As New Button With {.Text = "Continue…", .DialogResult = DialogResult.OK, .AutoSize = True}
            fields.Controls.Add(accept)
            options.Controls.Add(fields)
            options.AcceptButton = accept
            If options.ShowDialog(Me) <> DialogResult.OK Then Return
            includeImage = image.Checked
            includeVideo = recording.Checked
        End Using
        Using picker As New SaveFileDialog With {.Filter = "HTML report (*.html)|*.html", .FileName = Path.GetFileName(folder) & ".html"}
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Dim report = Await File.ReadAllTextAsync(reportPath)
                Dim metadata = ReadRunMetadata(reportPath)
                Dim imageData = If(includeImage, Convert.ToBase64String(Await File.ReadAllBytesAsync(screenshotPath)), Nothing)
                Dim videoData = If(includeVideo, Convert.ToBase64String(Await File.ReadAllBytesAsync(videoPath)), Nothing)
                Await File.WriteAllTextAsync(picker.FileName, BuildHtmlReport(report, metadata, imageData, videoData))
                StatusText.Text = "HTML report exported."
            Catch ex As Exception
                StatusText.Text = "Could not export the report."
                Output.AppendText(ex.Message & Environment.NewLine)
            End Try
        End Using
    End Sub

    Private Shared Function BuildHtmlReport(report As String, metadata As RunMetadata, image As String, video As String) As String
        Dim html As New StringBuilder("<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Exploratory QA report</title>" &
            "<style>body{font:16px system-ui,sans-serif;max-width:960px;margin:2rem auto;padding:0 1rem;color:#20242b}h2{margin-top:1.8rem}p{line-height:1.5}img,video{max-width:100%}button{cursor:pointer} .step{margin:.25rem 0}</style><main>")
        For Each line In report.ReplaceLineEndings(vbLf).Split(ChrW(10))
            If line.StartsWith("[Final screenshot](", StringComparison.Ordinal) OrElse line.StartsWith("[Watch the test recording](", StringComparison.Ordinal) Then Continue For
            Dim portable = Regex.Replace(line, "^(- )\[([0-9.]+s)\]\([^)]*#t=[0-9.]+\)", "$1$2")
            Dim safe = WebUtility.HtmlEncode(portable.Replace("**", "").Replace("`", ""))
            If line.StartsWith("## ", StringComparison.Ordinal) Then
                html.Append("<h2>").Append(safe.Substring(3)).AppendLine("</h2>")
            ElseIf line.StartsWith("# ", StringComparison.Ordinal) Then
                html.Append("<h1>").Append(safe.Substring(2)).AppendLine("</h1>")
            ElseIf Not String.IsNullOrWhiteSpace(safe) Then
                html.Append("<p class='step'>").Append(safe).AppendLine("</p>")
            End If
        Next
        If image IsNot Nothing Then html.Append("<h2>Final screenshot</h2><img alt='Final browser screenshot' src='data:image/png;base64,").Append(image).AppendLine("'>")
        If video IsNot Nothing Then
            html.Append("<h2>Recording</h2><video id='recording' controls preload='metadata' src='data:video/webm;base64,").Append(video).AppendLine("'></video>")
            If metadata IsNot Nothing Then
                html.AppendLine("<h2>Jump to a recorded action</h2>")
                For Each entry In metadata.Timeline.Where(Function(item) item.Phase = "Browser")
                    Dim atSeconds = (entry.AtMs / 1000).ToString("0.000", CultureInfo.InvariantCulture)
                    html.Append("<p><button type='button' data-at='").Append(atSeconds).Append("'>").
                        Append(Seconds(entry.AtMs)).Append("</button> ").Append(WebUtility.HtmlEncode(entry.Detail)).AppendLine("</p>")
                Next
                html.AppendLine("<script>document.querySelectorAll('[data-at]').forEach(b=>b.addEventListener('click',()=>{const v=document.getElementById('recording');const seek=()=>{v.currentTime=Number(b.dataset.at);v.scrollIntoView();};if(v.readyState)seek();else v.addEventListener('loadedmetadata',seek,{once:true});}));</script>")
            End If
        End If
        Return html.AppendLine("</main></html>").ToString()
    End Function
End Class

Friend Class RunMetadata
    Public Property Model As String = ""
    Public Property Verdict As String = ""
    Public Property CheckResults As New List(Of RunCheck)
    Public Property Timeline As New List(Of RunTimelineEntry)
    Public Property ElapsedMs As Long
    Public Property ModelMs As Long
    Public Property BrowserMs As Long
End Class

Friend Class RunCheck
    Public Property Check As String = ""
    Public Property Status As String = ""
    Public Property Evidence As String = ""
End Class

Friend Class RunTimelineEntry
    Public Property AtMs As Long
    Public Property DurationMs As Long
    Public Property Phase As String = ""
    Public Property Detail As String = ""

    Public ReadOnly Property DisplayText As String
        Get
            Return (AtMs / 1000).ToString("0.0", CultureInfo.InvariantCulture) & "s  •  " & Phase &
                "  (" & (DurationMs / 1000).ToString("0.0", CultureInfo.InvariantCulture) & "s)  •  " & Detail
        End Get
    End Property
End Class
