using SQLite;
namespace GymTracker.Avalonia.Models;

public class ExerciseLog
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [Indexed]
    public int WorkoutSessionId{ get; set; }
    [Indexed]
    public int ExerciseId { get; set; }
    
}