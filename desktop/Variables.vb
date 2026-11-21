Imports System.Diagnostics
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

Partial Friend Class QaForm
    Private Shared ReadOnly VariableName As New Regex("^[a-z][a-z0-9_-]*$", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private Shared ReadOnly VariableReference As New Regex("\{\{([a-z][a-z0-9_-]*)\}\}", RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private Shared ReadOnly CredentialGroup As New Regex(
        "\b(?:using|with)\s+(?:the\s+)?([a-z][a-z0-9_-]*)\s+credentials\b",
        RegexOptions.IgnoreCase Or RegexOptions.CultureInvariant)
    Private Shared ReadOnly VariableKinds As String() = {"email", "password", "phone", "text"}

    Private Sub OpenProfileVariables(sender As Object, args As EventArgs)
        Dim origin = TryCast(ProfilePicker.SelectedItem, String)
        If origin Is Nothing Then
            StatusText.Text = "Save and select a website profile first."
            Return
        End If
        Dim saved = Profiles(origin).Variables
        Using dialog As New Form With {
            .Text = "Profile variables — " & origin, .StartPosition = FormStartPosition.CenterParent,
            .ClientSize = New Drawing.Size(470, 340), .MinimizeBox = False, .MaximizeBox = False,
            .FormBorderStyle = FormBorderStyle.FixedDialog
        }
            Dim layout As New TableLayoutPanel With {.Dock = DockStyle.Fill, .Padding = New Padding(12), .ColumnCount = 2}
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
            layout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            Dim names As New ListBox With {.Dock = DockStyle.Fill}
            Dim name As New TextBox With {.PlaceholderText = "test_email"}
            Dim kind As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList}
            kind.Items.AddRange(VariableKinds)
            kind.SelectedIndex = 0
            Dim value As New TextBox
            Dim message As New Label With {.AutoSize = True, .Text = "In a charter, reference a variable as {{test_email}}."}
            dialog.Controls.Add(layout)
            AddFullRow(layout, names, SizeType.Percent)
            AddField(layout, "Name", name)
            AddField(layout, "Type", kind)
            AddField(layout, "Value", value)
            AddFullRow(layout, message, SizeType.AutoSize)

            Dim refresh As Action = Sub()
                                       names.Items.Clear()
                                       For Each key In saved.Keys.OrderBy(Function(entry) entry)
                                           names.Items.Add(key)
                                       Next
                                   End Sub
            refresh()
            AddHandler names.SelectedIndexChanged, Sub(listSender, listArgs)
                Dim selected = TryCast(names.SelectedItem, String)
                If selected Is Nothing Then Return
                name.Text = selected
                kind.SelectedItem = saved(selected).Kind
                value.Clear()
                message.Text = "Leave Value blank to keep it, or enter a replacement."
            End Sub
            Dim buttons As New FlowLayoutPanel With {.AutoSize = True}
            buttons.Controls.Add(ProfileButton("Save variable", Sub(buttonSender, buttonArgs)
                Dim key = name.Text.Trim().ToLowerInvariant()
                If Not VariableName.IsMatch(key) Then
                    message.Text = "Enter a name such as mx1_phone."
                    Return
                End If
                Dim previous As SavedVariable = Nothing
                saved.TryGetValue(key, previous)
                If previous Is Nothing AndAlso value.Text.Length = 0 Then
                    message.Text = "Enter a value for the new variable."
                    Return
                End If
                Try
                    Dim encrypted As String
                    If value.Text.Length = 0 Then
                        encrypted = previous.EncryptedValue
                    Else
                        Dim encoded = ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Text),
                                                            Encoding.UTF8.GetBytes(origin), DataProtectionScope.CurrentUser)
                        encrypted = Convert.ToBase64String(encoded)
                    End If
                    saved(key) = New SavedVariable With {.Kind = CStr(kind.SelectedItem), .EncryptedValue = encrypted}
                    WriteProfiles()
                    value.Clear()
                    refresh()
                    message.Text = "Saved {{" & key & "}}."
                Catch ex As CryptographicException
                    message.Text = "Windows could not protect this value."
                End Try
            End Sub))
            buttons.Controls.Add(ProfileButton("Remove selected", Sub(buttonSender, buttonArgs)
                Dim selected = TryCast(names.SelectedItem, String)
                If selected Is Nothing Then Return
                saved.Remove(selected)
                WriteProfiles()
                refresh()
                name.Clear()
                value.Clear()
                message.Text = "Variable removed."
            End Sub))
            Dim closeButton As New Button With {.Text = "Close", .AutoSize = True, .DialogResult = DialogResult.Cancel}
            buttons.Controls.Add(closeButton)
            dialog.CancelButton = closeButton
            AddFullRow(layout, buttons, SizeType.AutoSize)
            dialog.ShowDialog(Me)
        End Using
    End Sub

    Private Sub AddProfileVariables(startInfo As ProcessStartInfo, origin As String, charter As String)
        startInfo.Environment.Remove("QA_PROFILE_VARIABLES")
        Dim requested As New HashSet(Of String)(
            VariableReference.Matches(charter).Select(Function(entry) entry.Groups(1).Value.ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase)
        Dim profile As WebsiteProfile = Nothing
        Profiles.TryGetValue(origin, profile)
        For Each entry As Match In CredentialGroup.Matches(charter)
            Dim group = entry.Groups(1).Value.ToLowerInvariant()
            Dim matching = If(profile?.Variables.Keys.AsEnumerable(), Enumerable.Empty(Of String)()).
                Where(Function(key) key.StartsWith(group & "_", StringComparison.OrdinalIgnoreCase) OrElse
                                     key.StartsWith(group & "-", StringComparison.OrdinalIgnoreCase)).ToArray()
            If matching.Length = 0 Then
                Throw New ArgumentException("No variables match """ & group & " credentials"". Save names like " &
                                            group & "_email and " & group & "_password in this website profile.")
            End If
            requested.UnionWith(matching)
        Next
        Dim values As New Dictionary(Of String, Object)
        For Each key In requested
            Dim saved As SavedVariable = Nothing
            If profile Is Nothing OrElse Not profile.Variables.TryGetValue(key, saved) Then
                Throw New ArgumentException("No variable named {{" & key & "}} is saved for this website profile.")
            End If
            Dim decoded As Byte()
            Try
                decoded = ProtectedData.Unprotect(Convert.FromBase64String(saved.EncryptedValue),
                                                  Encoding.UTF8.GetBytes(origin), DataProtectionScope.CurrentUser)
            Catch ex As CryptographicException
                Throw New InvalidOperationException("Save {{" & key & "}} again under this Windows account.", ex)
            End Try
            Try
                values.Add(key, New With {.kind = saved.Kind, .value = Encoding.UTF8.GetString(decoded)})
            Finally
                CryptographicOperations.ZeroMemory(decoded)
            End Try
        Next
        If values.Count > 0 Then startInfo.Environment("QA_PROFILE_VARIABLES") = JsonSerializer.Serialize(values)
    End Sub
End Class

Friend Class SavedVariable
    Public Property Kind As String = ""
    Public Property EncryptedValue As String = ""
End Class
