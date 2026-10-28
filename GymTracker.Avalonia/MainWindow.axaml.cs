using Avalonia.Controls;

namespace GymTracker.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        HomePage.StartWorkoutRequested += (_, _) => Tabs.SelectedIndex = 1;
        WorkoutPage.WorkoutFinished += (_, _) => Tabs.SelectedIndex = 0;

        Tabs.SelectionChanged += async (_, e) =>
        {
            // ignore SelectionChanged bubbling up from inner controls (e.g. the exercise ComboBox)
            if (e.Source == Tabs && Tabs.SelectedIndex == 0)
                await HomePage.RefreshAsync();
        };
    }
}