namespace GymTracker.Avalonia.Models;

// Snapshot of an in-progress workout, stored as JSON in AppSetting so a crash can't lose it.
public class WorkoutDraft
{
    public double ElapsedBeforeSeconds { get; set; }   // time banked before the last resume
    public DateTime? RunningSinceUtc { get; set; }     // null = timer paused
    public List<DraftExercise> Exercises { get; set; } = new();
}

public class DraftExercise
{
    public int ExerciseId { get; set; }
    public List<DraftSet> Sets { get; set; } = new();
}

public class DraftSet
{
    public double Weight { get; set; }
    public int Reps { get; set; }
}
