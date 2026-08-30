using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using Microsoft.Win32;

namespace ForzaCryptoTool;

public partial class ProfileEditorView : UserControl
{
    private readonly MainWindow _shell;
    private readonly CryptoService _crypto;
    private Fh6ProfileEditorSession? _session;
    private string? _encryptedSourcePath;
    private string? _workingDirectory;
    private List<PropertyRow> _properties = [];
    private List<BinaryRow> _binary = [];
    private PropertyRow? _selectedProperty;
    private BxmlStringRow? _selectedBxml;
    private BxmlNodeRow? _selectedBxmlNode;
    private BinaryRow? _selectedBinary;
    private DataTable? _databaseTable;
    public IReadOnlyList<string> BinaryScalarTypes { get; } = Enum.GetNames<BinaryScalarType>();

    internal ProfileEditorView(MainWindow shell)
    {
        InitializeComponent();
        DataContext = this;
        _shell = shell;
        _crypto = new CryptoService(App.Backend);
        _crypto.Progress += message => _shell.SetStatus(message);
        DragEnter += (_, e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        Drop += OnDrop;
    }

    internal async Task LoadFileAsync(string path)
    {
        if (!ConfirmDiscardChanges()) return;
        try
        {
            _shell.SetBusy(true, "Opening FH6 profile…");
            CloseSession();
            string fullPath = Path.GetFullPath(path);
            var detection = FileDetection.Detect(fullPath);
            string editorPath;
            if (detection.Kind == DetectedKind.ProfileData)
            {
                _shell.SetBusy(true, "Decrypting profile for editing...");
                _workingDirectory = Path.Combine(Path.GetTempPath(), "ForzaCryptoTool", "profile-editor-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_workingDirectory);
                editorPath = Path.Combine(_workingDirectory, "C_ProfileData_decrypted.bin");
                var decrypted = await _crypto.DecryptAsync(fullPath, editorPath);
                if (!decrypted.Success) throw new InvalidDataException(decrypted.Message);
                _encryptedSourcePath = fullPath;
            }
            else if (detection.Kind == DetectedKind.ProfileDecrypted)
            {
                editorPath = fullPath;
            }
            else
            {
                throw new InvalidDataException($"{detection.KindLabel} is not an FH6 ProfileData file. Open an encrypted C_ProfileData or a decrypted profile .bin.");
            }
            _session = Fh6ProfileEditorSession.Open(editorPath);
            BuildRows();
            PopulateOverview();
            EmptyText.Visibility = Visibility.Collapsed;
            EditorTabs.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = true;
            FileNameText.Text = Path.GetFileName(fullPath);
            FileSummaryText.Text = $"{path}  ·  {_session.Document.OriginalSize:N0} bytes  ·  SQLite {_session.CheckDatabaseIntegrity()}";
            UpdateDirtyState();
            _shell.SetStatus("FH6 profile opened and validated.");
            WorkflowText.Text = _encryptedSourcePath is null
                ? "DECRYPTED INPUT  >  EDIT  >  SAVE VERIFIED COPY"
                : "ENCRYPTED INPUT  >  TEMPORARY DECRYPT  >  EDIT  >  ENCRYPTED OUTPUT";
            App.Settings.AddRecent(fullPath);
        }
        catch (Exception ex)
        {
            Logger.Exception("Profile editor open failed", ex);
            CloseSession();
            MessageBox.Show(ex.Message, "Could not open FH6 profile", MessageBoxButton.OK, MessageBoxImage.Error);
            _shell.SetStatus("Profile open failed.");
        }
        finally { _shell.SetBusy(false); }
    }

    private void BuildRows()
    {
        var document = _session!.Document;
        BuildPropertyRows();
        _binary = document.Binary.Records.Select(record => new BinaryRow(record)).ToList();
        BinaryGrid.ItemsSource = _binary;
        RefreshBxmlRows();
        var tables = _session.ListTables().Select(table => new TableChoice(table.Name, $"{table.Name}  ·  {table.Rows:N0} rows")).ToList();
        TableCombo.ItemsSource = tables;
        if (tables.Count > 0) TableCombo.SelectedIndex = 0;
    }

    private void BuildPropertyRows()
    {
        var properties = _session!.Document.Properties;
        _properties = properties.Walk().Where(item => item.Property.Kind is not (PropertyKind.PropertyBag or PropertyKind.DatabasePropertyBag))
            .Select(item => new PropertyRow(item.Path, item.Property)).ToList();
        string[] names = ["EventHashes", "DLCHashes"];
        for (int setIndex = 0; setIndex < properties.HashSets.Count && setIndex < names.Length; setIndex++)
            for (int valueIndex = 0; valueIndex < properties.HashSets[setIndex].Count; valueIndex++)
                _properties.Add(new PropertyRow($"/{names[setIndex]}/Value{valueIndex}", "UInt32Hash", $"0x{properties.HashSets[setIndex][valueIndex]:X8}"));
        PropertiesGrid.ItemsSource = _properties;
    }

    private void PopulateOverview()
    {
        var document = _session!.Document;
        OverviewProperties.Text = _properties.Count.ToString("N0");
        OverviewBxml.Text = document.Bxml.Walk().Count().ToString("N0");
        OverviewBinary.Text = document.Binary.Records.Count.ToString("N0");
        OverviewSqlite.Text = _session.CheckDatabaseIntegrity().ToUpperInvariant();
        XuidText.Text = document.Binary.Xuid.ToString();
        string[] kinds = ["Typed properties", "BXML save state", "Binary career state", "SQLite career database"];
        SectionsGrid.ItemsSource = document.Sections.Select((section, index) => new SectionRow(index, kinds[index], $"0x{section.Marker:X8}", section.Payload.Length, $"0x{section.Offset:X}")).ToList();
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Open encrypted or decrypted FH6 C_ProfileData", Filter = "FH6 ProfileData|C_Profile*;*.bin|All files|*.*" };
        if (dialog.ShowDialog() == true) await LoadFileAsync(dialog.FileName);
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths) await LoadFileAsync(paths[0]);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        bool encryptOutput = _encryptedSourcePath is not null;
        string source = _encryptedSourcePath ?? _session.SourcePath;
        var dialog = new SaveFileDialog
        {
            Title = encryptOutput ? "Save edited encrypted FH6 ProfileData" : "Save verified decrypted FH6 profile copy",
            FileName = Path.GetFileNameWithoutExtension(source) + "_edited" + Path.GetExtension(source),
            InitialDirectory = Path.GetDirectoryName(source),
            Filter = encryptOutput ? "Encrypted FH6 ProfileData|C_Profile*|All files|*.*" : "Decrypted FH6 profile|*.bin;C_Profile*|All files|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            _shell.SetBusy(true, "Saving and verifying FH6 profile…");
            string verifiedPath = dialog.FileName;
            if (encryptOutput)
                verifiedPath = Path.Combine(_workingDirectory!, "C_ProfileData_edited_decrypted.bin");

            var result = _session.Save(verifiedPath);
            if (encryptOutput)
            {
                _shell.SetBusy(true, "Re-encrypting edited FH6 ProfileData...");
                var encrypted = await _crypto.EncryptAsync(verifiedPath, dialog.FileName);
                if (!encrypted.Success) throw new InvalidDataException(encrypted.Message);
            }

            _session.MarkSaved();
            UpdateDirtyState();

            _shell.SetStatus($"Saved {(encryptOutput ? "encrypted" : "decrypted")} profile: {dialog.FileName}");
            MessageBox.Show($"Edited profile was verified and {(encryptOutput ? "re-encrypted" : "saved")} successfully.\n\nOutput: {dialog.FileName}\nSQLite: {result.SqliteIntegrity}\nProperties: {result.Properties:N0}\nBinary records: {result.BinaryRecords:N0}",
                encryptOutput ? "Encrypted FH6 profile saved" : "FH6 profile saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Exception("Profile editor save failed", ex);
            MessageBox.Show(ex.Message, "Profile was not saved", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _shell.SetBusy(false); }
    }

    private void OnPropertySearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = PropertySearch.Text.Trim();
        PropertiesGrid.ItemsSource = string.IsNullOrEmpty(query) ? _properties : _properties.Where(row => row.Search.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void OnPropertySelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedProperty = PropertiesGrid.SelectedItem as PropertyRow;
        bool editable = _selectedProperty?.Editable == true;
        PropertyValueText.IsEnabled = editable; ApplyPropertyButton.IsEnabled = editable;
        SelectedPropertyText.Text = _selectedProperty?.Path ?? "Select an editable property";
        PropertyValueText.Text = _selectedProperty?.Value ?? "";
    }

    private void OnApplyProperty(object sender, RoutedEventArgs e)
    {
        if (_session is null || _selectedProperty is null) return;
        try
        {
            _session.UpdateProperty(_selectedProperty.Path, PropertyValueText.Text);
            string changedPath = _selectedProperty.Path;
            BuildPropertyRows();
            _selectedProperty = _properties.FirstOrDefault(row => row.Path == changedPath);
            UpdateDirtyState();
            _shell.SetStatus($"Updated {changedPath}.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid property value", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnApplyXuid(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try { _session.UpdateXuid(XuidText.Text); XuidText.Text = _session.Document.Binary.Xuid.ToString(); UpdateDirtyState(); _shell.SetStatus("Canonical profile XUID updated."); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid XUID", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnBinarySearchChanged(object sender, TextChangedEventArgs e)
    {
        string query = BinarySearch.Text.Trim();
        BinaryGrid.ItemsSource = string.IsNullOrEmpty(query) ? _binary : _binary.Where(row => row.Search.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void OnBinarySelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedBinary = BinaryGrid.SelectedItem as BinaryRow;
        RefreshBinarySelection();
    }

    private void RefreshBinarySelection()
    {
        BinarySelectionText.Text = _selectedBinary is null ? "Select a record to inspect or patch a fixed-width scalar." : $"#{_selectedBinary.Ordinal} · {_selectedBinary.Name} · {_selectedBinary.Serializer}";
        if (_selectedBinary is null || _session is null) { BinaryHexText.Text = ""; return; }
        byte[] payload = _session.Document.Binary.Records[_selectedBinary.Ordinal].Payload;
        BinaryHexText.Text = Convert.ToHexString(payload.AsSpan(0, Math.Min(128, payload.Length))).Chunk(2).Select(chars => new string(chars)).Aggregate((a, b) => a + " " + b)
            + (payload.Length > 128 ? " …" : "");
        OnReadBinaryScalar(this, new RoutedEventArgs());
    }

    private void OnReadBinaryScalar(object sender, RoutedEventArgs e)
    {
        if (_session is null || _selectedBinary is null) return;
        try
        {
            int offset = ParseOffset(BinaryOffsetText.Text);
            var type = Enum.Parse<BinaryScalarType>(BinaryTypeCombo.SelectedItem?.ToString() ?? "UInt32");
            BinaryValueText.Text = _session.ReadBinaryScalar(_selectedBinary.Ordinal, offset, type);
        }
        catch (Exception ex) { _shell.SetStatus(ex.Message); }
    }

    private void OnApplyBinaryScalar(object sender, RoutedEventArgs e)
    {
        if (_session is null || _selectedBinary is null) return;
        try
        {
            int offset = ParseOffset(BinaryOffsetText.Text);
            var type = Enum.Parse<BinaryScalarType>(BinaryTypeCombo.SelectedItem?.ToString() ?? "UInt32");
            _session.UpdateBinaryScalar(_selectedBinary.Ordinal, offset, type, BinaryValueText.Text);
            RefreshBinarySelection();
            UpdateDirtyState(); _shell.SetStatus($"Updated {_selectedBinary.Name} payload offset 0x{offset:X}.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid binary scalar", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static int ParseOffset(string text) => text.Trim().StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToInt32(text.Trim()[2..], 16) : int.Parse(text.Trim());

    private void RefreshBxmlRows()
    {
        var bxml = _session!.Document.Bxml;
        var refs = new int[bxml.Strings.Count];
        foreach (var (_, node) in bxml.Walk())
        {
            refs[node.NameIndex]++;
            foreach (var attribute in node.Attributes) { refs[attribute.KeyIndex]++; refs[attribute.ValueIndex]++; }
        }
        BxmlGrid.ItemsSource = bxml.Strings.Select((value, index) => new BxmlStringRow(index, value, refs[index])).ToList();
        BxmlNodesGrid.ItemsSource = bxml.Walk().Select(item => new BxmlNodeRow(item.Depth, item.Node, bxml.Strings)).ToList();
        BxmlDocumentFlagText.Text = bxml.DocumentFlag.ToString();
    }

    private void OnBxmlStringSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedBxml = BxmlGrid.SelectedItem as BxmlStringRow;
        BxmlValueText.IsEnabled = _selectedBxml is not null; ApplyBxmlButton.IsEnabled = _selectedBxml is not null;
        BxmlValueText.Text = _selectedBxml?.Value ?? "";
    }

    private void OnApplyBxmlString(object sender, RoutedEventArgs e)
    {
        if (_session is null || _selectedBxml is null) return;
        _session.Document.Bxml.Strings[_selectedBxml.Index] = BxmlValueText.Text;
        _session.BxmlDirty = true; RefreshBxmlRows(); UpdateDirtyState();
        _shell.SetStatus($"Updated BXML string {_selectedBxml.Index}.");
    }

    private void OnBxmlNodeSelected(object sender, SelectionChangedEventArgs e)
    {
        _selectedBxmlNode = BxmlNodesGrid.SelectedItem as BxmlNodeRow;
        bool selected = _selectedBxmlNode is not null;
        BxmlNodeNameText.IsEnabled = selected; BxmlNodeFlagsText.IsEnabled = selected; ApplyBxmlNodeButton.IsEnabled = selected;
        BxmlNodeNameText.Text = _selectedBxmlNode?.Name ?? "";
        BxmlNodeFlagsText.Text = _selectedBxmlNode?.FlagsHex ?? "";
    }

    private void OnApplyBxmlNode(object sender, RoutedEventArgs e)
    {
        if (_session is null || _selectedBxmlNode is null) return;
        try
        {
            XmlConvert.VerifyName(BxmlNodeNameText.Text);
            int flags = ParseOffset(BxmlNodeFlagsText.Text);
            if (flags is < 0 or > 7) throw new InvalidDataException("BXML node flags must be between 0 and 7.");
            var bxml = _session.Document.Bxml;
            _selectedBxmlNode.Node.NameIndex = bxml.Intern(BxmlNodeNameText.Text);
            _selectedBxmlNode.Node.Flags = (byte)flags;
            _session.BxmlDirty = true; RefreshBxmlRows(); UpdateDirtyState();
            _shell.SetStatus("Updated BXML node name and flags.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid BXML node", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnApplyBxmlDocumentFlag(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try
        {
            int flag = ParseOffset(BxmlDocumentFlagText.Text);
            if (flag is < 0 or > 255) throw new InvalidDataException("BXML document flag must fit one byte.");
            _session.Document.Bxml.DocumentFlag = (byte)flag; _session.BxmlDirty = true; UpdateDirtyState();
            _shell.SetStatus("Updated BXML document flag.");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Invalid BXML flag", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OnTableSelected(object sender, SelectionChangedEventArgs e) => LoadSelectedTable();
    private void OnRefreshTable(object sender, RoutedEventArgs e) => LoadSelectedTable();
    private void LoadSelectedTable()
    {
        if (_session is null || TableCombo.SelectedItem is not TableChoice table) return;
        try { BindQuery(_session.BrowseTable(table.Name)); SqlResultText.Text = $"Showing the first 100 rows from {table.Name}."; }
        catch (Exception ex) { SqlResultText.Text = ex.Message; }
    }

    private void OnRunSql(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        try
        {
            var batch = _session.ExecuteSql(SqlText.Text);
            if (batch.Statements.Count > 0) BindQuery(batch.Statements[^1]);
            SqlResultText.Text = $"{batch.Statements.Count} statement(s) completed · {batch.Changes:N0} row change(s). Results are limited to 500 rows.";
            if (batch.Changes > 0) { UpdateDirtyState(); RefreshTableChoices(); }
        }
        catch (Exception ex) { SqlResultText.Text = ex.Message; }
    }

    private void RefreshTableChoices()
    {
        if (_session is null) return;
        string? selected = (TableCombo.SelectedItem as TableChoice)?.Name;
        var choices = _session.ListTables().Select(table => new TableChoice(table.Name, $"{table.Name}  ·  {table.Rows:N0} rows")).ToList();
        TableCombo.ItemsSource = choices;
        TableCombo.SelectedItem = choices.FirstOrDefault(choice => choice.Name == selected) ?? choices.FirstOrDefault();
    }

    private void BindQuery(ProfileQueryResult result)
    {
        var table = new DataTable();
        foreach (string column in result.Columns)
        {
            string unique = column; int suffix = 2;
            while (table.Columns.Contains(unique)) unique = column + "_" + suffix++;
            table.Columns.Add(unique, typeof(object));
        }
        foreach (object?[] values in result.Rows) table.Rows.Add(values.Select(value => value ?? DBNull.Value).ToArray());
        DatabaseGrid.ItemsSource = null;
        _databaseTable?.Dispose();
        _databaseTable = table;
        DatabaseGrid.AutoGenerateColumns = true;
        DatabaseGrid.ItemsSource = table.DefaultView;
    }

    private void UpdateDirtyState()
    {
        bool dirty = _session?.IsDirty == true;
        DirtyText.Text = dirty ? "UNSAVED CHANGES" : "VALIDATED";
        SaveButton.Content = _encryptedSourcePath is not null
            ? (dirty ? "Save encrypted edit" : "Save encrypted copy")
            : (dirty ? "Save edited copy" : "Save copy");
    }

    private void CloseSession()
    {
        DatabaseGrid.ItemsSource = null;
        _databaseTable?.Dispose();
        _databaseTable = null;
        _session?.Dispose();
        _session = null;
        _encryptedSourcePath = null;
        if (_workingDirectory is not null)
        {
            try
            {
                string safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ForzaCryptoTool")) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(_workingDirectory);
                if (candidate.StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(candidate))
                    Directory.Delete(candidate, recursive: true);
            }
            catch (Exception ex) { Logger.Warn($"Could not remove profile editor temporary files: {ex.Message}"); }
            _workingDirectory = null;
        }
    }

    private bool ConfirmDiscardChanges()
    {
        if (_session?.IsDirty != true) return true;
        return MessageBox.Show(
            "This profile has unsaved changes. Discard them?",
            "Unsaved profile changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    internal bool TryClose()
    {
        if (!ConfirmDiscardChanges()) return false;
        CloseSession();
        return true;
    }

    private sealed record SectionRow(int Index, string Kind, string Marker, int Size, string OffsetHex);
    private sealed record BinaryRow(int Ordinal, string Name, string Serializer, string Tag, int Size, string OffsetHex, string Search)
    {
        public BinaryRow(BinaryCareerRecord r) : this(r.Ordinal, r.Name, r.Serializer, $"0x{r.Tag:X8}", r.Payload.Length, $"0x{r.PayloadOffset:X}", r.Name + " " + r.Serializer) { }
    }
    private sealed class PropertyRow
    {
        public PropertyRow(string path, ProfileProperty property)
        { Path = path; Type = property.TypeName; Value = property.DisplayValue; Editable = property.Editable; }
        public PropertyRow(string path, string type, string value)
        { Path = path; Type = type; Value = value; Editable = true; }
        public string Path { get; }
        public string Type { get; }
        public string Value { get; }
        public bool Editable { get; }
        public string Search => Path + " " + Type + " " + Value;
    }
    private sealed record BxmlStringRow(int Index, string Value, int References);
    private sealed class BxmlNodeRow
    {
        public BxmlNodeRow(int depth, BxmlNode node, IReadOnlyList<string> strings)
        {
            Node = node; Name = strings[node.NameIndex]; DisplayName = new string(' ', depth * 2) + Name;
            OffsetHex = $"0x{node.Offset:X}"; FlagsHex = $"0x{node.Flags:X2}"; Children = node.Children.Count;
            Attributes = string.Join(" · ", node.Attributes.Select(attribute => $"{strings[attribute.KeyIndex]}={strings[attribute.ValueIndex]}"));
        }
        public BxmlNode Node { get; }
        public string Name { get; }
        public string DisplayName { get; }
        public string OffsetHex { get; }
        public string FlagsHex { get; }
        public int Children { get; }
        public string Attributes { get; }
    }
    private sealed record TableChoice(string Name, string Display);
}
