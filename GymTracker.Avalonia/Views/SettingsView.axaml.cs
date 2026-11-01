using Avalonia.Controls;

namespace GymTracker.Avalonia.Views;

public partial class SettingsView : UserControl
{
    private const int MinGoal = 1;
    private const int MaxGoal = 7;

    private int _goal = 3;
    private readonly SemaphoreSlim _saveLock = new(1, 1);   // keeps goal writes in tap order

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

        // Update the screen right away so the tap feels instant, and so fast taps stack
        // (before, two quick taps both started from the same value and one got lost).
        int previous = _goal;
        _goal = goal;
        ErrorText.IsVisible = false;
        UpdateUi();

        await _saveLock.WaitAsync();   // one write at a time, always the latest value
        try
        {
            await App.Db.SetWeeklyGoalAsync(_goal);
        }
        catch
        {
            _goal = previous;          // couldn't save, so show what is actually stored
            UpdateUi();
            ErrorText.Text = "Couldn't save your goal. Try again.";
            ErrorText.IsVisible = true;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void UpdateUi()
    {
        GoalValue.Text = _goal.ToString();
        GoalMinus.IsEnabled = _goal > MinGoal;
        GoalPlus.IsEnabled = _goal < MaxGoal;
    }
}
