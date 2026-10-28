namespace GymTracker.Avalonia.Models;

public class WorkoutDaySummary
{
    public int SessionId { get; set; }
    public string DayLabel { get; set; }       // placeholder for now, e.g. "Push Day"
    public DateTime Date { get; set; }
    public List<string> ExerciseNames { get; set; }
    public int TotalSets { get; set; }
    public bool IsExpanded { get; set; }   // controls accordion state
}