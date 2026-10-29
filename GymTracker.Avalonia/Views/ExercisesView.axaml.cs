using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Views;

public class ExerciseGroup
{
    public string Name { get; set; } = "";
    public List<Exercise> Items { get; set; } = new();
    public string CountText => $"{Items.Count} exercise{(Items.Count == 1 ? "" : "s")}";
}

public partial class ExercisesView : UserControl
{
    private static readonly string[] GroupOrder = { "Chest", "Back", "Shoulders", "Arms", "Legs", "Core" };

    private List<Exercise> _all = new();
    private List<string> _groupChoices = new();
    private Exercise? _editing;          // null = adding a new exercise
    private bool _confirmDelete;

    public ExercisesView()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) => Render();
        AddButton.Click += (_, _) => OpenForm(null);
        CancelButton.Click += (_, _) => CloseForm();
        SaveButton.Click += async (_, _) => await SaveAsync();
        DeleteButton.Click += async (_, _) => await DeleteAsync();

        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        _all = await App.Db.GetExercisesAsync();

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
                Items = g.OrderBy(e => e.Name).ToList()
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
        CountText.Text = $"{_all.Count} exercise{(_all.Count == 1 ? "" : "s")}, tap one to edit";
    }

    private void OnEditExercise(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is Exercise ex)
            OpenForm(ex);
    }

    private void OpenForm(Exercise? exercise)
    {
        _editing = exercise;
        _confirmDelete = false;

        FormTitle.Text = exercise is null ? "New exercise" : "Edit exercise";
        SaveButton.Content = exercise is null ? "Save exercise" : "Save changes";
        DeleteButton.Content = "Delete exercise";
        DeleteButton.IsVisible = exercise is not null;

        NameBox.Text = exercise?.Name ?? "";
        SubTargetBox.Text = exercise?.SubTarget ?? "";
        GroupBox.SelectedItem = exercise is null
            ? null
            : _groupChoices.FirstOrDefault(g => g.Equals(exercise.MuscleGroup, StringComparison.OrdinalIgnoreCase));

        FormError.IsVisible = false;
        FormPanel.IsVisible = true;
        Scroller.Offset = new Vector(0, 0);   // the form sits at the top, so bring it into view
        NameBox.Focus();
    }

    private void CloseForm()
    {
        FormPanel.IsVisible = false;
        _editing = null;
        _confirmDelete = false;
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

            CloseForm();
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
            CloseForm();
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