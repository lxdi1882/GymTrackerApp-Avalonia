using SQLite;

namespace GymTracker.Avalonia.Models;

public class Exercise
{
[PrimaryKey,AutoIncrement]
    public int Id{ get; set; }
    
    public string Name { get; set; }
    public string MuscleGroup { get; set; }
    public string SubTarget { get; set; }
    
    public bool IsDefault { get; set; }
}

