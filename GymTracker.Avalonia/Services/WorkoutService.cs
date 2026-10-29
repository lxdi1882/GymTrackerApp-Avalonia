using GymTracker.Avalonia.Data;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Services;


public class WorkoutService
{
    
    
    private readonly DatabaseService _db;
    private Dictionary<string, ExerciseEntry> workoutInProgress = new Dictionary<string, ExerciseEntry>();

    // when the first set was added; lives here (not in the view) so it survives the screen being recreated
    public DateTime? StartedAt { get; private set; }
    
    public WorkoutService(DatabaseService db)
    {
        _db = db;
    }
    
    public string GetWorkoutSummary()
    {
        string summary = "";

        foreach (var entry in workoutInProgress.Values)
        {
            summary += $"\n{entry.Exercise.Name} ({entry.Exercise.MuscleGroup}):\n";

            foreach (var set in entry.Sets)
            {
                summary += $"{set.Weight}kg x {set.Reps} reps\n";
            }
        }

        return summary;
    }
    
 public bool AddSet(Exercise exercise, double weight, int reps)
{
    var newSet = new SetEntry
    {
        Weight = weight,
        Reps = reps
    };

    if (!workoutInProgress.ContainsKey(exercise.Name))
    {
        workoutInProgress[exercise.Name] = new ExerciseEntry
        {
            Exercise = exercise,
            Sets = new List<SetEntry>()
        };
    }

    workoutInProgress[exercise.Name].Sets.Add(newSet);

    StartedAt ??= DateTime.Now;   // the timer starts with the first set

    return true;
}


    public IReadOnlyList<ExerciseEntry> GetInProgress() => workoutInProgress.Values.ToList();
 
public void RemoveSet(string exerciseName, SetEntry set)
{
    if (!workoutInProgress.TryGetValue(exerciseName, out var entry))
        return;
 
    entry.Sets.Remove(set);
 
    if (entry.Sets.Count == 0)
        workoutInProgress.Remove(exerciseName);

    if (workoutInProgress.Count == 0)
        StartedAt = null;   // nothing left, so the timer resets
}
 

    public async Task<int> FinishWorkoutAsync(int durationSeconds = 0)
{
    var session = new WorkoutSession
    {
        Date = DateTime.Now,
        DurationSeconds = durationSeconds
    };

        await _db.AddWorkoutSessionAsync(session);

        foreach (var entry in workoutInProgress.Values)
        {
            var exercise = await _db.GetExerciseByNameAsync(entry.Exercise.Name);

            var log = new ExerciseLog
            {
                WorkoutSessionId = session.Id,
                ExerciseId = exercise.Id
            };

            await _db.AddExerciseLogAsync(log);
            foreach (var set in entry.Sets)
            {
                set.ExerciseLogId = log.Id;
                await _db.AddSetEntryAsync(set);
                System.Diagnostics.Debug.WriteLine($"Saved set: LogId={set.ExerciseLogId}, Weight={set.Weight}, Reps={set.Reps}, SetId={set.Id}");
            }
            
            System.Diagnostics.Debug.WriteLine($"Saved log: SessionId={log.WorkoutSessionId}, ExerciseId={log.ExerciseId}, LogId={log.Id}");
        }

        int count = workoutInProgress.Count;
        workoutInProgress.Clear();
        StartedAt = null;   // workout saved, timer resets

        return count;
    }
    
    // public int FinishWorkout()
    // {
    //     var session = new WorkoutSession
    //     {
    //         Date = DateTime.Now,
    //         Exercises = workoutInProgress.Values.ToList()
    //     };
    //
    //     completedSessions.Add(session);
    //
    //     int count = session.Exercises.Count;
    //
    //     workoutInProgress.Clear();
    //
    //     return count;
    // }
    public async Task<string> GetHistoryAsync()
    {
        var sessions = await _db.GetAllSessionsAsync();
        string summary = "";

        foreach (var session in sessions)
        {
            summary += $"\nDate: {session.Date} \n";
            
            var logs = await _db.GetLogsForSessionAsync(session.Id);

            foreach (var log in logs)
            {
                var exercise = await _db.GetExerciseByIdAsync(log.ExerciseId);
                summary += $"\n{exercise.Name}: \n";
                
                var sets = await _db.GetSetsForLogAsync(log.Id);

                foreach (var set in sets)
                {
                    summary += $"{set.Weight}kg x {set.Reps} reps \n";
                }
            }
        }
        
        return summary;
    }
    // public string GetHistory()
    // {
    //     string summary = "";
    //
    //     foreach (var session in completedSessions)
    //     {
    //         summary += $"\nDate: {session.Date}\n";
    //
    //         foreach (var entry in session.Exercises)
    //         {
    //             summary += $"\n{entry.Exercise.Name}:\n";
    //
    //             foreach (var set in entry.Sets)
    //             {
    //                 summary += $" {set.Weight}kg x {set.Reps} reps\n";
    //             }
    //         }
    //     }
    //
    //     return summary;
    // }
    
    public async Task<List<WorkoutDaySummary>> GetWorkoutDaySummariesAsync()
    {
        var sessions = await _db.GetAllSessionsAsync();
        var summaries = new List<WorkoutDaySummary>();

        foreach (var session in sessions)
        {
            var logs = await _db.GetLogsForSessionAsync(session.Id);
            var exerciseNames = new List<string>();
            int totalSets = 0;

            foreach (var log in logs)
            {
                var exercise = await _db.GetExerciseByIdAsync(log.ExerciseId);
                exerciseNames.Add(exercise.Name);

                var sets = await _db.GetSetsForLogAsync(log.Id);
                totalSets += sets.Count;
            }

            summaries.Add(new WorkoutDaySummary
            {
                SessionId = session.Id,
                DayLabel = "Workout",         // placeholder until Program tables exist
                Date = session.Date,
                ExerciseNames = exerciseNames,
                TotalSets = totalSets,
                IsExpanded = false
            });
        }

        return summaries.OrderByDescending(s => s.Date).ToList();
    }
    
}