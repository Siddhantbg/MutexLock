using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using FolderLock.Core.Data;
using FolderLock.Core.Security;
using FolderLock.Core.Services;
using Microsoft.Win32;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace FolderLock.App;

public partial class MainWindow : FluentWindow
{
    private static readonly TimeSpan BusyRevealDelay = TimeSpan.FromMilliseconds(250);
    private static readonly Duration BusyFadeDuration = new(TimeSpan.FromMilliseconds(200));
    private const double BusyBlurRadius = 10;

    private readonly ObservableCollection<FolderItem> _items = new();
    private readonly DispatcherTimer _watchdog;
    private readonly ICollectionView _view;
    private readonly AutoLockManager _autoLock;
    private string _filter = "All";
    private string _search = string.Empty;
    private CancellationTokenSource? _operationCts;

    public MainWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = FilterItem;
        FolderList.ItemsSource = _view;

        Closing += OnClosing;
        StartupMenuItem.IsChecked = StartupRegistration.IsEnabled();
        ApplyWindowSettings();
        UpdateThemeChecks();
        UpdateLanguageChecks();
        UpdateAutoLockChecks();
        _ = UpdateWindowsHelloAsync();
        Reload();

        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _watchdog.Tick += (_, _) => RunWatchdog();
        _watchdog.Start();

        _autoLock = new AutoLockManager(this);
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            _autoLock.Attach(handle);
        };
    }

    public void LockAllUnlocked(string reason)
    {
        Task.Run(() =>
        {
            var locked = AppServices.Folders.LockAll(AppServices.Keyring);
            if (locked.Count == 0)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                Reload();
                UpdateStatus($"Auto-locked {locked.Count} folder(s) ({reason}).");
                Tray?.ShowBalloon("FolderLock", $"{reason}: auto-locked {locked.Count} folder(s).");
            });
        });
    }

    public void LockFolderSilently(long id, string reason)
    {
        Task.Run(() =>
        {
            try
            {
                if (!AppServices.Keyring.TryGet(id, out var secret))
                {
                    return;
                }

                AppServices.Folders.Lock(id, secret);
                AppServices.Keyring.Remove(id);

                Dispatcher.Invoke(() =>
                {
                    Reload(id);
                    UpdateStatus($"Automatically re-locked ({reason}).");
                    Tray?.ShowBalloon("FolderLock", "Temporary unlock expired; folder re-locked.");
                });
            }
            catch
            {
            }
        });
    }

    public void PanicLock()
    {
        try
        {
            Clipboard.Clear();
        }
        catch
        {
        }

        WindowState = WindowState.Minimized;
        LockAllUnlocked("Panic lock");
    }

    private void RunWatchdog()
    {
        try
        {
            if (AppServices.Folders.EnforceLocks())
            {
                Reload();
                UpdateStatus("Restored a lock that was modified externally.");
            }
        }
        catch
        {
        }
    }

    public bool ExitRequested { get; set; }

    public TrayIcon? Tray { get; set; }

    private FolderItem? Selected => FolderList.SelectedItem as FolderItem;

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveWindowSettings();

        if (ExitRequested)
        {
            _autoLock.Dispose();
            AppServices.Keyring.Clear();
            return;
        }

        e.Cancel = true;
        Hide();
        Tray?.ShowMinimizedNotice();
    }

    private void ApplyWindowSettings()
    {
        var settings = AppSettings.Current;

        if (settings.WindowWidth >= MinWidth)
        {
            Width = settings.WindowWidth;
        }

        if (settings.WindowHeight >= MinHeight)
        {
            Height = settings.WindowHeight;
        }

        if (!double.IsNaN(settings.WindowLeft) && !double.IsNaN(settings.WindowTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
        }

        if (settings.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveWindowSettings()
    {
        var settings = AppSettings.Current;

        if (WindowState == WindowState.Maximized)
        {
            settings.WindowMaximized = true;
        }
        else
        {
            settings.WindowMaximized = false;
            settings.WindowWidth = ActualWidth;
            settings.WindowHeight = ActualHeight;
            settings.WindowLeft = Left;
            settings.WindowTop = Top;
        }

        settings.Save();
    }

    private void UpdateThemeChecks()
    {
        var theme = AppSettings.Current.Theme;
        ThemeSystemItem.IsChecked = theme == "System";
        ThemeDarkItem.IsChecked = theme == "Dark";
        ThemeLightItem.IsChecked = theme == "Light";
    }

    private void UpdateLanguageChecks()
    {
        var language = AppSettings.Current.Language;
        LangSystemItem.IsChecked = language == "System";
        LangZhItem.IsChecked = language == "中文";
        LangEnItem.IsChecked = language == "English";
    }

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string language })
        {
            return;
        }

        L.SetLanguage(language);
        UpdateLanguageChecks();

        var choice = MessageBox.Show(
            this,
            L.Current == "en" ? "Restart to apply the language?" : "需要重启以应用语言，是否立即重启？",
            "FolderLock",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Yes)
        {
            RestartApplication();
        }
    }

    private void RestartApplication()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
            }
        }
        catch
        {
        }

        ExitRequested = true;
        Application.Current.Shutdown();
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string theme })
        {
            AppSettings.Current.Theme = theme;
            AppSettings.Current.Save();
            ThemeService.Apply(theme);
            UpdateThemeChecks();
        }
    }

    private void Reload(long? selectId = null)
    {
        _items.Clear();
        foreach (var record in AppServices.Folders.GetAll())
        {
            _items.Add(new FolderItem(record));
        }

        _view.Refresh();

        if (selectId is { } id)
        {
            var match = _items.FirstOrDefault(item => item.Id == id);
            if (match is not null)
            {
                FolderList.SelectedItem = match;
            }
        }

        UpdateStatus();
        UpdateDetail();
    }

    private void UpdateStatus(string? message = null)
    {
        var locked = _items.Count(item => item.IsLocked);
        StatusText.Text = message ?? L.T("Status.Ready");
        CountText.Text = L.Format("Status.Count", _items.Count, locked);
        Tray?.UpdateStatus(_items.Count, locked);
        UpdateNavigation();
    }

    private void UpdateNavigation()
    {
        var total = _items.Count;
        var locked = _items.Count(item => item.IsLocked);
        var encrypted = _items.Count(item => item.IsEncrypted);

        NavAllCount.Text = total.ToString();
        NavLockedCount.Text = locked.ToString();
        NavUnlockedCount.Text = (total - locked).ToString();
        NavEncryptedCount.Text = encrypted.ToString();

        var visible = _items.Count(FilterItem);
        PageTitle.Text = _filter switch
        {
            "Locked" => L.T("Page.Locked"),
            "Unlocked" => L.T("Page.Unlocked"),
            "Encrypted" => L.T("Page.Encrypted"),
            _ => L.T("Page.All"),
        };
        PageSubtitle.Text = L.Format("Page.Count", visible);

        var empty = visible == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        FolderList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
    }

    private bool FilterItem(object obj)
    {
        if (obj is not FolderItem item)
        {
            return false;
        }

        var navMatch = _filter switch
        {
            "Locked" => item.IsLocked,
            "Unlocked" => !item.IsLocked,
            "Encrypted" => item.IsEncrypted,
            _ => true,
        };

        if (!navMatch)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_search))
        {
            return true;
        }

        var query = _search.Trim();
        return item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Path.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateDetail()
    {
        var item = Selected;
        if (item is null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DetailPanel.Visibility = Visibility.Visible;
        DetailName.Text = item.DisplayName;
        DetailPath.Text = item.Path;
        DetailStatus.Text = item.StatusText;
        DetailMode.Text = item.ModeText;
        DetailLast.Text = item.LastLockedText;
        DetailIcon.Symbol = item.IsLocked
            ? SymbolRegular.LockClosed24
            : SymbolRegular.LockOpen24;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag })
        {
            _filter = tag;
            _view.Refresh();
            UpdateNavigation();
            UpdateDetail();
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _search = SearchBox.Text ?? string.Empty;
        _view.Refresh();
        UpdateNavigation();
    }

    private void FolderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDetail();
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = MoreButton;
            menu.IsOpen = true;
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder to lock",
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var passwordDialog = new PasswordDialog(
            L.T("Dialog.SetPassword"),
            "Set a password for this folder:",
            requireConfirmation: true,
            showEncryptionOption: true)
        {
            Owner = this,
        };

        if (passwordDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var secret = passwordDialog.GetSecret();
            var record = AppServices.Folders.Add(
                dialog.FolderName,
                secret,
                passwordDialog.UseEncryption,
                out var recoveryCode);

            if (recoveryCode is not null)
            {
                ShowRecoveryCode(recoveryCode);
            }

            Reload(record.Id);
            UpdateStatus("Folder added.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ShowRecoveryCode(string code)
    {
        new RecoveryCodeDialog(code) { Owner = this }.ShowDialog();
    }

    private async void Lock_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item is null)
        {
            WarnSelectFolder();
            return;
        }

        if (item.IsLocked)
        {
            ShowInfo("This folder is already locked.");
            return;
        }

        var dialog = new PasswordDialog(L.T("Dialog.Lock"), $"Enter the password for \"{item.DisplayName}\" to lock it:") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var secret = dialog.GetSecret();

        if (item.IsEncrypted)
        {
            await RunVaultOperationAsync(
                "Encrypting",
                (progress, token) => AppServices.Folders.LockAsync(item.Id, secret, progress, token));
            Reload(item.Id);
            ReportLockResult(item);
            return;
        }

        try
        {
            await RunBusyAsync("Locking", () => AppServices.Folders.Lock(item.Id, secret));
            Reload(item.Id);
            ReportLockResult(item);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void RememberSecret(long id, Secret secret)
    {
        if (AppSettings.Current.CachePasswords)
        {
            AppServices.Keyring.Store(id, secret);
        }

        _autoLock.NoteUnlocked(id);
    }

    private void ReportLockResult(FolderItem item)
    {
        AppServices.Keyring.Remove(item.Id);
        _autoLock.NoteLocked(item.Id);

        if (AppServices.Folders.LastTraceClean is { } clean)
        {
            UpdateStatus(
                $"Encrypted and cleaned traces: {clean.ShortcutsRemoved} shortcut(s), {clean.RegistryValuesRemoved} registry value(s), {clean.CacheFilesRemoved} cache file(s).");
        }
        else
        {
            UpdateStatus($"Locked: {item.DisplayName}");
        }
    }

    private async void Unlock_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item is null)
        {
            WarnSelectFolder();
            return;
        }

        if (!item.IsLocked)
        {
            ShowInfo("This folder is not locked.");
            return;
        }

        if (await TryUnlockAsync(item))
        {
            Reload(item.Id);
            UpdateStatus($"Unlocked: {item.DisplayName}");
            TryOpen(item);
        }
    }

    private async Task<bool> TryUnlockAsync(FolderItem item)
    {
        var prompt = item.IsEncrypted
            ? $"Enter the password or recovery code for \"{item.DisplayName}\" to unlock it:"
            : $"Enter the password for \"{item.DisplayName}\" to unlock it:";
        var dialog = new PasswordDialog(L.T("Dialog.Unlock"), prompt) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        using var secret = dialog.GetSecret();

#if !FOLDERLOCK_NO_HELLO
        if (AppSettings.Current.WindowsHello && await WindowsHello.IsAvailableAsync())
        {
            var message = L.Current == "en" ? "Verify to unlock" : "请验证以解锁";
            if (!await WindowsHello.VerifyAsync(message))
            {
                UpdateStatus(L.Current == "en" ? "Windows Hello verification failed." : "Windows Hello 验证未通过。");
                return false;
            }
        }
#endif

        if (item.IsEncrypted)
        {
            var success = false;
            await RunVaultOperationAsync("Decrypting", async (progress, token) =>
            {
                await AppServices.Folders.UnlockAsync(item.Id, secret, progress, token);
                success = true;
            });

            if (success)
            {
                RememberSecret(item.Id, secret);
            }

            return success;
        }

        try
        {
            await RunBusyAsync("Unlocking", () => AppServices.Folders.Unlock(item.Id, secret));
            RememberSecret(item.Id, secret);
            return true;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
    }

    private async Task RunVaultOperationAsync(
        string title,
        Func<IProgress<VaultProgress>, CancellationToken, Task> operation)
    {
        _operationCts = new CancellationTokenSource();
        CancelButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Visible;
        ProgressText.Text = $"{title}…";
        SetBusy(true);

        var progress = new Progress<VaultProgress>(value =>
        {
            ProgressText.Text = $"{title}… {value.Percent:0}%";
        });

        try
        {
            await operation(progress, _operationCts.Token);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Operation cancelled.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(false);
            _operationCts.Dispose();
            _operationCts = null;
        }
    }

    private async Task RunBusyAsync(string title, Action work)
    {
        CancelButton.Visibility = Visibility.Collapsed;
        ProgressText.Text = $"{title}…";
        SetBusy(true);

        try
        {
            await Task.Run(work);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        // The watchdog would re-lock a folder whose ACL is restored before its record is updated.
        if (busy)
        {
            _watchdog.Stop();
        }
        else
        {
            _watchdog.Start();
        }

        foreach (var pane in new UIElement[] { NavPane, ContentPane, DetailPanel })
        {
            pane.IsEnabled = !busy;

            if (busy)
            {
                var blur = new BlurEffect { Radius = 0, KernelType = KernelType.Gaussian };
                pane.Effect = blur;
                blur.BeginAnimation(
                    BlurEffect.RadiusProperty,
                    new DoubleAnimation(0, BusyBlurRadius, BusyFadeDuration) { BeginTime = BusyRevealDelay });
            }
            else
            {
                pane.Effect = null;
            }
        }

        if (busy)
        {
            ProgressOverlay.Opacity = 0;
            ProgressOverlay.Visibility = Visibility.Visible;
            ProgressOverlay.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, BusyFadeDuration) { BeginTime = BusyRevealDelay });
        }
        else
        {
            ProgressOverlay.BeginAnimation(OpacityProperty, null);
            ProgressOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        _operationCts?.Cancel();
        CancelButton.IsEnabled = false;
        ProgressText.Text = "Cancelling…";
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item is null)
        {
            WarnSelectFolder();
            return;
        }

        var dialog = new ChangePasswordDialog(L.T("Dialog.ChangePassword")) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var current = dialog.GetCurrentSecret();
            using var next = dialog.GetNewSecret();
            AppServices.Folders.ChangePassword(item.Id, current, next);
            UpdateStatus("Password updated.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ViewRecovery_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item is null)
        {
            WarnSelectFolder();
            return;
        }

        if (!item.IsEncrypted)
        {
            ShowInfo("This folder is not encrypted, so it has no recovery code.");
            return;
        }

        try
        {
            var code = AppServices.Folders.GetRecoveryCode(item.Id);
            if (code is null)
            {
                ShowInfo("Cannot read the recovery code (it may have been created by another Windows account).");
                return;
            }

            ShowRecoveryCode(code);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item is null)
        {
            WarnSelectFolder();
            return;
        }

        var dialog = new PasswordDialog(L.T("Dialog.Remove"), $"Enter the password for \"{item.DisplayName}\" to remove it from the list (files will not be deleted):") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            using var secret = dialog.GetSecret();
            AppServices.Folders.Remove(item.Id, secret);
            Reload();
            UpdateStatus("Removed.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item is null)
        {
            WarnSelectFolder();
            return;
        }

        if (item.IsLocked)
        {
            if (await TryUnlockAsync(item))
            {
                Reload(item.Id);
                TryOpen(item);
            }

            return;
        }

        TryOpen(item);
    }

    private void FolderList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Open_Click(sender, e);
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppServices.Folders.SyncLockStates();
            Reload();
            UpdateStatus("Refreshed.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void FolderList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void FolderList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
        {
            return;
        }

        var folders = paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (folders.Count == 0)
        {
            return;
        }

        var passwordDialog = new PasswordDialog(
            L.T("Dialog.SetPassword"),
            $"Set one password for the {folders.Count} dropped folder(s):",
            requireConfirmation: true,
            showEncryptionOption: true)
        {
            Owner = this,
        };

        if (passwordDialog.ShowDialog() != true)
        {
            return;
        }

        using var sharedSecret = passwordDialog.GetSecret();
        var added = 0;
        var failures = new List<string>();
        var recoveryCodes = new List<string>();
        foreach (var folder in folders)
        {
            try
            {
                AppServices.Folders.Add(
                    folder,
                    sharedSecret,
                    passwordDialog.UseEncryption,
                    out var recoveryCode);

                if (recoveryCode is not null)
                {
                    recoveryCodes.Add(recoveryCode);
                }

                added++;
            }
            catch (Exception ex)
            {
                failures.Add($"{folder}：{ex.Message}");
            }
        }

        foreach (var code in recoveryCodes)
        {
            ShowRecoveryCode(code);
        }

        Reload();
        if (failures.Count > 0)
        {
            MessageBox.Show(
                this,
                $"Added {added} folder(s), {failures.Count} failed:\n\n{string.Join("\n", failures)}",
                "FolderLock",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            UpdateStatus($"Added {added} folder(s).");
        }
    }

    private void TryOpen(FolderItem item)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.Path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    public void HandleStartupArgs(string[] args)
    {
        var (verb, path) = ParseCommand(args);
        if (verb is not null && path is not null)
        {
            HandleExternalCommand(verb, path);
        }
    }

    public static (string? Verb, string? Path) ParseCommand(string[] args)
    {
        if (args.Length == 0)
        {
            return (null, null);
        }

        if (args.Length == 1)
        {
            return ("add", args[0]);
        }

        var first = args[0];
        if (first.StartsWith("--", StringComparison.Ordinal))
        {
            return (first[2..].ToLowerInvariant(), args[1]);
        }

        return ("add", first);
    }

    public void HandleExternalCommand(string verb, string path)
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Reload();

        try
        {
            switch (verb)
            {
                case "add":
                    ExternalAdd(path);
                    break;
                case "lock":
                    ExternalLock(path);
                    break;
                case "unlock":
                    ExternalUnlock(path);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ExternalAdd(string path)
    {
        var normalized = NormalizeExisting(path);
        if (normalized is null)
        {
            return;
        }

        if (AppServices.Folders.GetByPath(normalized) is not null)
        {
            ShowInfo("This folder is already in the list.");
            return;
        }

        var dialog = new PasswordDialog(
            L.T("Dialog.SetPassword"),
            $"Set a password for \"{Path.GetFileName(normalized)}\":",
            requireConfirmation: true,
            showEncryptionOption: true)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var secret = dialog.GetSecret();
        var record = AppServices.Folders.Add(
            normalized,
            secret,
            dialog.UseEncryption,
            out var recoveryCode);

        if (recoveryCode is not null)
        {
            ShowRecoveryCode(recoveryCode);
        }

        Reload(record.Id);
        UpdateStatus("Folder added.");
    }

    private void ExternalLock(string path)
    {
        var normalized = NormalizeExisting(path);
        if (normalized is null)
        {
            return;
        }

        var record = AppServices.Folders.GetByPath(normalized);
        if (record is null)
        {
            var create = new PasswordDialog(
                L.T("Dialog.SetPassword"),
                $"Set a password for \"{Path.GetFileName(normalized)}\":",
                requireConfirmation: true,
                showEncryptionOption: true)
            {
                Owner = this,
            };

            if (create.ShowDialog() != true)
            {
                return;
            }

            using var createdSecret = create.GetSecret();
            record = AppServices.Folders.Add(
                normalized,
                createdSecret,
                create.UseEncryption,
                out var recoveryCode);

            if (recoveryCode is not null)
            {
                ShowRecoveryCode(recoveryCode);
            }

            AppServices.Folders.Lock(record.Id, createdSecret);
            Reload(record.Id);
            UpdateStatus("Locked.");
            return;
        }

        if (record.IsLocked)
        {
            ShowInfo("This folder is already locked.");
            return;
        }

        var dialog = new PasswordDialog(L.T("Dialog.Lock"), $"Enter the password for \"{record.DisplayName}\" to lock it:") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var secret = dialog.GetSecret();
        AppServices.Folders.Lock(record.Id, secret);
        Reload(record.Id);
        UpdateStatus("Locked.");
    }

    private void ExternalUnlock(string path)
    {
        var normalized = NormalizeExisting(path);
        if (normalized is null)
        {
            return;
        }

        var record = AppServices.Folders.GetByPath(normalized);
        if (record is null)
        {
            ShowInfo("This folder is not in the list.");
            return;
        }

        if (!record.IsLocked)
        {
            ShowInfo("This folder is not locked.");
            return;
        }

        var dialog = new PasswordDialog(L.T("Dialog.Unlock"), $"Enter the password for \"{record.DisplayName}\" to unlock it:") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var secret = dialog.GetSecret();
        AppServices.Folders.Unlock(record.Id, secret);
        Reload(record.Id);
        UpdateStatus("Unlocked.");
    }

    private string? NormalizeExisting(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!Directory.Exists(full))
        {
            ShowInfo($"Folder does not exist: {full}");
            return null;
        }

        return full;
    }

    private void InstallShell_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Install();
            ShowInfo("Context menu registered.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void UninstallShell_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellIntegration.Uninstall();
            ShowInfo("Context menu removed.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ToggleStartup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (StartupMenuItem.IsChecked)
            {
                StartupRegistration.Enable();
                ShowInfo("Start with Windows enabled (starts minimized to the tray).");
            }
            else
            {
                StartupRegistration.Disable();
                ShowInfo("Start with Windows disabled.");
            }
        }
        catch (Exception ex)
        {
            StartupMenuItem.IsChecked = StartupRegistration.IsEnabled();
            ShowError(ex);
        }
    }

    private void UpdateAutoLockChecks()
    {
        var settings = AppSettings.Current;
        AutoLockSessionItem.IsChecked = settings.AutoLockOnSessionLock;
        AutoLockIdleItem.IsChecked = settings.AutoLockIdleMinutes > 0;
        AutoRelockItem.IsChecked = settings.AutoRelockMinutes > 0;
        PanicHotkeyItem.IsChecked = settings.PanicHotkeyEnabled;
    }

    private void AutoLockSession_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.AutoLockOnSessionLock = AutoLockSessionItem.IsChecked;
        AppSettings.Current.Save();
    }

    private void AutoLockIdle_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.AutoLockIdleMinutes = AutoLockIdleItem.IsChecked ? 15 : 0;
        AppSettings.Current.Save();
    }

    private void AutoRelock_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.AutoRelockMinutes = AutoRelockItem.IsChecked ? 10 : 0;
        AppSettings.Current.Save();
    }

    private void PanicHotkey_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Current.PanicHotkeyEnabled = PanicHotkeyItem.IsChecked;
        AppSettings.Current.Save();

        if (PanicHotkeyItem.IsChecked)
        {
            _autoLock.RegisterHotkey();
        }
        else
        {
            _autoLock.UnregisterHotkey();
        }
    }

    private async Task UpdateWindowsHelloAsync()
    {
#if FOLDERLOCK_NO_HELLO
        WindowsHelloItem.Visibility = Visibility.Collapsed;
        AppSettings.Current.WindowsHello = false;
        await Task.CompletedTask;
#else
        var available = await WindowsHello.IsAvailableAsync();
        WindowsHelloItem.IsEnabled = available;

        if (!available && AppSettings.Current.WindowsHello)
        {
            AppSettings.Current.WindowsHello = false;
            AppSettings.Current.Save();
        }

        WindowsHelloItem.IsChecked = available && AppSettings.Current.WindowsHello;
#endif
    }

    private async void WindowsHello_Click(object sender, RoutedEventArgs e)
    {
#if FOLDERLOCK_NO_HELLO
        await Task.CompletedTask;
#else
        if (WindowsHelloItem.IsChecked && !await WindowsHello.IsAvailableAsync())
        {
            WindowsHelloItem.IsChecked = false;
            ShowInfo(L.Current == "en"
                ? "Windows Hello is not available on this device."
                : "此设备不支持 Windows Hello。");
            return;
        }

        AppSettings.Current.WindowsHello = WindowsHelloItem.IsChecked;
        AppSettings.Current.Save();
#endif
    }

    private void InstallService_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ServiceRegistration.InstallElevated();
            ShowInfo("Guard service install command executed.\nYou can find FolderLockGuard in the Services manager.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void UninstallService_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ServiceRegistration.UninstallElevated();
            ShowInfo("Guard service uninstall command executed.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        var url = AppSettings.Current.UpdateUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            ShowInfo(L.Current == "en"
                ? "No update source configured. Set UpdateUrl in settings.json."
                : "未配置更新源。请在 settings.json 中设置 UpdateUrl。");
            return;
        }

        try
        {
            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
            var manifest = await FolderLock.Core.Update.Updater.CheckAsync(url, current);

            if (manifest is null)
            {
                ShowInfo(L.Current == "en" ? "You are on the latest version." : "已是最新版本。");
                return;
            }

            var choice = MessageBox.Show(
                this,
                $"{(L.Current == "en" ? "New version" : "发现新版本")} {manifest.Version}\n{manifest.Notes}",
                "FolderLock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (choice != MessageBoxResult.Yes)
            {
                return;
            }

            var target = Path.Combine(Path.GetTempPath(), "FolderLock.App.new.exe");
            await FolderLock.Core.Update.Updater.DownloadAsync(manifest, target);
            ApplyUpdate(target);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ApplyUpdate(string newExecutable)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrEmpty(current))
        {
            return;
        }

        var script = Path.Combine(Path.GetTempPath(), $"folderlock-update-{Guid.NewGuid():N}.cmd");
        var content =
            "@echo off\r\n" +
            "timeout /t 1 /nobreak >nul\r\n" +
            $"copy /y \"{newExecutable}\" \"{current}\" >nul\r\n" +
            $"start \"\" \"{current}\"\r\n" +
            "del \"%~f0\"\r\n";

        File.WriteAllText(script, content);

        Process.Start(new ProcessStartInfo
        {
            FileName = script,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });

        ExitRequested = true;
        Application.Current.Shutdown();
    }

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        new LogsWindow { Owner = this }.ShowDialog();
    }

    private void VaultTools_Click(object sender, RoutedEventArgs e)
    {
        var item = Selected;
        if (item is null)
        {
            WarnSelectFolder();
            return;
        }

        if (!item.IsEncrypted)
        {
            ShowInfo("This folder is not encrypted, so there is no vault to verify.");
            return;
        }

        var dialog = new PasswordDialog(L.T("Dialog.Verify"), $"Enter the password or recovery code for \"{item.DisplayName}\":") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var secret = dialog.GetSecret();
        try
        {
            var info = AppServices.Folders.InspectVault(item.Id);
            if (AppServices.Folders.VerifyVault(item.Id, secret))
            {
                ShowInfo(
                    $"Vault is intact.\nVersion: {info.Version}\nKDF iterations: {info.Iterations}\nHas recovery code: {(info.HasRecovery ? "Yes" : "No")}\nSize: {info.Size / 1024.0 / 1024.0:0.0} MB");
                return;
            }

            var choice = MessageBox.Show(
                this,
                "Vault verification failed; it may be corrupted. Try to salvage recoverable content?",
                "FolderLock",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes)
            {
                return;
            }

            var folderDialog = new OpenFolderDialog { Title = "Choose salvage output location" };
            if (folderDialog.ShowDialog(this) != true)
            {
                return;
            }

            var output = Path.Combine(folderDialog.FolderName, item.DisplayName + "-salvaged");
            var result = AppServices.Folders.SalvageVault(item.Id, secret, output);

            ShowInfo(result.Success
                ? $"Salvage complete. Contents restored to:\n{output}"
                : $"Salvage may be incomplete: {result.Error}\nAttempted output to:\n{output}");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ExportKit_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordDialog(L.T("Dialog.ExportKit"), "Set a passphrase to protect the recovery kit:", requireConfirmation: true)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var secret = dialog.GetSecret();

        var save = new SaveFileDialog
        {
            Title = "Export recovery kit",
            FileName = $"FolderLock-Kit-{DateTime.Now:yyyyMMdd-HHmm}.flkit",
            Filter = "FolderLock recovery kit (*.flkit)|*.flkit|All files (*.*)|*.*",
        };

        if (save.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var count = AppServices.Folders.ExportKit(save.FileName, secret);
            ShowInfo($"Exported {count} item(s) to:\n{save.FileName}");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ImportKit_Click(object sender, RoutedEventArgs e)
    {
        var open = new OpenFileDialog
        {
            Title = "Import recovery kit",
            Filter = "FolderLock recovery kit (*.flkit)|*.flkit|All files (*.*)|*.*",
        };

        if (open.ShowDialog(this) != true)
        {
            return;
        }

        var dialog = new PasswordDialog(L.T("Dialog.ImportKit"), "Enter the recovery kit passphrase:") { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        using var secret = dialog.GetSecret();

        try
        {
            var count = AppServices.Folders.ImportKit(open.FileName, secret);
            Reload();
            ShowInfo($"Imported {count} item(s).");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void OpenDataDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = Path.GetDirectoryName(FolderStore.GetDefaultDatabasePath())!;
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void SafetyInfo_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "FolderLock offers two locking modes:\n\n" +
            "[ACL lock] Based on NTFS permissions; locking and unlocking are instant.\n" +
            "• Blocks other standard users on this PC.\n" +
            "• Does not stop administrators, the SYSTEM account, or Safe Mode.\n" +
            "• If you forget the password, permissions can be restored manually from the folder's Properties > Security tab.\n\n" +
            "[AES encryption] Encrypts the folder into a .flvault container with AES-256-GCM.\n" +
            "• Cannot be decrypted without the password, even if copied or accessed offline.\n" +
            "• If you forget the password, the recovery code can unlock it. Keep a separate copy.\n" +
            "• When locking, source files are overwritten with random data before deletion, and recent items,\n" +
            "  jump lists, common dialog MRU lists and thumbnail caches are cleaned.\n" +
            "• Note: SSD TRIM/wear-levelling, the page file and the search index may still\n" +
            "  retain copies of data that this tool cannot remove. Use full-disk encryption as well.\n" +
            "• Locking and unlocking take time; please be patient with large folders.",
            "Security notes",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ShowInfo(string message)
    {
        MessageBox.Show(this, message, "FolderLock", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void WarnSelectFolder()
    {
        MessageBox.Show(this, "Please select a folder in the list first.", "FolderLock", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowError(Exception ex)
    {
        var message = ex is FolderLockException ? ex.Message : $"Operation failed: {ex.Message}";
        MessageBox.Show(this, message, "FolderLock", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
