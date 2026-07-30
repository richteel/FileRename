using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace FileRename;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ObservableCollection<FilePreviewItem> _previewItems = new();
    private bool _uiReady;

    public MainWindow()
    {
        InitializeComponent();
        PreviewGrid.ItemsSource = _previewItems;
        _uiReady = true;
        RefreshPreview();
    }

    private void OnBrowseInputFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select the input folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(InputFolderTextBox.Text) ? InputFolderTextBox.Text : string.Empty
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            InputFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void OnBrowseOutputFolder(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Select the destination folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(OutputFolderTextBox.Text) ? OutputFolderTextBox.Text : string.Empty
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            OutputFolderTextBox.Text = dialog.SelectedPath;
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (OutputFolderTextBox == null || BrowseOutputButton == null || ActionButton == null)
        {
            // Fired while the window is still being constructed.
            return;
        }

        bool copyMode = CopyToFolderRadio.IsChecked == true;
        OutputFolderTextBox.IsEnabled = copyMode;
        BrowseOutputButton.IsEnabled = copyMode;
        ActionButton.Content = copyMode ? "Copy Files" : "Rename Files";
        RefreshPreview();
    }

    private void OnMaskOrFolderChanged(object sender, TextChangedEventArgs e) => RefreshPreview();

    private void OnRefreshPreview(object sender, RoutedEventArgs e) => RefreshPreview();

    /// <summary>Rebuilds the preview grid from the current folder and mask settings.</summary>
    private void RefreshPreview()
    {
        if (!_uiReady)
        {
            return;
        }

        _previewItems.Clear();

        string inputFolder = InputFolderTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(inputFolder) || !Directory.Exists(inputFolder))
        {
            StatusText.Text = "Select a valid input folder to see a preview.";
            ActionButton.IsEnabled = false;
            return;
        }

        string inputMask = InputMaskTextBox.Text;
        string outputMask = OutputMaskTextBox.Text;
        bool copyMode = CopyToFolderRadio.IsChecked == true;

        string[] files;
        try
        {
            files = Directory.EnumerateFiles(inputFolder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read input folder: {ex.Message}";
            ActionButton.IsEnabled = false;
            return;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int renameCount = 0;
        int problemCount = 0;
        string? maskError = null;

        foreach (var fullPath in files)
        {
            string name = Path.GetFileName(fullPath);
            var matchStatus = MaskEngine.ComputeNewName(name, inputMask, outputMask, out string newName, out string? error);
            maskError ??= error;

            string statusLabel = matchStatus switch
            {
                MaskEngine.MatchStatus.NoMatch => "No match",
                MaskEngine.MatchStatus.NoChange => "No change",
                MaskEngine.MatchStatus.Rename => "Rename",
                _ => string.Empty
            };

            if (matchStatus == MaskEngine.MatchStatus.Rename)
            {
                if (!usedNames.Add(newName))
                {
                    statusLabel = "Duplicate!";
                    problemCount++;
                }
                else if (!copyMode && File.Exists(Path.Combine(inputFolder, newName)))
                {
                    statusLabel = "Target exists!";
                    problemCount++;
                }
                else
                {
                    renameCount++;
                }
            }

            _previewItems.Add(new FilePreviewItem
            {
                FullPath = fullPath,
                OriginalName = name,
                NewName = newName,
                Status = statusLabel
            });
        }

        if (maskError != null)
        {
            StatusText.Text = maskError;
            ActionButton.IsEnabled = false;
            return;
        }

        if (copyMode && string.IsNullOrWhiteSpace(OutputFolderTextBox.Text.Trim()))
        {
            StatusText.Text = $"{files.Length} file(s) found. Select a destination folder to copy to.";
            ActionButton.IsEnabled = false;
            return;
        }

        string action = copyMode ? "copied" : "renamed";
        string summary = $"{files.Length} file(s) found \u2014 {renameCount} will be {action}, {files.Length - renameCount - problemCount} unchanged.";
        if (problemCount > 0)
        {
            summary += $" {problemCount} skipped due to conflicts.";
        }

        StatusText.Text = summary;
        ActionButton.IsEnabled = files.Length > 0;
    }

    private void OnPerformAction(object sender, RoutedEventArgs e)
    {
        bool copyMode = CopyToFolderRadio.IsChecked == true;
        string destFolder = OutputFolderTextBox.Text.Trim();

        if (copyMode)
        {
            if (string.IsNullOrWhiteSpace(destFolder))
            {
                MessageBox.Show(this, "Please select a destination folder.", "File Renamer", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Directory.CreateDirectory(destFolder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not create the destination folder: {ex.Message}", "File Renamer", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        var actionable = _previewItems
            .Where(i => i.Status == "Rename" || (copyMode && i.Status == "No change"))
            .ToList();

        if (actionable.Count == 0)
        {
            MessageBox.Show(this, "There is nothing to do \u2014 no files match the current masks.", "File Renamer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string verb = copyMode ? "copy" : "rename";
        var confirm = MessageBox.Show(
            this,
            $"This will {verb} {actionable.Count} file(s). Continue?",
            copyMode ? "Confirm Copy" : "Confirm Rename",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        int success = 0;
        var errors = new List<string>();

        foreach (var item in actionable)
        {
            try
            {
                if (copyMode)
                {
                    string destPath = Path.Combine(destFolder, item.NewName);
                    File.Copy(item.FullPath, destPath, overwrite: false);
                }
                else
                {
                    string destPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, item.NewName);
                    File.Move(item.FullPath, destPath);
                }

                success++;
            }
            catch (Exception ex)
            {
                errors.Add($"{item.OriginalName}: {ex.Message}");
            }
        }

        RefreshPreview();

        string summary = $"{success} of {actionable.Count} file(s) {(copyMode ? "copied" : "renamed")} successfully.";
        if (errors.Count > 0)
        {
            summary += $"\n\n{errors.Count} error(s):\n" + string.Join("\n", errors.Take(10));
            if (errors.Count > 10)
            {
                summary += $"\n...and {errors.Count - 10} more.";
            }

            MessageBox.Show(this, summary, "File Renamer \u2014 Completed with errors", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(this, summary, "File Renamer \u2014 Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
