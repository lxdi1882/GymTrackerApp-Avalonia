using Avalonia.Automation;
using Avalonia.Controls;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Views;

public partial class HomeView : UserControl
{
    public event EventHandler? StartWorkoutRequested;

    private int _shownGoal;
    private int _shownFilled;
    private int _progressToken;

    public HomeView()
    {
        InitializeComponent();
        StartButton.Click += (_, _) => StartWorkoutRequested?.Invoke(this, EventArgs.Empty);
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        DateText.Text = DateTime.Now.ToString("dddd, MMM d");

        // Weekly streak
        int goal = await App.Db.GetWeeklyGoalAsync();
        var (streak, thisWeek) = await App.Db.GetWeeklyStreakAsync(goal);
        StreakText.Text = streak.ToString();
        StreakHint.Text = $"{thisWeek} {(thisWeek == 1 ? "day" : "days")} this week, goal {goal}";
        GoalCheck.IsVisible = thisWeek >= goal;
        AutomationProperties.SetName(Segments, $"{thisWeek} of {goal} training days this week");
        _ = UpdateProgressAsync(goal, thisWeek);

        // PRs: best per exercise, newest first
        var prs = await App.Db.GetRecentPRsAsync(7);
        var items = prs
            .GroupBy(p => p.Exercise.Name)
            .Select(g => g.OrderByDescending(p => p.Set.Weight).First())
            .OrderByDescending(p => p.Date)
            .Take(5)
            .Select(p => new PrDisplay
            {
                ExerciseName = p.Exercise.Name,
                ResultText = $"{p.Set.Weight:0.##}kg x {p.Set.Reps}"
            })
            .ToList();

        PrList.ItemsSource = items;
        PrEmpty.IsVisible = items.Count == 0;

        // Last workout
        var last = (await App.Workouts.GetWorkoutDaySummariesAsync()).FirstOrDefault();
        LastCard.IsVisible = last is not null;
        LastEmpty.IsVisible = last is null;

        if (last is not null)
        {
            LastTitle.Text = $"{last.DayLabel}, {last.Date:ddd MMM d}";
            LastExercises.Text = string.Join(", ", last.ExerciseNames);

            var session = (await App.Db.GetAllSessionsAsync()).FirstOrDefault(x => x.Id == last.SessionId);
            var meta = $"{last.ExerciseNames.Count} exercises, {last.TotalSets} sets";
            if (session is { DurationSeconds: > 0 })
                meta += $", {HistoryView.FormatDuration(session.DurationSeconds)}";
            LastMeta.Text = meta;
        }
    }

    // Segments light up one by one, only the newly earned ones animate.
    private async Task UpdateProgressAsync(int goal, int thisWeek)
    {
        int token = ++_progressToken;
        int filled = Math.Min(thisWeek, goal);

        if (goal != _shownGoal)
        {
            Segments.Children.Clear();
            Segments.Columns = goal;
            for (int i = 0; i < goal; i++)
                Segments.Children.Add(new Border { Classes = { "seg" } });

            _shownGoal = goal;
            _shownFilled = 0;
        }

        var segs = Segments.Children.OfType<Border>().ToList();

        // un-fill anything above the target (e.g. goal changed or workout removed)
        for (int i = filled; i < segs.Count; i++)
            segs[i].Classes.Set("filled", false);
        _shownFilled = Math.Min(_shownFilled, filled);

        // fill the new ones in sequence
        while (_shownFilled < filled)
        {
            if (token != _progressToken) return;   // a newer refresh took over

            await Task.Delay(110);
            if (token != _progressToken) return;

            segs[_shownFilled].Classes.Set("filled", true);
            _shownFilled++;
        }
    }
}