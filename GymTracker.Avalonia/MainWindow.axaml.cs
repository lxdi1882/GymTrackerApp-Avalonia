using Avalonia.Controls;

namespace GymTracker.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        NavHome.IsCheckedChanged += async (_, _) => { if (NavHome.IsChecked == true) await ShowAsync(0); };
        NavWorkout.IsCheckedChanged += async (_, _) => { if (NavWorkout.IsChecked == true) await ShowAsync(1); };
        NavHistory.IsCheckedChanged += async (_, _) => { if (NavHistory.IsChecked == true) await ShowAsync(2); };

        // Setting IsChecked switches the page through the handlers above
        HomePage.StartWorkoutRequested += (_, _) => NavWorkout.IsChecked = true;
        WorkoutPage.WorkoutFinished += (_, _) => NavHome.IsChecked = true;
    }

    private async Task ShowAsync(int index)
    {
        HomePage.IsVisible = index == 0;
        WorkoutPage.IsVisible = index == 1;
        HistoryPage.IsVisible = index == 2;

        switch (index)
        {
            case 0: await HomePage.RefreshAsync(); break;
            case 1: await WorkoutPage.LoadAsync(); break;
            case 2: await HistoryPage.RefreshAsync(); break;
        }
    }
}
