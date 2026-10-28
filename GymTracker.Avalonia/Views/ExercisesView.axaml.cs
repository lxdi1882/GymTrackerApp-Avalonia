using Avalonia.Controls;
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

    public ExercisesView()
    {
        InitializeComponent();

        SearchBox.TextChanged += (_, _) => Render();
        AddButton.Click += (_, _) => OpenForm();
        CancelButton.Click += (_, _) => FormPanel.IsVisible = false;
        SaveButton.Click += async (_, _) => await SaveAsync();

        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        _all = await App.Db.GetExercisesAsync();

        // muscle group choices: the standard ones plus anything already in the DB
        GroupBox.ItemsSource = GroupOrder
            .Concat(_all.Select(e => e.MuscleGroup).Where(g => !string.IsNullOrWhiteSpace(g)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        CountText.Text = $"{_all.Count} exercise{(_all.Count == 1 ? "" : "s")}";
    }

    private void OpenForm()
    {
        NameBox.Text = "";
        SubTargetBox.Text = "";
        GroupBox.SelectedItem = null;
        FormError.IsVisible = false;
        FormPanel.IsVisible = true;
        NameBox.Focus();
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

        if (_all.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowError("An exercise with that name already exists.");
            return;
        }

        try
        {
            await App.Db.AddExerciseAsync(new Exercise
            {
                Name = name,
                MuscleGroup = group,
                SubTarget = SubTargetBox.Text?.Trim() ?? "",
                IsDefault = false
            });

            FormPanel.IsVisible = false;
            await RefreshAsync();
        }
        catch
        {
            ShowError("Couldn't save the exercise. Try again.");
        }
    }

    private void ShowError(string message)
    {
        FormError.Text = message;
        FormError.IsVisible = true;
    }
}
