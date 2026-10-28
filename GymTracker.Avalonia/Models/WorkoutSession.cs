using SQLite;

namespace GymTracker.Avalonia.Models;

public class WorkoutSession
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    
    public int ProgramId { get; set; }
    public int TrainingDayId { get; set; }
    public DateTime Date{ get; set; }
    //public List<ExerciseEntry> Exercises{ get; set; }
}