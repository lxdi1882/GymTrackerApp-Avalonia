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

public partial class WorkoutView : UserControl
{
    public event EventHandler? WorkoutFinished;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime? _start;

    public WorkoutView()
    {
        InitializeComponent();

        AddSetButton.Click += (_, _) => AddSet();
        RepsBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) AddSet(); };
        FinishButton.Click += OnFinishClick;
        CancelButton.Click += (_, _) => ConfirmPanel.IsVisible = false;
        SaveButton.Click += OnSaveClick;
        _timer.Tick += (_, _) => UpdateTimer();

        Loaded += async (_, _) => await LoadAsync();
    }

    public async Task LoadAsync()
    {
        var selected = (ExerciseBox.SelectedItem as Exercise)?.Name;

        var list = (await App.Db.GetExercisesAsync())
            .OrderBy(e => e.MuscleGroup)
            .ThenBy(e => e.Name)
            .ToList();

        ExerciseBox.ItemsSource = list;
        ExerciseBox.SelectedItem = list.FirstOrDefault(e => e.Name == selected);
        Render();
    }

    private void AddSet()
    {
        if (ExerciseBox.SelectedItem is not Exercise exercise)
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
        App.Workouts.AddSet(exercise, weight, reps);

        if (_start is null)
        {
            _start = DateTime.Now;
            _timer.Start();
        }

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
        int seconds = _start is null ? 0 : (int)(DateTime.Now - _start.Value).TotalSeconds;

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

        _timer.Stop();
        _start = null;
        TimerText.Text = "00:00";
        WeightBox.Text = "";
        RepsBox.Text = "";
        ConfirmPanel.IsVisible = false;
        Render();
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
        {
            ConfirmPanel.IsVisible = false;
            _timer.Stop();
            _start = null;
            TimerText.Text = "00:00";
        }
    }

    private void UpdateTimer()
    {
        if (_start is null) return;
        var t = DateTime.Now - _start.Value;
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
