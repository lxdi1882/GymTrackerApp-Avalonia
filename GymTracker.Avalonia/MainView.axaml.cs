using Avalonia.Controls;
using Avalonia.Media;

namespace GymTracker.Avalonia;

public partial class MainView : UserControl
{
    private readonly Control[] _hosts;

    public MainView()
    {
        InitializeComponent();

        _hosts = new Control[] { HomeHost, WorkoutHost, HistoryHost, ExercisesHost, SettingsHost };

        // first launch: let the home page ease in
        Loaded += async (_, _) =>
        {
            ApplySystemBars();
            await Task.Delay(16);
            HomeHost.Classes.Set("off", false);
        };
        ActualThemeVariantChanged += (_, _) => ApplySystemBars();

        // NavigationBar only reports selection; it doesn't switch pages for us
        NavBar.SelectionChanged += async (_, _) =>
        {
            if (NavBar.SelectedIndex >= 0)
                await ShowAsync(NavBar.SelectedIndex);
        };

        // Setting SelectedIndex switches the page through the handler above
        HomePage.StartWorkoutRequested += (_, _) => NavBar.SelectedIndex = 1;
        WorkoutPage.WorkoutFinished += (_, _) => NavBar.SelectedIndex = 0;

        _ = App.Workouts.RestoreDraftAsync();
    }

    // Paints the Android system bar (home/back buttons area) the same as the page surface.
    // On desktop InsetsManager is null, so this does nothing there.
    private void ApplySystemBars()
    {
        if (TopLevel.GetTopLevel(this)?.InsetsManager is not { } insets) return;

        if (this.TryFindResource("Md3SurfaceBrush", ActualThemeVariant, out var res)
            && res is ISolidColorBrush brush)
        {
            insets.SystemBarColor = brush.Color;
        }
    }

    private async Task ShowAsync(int index)
    {
        for (int i = 0; i < _hosts.Length; i++)
        {
            var host = _hosts[i];

            if (i != index)
            {
                host.IsVisible = false;
                host.Classes.Set("off", true);
            }
            else if (!host.IsVisible)
            {
                host.Classes.Set("off", true);
                host.IsVisible = true;
                _ = PlayEnterAsync(host);
            }
        }

        switch (index)
        {
            case 0: await HomePage.RefreshAsync(); break;
            case 1: await WorkoutPage.LoadAsync(); break;
            case 2: await HistoryPage.RefreshAsync(); break;
            case 3: await ExercisesPage.RefreshAsync(); break;
            case 4: await SettingsPage.LoadAsync(); break;
        }
    }

    private static async Task PlayEnterAsync(Control host)
    {
        await Task.Delay(16);
        host.Classes.Set("off", false);
    }
}