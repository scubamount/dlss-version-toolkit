using System.IO;
using System.Windows;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Views;

/// <summary>
/// Pre-flight confirmation for Update All (v0.0.54).
///
/// Update All used to fire immediately on click. Local DLL import is the one step it cannot do
/// unattended — it needs a folder the user chose — so it is offered here as an opt-in rather than
/// being silently skipped or silently run. Everything else Update All does is unconditional and is
/// described, not toggled: partial runs were how the old flat sidebar produced states nobody could
/// reproduce.
/// </summary>
public partial class UpdateAllPreflightDialog : Window
{
    /// <summary>True when the user asked for the local DLL import step.</summary>
    public bool ImportLocalDlls { get; private set; }

    /// <summary>Folder to import from. Only meaningful when <see cref="ImportLocalDlls"/> is true.</summary>
    public string? ImportFolder { get; private set; }

    private string _folder;

    /// <param name="defaultImportFolder">
    /// The override library path. Pre-filled so the common case is two clicks: tick, run.
    /// </param>
    public UpdateAllPreflightDialog(string defaultImportFolder)
    {
        InitializeComponent();
        _folder = defaultImportFolder ?? "";
        FolderTextBox.Text = _folder;
    }

    private void ImportCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        FolderRow.Visibility = ImportCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        RefreshWarning();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select the folder containing nvngx_*.dll files"
        };

        if (!string.IsNullOrWhiteSpace(_folder) && Directory.Exists(_folder))
            dialog.InitialDirectory = _folder;

        if (dialog.ShowDialog() == true)
        {
            _folder = dialog.FolderName;
            FolderTextBox.Text = _folder;
            RefreshWarning();
        }
    }

    /// <summary>
    /// Tells the user BEFORE the run whether the folder actually holds importable DLLs. The import
    /// service reports an empty folder afterwards, but by then Update All has already done several
    /// minutes of work — a warning that arrives after the fact is not a warning.
    /// </summary>
    private void RefreshWarning()
    {
        if (ImportCheckBox.IsChecked != true)
        {
            ImportWarning.Visibility = Visibility.Collapsed;
            return;
        }

        string? message = null;

        if (string.IsNullOrWhiteSpace(_folder))
        {
            message = "Choose a folder to import from.";
        }
        else if (!Directory.Exists(_folder))
        {
            message = "That folder does not exist yet. Create it and put your nvngx_*.dll files in it.";
        }
        else
        {
            var present = UpgradeService.NgxDllNames
                .Where(n => File.Exists(Path.Combine(_folder, n)))
                .ToList();

            if (present.Count == 0)
                message = "No nvngx_*.dll files in that folder — the import step will be skipped.";
        }

        ImportWarning.Text = message ?? "";
        ImportWarning.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        ImportLocalDlls = ImportCheckBox.IsChecked == true;
        ImportFolder = ImportLocalDlls ? _folder : null;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
