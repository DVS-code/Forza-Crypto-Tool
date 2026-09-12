using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ForzaCryptoTool;

public partial class AssetBrowserView : UserControl
{
    private const string DefaultInstallRoot = @"C:\Program Files (x86)\Steam\steamapps\common\ForzaHorizon6";

    private const string LoadingPlaceholder = "__loading__";

    private readonly MainWindow _shell;
    private readonly AssetVfs _vfs;
    private readonly ArchiveVfsProvider _archives;
    private readonly ArchiveDirectoryCache _cache = new();
    private readonly HexViewer _hex = new();
    private readonly AssetEditService _editor;
    private CancellationTokenSource? _pendingRead;

    private VfsNode? _openNode;
    private byte[]? _openBytes;
    private string? _openText;
    private bool _dirty;
    private bool _suppressTextChanged;

    internal AssetBrowserView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;

        var ivTable = IvTableStore.Load();
        var crypto = new CryptoService(App.Backend);
        crypto.Progress += message => _shell.SetStatus(message);

        _archives = new ArchiveVfsProvider(_cache, new M22EntryDecryptor(crypto, ivTable));
        _vfs = new AssetVfs([new FileSystemVfsProvider(), _archives]);
        _editor = new AssetEditService(crypto);
        _editor.Progress += message => _shell.SetStatus(message);

        HexHost.Content = _hex;

        RootBox.Text = Directory.Exists(DefaultInstallRoot)
            ? DefaultInstallRoot
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        DragEnter += (_, e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy : DragDropEffects.None;
        Drop += OnDrop;

        Loaded += (_, _) => { if (Tree.Items.Count == 0) LoadRoot(); };
    }

    private void LoadRoot()
    {
        Tree.Items.Clear();
        string root = RootBox.Text.Trim();
        if (!Directory.Exists(root))
        {
            _shell.SetStatus($"Folder not found: {root}");
            return;
        }

        try
        {
            foreach (var node in _vfs.List(root))
                Tree.Items.Add(CreateItem(node));
            _shell.SetStatus($"{Tree.Items.Count} items in {Path.GetFileName(root)}");
        }
        catch (Exception ex)
        {
            Logger.Exception("Asset browser root listing failed", ex);
            _shell.SetStatus("Could not list that folder.");
        }
    }

    private TreeViewItem CreateItem(VfsNode node)
    {
        var item = new TreeViewItem { Header = BuildHeader(node), Tag = node };

        if (node.IsContainer) item.Items.Add(LoadingPlaceholder);
        return item;
    }

    private object BuildHeader(VfsNode node)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        panel.Children.Add(new TextBlock
        {
            Text = node.Kind switch
            {
                VfsNodeKind.Directory => "\U0001F4C1",
                VfsNodeKind.ArchiveRoot => "\U0001F5DC",
                _ => "\U0001F4C4",
            },
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

        panel.Children.Add(new TextBlock
        {
            Text = node.Name,
            Foreground = (Brush)FindResource(node.CanOpen || node.IsContainer ? "TextPrimary" : "TextDisabled"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (node.Kind == VfsNodeKind.ArchiveEntry || node.Kind == VfsNodeKind.File)
        {
            panel.Children.Add(new TextBlock
            {
                Text = node.SizeLabel,
                Foreground = (Brush)FindResource("TextDisabled"),
                FontSize = (double)FindResource("FontTiny"),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        if (node.Kind == VfsNodeKind.ArchiveEntry)
            panel.Children.Add(BuildStatusBadge(node));

        return panel;
    }

    private UIElement BuildStatusBadge(VfsNode node)
    {
        var (label, brushKey) = node.Decryptability switch
        {
            Decryptability.NotEncrypted => ("plain", "Success"),
            Decryptability.SingleChunk => ("encrypted", "Info"),
            Decryptability.MultiChunk when node.CanOpen => ("encrypted", "Info"),
            Decryptability.MultiChunk => ("locked", "Warning"),
            _ => ("unknown", "TextDisabled"),
        };

        var brush = (Brush)FindResource(brushKey);
        return new Border
        {
            Background = (Brush)FindResource(brushKey switch
            {
                "Success" => "SuccessDim",
                "Info" => "InfoDim",
                "Warning" => "WarningDim",
                _ => "Overlay",
            }),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = node.Blocker ?? "Can be opened.",
            Child = new TextBlock
            {
                Text = label,
                Foreground = brush,
                FontSize = (double)FindResource("FontTiny"),
            },
        };
    }

    private void OnItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item) return;
        if (item.Items.Count != 1 || item.Items[0] is not string placeholder || placeholder != LoadingPlaceholder)
            return;
        if (item.Tag is not VfsNode node) return;

        item.Items.Clear();
        try
        {
            var children = _vfs.List(node.VfsPath);

            if (node.Kind == VfsNodeKind.ArchiveRoot)
            {
                var summary = _archives.Summarize(node.VfsPath);
                _shell.SetStatus($"{node.Name}: {summary.Label}");
                if (OnlyOpenable.IsChecked == true)
                    children = children.Where(c => c.CanOpen).ToList();
            }

            foreach (var child in children)
                item.Items.Add(CreateItem(child));

            if (item.Items.Count == 0)
            {
                item.Items.Add(new TextBlock
                {
                    Text = OnlyOpenable.IsChecked == true ? "(no openable entries)" : "(empty)",
                    Foreground = (Brush)FindResource("TextDisabled"),
                    FontStyle = FontStyles.Italic,
                });
            }
        }
        catch (Exception ex)
        {
            Logger.Exception($"Listing '{node.VfsPath}' failed", ex);
            item.Items.Add(new TextBlock
            {
                Text = "(could not read)",
                Foreground = (Brush)FindResource("Danger"),
            });
        }
    }

    private async void OnSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: VfsNode node } item) return;

        if (!ConfirmDiscardChanges()) return;
        ResetEditState();

        if (node.IsContainer && !item.IsExpanded) item.IsExpanded = true;

        _pendingRead?.Cancel();
        _pendingRead = new CancellationTokenSource();
        var ct = _pendingRead.Token;

        DetailName.Text = node.Name;
        DetailMeta.Text = BuildMetaLine(node);
        BlockerPanel.Visibility = Visibility.Collapsed;

        if (node.IsContainer)
        {
            if (node.Kind == VfsNodeKind.ArchiveRoot)
            {
                var summary = _archives.Summarize(node.VfsPath);
                DetailMeta.Text = $"{summary.Label} · {node.SizeLabel}";
                ShowEmptyState("\U0001F5DC", node.Name,
                    summary.Openable > 0
                        ? $"This archive holds {summary.Total:N0} entries, {summary.Openable:N0} of which can be opened. "
                          + "Expand it on the left and pick a file to view."
                        : $"This archive holds {summary.Total:N0} entries, but none can be decrypted with the IVs "
                          + "currently available. Untick “Only openable” to inspect them as raw bytes.");
            }
            else
            {
                ShowEmptyState("\U0001F4C1", node.Name, "Expand this folder on the left to see what's inside.");
            }
            return;
        }

        if (!node.CanOpen)
        {
            ShowBlocker(node.Blocker ?? "This file cannot be opened.");
            ShowEmptyState("\U0001F512", node.Name, "This entry cannot be decrypted, so there is nothing to display.");
            return;
        }

        try
        {
            _shell.SetBusy(true, $"Opening {node.Name}…");
            var content = await Task.Run(() => _vfs.ReadAsync(node.VfsPath, ct), ct);
            if (ct.IsCancellationRequested) return;

            ShowContent(node, content);
            _shell.SetStatus(content.SourceDescription);
        }
        catch (OperationCanceledException)
        {
        }
        catch (VfsEntryLockedException ex)
        {
            ShowBlocker(ex.Message);
            ShowEmptyState("\U0001F512", node.Name, "This entry cannot be decrypted, so there is nothing to display.");
            _shell.SetStatus("Entry is locked.");
        }
        catch (Exception ex)
        {
            Logger.Exception($"Reading '{node.VfsPath}' failed", ex);
            ShowBlocker(BuildConfig.ShowFullExceptions ? ex.ToString() : ex.Message);
            ShowEmptyState("⚠", node.Name, "This file could not be read. The reason is shown above.");
            _shell.SetStatus("Could not open that file.");
        }
        finally
        {
            _shell.SetBusy(false);
        }
    }

    private void ShowEmptyState(string icon, string title, string body)
    {
        TextPane.Clear();
        _hex.Clear();
        EmptyIcon.Text = icon;
        EmptyTitle.Text = title;
        EmptyBody.Text = body;
        EmptyState.Visibility = Visibility.Visible;
        ContentTabs.Visibility = Visibility.Collapsed;
    }

    private void ShowContent(VfsNode node, VfsContent content)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        ContentTabs.Visibility = Visibility.Visible;

        if (content.Bytes.Length == 0)
        {
            ShowEmptyState("\U0001F4C4", node.Name, "This file is empty (0 bytes).");
            return;
        }

        _hex.Show(content.Bytes);
        _openNode = node;
        _openBytes = content.Bytes;

        bool isText = LooksLikeText(content.Bytes);
        bool truncatedForDisplay = false;

        _suppressTextChanged = true;
        if (isText)
        {
            string decoded = DecodeText(content.Bytes, out truncatedForDisplay);
            TextPane.Text = decoded;
            _openText = decoded;
            ContentTabs.SelectedIndex = 0;
        }
        else
        {
            TextPane.Text = string.Empty;
            _openText = null;
            ContentTabs.SelectedIndex = 1;
        }
        _suppressTextChanged = false;

        bool editable = isText && !truncatedForDisplay && AssetEditService.CanSave(node);
        TextPane.IsReadOnly = !editable;
        RevertButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        DirtyMarker.Visibility = Visibility.Collapsed;

        DetailMeta.Text = BuildMetaLine(node) + " · " + content.SourceDescription
                          + (_hex.Truncated ? " · hex view truncated" : string.Empty);
    }

    private void ShowBlocker(string message)
    {
        BlockerText.Text = message;
        BlockerPanel.Visibility = Visibility.Visible;
    }

    private static string BuildMetaLine(VfsNode node)
    {
        var parts = new List<string> { node.SizeLabel };
        if (node.Kind == VfsNodeKind.ArchiveEntry)
        {
            parts.Add(node.Decryptability switch
            {
                Decryptability.NotEncrypted => "not encrypted",
                Decryptability.SingleChunk => "method 22, single chunk",
                Decryptability.MultiChunk => "method 22, multi-chunk",
                _ => "unrecognized",
            });
            if (node.PhysicalSize != node.Size)
                parts.Add($"{node.PhysicalSize:N0} B on disk");
        }
        return string.Join(" · ", parts);
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        int sample = Math.Min(data.Length, 1024);
        if (sample == 0) return true;
        int printable = 0;
        for (int i = 0; i < sample; i++)
        {
            byte b = data[i];
            if (b == 0) return false;
            if (b >= 32 || b is 9 or 10 or 13) printable++;
        }
        return printable >= sample * 0.90;
    }

    private static string DecodeText(byte[] data, out bool truncated)
    {
        const int maxChars = 4 * 1024 * 1024;
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(data);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];

        truncated = text.Length > maxChars;
        return truncated ? text[..maxChars] + "\n\n\u2026 truncated for display \u2026" : text;
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged || TextPane.IsReadOnly) return;

        _dirty = _openText is not null && TextPane.Text != _openText;
        DirtyMarker.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = _dirty;
        RevertButton.IsEnabled = _dirty;
    }

    private void OnRevertClick(object sender, RoutedEventArgs e)
    {
        if (_openText is null) return;
        _suppressTextChanged = true;
        TextPane.Text = _openText;
        _suppressTextChanged = false;
        ResetDirtyFlags();
        _shell.SetStatus("Reverted to the file on disk.");
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_openNode is null || _openBytes is null || !_dirty) return;

        var node = _openNode;
        var (container, entryName) = AssetVfs.SplitPath(node.VfsPath);
        byte[] content = AssetEditService.EncodeTextLike(_openBytes, TextPane.Text);

        string subject = entryName is null
            ? $"{node.Name} \u2014 {content.Length:N0} bytes"
            : $"{entryName} in {Path.GetFileName(container)} \u2014 the whole archive is rewritten, "
              + "with every other entry copied unchanged.";

        string outputPath = Path.Combine(App.Settings.ResolveOutputFolder(), Path.GetFileName(container));
        var dialog = new SaveTargetDialog(subject, outputPath, container, canOverwrite: File.Exists(container))
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true || dialog.Result == SaveTarget.Cancel) return;

        string destination = dialog.Result == SaveTarget.OverwriteOriginal ? container : outputPath;

        try
        {
            SaveButton.IsEnabled = false;
            RevertButton.IsEnabled = false;
            _shell.SetBusy(true, $"Saving {node.Name}\u2026");

            var outcome = entryName is null
                ? await Task.Run(() => _editor.SaveLooseFile(destination, content))
                : await _editor.SaveArchiveEntryAsync(container, entryName, content, destination);

            if (!outcome.Success)
            {
                MessageBox.Show(outcome.Message, "Could not save", MessageBoxButton.OK, MessageBoxImage.Error);
                _shell.SetStatus("Save failed.");
                SaveButton.IsEnabled = true;
                RevertButton.IsEnabled = true;
                return;
            }

            _openText = TextPane.Text;
            _openBytes = content;
            ResetDirtyFlags();

            if (dialog.Result == SaveTarget.OverwriteOriginal) _cache.Clear();

            App.Settings.AddRecent(outcome.OutputPath!);
            _shell.SetStatus(outcome.Message);
            MessageBox.Show(outcome.Message, "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logger.Exception($"Saving '{node.VfsPath}' failed", ex);
            MessageBox.Show(BuildConfig.ShowFullExceptions ? ex.ToString() : ex.Message,
                "Could not save", MessageBoxButton.OK, MessageBoxImage.Error);
            _shell.SetStatus("Save failed.");
            SaveButton.IsEnabled = true;
            RevertButton.IsEnabled = true;
        }
        finally
        {
            _shell.SetBusy(false);
        }
    }

    private void ResetDirtyFlags()
    {
        _dirty = false;
        DirtyMarker.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = false;
        RevertButton.IsEnabled = false;
    }

    private void ResetEditState()
    {
        _openNode = null;
        _openBytes = null;
        _openText = null;
        TextPane.IsReadOnly = true;
        ResetDirtyFlags();
    }

    private bool ConfirmDiscardChanges()
    {
        if (!_dirty) return true;
        return MessageBox.Show(
            "This file has unsaved changes. Discard them?",
            "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder to browse",
            InitialDirectory = Directory.Exists(RootBox.Text) ? RootBox.Text : string.Empty,
        };
        if (dialog.ShowDialog() != true) return;
        RootBox.Text = dialog.FolderName;
        LoadRoot();
    }

    private void OnReloadClick(object sender, RoutedEventArgs e)
    {
        _cache.Clear();
        LoadRoot();
    }

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        LoadRoot();
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        string path = paths[0];
        RootBox.Text = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? RootBox.Text;
        LoadRoot();
    }

    internal bool TryClose()
    {
        _pendingRead?.Cancel();
        return true;
    }
}
