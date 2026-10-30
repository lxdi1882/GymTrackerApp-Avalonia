namespace GymTracker.Avalonia.Models;

public class WorkoutDraft
{
    public double ElapsedBeforeSeconds { get; set; }
    public DateTime? RunningSinceUtc { get; set; }
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