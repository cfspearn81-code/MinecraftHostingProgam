using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Themes.Fluent;
using Harbor.Core;
using Harbor.Foundation;
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
        sidebar.Children.Add(new TextBlock { Text = "Server operations are currently available through the CLI.", TextWrapping = TextWrapping.Wrap, Foreground = Brush("#AABCCC"), FontSize = 12 });
        var sideBackground = new Border { Background = Brush("#111C2B"), Child = sidebar }; Grid.SetColumn(sideBackground, 0); grid.Children.Add(sideBackground);
        var main = new StackPanel { Margin = new Thickness(36), Spacing = 16 };
        main.Children.Add(new TextBlock { Text = "Your servers", FontSize = 28, FontWeight = FontWeight.SemiBold, Foreground = Brush("#111C2B") });
        main.Children.Add(new TextBlock { Text = "Local Minecraft Java servers, on your computer.", Foreground = Brush("#496174") });
        var refresh = new Button { Content = "Refresh", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 8), Background = Brush("#F28C28"), Foreground = Brush("#111C2B") };
        refresh.Click += async (_, _) => { refresh.IsEnabled = false; try { await model.RefreshAsync(); } finally { refresh.IsEnabled = true; } };
        main.Children.Add(refresh); main.Children.Add(message); main.Children.Add(cards);
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
            cards.Children.Add(new Border { Background = Brushes.White, BorderBrush = Brush("#D5E2EC"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(20), Child = content });
        }
    }
}
