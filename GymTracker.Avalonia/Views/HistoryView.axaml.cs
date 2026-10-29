using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GymTracker.Avalonia.Models;
 
namespace GymTracker.Avalonia.Views;
 
public class HistorySession : INotifyPropertyChanged
{
    public int SessionId { get; set; }
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public List<ExerciseLogDisplay> Exercises { get; set; } = new();
 
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(Chevron)); }
    }
 
    public string Chevron => IsExpanded ? "▲" : "▼";
 
    private bool _confirmDelete;
    public bool ConfirmDelete
    {
        get => _confirmDelete;
        set { _confirmDelete = value; OnPropertyChanged(); OnPropertyChanged(nameof(DeleteLabel)); }
    }
 
    public string DeleteLabel => ConfirmDelete ? "Tap again to delete" : "Delete workout";
 
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
 
public partial class HistoryView : UserControl
{
    private readonly HashSet<int> _expandedIds = new();
 
    public HistoryView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }
 
    public async Task RefreshAsync()
    {
        try
        {
            ErrorText.IsVisible = false;
            var sessions = (await App.Db.GetAllSessionsAsync())
                .OrderByDescending(s => s.Date)
                .ToList();
 
            var items = new List<HistorySession>();
 
            foreach (var session in sessions)
            {
                var exercises = new List<ExerciseLogDisplay>();
                int totalSets = 0;
 
                foreach (var log in await App.Db.GetLogsForSessionAsync(session.Id))
                {
                    var exercise = await App.Db.GetExerciseByIdAsync(log.ExerciseId);
                    var sets = await App.Db.GetSetsForLogAsync(log.Id);
                    totalSets += sets.Count;
 
                    exercises.Add(new ExerciseLogDisplay
                    {
                        ExerciseName = exercise?.Name ?? "Unknown exercise",
                        SetLines = sets
                            .Select((s, i) => $"Set {i + 1}:  {s.Weight:0.##}kg x {s.Reps}")
                            .ToList()
                    });
                }
 
                var parts = new List<string>
                {
                    $"{exercises.Count} exercise{(exercises.Count == 1 ? "" : "s")}",
                    $"{totalSets} set{(totalSets == 1 ? "" : "s")}"
                };
                if (session.DurationSeconds > 0)
                    parts.Add(FormatDuration(session.DurationSeconds));
 
                items.Add(new HistorySession
                {
                    SessionId = session.Id,
                    Title = session.Date.ToString("dddd, MMM d"),
                    Subtitle = string.Join(", ", parts),
                    Exercises = exercises,
                    IsExpanded = _expandedIds.Contains(session.Id)
                });
            }
 
            SessionList.ItemsSource = items;
            EmptyText.IsVisible = items.Count == 0;
            SummaryText.Text = items.Count == 0
                ? ""
                : $"{items.Count} workout{(items.Count == 1 ? "" : "s")} logged";
        }
        catch
        {
            ErrorText.Text = "Couldn't load your history. Switch tabs to try again.";
            ErrorText.IsVisible = true;
        }
    }
 
    private void OnToggleSession(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is HistorySession s)
        {
            s.IsExpanded = !s.IsExpanded;
            if (s.IsExpanded) _expandedIds.Add(s.SessionId);
            else _expandedIds.Remove(s.SessionId);
        }
    }
 
    private async void OnDeleteSession(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HistorySession s) return;
 
        // first tap arms the button, second tap deletes (resets after 4s)
        if (!s.ConfirmDelete)
        {
            s.ConfirmDelete = true;
            await Task.Delay(4000);
            s.ConfirmDelete = false;
            return;
        }
 
        try
        {
            await App.Db.DeleteSessionAsync(s.SessionId);
            _expandedIds.Remove(s.SessionId);
            await RefreshAsync();
        }
        catch
        {
            ErrorText.Text = "Couldn't delete the workout. Try again.";
            ErrorText.IsVisible = true;
        }
    }
 
    private void OnToggleExercise(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ExerciseLogDisplay ex)
            ex.IsExpanded = !ex.IsExpanded;
    }
 
    public static string FormatDuration(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return t.TotalMinutes < 1 ? "<1 min" : $"{(int)t.TotalMinutes} min";
    }
}