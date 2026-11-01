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
    private bool _closing;      // true while the rows fade out, they stay in place until it's done
    private int _closeToken;    // lets a late close step notice the group was reopened

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;

            if (value)
            {
                _closeToken++;      // reopened: cancel any fade-out that is still finishing
                _closing = false;
                OnPropertyChanged(nameof(IsClosing));
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCollapsed));
            OnPropertyChanged(nameof(VisibleItems));
        }
    }

    public bool IsCollapsed => !IsExpanded;

    // Only true once the user taps the group, so the fade-in plays for taps but not when
    // groups are rebuilt (search typing, refresh). Set it BEFORE IsExpanded.
    private bool _animate;
    public bool Animate
    {
        get => _animate;
        set
        {
            if (_animate == value) return;
            _animate = value;
            OnPropertyChanged();
        }
    }

    // drives the "closing" fade-out style in App.axaml
    public bool IsClosing => _closing;

    // rows are only created once the group is opened (and kept while they fade out)
    public List<Exercise> VisibleItems => IsExpanded || _closing ? Items : None;

    // Collapse with a quick fade instead of the rows vanishing in one frame.
    public async Task CollapseAsync()
    {
        if (!_isExpanded) return;

        int token = ++_closeToken;
        _closing = true;
        OnPropertyChanged(nameof(IsClosing));

        IsExpanded = false;   // the chevron flips right away, the rows stay because _closing is true

        await Task.Delay(180);   // a bit longer than the 0.16s fade in App.axaml
        if (token != _closeToken) return;

        _closing = false;
        OnPropertyChanged(nameof(IsClosing));
        OnPropertyChanged(nameof(VisibleItems));
    }

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
    private bool _busy;                 // a save or delete is running, so a second tap can't repeat it
    private bool _deleteChecking;       // the "is it used?" lookup before arming the delete button
    private int _armToken;              // lets an old "disarm" timer notice the button was re-armed
    private Button? _progressButton;    // the button currently showing "Saving..." / "Deleting..."
    private object? _progressOriginal;  // and the label to put back

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

    private async void OnToggleGroup(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExerciseGroup g) return;

        // while searching, groups are opened automatically, so don't remember those
        bool remember = (SearchBox.Text?.Trim().Length ?? 0) == 0;

        if (g.IsExpanded)
        {
            if (remember) _expanded.Remove(g.Name);
            await g.CollapseAsync();
        }
        else
        {
            if (remember) _expanded.Add(g.Name);
            g.Animate = true;   // before IsExpanded, so both classes are there when the fade starts
            g.IsExpanded = true;
        }
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
        _armToken++;   // any old "disarm" timer is now stale

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
        if (!FormPanel.IsVisible || _closing || _busy) return;   // can't dismiss mid-save

        _closing = true;
        _confirmDelete = false;
        _armToken++;

        Scrim.IsOpen = false;
        FormPanel.Classes.Set("off", true);   // transitions back out

        await Task.Delay(230);   // a bit longer than the 0.22s transition in App.axaml

        FormPanel.IsVisible = false;

        _editing = null;
        _closing = false;
    }

    private async Task SaveAsync()
    {
        if (_busy) return;   // a second tap must never save the exercise twice

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

        // no awaits between the _busy check above and this lock, so two taps can't both get through
        LockForm(true);

        var editing = _editing;
        string oldName = editing?.Name ?? "";
        string oldGroup = editing?.MuscleGroup ?? "";
        string oldSubTarget = editing?.SubTarget ?? "";
        bool written = false;

        try
        {
            string subTarget = SubTargetBox.Text?.Trim() ?? "";
            Task write;

            if (editing is null)
            {
                write = App.Db.AddExerciseAsync(new Exercise
                {
                    Name = name,
                    MuscleGroup = group,
                    SubTarget = subTarget,
                    IsDefault = false
                });
            }
            else
            {
                editing.Name = name;
                editing.MuscleGroup = group;
                editing.SubTarget = subTarget;
                write = App.Db.UpdateExerciseAsync(editing);
            }

            // only show "Saving..." if this one is slow, so fast saves don't flash it
            if (await Task.WhenAny(write, Task.Delay(150)) != write)
                ShowFormProgress(SaveButton, "Saving...");

            await write;
            written = true;

            _expanded.Add(group);   // open the group so you can see what you just saved

            LockForm(false);
            await CloseFormAsync();
            await RefreshAsync();
        }
        catch
        {
            // if the write itself failed, put the in-memory exercise back the way it was
            if (!written && editing is not null)
            {
                editing.Name = oldName;
                editing.MuscleGroup = oldGroup;
                editing.SubTarget = oldSubTarget;
            }

            LockForm(false);
            ShowError("Couldn't save the exercise. Try again.");
        }
    }

    private async Task DeleteAsync()
    {
        if (_editing is null || _busy || _deleteChecking) return;

        var exercise = _editing;

        if (IsInCurrentWorkout(exercise))
        {
            ShowError("This exercise is in your current workout. Finish the workout before deleting it.");
            return;
        }

        // A fast double tap used to slip past the arming step while this lookup was running,
        // and delete without the "tap again" confirmation. The flag closes that gap.
        _deleteChecking = true;
        int uses;
        try { uses = await App.Db.CountLogsForExerciseAsync(exercise.Id); }
        finally { _deleteChecking = false; }

        if (!ReferenceEquals(_editing, exercise)) return;   // the form was closed meanwhile

        if (uses > 0)
        {
            ShowError($"Logged in {uses} workout{(uses == 1 ? "" : "s")}, so deleting it would break your history. " +
                      "Rename it instead.");
            return;
        }

        // first tap arms the button, second tap deletes (disarms itself after 4s)
        if (!_confirmDelete)
        {
            _confirmDelete = true;
            DeleteButton.Content = "Tap again to delete";
            _ = DisarmDeleteAsync(++_armToken);   // not awaited, so the second tap isn't stuck behind it
            return;
        }

        LockForm(true);

        try
        {
            var delete = App.Db.DeleteExerciseAsync(exercise);

            // only show "Deleting..." if this one is slow, so fast deletes don't flash it
            if (await Task.WhenAny(delete, Task.Delay(150)) != delete)
                ShowFormProgress(DeleteButton, "Deleting...");

            await delete;

            LockForm(false);
            await CloseFormAsync();
            await RefreshAsync();
        }
        catch
        {
            LockForm(false);   // still armed, so one more tap retries
            ShowError("Couldn't delete the exercise. Try again.");
        }
    }

    private async Task DisarmDeleteAsync(int token)
    {
        await Task.Delay(4000);

        // stale timer (form closed, reopened or re-armed since) or a delete is already running
        if (token != _armToken || !_confirmDelete || _busy) return;

        _confirmDelete = false;
        DeleteButton.Content = "Delete exercise";
    }

    // Disables the dialog buttons while a save or delete runs.
    private void LockForm(bool locked)
    {
        _busy = locked;
        SaveButton.IsEnabled = !locked;
        CancelButton.IsEnabled = !locked;
        DeleteButton.IsEnabled = !locked;

        if (!locked) ShowFormProgress(null, null);
    }

    // Swaps one button's label for the progress text and pulses the busy bar. null clears it.
    private void ShowFormProgress(Button? button, string? text)
    {
        if (_progressButton is not null)
        {
            _progressButton.Content = _progressOriginal;   // put the old label back first
            _progressButton = null;
        }

        if (button is null || text is null)
        {
            FormBusy.Classes.Set("busy", false);
            return;
        }

        _progressButton = button;
        _progressOriginal = button.Content;
        button.Content = text;
        FormBusy.Classes.Set("busy", true);
    }

    private static bool IsInCurrentWorkout(Exercise exercise)
        => App.Workouts.GetInProgress().Any(x => x.Exercise.Id == exercise.Id);

    private void ShowError(string message)
    {
        FormError.Text = message;
        FormError.IsVisible = true;
    }
}