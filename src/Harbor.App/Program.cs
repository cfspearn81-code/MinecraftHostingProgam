using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Harbor.Core;
using Harbor.Foundation;
using Harbor.Management;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Harbor.App;
internal static class Program
{
    [STAThread] public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
public sealed class DashboardViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<ServerDefinition> Servers { get; private set; } = Array.Empty<ServerDefinition>();
    public string Message { get; private set; } = "Loading local servers…";
    public async Task RefreshAsync()
    {
        try
        {
            var store = new ServerStore();
            Servers = await store.ListAsync();
            Message = Servers.Count == 0 ? "No servers yet. Use the Harbor CLI to create a vanilla server." : $"{Servers.Count} local server(s). Status is not monitored in this preview.";
        }
        catch (Exception ex) { Servers = Array.Empty<ServerDefinition>(); Message = $"Could not load servers: {ex.Message}"; }
        OnChanged(nameof(Servers)); OnChanged(nameof(Message));
    }
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class MainWindow : Window
{
    private readonly DashboardViewModel model = new();
    private readonly StackPanel cards = new() { Spacing = 12 };
    private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel backupPanel = new() { Spacing = 12, IsVisible = false };
    private readonly StackPanel backupActions = new() { Spacing = 12 };
    private readonly TextBlock backupStatus = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#496174") };
    private readonly ListBox backupList = new() { MinHeight = 80, MaxHeight = 200 };
    private readonly Button cancelOperation = new() { Content = "Cancel operation", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly WorldBackups backups = new(new ServerStore());
    private IReadOnlyList<WorldBackups.Backup> backupItems = Array.Empty<WorldBackups.Backup>();
    private ServerDefinition? selected;
    private CancellationTokenSource? operation;
    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
    public MainWindow()
    {
        Title = "Harbor"; Width = 1100; Height = 720; MinWidth = 650; MinHeight = 450;
        Background = Brush("#EAF4FC"); FontFamily = new FontFamily("Noto Sans, DejaVu Sans, sans-serif");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        var sidebar = new StackPanel { Margin = new Thickness(24, 32), Spacing = 14 };
        sidebar.Children.Add(new TextBlock { Text = "HARBOR", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Brushes.White });
        sidebar.Children.Add(new TextBlock { Text = "LOCAL MINECRAFT", FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = Brush("#AABCCC") });
        sidebar.Children.Add(new Border { Background = Brush("#26384B"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 30, 0, 0), Child = new TextBlock { Text = "Servers", Foreground = Brushes.White, FontWeight = FontWeight.SemiBold } });
        sidebar.Children.Add(new TextBlock { Text = "EARLY PREVIEW", Margin = new Thickness(0, 20, 0, 0), FontSize = 10, Foreground = Brush("#AABCCC") });
        sidebar.Children.Add(new TextBlock { Text = "Use the CLI to install and run servers. World backups are available here.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#AABCCC"), FontSize = 12 });
        var sideBackground = new Border { Background = Brush("#111C2B"), Child = sidebar }; Grid.SetColumn(sideBackground, 0); grid.Children.Add(sideBackground);
        var main = new StackPanel { Margin = new Thickness(36), Spacing = 16 };
        main.Children.Add(new TextBlock { Text = "Your servers", FontSize = 28, FontWeight = FontWeight.SemiBold, Foreground = Brush("#111C2B") });
        main.Children.Add(new TextBlock { Text = "Local Minecraft Java servers, on your computer.", Foreground = Brush("#496174") });
        var refresh = new Button { Content = "Refresh", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8), Background = Brush("#F28C28"), Foreground = Brush("#111C2B") };
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { await model.RefreshAsync(); } finally { refresh.IsEnabled = true; } };
        main.Children.Add(refresh); main.Children.Add(message); main.Children.Add(cards);
        main.Children.Add(backupPanel);
        Closing += (_, e) => { if (operation is not null) { e.Cancel = true; backupStatus.Text = "An operation is running. Cancel it and wait for it to finish before closing."; } };
        var scroll = new ScrollViewer { Content = main }; Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); Content = grid;
        model.PropertyChanged += (_, _) => Render();
        Opened += async (_, _) => await model.RefreshAsync();
    }
    private void Render()
    {
        message.Text = model.Message; message.Foreground = Brush("#496174"); cards.Children.Clear();
        foreach (var server in model.Servers)
        {
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(new TextBlock { Text = server.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brush("#111C2B"), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = $"Vanilla  {server.MinecraftVersion}   ·   {server.MemoryMiB} MiB heap   ·   Port {server.Port}", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#496174") });
            content.Children.Add(new TextBlock { Text = "Status unavailable in this preview", Foreground = Brush("#496174"), FontSize = 12 });
            var viewBackups = new Button { Content = "World backups", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            viewBackups.Click += async (_, _) => { selected = server; await ShowBackupsAsync(); };
            content.Children.Add(viewBackups);
            cards.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brush("#D5E2EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(20), Child = content });
        }
    }
    private async Task ShowBackupsAsync()
    {
        if (operation is not null || selected is null) return;
        var serverId = selected.Id;
        cancelOperation.Click -= CancelClicked;
        backupPanel.Children.Clear(); backupActions.Children.Clear();
        backupPanel.IsVisible = true;
        backupActions.Children.Add(new TextBlock { Text = $"World backups · {selected.Name}", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brush("#111C2B"), TextWrapping = TextWrapping.Wrap });
        backupActions.Children.Add(new TextBlock { Text = "Only the default world folder is included, not the full server. Stop the server first. Keep independent backups.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#496174") });
        backupActions.Children.Add(backupList);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        var create = new Button { Content = "Create backup", Margin = new Thickness(0, 0, 8, 8), Background = Brush("#F28C28"), Foreground = Brush("#111C2B") };
        var restore = new Button { Content = "Restore selected", Margin = new Thickness(0, 0, 8, 8) };
        var delete = new Button { Content = "Delete selected", Margin = new Thickness(0, 0, 8, 8) };
        var close = new Button { Content = "Close", Margin = new Thickness(0, 0, 8, 8) };
        buttons.Children.Add(create); buttons.Children.Add(restore); buttons.Children.Add(delete); buttons.Children.Add(close);
        backupActions.Children.Add(buttons); backupPanel.Children.Add(backupActions); backupPanel.Children.Add(backupStatus); backupPanel.Children.Add(cancelOperation);
        cancelOperation.Click += CancelClicked;
        create.Click += async (_, _) => await RunBackupOperationAsync(async ct => { var id = await backups.CreateAsync(serverId, ct); return $"Backup created: {id}"; });
        restore.Click += async (_, _) =>
        {
            if (backupList.SelectedIndex < 0 || backupList.SelectedIndex >= backupItems.Count) { backupStatus.Text = "Select a backup first."; return; }
            var id = backupItems[backupList.SelectedIndex].Id;
            if (!await ConfirmAsync("Restore world?", "This replaces the default world. Harbor will first create a recovery backup. The server must be stopped.")) return;
            await RunBackupOperationAsync(async ct => { var recovery = await backups.RestoreAsync(serverId, id, ct); return recovery is null ? "World restored." : $"World restored. Recovery backup: {recovery}"; });
        };
        delete.Click += async (_, _) =>
        {
            if (backupList.SelectedIndex < 0 || backupList.SelectedIndex >= backupItems.Count) { backupStatus.Text = "Select a backup first."; return; }
            var id = backupItems[backupList.SelectedIndex].Id;
            if (!await ConfirmAsync("Delete backup?", "This permanently deletes the selected backup. This cannot be undone.")) return;
            await RunBackupOperationAsync(async ct => { await backups.DeleteAsync(serverId, id, ct); return "Backup deleted."; });
        };
        close.Click += (_, _) => { backupPanel.IsVisible = false; selected = null; cancelOperation.Click -= CancelClicked; };
        await RefreshBackupsAsync(serverId);
    }
    private void CancelClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e) { operation?.Cancel(); cancelOperation.IsEnabled = false; backupStatus.Text = "Cancellation requested. Waiting for a safe stopping point…"; }
    private async Task RefreshBackupsAsync(Guid id)
    {
        try
        {
            backupItems = await Task.Run(() => backups.ListAsync(id));
            backupList.ItemsSource = backupItems.Select(x => $"{x.ModifiedUtc:yyyy-MM-dd HH:mm} UTC  ·  {x.Bytes:N0} bytes  ·  {x.Id}").ToArray();
            backupStatus.Text = backupItems.Count == 0 ? "No world backups yet." : $"{backupItems.Count} world backup(s).";
        }
        catch (Exception ex) { backupItems = Array.Empty<WorldBackups.Backup>(); backupList.ItemsSource = null; backupStatus.Text = ex.Message; }
    }
    private async Task RunBackupOperationAsync(Func<CancellationToken, Task<string>> action)
    {
        if (operation is not null || selected is null) return;
        var id = selected.Id; using var cancellation = new CancellationTokenSource(); operation = cancellation;
        backupActions.IsEnabled = false; cards.IsEnabled = false; cancelOperation.IsVisible = true; cancelOperation.IsEnabled = true;
        backupStatus.Text = "Working… Keep Harbor open while the operation finishes.";
        string result;
        try { result = await Task.Run(() => action(cancellation.Token)); }
        catch (OperationCanceledException) { result = "Operation cancelled."; }
        catch (Exception ex) { result = "Could not complete operation: " + ex.Message; }
        finally { operation = null; backupActions.IsEnabled = true; cards.IsEnabled = true; cancelOperation.IsVisible = false; }
        await RefreshBackupsAsync(id);
        backupStatus.Text = result;
    }
    private async Task<bool> ConfirmAsync(string title, string explanation)
    {
        var dialog = new Window { Title = title, Width = 430, Height = 205, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#EAF4FC") };
        var stack = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        stack.Children.Add(new TextBlock { Text = explanation, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#111C2B") });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var confirm = new Button { Content = "Confirm", Background = Brush("#F28C28"), Foreground = Brush("#111C2B") };
        var cancel = new Button { Content = "Cancel" };
        confirm.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        buttons.Children.Add(confirm); buttons.Children.Add(cancel); stack.Children.Add(buttons); dialog.Content = stack;
        return await dialog.ShowDialog<bool>(this);
    }
}
