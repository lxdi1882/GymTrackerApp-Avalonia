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

        // bring back an interrupted workout first, so we know if one is in progress
        await App.Workouts.RestoreDraftAsync();

        bool inProgress = App.Workouts.HasWorkout;
        StartButton.Content = inProgress ? "Resume workout" : "Start workout";

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

        // Last workout: only the latest session is loaded, not the whole history
        var session = await App.Db.GetLatestSessionAsync();

        if (session is null)
        {
            LastCard.IsVisible = false;
            LastEmpty.IsVisible = true;
            return;
        }

        // 3 queries however many exercises the workout had (it used to be 2 per exercise)
        var logs = (await App.Db.GetLogsForSessionAsync(session.Id)).OrderBy(l => l.Id).ToList();
        var sets = await App.Db.GetSetsForLogsAsync(logs.Select(l => l.Id).ToList());
        var exerciseNames = (await App.Db.GetExercisesAsync())
            .ToDictionary(e => e.Id, e => e.Name ?? "Unknown exercise");

        var names = logs
            .Select(l => exerciseNames.TryGetValue(l.ExerciseId, out var n) ? n : "Unknown exercise")
            .ToList();

        LastTitle.Text = $"Workout, {session.Date:ddd MMM d}";
        LastExercises.Text = string.Join(", ", names);

        var meta = $"{names.Count} exercises, {sets.Count} sets";
        if (session.DurationSeconds > 0)
            meta += $", {HistoryView.FormatDuration(session.DurationSeconds)}";
        LastMeta.Text = meta;

        // show the card only once it is filled in, so it never flashes empty or stale
        LastCard.IsVisible = true;
        LastEmpty.IsVisible = false;
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