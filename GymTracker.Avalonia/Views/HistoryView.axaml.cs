using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Views;

public class HistoryMonth
{
    public DateTime Start { get; set; }
    public int Count { get; set; }
    public string Title => Start.ToString("MMMM yyyy");
    public string Subtitle => $"{Count} workout{(Count == 1 ? "" : "s")}";
}

public class HistoryDay
{
    public int SessionId { get; set; }
    public DateTime Date { get; set; }
    public string Title => Date.ToString("dddd, MMM d");
    public string Subtitle { get; set; } = "";
    public string Preview { get; set; } = "";
    public int ExerciseCount { get; set; }
    public int TotalSets { get; set; }
    public List<ExerciseLogDisplay> Exercises { get; set; } = new();
}

public partial class HistoryView : UserControl
{
    private enum Level { Months, Days, Detail }

    private Level _level = Level.Months;
    private HistoryMonth? _month;
    private HistoryDay? _day;
    private List<HistoryMonth> _months = new();
    private List<HistoryDay> _days = new();

    // months you already opened stay in memory, so going back is instant
    private readonly Dictionary<DateTime, List<HistoryDay>> _cache = new();

    private int _navToken;
    private bool _closing;

    public HistoryView()
    {
        InitializeComponent();

        BackButton.Click += async (_, _) => await GoBackAsync();
        DeleteButton.Click += (_, _) => OpenConfirm();
        CancelButton.Click += async (_, _) => await CloseConfirmAsync();
        ConfirmDeleteButton.Click += async (_, _) => await DeleteDayAsync();
        Scrim.Dismissed += async (_, _) => await CloseConfirmAsync();

        KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Escape) return;

            if (ConfirmPanel.IsVisible)
            {
                e.Handled = true;
                await CloseConfirmAsync();
            }
            else if (_level != Level.Months)
            {
                e.Handled = true;
                await GoBackAsync();
            }
        };

        Loaded += async (_, _) => await RefreshAsync();
    }

    // called every time the History tab is shown
    public async Task RefreshAsync()
    {
        try
        {
            ErrorText.IsVisible = false;
            _cache.Clear();
            await LoadMonthsAsync();

            // stay on the level you were on, if it still exists
            if (_level != Level.Months && _month is not null)
            {
                var still = _months.FirstOrDefault(m => m.Start == _month.Start);
                if (still is null)
                {
                    _level = Level.Months;
                    _month = null;
                    _day = null;
                }
                else
                {
                    _month = still;
                    _days = await LoadMonthAsync(still.Start);

                    if (_level == Level.Detail)
                    {
                        int? id = _day?.SessionId;
                        _day = _days.FirstOrDefault(d => d.SessionId == id);
                        if (_day is null) _level = Level.Days;
                    }
                }
            }

            await ApplyLevelAsync(animate: false);
        }
        catch
        {
            ErrorText.Text = "Couldn't load your history. Switch tabs to try again.";
            ErrorText.IsVisible = true;
        }
    }

    // ---------- Loading ----------

    // sessions are tiny rows (no exercises or sets), so this stays cheap
    private async Task LoadMonthsAsync()
    {
        var sessions = await App.Db.GetAllSessionsAsync();

        _months = sessions
            .GroupBy(s => new DateTime(s.Date.Year, s.Date.Month, 1))
            .OrderByDescending(g => g.Key)
            .Select(g => new HistoryMonth { Start = g.Key, Count = g.Count() })
            .ToList();

        MonthList.ItemsSource = _months;
    }

    // one month = 4 queries in total, however many workouts it has
    private async Task<List<HistoryDay>> LoadMonthAsync(DateTime monthStart)
    {
        if (_cache.TryGetValue(monthStart, out var cached)) return cached;

        var sessions = await App.Db.GetSessionsInRangeAsync(monthStart, monthStart.AddMonths(1));
        var logs = await App.Db.GetLogsForSessionsAsync(sessions.Select(s => s.Id).ToList());
        var sets = await App.Db.GetSetsForLogsAsync(logs.Select(l => l.Id).ToList());
        var names = (await App.Db.GetExercisesAsync())
            .ToDictionary(e => e.Id, e => e.Name ?? "Unknown exercise");

        var logsBySession = logs.ToLookup(l => l.WorkoutSessionId);
        var setsByLog = sets.ToLookup(s => s.ExerciseLogId);

        var days = new List<HistoryDay>();

        foreach (var session in sessions)
        {
            var exercises = logsBySession[session.Id]
                .OrderBy(l => l.Id)
                .Select(l => new ExerciseLogDisplay
                {
                    ExerciseName = names.TryGetValue(l.ExerciseId, out var n) ? n : "Unknown exercise",
                    SetLines = setsByLog[l.Id]
                        .OrderBy(s => s.Id)
                        .Select((s, i) => $"Set {i + 1}:  {s.Weight:0.##}kg x {s.Reps}")
                        .ToList()
                })
                .ToList();

            int totalSets = exercises.Sum(e => e.SetLines.Count);

            var parts = new List<string>
            {
                $"{exercises.Count} exercise{(exercises.Count == 1 ? "" : "s")}",
                $"{totalSets} set{(totalSets == 1 ? "" : "s")}"
            };
            if (session.DurationSeconds > 0)
                parts.Add(FormatDuration(session.DurationSeconds));

            days.Add(new HistoryDay
            {
                SessionId = session.Id,
                Date = session.Date,
                ExerciseCount = exercises.Count,
                TotalSets = totalSets,
                Subtitle = string.Join(", ", parts),
                Preview = string.Join(", ", exercises.Select(e => e.ExerciseName)),
                Exercises = exercises
            });
        }

        _cache[monthStart] = days;
        return days;
    }

    // ---------- Navigation ----------

    private async void OnOpenMonth(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HistoryMonth month) return;

        try
        {
            _days = await LoadMonthAsync(month.Start);
            _month = month;
            _day = null;
            _level = Level.Days;
            await ApplyLevelAsync(animate: true);
        }
        catch
        {
            ErrorText.Text = "Couldn't open that month. Try again.";
            ErrorText.IsVisible = true;
        }
    }

    private async void OnOpenDay(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not HistoryDay day) return;

        _day = day;
        _level = Level.Detail;
        await ApplyLevelAsync(animate: true);
    }

    private async Task GoBackAsync()
    {
        if (_level == Level.Detail)
        {
            _day = null;
            _level = Level.Days;
        }
        else if (_level == Level.Days)
        {
            _level = Level.Months;
        }
        else
        {
            return;
        }

        await ApplyLevelAsync(animate: true);
    }

    // shows the right level, the right header, and plays the enter animation
    private async Task ApplyLevelAsync(bool animate)
    {
        int token = ++_navToken;

        BackButton.IsVisible = _level != Level.Months;

        switch (_level)
        {
            case Level.Months:
                int total = _months.Sum(m => m.Count);
                TitleText.Text = "History";
                SummaryText.Text = total == 0 ? "" : $"{total} workout{(total == 1 ? "" : "s")} logged";
                break;

            case Level.Days:
                TitleText.Text = _month?.Title ?? "";
                SummaryText.Text = _month?.Subtitle ?? "";
                DayList.ItemsSource = _days;
                break;

            case Level.Detail:
                TitleText.Text = _day?.Title ?? "";
                SummaryText.Text = _day?.Subtitle ?? "";
                DetailList.ItemsSource = _day?.Exercises;
                break;
        }

        EmptyText.IsVisible = _level == Level.Months && _months.Count == 0;

        var target = _level switch
        {
            Level.Months => MonthsHost,
            Level.Days => DaysHost,
            _ => DetailHost
        };

        foreach (var host in new[] { MonthsHost, DaysHost, DetailHost })
        {
            if (host == target) continue;
            host.IsVisible = false;
            host.Classes.Set("off", true);   // park it in the "off" pose for next time
        }

        if (target.IsVisible) return;

        if (!animate)
        {
            target.Classes.Set("off", false);
            target.IsVisible = true;
            return;
        }

        target.Classes.Set("off", true);
        target.IsVisible = true;
        Scroller.ScrollToHome();

        await Task.Delay(16);                // let the "off" pose render first
        if (token != _navToken) return;      // another navigation took over
        target.Classes.Set("off", false);    // fade + rise, see App.axaml
    }

    // ---------- Delete ----------

    private async void OpenConfirm()
    {
        if (_day is null || _closing) return;

        ConfirmText.Text = $"{_day.Title}, with {_day.ExerciseCount} exercise" +
                           $"{(_day.ExerciseCount == 1 ? "" : "s")} and {_day.TotalSets} set" +
                           $"{(_day.TotalSets == 1 ? "" : "s")}, will be removed. This can't be undone.";

        ConfirmPanel.Classes.Set("off", true);
        ConfirmPanel.IsVisible = true;
        Scrim.IsOpen = true;

        await Task.Delay(16);
        ConfirmPanel.Classes.Set("off", false);
    }

    private async Task CloseConfirmAsync()
    {
        if (!ConfirmPanel.IsVisible || _closing) return;

        _closing = true;
        Scrim.IsOpen = false;
        ConfirmPanel.Classes.Set("off", true);

        await Task.Delay(230);   // a bit longer than the 0.22s transition in App.axaml

        ConfirmPanel.IsVisible = false;
        _closing = false;
    }

    private async Task DeleteDayAsync()
    {
        if (_day is null || _month is null) return;

        try
        {
            await App.Db.DeleteSessionAsync(_day.SessionId);
            await CloseConfirmAsync();

            _cache.Clear();
            await LoadMonthsAsync();

            var still = _months.FirstOrDefault(m => m.Start == _month.Start);
            _day = null;

            if (still is null)
            {
                _month = null;
                _level = Level.Months;
            }
            else
            {
                _month = still;
                _days = await LoadMonthAsync(still.Start);
                _level = Level.Days;
            }

            await ApplyLevelAsync(animate: true);
        }
        catch
        {
            await CloseConfirmAsync();
            ErrorText.Text = "Couldn't delete the workout. Try again.";
            ErrorText.IsVisible = true;
        }
    }

    public static string FormatDuration(int seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes:00}m";
        return t.TotalMinutes < 1 ? "<1 min" : $"{(int)t.TotalMinutes} min";
    }
}