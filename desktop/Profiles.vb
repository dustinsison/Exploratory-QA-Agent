Imports System.IO
Imports System.Text.Json
Imports System.Windows.Forms

Partial Friend Class QaForm
    Private ReadOnly Profiles As New Dictionary(Of String, WebsiteProfile)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly ProfilesTab As New TabPage("Website profiles")
    Private ReadOnly ProfilePicker As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 245}
    Private ReadOnly SavedCharters As New ListBox With {.Dock = DockStyle.Fill, .DisplayMember = NameOf(RunInputs.Charter)}
    Private ReadOnly SavedRuns As New ListBox With {.Dock = DockStyle.Fill, .DisplayMember = NameOf(ProfileRun.DisplayText)}
    Private Const EmptyReport As String = "Run a test or select a previous run to view its report."
    Private ReadOnly ReportTab As New TabPage("Report")
    Private ReadOnly ReportText As New RichTextBox With {
        .Dock = DockStyle.Fill, .Multiline = True, .ReadOnly = True, .ScrollBars = RichTextBoxScrollBars.Vertical, .DetectUrls = False, .Text = EmptyReport
    }

    Private Sub InitializeProfiles()
        Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(8)}
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50))
        Dim profileRow As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        profileRow.Controls.Add(ProfilePicker)
        profileRow.Controls.Add(ProfileButton("Save current site / charter", AddressOf SaveCurrentProfile))
        profileRow.Controls.Add(ProfileButton("Asset permissions", AddressOf OpenProfileAssets))
        profileRow.Controls.Add(ProfileButton("Variables…", AddressOf OpenProfileVariables))
        AddFullRow(layout, profileRow, SizeType.AutoSize)

        Dim titles = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        layout.Controls.Add(New Label With {.Text = "Saved charters", .AutoSize = True}, 0, titles)
        layout.Controls.Add(New Label With {.Text = "Previous runs", .AutoSize = True}, 1, titles)

        Dim lists = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(SizeType.Percent, 100))
        layout.Controls.Add(SavedCharters, 0, lists)
        layout.Controls.Add(SavedRuns, 1, lists)
        Dim actions = layout.RowCount
        layout.RowCount += 1
        layout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim charterActions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        charterActions.Controls.Add(ProfileButton("Use charter", AddressOf UseSavedCharter))
        charterActions.Controls.Add(ProfileButton("Remove selected", Sub(sender, args) RemoveSavedCharters(False)))
        charterActions.Controls.Add(ProfileButton("Clear charters", Sub(sender, args) RemoveSavedCharters(True)))
        layout.Controls.Add(charterActions, 0, actions)
        Dim runActions As New FlowLayoutPanel With {.AutoSize = True, .Dock = DockStyle.Fill}
        runActions.Controls.Add(ProfileButton("View run", AddressOf ViewSavedRun))
        runActions.Controls.Add(ProfileButton("Import report…", AddressOf ImportReport))
        runActions.Controls.Add(ProfileButton("Remove selected", Sub(sender, args) RemoveSavedRuns(False)))
        runActions.Controls.Add(ProfileButton("Clear runs", Sub(sender, args) RemoveSavedRuns(True)))
        layout.Controls.Add(runActions, 1, actions)

        ProfilesTab.Controls.Add(layout)
        ReportTab.Controls.Add(ReportText)
        EvidenceTabs.TabPages.Add(ProfilesTab)
        EvidenceTabs.TabPages.Add(ReportTab)
        AddHandler ProfilePicker.SelectedIndexChanged, Sub(sender, args) RefreshProfileLists()
        AddHandler SavedCharters.DoubleClick, AddressOf UseSavedCharter
        AddHandler SavedRuns.DoubleClick, AddressOf ViewSavedRun

        Try
            If File.Exists(ProfilePath) Then
                Dim saved = JsonSerializer.Deserialize(Of Dictionary(Of String, WebsiteProfile))(File.ReadAllText(ProfilePath))
                If saved IsNot Nothing Then
                    For Each entry In saved
                        Profiles(entry.Key) = entry.Value
                    Next
                End If
            Else
                For Each entry In Enumerable.Reverse(History)
                    Dim address As Uri = Nothing
                    If Uri.TryCreate(entry.Url, UriKind.Absolute, address) Then
                        RememberCharter(GetProfile(address.GetLeftPart(UriPartial.Authority)), entry)
                    End If
                Next
                For Each assetSite In AssetSettings.Keys
                    GetProfile(assetSite)
                Next
                If Profiles.Count > 0 Then WriteProfiles()
            End If
        Catch ex As Exception
            Output.AppendText("Could not load website profiles: " & ex.GetBaseException().Message & Environment.NewLine)
        End Try
        RefreshProfilePicker()
    End Sub

    Private Shared Function ProfileButton(caption As String, handler As EventHandler) As Button
        Dim button As New Button With {.Text = caption, .AutoSize = True}
        AddHandler button.Click, handler
        Return button
    End Function

    Private Function GetProfile(siteOrigin As String) As WebsiteProfile
        Dim profile As WebsiteProfile = Nothing
        If Not Profiles.TryGetValue(siteOrigin, profile) Then
            profile = New WebsiteProfile With {.Url = siteOrigin}
            Profiles.Add(siteOrigin, profile)
        End If
        Return profile
    End Function

    Private Shared Sub RememberCharter(profile As WebsiteProfile, inputs As RunInputs)
        If String.IsNullOrWhiteSpace(inputs.Charter) Then Return
        profile.Charters.RemoveAll(Function(entry) entry.Charter = inputs.Charter AndAlso entry.Url = inputs.Url)
        profile.Charters.Insert(0, inputs)
    End Sub

    Private Sub SaveConfiguration(siteOrigin As String, inputs As RunInputs)
        Dim profile = GetProfile(siteOrigin)
        profile.Url = inputs.Url
        RememberCharter(profile, inputs)
        WriteProfiles()
        RefreshProfilePicker(siteOrigin)
    End Sub

    Private Sub SaveRun(siteOrigin As String, run As ProfileRun)
        Dim profile = GetProfile(siteOrigin)
        profile.Runs.RemoveAll(Function(entry) String.Equals(entry.ReportPath, run.ReportPath, StringComparison.OrdinalIgnoreCase))
        profile.Runs.Insert(0, run)
        WriteProfiles()
        RefreshProfilePicker(siteOrigin)
    End Sub

    Private Function WriteProfiles() As Boolean
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath))
            Dim pending = ProfilePath & ".tmp"
            File.WriteAllText(pending, JsonSerializer.Serialize(Profiles))
            File.Move(pending, ProfilePath, overwrite:=True)
            Return True
        Catch ex As Exception
            Output.AppendText("Could not save website profiles: " & ex.Message & Environment.NewLine)
            Return False
        End Try
    End Function

    Private Sub RefreshProfilePicker(Optional selectedOrigin As String = Nothing)
        selectedOrigin = If(selectedOrigin, TryCast(ProfilePicker.SelectedItem, String))
        ProfilePicker.Items.Clear()
        For Each origin In Profiles.Keys.OrderBy(Function(value) value)
            ProfilePicker.Items.Add(origin)
        Next
        If ProfilePicker.Items.Count > 0 Then
            Dim index = If(selectedOrigin Is Nothing, -1, ProfilePicker.Items.IndexOf(selectedOrigin))
            ProfilePicker.SelectedIndex = Math.Max(index, 0)
        End If
    End Sub

    Private Sub RefreshProfileLists()
        SavedCharters.Items.Clear()
        SavedRuns.Items.Clear()
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If origin Is Nothing Then Return
        For Each entry In Profiles(origin).Charters
            SavedCharters.Items.Add(entry)
        Next
        For Each run In Profiles(origin).Runs.OrderByDescending(Function(entry) entry.RecordedAt)
            SavedRuns.Items.Add(run)
        Next
    End Sub

    Private Function ConfirmClear(message As String) As Boolean
        Return MessageBox.Show(Me, message, "Clear saved items", MessageBoxButtons.YesNo,
                               MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) = DialogResult.Yes
    End Function

    Private Sub RemoveSavedCharters(clearAll As Boolean)
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If Running OrElse origin Is Nothing Then Return
        Dim removed = RemoveProfileItems(Profiles(origin).Charters, TryCast(SavedCharters.SelectedItem, RunInputs),
                                         clearAll, "charter", origin, "")
        If removed.Count > 0 Then StatusText.Text = If(clearAll, "Saved charters cleared.", "Saved charter removed.")
    End Sub

    Private Sub RemoveSavedRuns(clearAll As Boolean)
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If Running OrElse origin Is Nothing Then Return
        Dim removed = RemoveProfileItems(Profiles(origin).Runs, TryCast(SavedRuns.SelectedItem, ProfileRun),
                                         clearAll, "run", origin, " Reports, screenshots, and recordings will stay on disk.")
        If removed.Count = 0 Then Return
        If removed.Any(Function(entry) String.Equals(Path.GetDirectoryName(entry.ReportPath), ArtifactFolder,
                                                     StringComparison.OrdinalIgnoreCase)) Then
            ResetRunEvidence()
        End If
        StatusText.Text = If(clearAll, "Previous runs cleared.", "Previous run removed.")
    End Sub

    Private Function RemoveProfileItems(Of T As Class)(items As List(Of T), selected As T, clearAll As Boolean,
                                                       label As String, origin As String, note As String) As List(Of T)
        Dim removed As New List(Of T)
        If items.Count = 0 OrElse (Not clearAll AndAlso selected Is Nothing) Then Return removed
        Dim action = If(clearAll, "Clear all saved " & label & "s", "Remove the selected " & label)
        If Not ConfirmClear(action & " for " & origin & "?" & note) Then Return removed

        Dim previous As New List(Of T)(items)
        If clearAll Then
            removed.AddRange(items)
            items.Clear()
        ElseIf items.Remove(selected) Then
            removed.Add(selected)
        End If

        If Not WriteProfiles() Then
            items.Clear()
            items.AddRange(previous)
            StatusText.Text = "Could not save changes. See Activity for details."
            removed.Clear()
            Return removed
        End If
        RefreshProfileLists()
        Return removed
    End Function

    Private Sub SaveCurrentProfile(sender As Object, args As EventArgs)
        Dim origin = CurrentSite()
        If origin = "" Then
            StatusText.Text = "Enter an HTTP(S) URL to save a website profile."
            Return
        End If
        Try
            GetAllowedAssets(origin, AssetInput.Text)
            SaveConfiguration(origin, New RunInputs With {
                .Url = UrlInput.Text.Trim(), .Charter = CharterInput.Text.Trim(),
                .Model = If(TryCast(ModelInput.SelectedItem, String), ""),
                .YouTube = YouTube.Checked, .ShowBrowser = ShowBrowser.Checked, .AssetOrigins = AssetInput.Text.Trim()
            })
            StatusText.Text = "Website profile saved."
        Catch ex As ArgumentException
            StatusText.Text = ex.Message
        End Try
    End Sub

    Private Sub OpenProfileAssets(sender As Object, args As EventArgs)
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If origin Is Nothing Then Return
        If Not String.Equals(CurrentSite(), origin, StringComparison.OrdinalIgnoreCase) Then
            UrlInput.Text = Profiles(origin).Url
            AssetInput.Clear()
        End If
        EvidenceTabs.SelectedTab = AssetsTab
    End Sub

    Private Sub UseSavedCharter(sender As Object, args As EventArgs)
        Dim inputs = TryCast(SavedCharters.SelectedItem, RunInputs)
        If inputs IsNot Nothing Then ApplyInputs(inputs)
    End Sub

    Private Async Sub ViewSavedRun(sender As Object, args As EventArgs)
        Dim run = TryCast(SavedRuns.SelectedItem, ProfileRun)
        If run Is Nothing Then Return
        If Not File.Exists(run.ReportPath) Then
            StatusText.Text = "The saved report was moved or removed."
            Return
        End If
        Try
            Await ShowRunEvidence(run.ReportPath)
        Catch ex As Exception
            StatusText.Text = "Could not open the saved run."
            Output.AppendText(ex.Message & Environment.NewLine)
        End Try
    End Sub

    Private Sub ResetRunEvidence(Optional message As String = EmptyReport)
        ArtifactFolder = Nothing
        OpenButton.Enabled = False
        ReportText.Text = message
        ResetRecording()
    End Sub

    Private Async Function ShowRunEvidence(reportPath As String) As Task
        Dim report = File.ReadAllText(reportPath)
        ResetRunEvidence()
        RenderReport(report)
        ArtifactFolder = Path.GetDirectoryName(reportPath)
        OpenButton.Enabled = True
        If EvidenceTabs.SelectedTab IsNot AssetsTab Then EvidenceTabs.SelectedTab = ReportTab
        Await LoadRecording(ArtifactFolder)
    End Function

    Private Sub RenderReport(markdown As String)
        ReportText.Clear()
        Using heading As New Drawing.Font(ReportText.Font, Drawing.FontStyle.Bold)
            For Each line In markdown.ReplaceLineEndings(vbLf).Split(ChrW(10))
                Dim text = line
                Dim format = ReportText.Font
                If line.StartsWith("# ", StringComparison.Ordinal) OrElse line.StartsWith("## ", StringComparison.Ordinal) Then
                    text = line.Substring(line.IndexOf(" "c) + 1)
                    format = heading
                End If
                ReportText.SelectionFont = format
                ReportText.AppendText(text & vbCrLf)
            Next
        End Using
        ReportText.Select(0, 0)
        ReportText.ScrollToCaret()
    End Sub

    Private Sub ImportReport(sender As Object, args As EventArgs)
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If origin Is Nothing Then Return
        Using picker As New OpenFileDialog With {.Filter = "QA report (report.md)|report.md", .CheckFileExists = True}
            If picker.ShowDialog(Me) <> DialogResult.OK Then Return
            SaveRun(origin, New ProfileRun With {
                .ReportPath = picker.FileName, .RecordedAt = File.GetLastWriteTimeUtc(picker.FileName), .Status = "Imported"
            })
        End Using
    End Sub
End Class

Friend Class WebsiteProfile
    Public Property Url As String = ""
    Public Property AllowAllAssetOrigins As Boolean
    Public Property AllowOffSiteApi As Boolean
    Public Property Variables As New Dictionary(Of String, SavedVariable)(StringComparer.OrdinalIgnoreCase)
    Public Property Charters As New List(Of RunInputs)
    Public Property Runs As New List(Of ProfileRun)
End Class

Friend Class ProfileRun
    Public Property ReportPath As String = ""
    Public Property RecordedAt As DateTimeOffset
    Public Property Status As String = ""
    Public Property Charter As String = ""

    <System.Text.Json.Serialization.JsonIgnore>
    Public ReadOnly Property DisplayText As String
        Get
            Return RecordedAt.ToLocalTime().ToString("g") & " — " & Status & If(Charter = "", "", " — " & Charter)
        End Get
    End Property
End Class
