using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Views;

public class ExerciseGroup : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public List<Exercise> Items { get; set; } = new();
    public string CountText => $"{Items.Count} exercise{(Items.Count == 1 ? "" : "s")}";

    private static readonly List<Exercise> None = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCollapsed));
            OnPropertyChanged(nameof(VisibleItems));
        }
    }

    public bool IsCollapsed => !IsExpanded;

    // rows are only created once the group is opened
    public List<Exercise> VisibleItems => IsExpanded ? Items : None;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class ExercisesView : UserControl
{
    private static readonly string[] GroupOrder = { "Chest", "Back", "Shoulders", "Arms", "Legs", "Core" };

    private List<Exercise> _all = new();
    private List<string> _groupChoices = new();
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private string? _signature;          // lets RefreshAsync skip rebuilding when nothing changed
    private Exercise? _editing;          // null = adding a new exercise
    private bool _confirmDelete;
    private bool _closing;

    public ExercisesView()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) => Render();
        AddButton.Click += (_, _) => OpenForm(null);
        CancelButton.Click += async (_, _) => await CloseFormAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        DeleteButton.Click += async (_, _) => await DeleteAsync();

        // tap the dimmed area (Scrim.Dismissed) or press Esc to dismiss
        Scrim.Dismissed += async (_, _) => await CloseFormAsync();
        KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape && FormPanel.IsVisible)
            {
                e.Handled = true;
                await CloseFormAsync();
            }
        };

        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var data = await App.Db.GetExercisesAsync();

        // nothing added, renamed or deleted since last time: keep what's on screen
        string signature = string.Join("|", data.Select(e =>
            $"{e.Id}:{e.Name}:{e.MuscleGroup}:{e.SubTarget}"));
        if (signature == _signature) return;
        _signature = signature;

        _all = data;

        // muscle group choices: the standard ones plus anything already in the DB
        _groupChoices = GroupOrder
            .Concat(_all.Select(e => e.MuscleGroup).Where(g => !string.IsNullOrWhiteSpace(g)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        GroupBox.ItemsSource = _groupChoices;

        Render();
    }

    private void Render()
    {
        string query = SearchBox.Text?.Trim() ?? "";

        var filtered = _all.Where(e =>
            query.Length == 0 ||
            (e.Name ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (e.SubTarget ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (e.MuscleGroup ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        var groups = filtered
            .GroupBy(e => e.MuscleGroup ?? "Other")
            .Select(g => new ExerciseGroup
            {
                Name = g.Key,
                Items = g.OrderBy(e => e.Name).ToList(),
                // searching opens every matching group, otherwise only the ones you opened
                IsExpanded = query.Length > 0 || _expanded.Contains(g.Key)
            })
            .OrderBy(g =>
            {
                int i = Array.FindIndex(GroupOrder, x => x.Equals(g.Name, StringComparison.OrdinalIgnoreCase));
                return i < 0 ? int.MaxValue : i;
            })
            .ThenBy(g => g.Name)
            .ToList();

        GroupList.ItemsSource = groups;
        EmptyText.IsVisible = groups.Count == 0;
        CountText.Text = $"{_all.Count} exercise{(_all.Count == 1 ? "" : "s")}, tap a group to open it";
    }

    private void OnToggleGroup(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExerciseGroup g) return;

        g.IsExpanded = !g.IsExpanded;

        // while searching, groups are opened automatically, so don't remember those
        if ((SearchBox.Text?.Trim().Length ?? 0) > 0) return;

        if (g.IsExpanded) _expanded.Add(g.Name);
        else _expanded.Remove(g.Name);
    }

    private void OnEditExercise(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Exercise ex)
            OpenForm(ex);
    }

    private async void OpenForm(Exercise? exercise)
    {
        if (_closing) return;

        _editing = exercise;
        _confirmDelete = false;

        FormPanel.Title = exercise is null ? "New exercise" : "Edit exercise";
        SaveButton.Content = exercise is null ? "Save exercise" : "Save changes";
        DeleteButton.Content = "Delete exercise";
        DeleteButton.IsVisible = exercise is not null;

        NameBox.Text = exercise?.Name ?? "";
        SubTargetBox.Text = exercise?.SubTarget ?? "";
        GroupBox.SelectedItem = exercise is null
            ? null
            : _groupChoices.FirstOrDefault(g => g.Equals(exercise.MuscleGroup, StringComparison.OrdinalIgnoreCase));

        FormError.IsVisible = false;

        // start in the "off" pose, show it, then drop "off" a frame later so the transition plays
        FormPanel.Classes.Set("off", true);
        FormPanel.IsVisible = true;
        Scrim.IsOpen = true;
        NameBox.Focus();

        await Task.Delay(16);
        FormPanel.Classes.Set("off", false);
    }

    // plays the exit animation, then hides the dialog
    private async Task CloseFormAsync()
    {
        if (!FormPanel.IsVisible || _closing) return;

        _closing = true;
        _confirmDelete = false;

        Scrim.IsOpen = false;
        FormPanel.Classes.Set("off", true);   // transitions back out

        await Task.Delay(230);   // a bit longer than the 0.22s transition in App.axaml

        FormPanel.IsVisible = false;

        _editing = null;
        _closing = false;
    }

    private async Task SaveAsync()
    {
        string name = NameBox.Text?.Trim() ?? "";

        if (name.Length == 0)
        {
            ShowError("Give the exercise a name.");
            return;
        }

        if (GroupBox.SelectedItem is not string group)
        {
            ShowError("Pick a muscle group.");
            return;
        }

        if (_all.Any(e => e.Id != _editing?.Id &&
                          string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError("An exercise with that name already exists.");
            return;
        }

        if (_editing is not null &&
            !string.Equals(_editing.Name, name, StringComparison.Ordinal) &&
            IsInCurrentWorkout(_editing))
        {
            ShowError("This exercise is in your current workout. Finish the workout before renaming it.");
            return;
        }

        try
        {
            string subTarget = SubTargetBox.Text?.Trim() ?? "";

            if (_editing is null)
            {
                await App.Db.AddExerciseAsync(new Exercise
                {
                    Name = name,
                    MuscleGroup = group,
                    SubTarget = subTarget,
                    IsDefault = false
                });
            }
            else
            {
                _editing.Name = name;
                _editing.MuscleGroup = group;
                _editing.SubTarget = subTarget;
                await App.Db.UpdateExerciseAsync(_editing);
            }

            _expanded.Add(group);   // open the group so you can see what you just saved

            await CloseFormAsync();
            await RefreshAsync();
        }
        catch
        {
            ShowError("Couldn't save the exercise. Try again.");
        }
    }

    private async Task DeleteAsync()
    {
        if (_editing is null) return;

        if (IsInCurrentWorkout(_editing))
        {
            ShowError("This exercise is in your current workout. Finish the workout before deleting it.");
            return;
        }

        int uses = await App.Db.CountLogsForExerciseAsync(_editing.Id);
        if (uses > 0)
        {
            ShowError($"Logged in {uses} workout{(uses == 1 ? "" : "s")}, so deleting it would break your history. " +
                      "Rename it instead.");
            return;
        }

        // first tap arms the button, second tap deletes (resets after 4s)
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            DeleteButton.Content = "Tap again to delete";
            await Task.Delay(4000);

            if (_confirmDelete)
            {
                _confirmDelete = false;
                DeleteButton.Content = "Delete exercise";
            }
            return;
        }

        try
        {
            await App.Db.DeleteExerciseAsync(_editing);
            await CloseFormAsync();
            await RefreshAsync();
        }
        catch
        {
            ShowError("Couldn't delete the exercise. Try again.");
        }
    }

    private static bool IsInCurrentWorkout(Exercise exercise)
        => App.Workouts.GetInProgress().Any(x => x.Exercise.Id == exercise.Id);

    private void ShowError(string message)
    {
        FormError.Text = message;
        FormError.IsVisible = true;
    }
}