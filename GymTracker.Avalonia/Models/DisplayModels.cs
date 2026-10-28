// MauiApp2/Models/DisplayModels.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GymTracker.Avalonia.Models;

public class ExerciseLogDisplay : INotifyPropertyChanged
{
    public string ExerciseName { get; set; }
    public List<string> SetLines { get; set; }
    public string SetCountText => $"{SetLines.Count} set{(SetLines.Count == 1 ? "" : "s")}";

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnPropertyChanged(); OnPropertyChanged(nameof(ChevronGlyph)); }
    }

    public string ChevronGlyph => IsExpanded ? "▲" : "▼";

    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class PrDisplay
{
    public string ExerciseName { get; set; } = string.Empty;
    public string ResultText { get; set; } = string.Empty;
}