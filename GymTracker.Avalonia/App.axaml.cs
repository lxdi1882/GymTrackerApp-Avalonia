using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GymTracker.Avalonia.Services;

namespace GymTracker.Avalonia;

public partial class App : Application
{
    // Shared services (simple for now, swap for DI later)
    public static DatabaseService Db { get; } = new();
    public static WorkoutService Workouts { get; } = new(Db);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = Db.SeedExercisesAsync();
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
