using Avalonia.Controls;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Views;

public partial class HomeView : UserControl
{
    public event EventHandler? StartWorkoutRequested;

    public HomeView()
    {
        InitializeComponent();
        StartButton.Click += (_, _) => StartWorkoutRequested?.Invoke(this, EventArgs.Empty);
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        DateText.Text = DateTime.Now.ToString("dddd, MMM d");

        // Streak
        int streak = await App.Db.GetStreakAsync();
        StreakText.Text = streak.ToString();
        StreakHint.Text = streak == 0 ? "Train today to start one" : "Keep it going";

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
}