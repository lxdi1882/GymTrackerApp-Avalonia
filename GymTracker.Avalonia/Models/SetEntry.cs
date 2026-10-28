using SQLite;
namespace GymTracker.Avalonia.Models;
public class SetEntry
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }
    
    [Indexed]
    public int ExerciseLogId { get; set; }
    public double Weight{ get; set; }
    public int Reps{ get; set; }
}