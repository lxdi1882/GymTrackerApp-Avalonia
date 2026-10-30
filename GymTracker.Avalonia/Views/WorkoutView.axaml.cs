using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Views;

public class SetRow
{
    public int Number { get; set; }
    public string Text { get; set; } = "";
    public string ExerciseName { get; set; } = "";
    public SetEntry Set { get; set; } = null!;
}

public class ExerciseBlock
{
    public string Name { get; set; } = "";
    public List<SetRow> Sets { get; set; } = new();
}

public class PickerGroup
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string CountText => $"{Count} exercise{(Count == 1 ? "" : "s")}";
}

public class PickerItem
{
    public Exercise Exercise { get; set; } = null!;
    public string Name => Exercise.Name ?? "";
    public string Detail { get; set; } = "";
    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
    public bool IsSelected { get; set; }
}

public partial class WorkoutView : UserControl
{
    private static readonly string[] GroupOrder = { "Chest", "Back", "Shoulders", "Arms", "Legs", "Core" };

    public event EventHandler? WorkoutFinished;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

    private List<Exercise> _exercises = new();
    private List<PickerGroup> _groups = new();
    private Exercise? _selected;
    private string? _openGroup;      // null = showing the muscle group list
    private bool _pickerClosing;

    public WorkoutView()
    {
        InitializeComponent();

        AddSetButton.Click += (_, _) => AddSet();
        RepsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddSet(); };
        FinishButton.Click += OnFinishClick;
        CancelButton.Click += (_, _) => ConfirmPanel.IsVisible = false;
        SaveButton.Click += OnSaveClick;
        PauseButton.Click += (_, _) =>
        {
            if (App.Workouts.IsTimerRunning) App.Workouts.PauseTimer();
            else App.Workouts.ResumeTimer();
            SyncTimer();
        };
        _timer.Tick += (_, _) => UpdateTimer();

        // exercise picker sheet
        PickerButton.Click += (_, _) => OpenPicker();
        PickerSearch.TextChanged += (_, _) => UpdatePickerView();
        PickerBack.Click += (_, _) => { _openGroup = null; UpdatePickerView(); };
        PickerClose.Click += async (_, _) => await ClosePickerAsync();
        PickerScrim.Dismissed += async (_, _) => await ClosePickerAsync();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape && PickerPanel.IsVisible)
            {
                e.Handled = true;
                await ClosePickerAsync();
            }
        };

        Loaded += async (_, _) => await LoadAsync();
    }

    public async Task LoadAsync()
    {
        await App.Workouts.RestoreDraftAsync();   // brings back an interrupted workout

        int? selectedId = _selected?.Id;

        _exercises = (await App.Db.GetExercisesAsync())
            .OrderBy(e => e.MuscleGroup)
            .ThenBy(e => e.Name)
            .ToList();

        _selected = _exercises.FirstOrDefault(e => e.Id == selectedId);

        BuildGroups();
        UpdatePickerText();
        Render();   // also resumes the timer if a workout is already running
    }

    // ---------- Exercise picker ----------

    private static string GroupKey(Exercise e) => e.MuscleGroup ?? "Other";

    private void UpdatePickerText()
    {
        bool has = _selected is not null;
        PickerText.Text = _selected?.Name ?? "Choose exercise";
        PickerText.Classes.Set("muted", !has);   // grey placeholder until an exercise is picked
        PickerSub.Text = _selected?.SubTarget ?? "";
        PickerSub.IsVisible = has && !string.IsNullOrWhiteSpace(_selected!.SubTarget);
    }

    // just a handful of rows (one per muscle group), so this is cheap
    private void BuildGroups()
    {
        _groups = _exercises
            .GroupBy(GroupKey)
            .Select(g => new PickerGroup { Name = g.Key, Count = g.Count() })
            .OrderBy(g =>
            {
                int i = Array.FindIndex(GroupOrder, x => x.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
                return i < 0 ? int.MaxValue : i;
            })
            .ThenBy(g => g.Name)
            .ToList();
    }

    // decides what the sheet shows: group list, one group's exercises, or search results
    private void UpdatePickerView()
    {
        string query = PickerSearch.Text?.Trim() ?? "";
        bool searching = query.Length > 0;
        bool showGroups = !searching && _openGroup is null;
        bool inGroup = !searching && _openGroup is not null;

        PickerBack.IsVisible = inGroup;
        PickerTitle.Text = inGroup ? _openGroup : "Choose exercise";
        PickerGroupList.IsVisible = showGroups;
        PickerList.IsVisible = !showGroups;

        if (showGroups)
        {
            PickerGroupList.ItemsSource = _groups;
            PickerEmpty.Text = "No exercises yet. Add some in the Exercises tab.";
            PickerEmpty.IsVisible = _groups.Count == 0;
            return;
        }

        IEnumerable<Exercise> source = searching
            ? _exercises.Where(e =>
                (e.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.SubTarget ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (e.MuscleGroup ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            : _exercises.Where(e => string.Equals(GroupKey(e), _openGroup, StringComparison.OrdinalIgnoreCase));

        var items = source
            .OrderBy(e => searching ? GroupKey(e) : "")
            .ThenBy(e => e.Name)
            .Select(e => new PickerItem
            {
                Exercise = e,
                IsSelected = _selected?.Id == e.Id,
                // search results mix groups, so show "Chest · Upper"
                Detail = searching
                    ? string.Join(" · ", new[] { GroupKey(e), e.SubTarget }.Where(s => !string.IsNullOrWhiteSpace(s)))
                    : e.SubTarget ?? ""
            })
            .ToList();

        PickerList.ItemsSource = items;
        PickerEmpty.Text = "No exercises match your search.";
        PickerEmpty.IsVisible = items.Count == 0;
        PickerScroll.ScrollToHome();
    }

    private async void OpenPicker()
    {
        if (_pickerClosing) return;

        // reopen on the group of the current exercise, since you usually stay in the same muscle
        _openGroup = _selected is null ? null : GroupKey(_selected);

        PickerSearch.Text = "";
        UpdatePickerView();

        // let the sheet use about half the window, so it never covers everything
        PickerScroll.MaxHeight = Math.Max(240, Bounds.Height * 0.55);

        PickerPanel.Classes.Set("off", true);
        PickerPanel.IsVisible = true;
        PickerScrim.IsOpen = true;

        // on a phone, focusing the search box pops the keyboard over the list
        if (!OperatingSystem.IsAndroid())
            PickerSearch.Focus();

        await Task.Delay(16);
        PickerPanel.Classes.Set("off", false);
    }

    private async Task ClosePickerAsync()
    {
        if (!PickerPanel.IsVisible || _pickerClosing) return;

        _pickerClosing = true;
        PickerScrim.IsOpen = false;
        PickerPanel.Classes.Set("off", true);

        await Task.Delay(300);   // a bit longer than the 0.28s transition in App.axaml

        PickerPanel.IsVisible = false;
        _pickerClosing = false;
    }

    private void OnPickGroup(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PickerGroup group) return;

        _openGroup = group.Name;
        UpdatePickerView();
    }

    private async void OnPickExercise(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not PickerItem item) return;

        _selected = item.Exercise;
        UpdatePickerText();
        ErrorText.IsVisible = false;
        await ClosePickerAsync();
        WeightBox.Focus();
    }

    // ---------- Workout ----------

    private void AddSet()
    {
        if (_selected is not Exercise exercise)
        {
            ShowError("Choose an exercise first.");
            return;
        }

        if (!TryParseWeight(WeightBox.Text, out double weight))
        {
            ShowError("Enter a weight, like 60 or 62.5.");
            return;
        }

        if (!int.TryParse(RepsBox.Text?.Trim(), out int reps) || reps <= 0)
        {
            ShowError("Enter reps as a whole number.");
            return;
        }

        ErrorText.IsVisible = false;
        App.Workouts.AddSet(exercise, weight, reps);   // also starts the timer on the first set

        RepsBox.Text = "";   // keep exercise + weight for the next set
        RepsBox.Focus();
        Render();
    }

    private void OnDeleteSet(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SetRow row)
        {
            App.Workouts.RemoveSet(row.ExerciseName, row.Set);
            Render();
        }
    }

    private void OnFinishClick(object? sender, RoutedEventArgs e)
    {
        var entries = App.Workouts.GetInProgress();
        int sets = entries.Sum(x => x.Sets.Count);
        ConfirmText.Text = $"Save {entries.Count} exercise{(entries.Count == 1 ? "" : "s")}, " +
                           $"{sets} set{(sets == 1 ? "" : "s")}?";
        ConfirmPanel.IsVisible = true;
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        int seconds = (int)App.Workouts.ElapsedSeconds;

        try
        {
            await App.Workouts.FinishWorkoutAsync(seconds);
        }
        catch
        {
            ShowError("Couldn't save the workout. Try again.");
            ConfirmPanel.IsVisible = false;
            return;
        }

        WeightBox.Text = "";
        RepsBox.Text = "";
        ConfirmPanel.IsVisible = false;
        Render();   // nothing in progress anymore, so this stops and resets the timer
        WorkoutFinished?.Invoke(this, EventArgs.Empty);
    }

    private void Render()
    {
        var blocks = App.Workouts.GetInProgress()
            .Select(entry => new ExerciseBlock
            {
                Name = entry.Exercise.Name,
                Sets = entry.Sets.Select((s, i) => new SetRow
                {
                    Number = i + 1,
                    Text = $"{s.Weight:0.##}kg x {s.Reps}",
                    ExerciseName = entry.Exercise.Name,
                    Set = s
                }).ToList()
            })
            .ToList();

        BlockList.ItemsSource = blocks;
        EmptyText.IsVisible = blocks.Count == 0;
        FinishButton.IsEnabled = blocks.Count > 0;

        if (blocks.Count == 0)
            ConfirmPanel.IsVisible = false;

        SyncTimer();
    }

    // The service owns the timer; the view just mirrors it.
    private void SyncTimer()
    {
        var w = App.Workouts;

        if (w.IsTimerRunning) _timer.Start();
        else _timer.Stop();

        PauseButton.IsVisible = w.HasWorkout;
        PauseButton.Content = w.IsTimerRunning ? "Pause" : "Resume";
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        var t = TimeSpan.FromSeconds(App.Workouts.ElapsedSeconds);
        TimerText.Text = t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }

    private static bool TryParseWeight(string? text, out double weight)
    {
        weight = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out weight) && weight >= 0;
    }
}