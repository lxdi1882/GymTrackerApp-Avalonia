using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Data;

public class ExerciseLibrary
{
    public static List<Exercise> All = new List<Exercise>
    {
        new Exercise { Name = "Peck Deck Machine", MuscleGroup = "Chest", SubTarget = "Mid" },
        new Exercise { Name = "Incline Dumbbell Press", MuscleGroup = "Chest", SubTarget = "Upper" },

        new Exercise { Name = "Close Grip Pull-Ups", MuscleGroup = "Back", SubTarget = "Lats" },
        new Exercise { Name = "T-Bar Rows (Wide Grip)", MuscleGroup = "Back", SubTarget = "Upper Back" },
        new Exercise { Name = "Lat Pulldown Machine", MuscleGroup = "Back", SubTarget = "Lats" },

        new Exercise { Name = "Dips", MuscleGroup = "Arms", SubTarget = "Triceps (Medial)" },
        new Exercise { Name = "Single Arm Cable Extension", MuscleGroup = "Arms", SubTarget = "Triceps (Long Head)" },
        new Exercise { Name = "Preacher Curls Machine", MuscleGroup = "Arms", SubTarget = "Biceps" },
        new Exercise { Name = "Reverse Curls", MuscleGroup = "Arms", SubTarget = "Forearms" },

        new Exercise { Name = "Machine Shoulder Press", MuscleGroup = "Shoulders", SubTarget = "Front Delts" },
        new Exercise { Name = "Lateral Raise", MuscleGroup = "Shoulders", SubTarget = "Side Delts" },

        new Exercise { Name = "Deadlift", MuscleGroup = "Legs", SubTarget = "Posterior Chain" },
        new Exercise { Name = "Leg Press Machine", MuscleGroup = "Legs", SubTarget = "Quads" },
        new Exercise { Name = "Leg Extension", MuscleGroup = "Legs", SubTarget = "Quads" },
        new Exercise { Name = "Hamstring Curls", MuscleGroup = "Legs", SubTarget = "Hamstrings" },
      
    };
}