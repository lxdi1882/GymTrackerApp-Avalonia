using Avalonia.Controls;

namespace GymTracker.Avalonia.Views;

public partial class SettingsView : UserControl
{
    private const int MinGoal = 1;
    private const int MaxGoal = 7;

    private int _goal = 3;

    public SettingsView()
    {
        InitializeComponent();

        GoalMinus.Click += async (_, _) => await ChangeGoalAsync(-1);
        GoalPlus.Click += async (_, _) => await ChangeGoalAsync(1);

        Loaded += async (_, _) => await LoadAsync();
    }

    public async Task LoadAsync()
    {
        _goal = await App.Db.GetWeeklyGoalAsync();
        UpdateUi();
    }

    private async Task ChangeGoalAsync(int delta)
    {
        int goal = Math.Clamp(_goal + delta, MinGoal, MaxGoal);
        if (goal == _goal) return;

        try
        {
            ErrorText.IsVisible = false;
            await App.Db.SetWeeklyGoalAsync(goal);
            _goal = goal;
            UpdateUi();
        }
        catch
        {
            ErrorText.Text = "Couldn't save your goal. Try again.";
            ErrorText.IsVisible = true;
        }
    }

    private void UpdateUi()
    {
        GoalValue.Text = _goal.ToString();
        GoalMinus.IsEnabled = _goal > MinGoal;
        GoalPlus.IsEnabled = _goal < MaxGoal;
    }
}
