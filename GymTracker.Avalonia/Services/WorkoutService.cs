using System.Text.Json;
using GymTracker.Avalonia.Data;
using GymTracker.Avalonia.Models;

namespace GymTracker.Avalonia.Services;


public class WorkoutService
{
    
    
    private readonly DatabaseService _db;
    private Dictionary<string, ExerciseEntry> workoutInProgress = new Dictionary<string, ExerciseEntry>();

    // ---- timer (lives here so it survives the screen being recreated and gets persisted) ----
    private double _elapsedBefore;          // seconds banked before the last resume
    private DateTime? _runningSinceUtc;     // null = paused / not started

    public bool HasWorkout => workoutInProgress.Count > 0;
    public bool IsTimerRunning => _runningSinceUtc is not null;
    public double ElapsedSeconds =>
        _elapsedBefore + (_runningSinceUtc is { } since ? (DateTime.UtcNow - since).TotalSeconds : 0);

    // ---- draft persistence ----
    private Task? _restoreTask;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    
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
    if (!HasWorkout)
        _runningSinceUtc = DateTime.UtcNow;   // the first set starts the clock

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

    SaveDraft();
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

    if (!HasWorkout)
        ResetTimer();   // nothing left, so the timer resets

    SaveDraft();
}

    public void PauseTimer()
    {
        if (_runningSinceUtc is null) return;
        _elapsedBefore = ElapsedSeconds;   // bank it before clearing
        _runningSinceUtc = null;
        SaveDraft();
    }

    public void ResumeTimer()
    {
        if (_runningSinceUtc is not null || !HasWorkout) return;
        _runningSinceUtc = DateTime.UtcNow;
        SaveDraft();
    }

    private void ResetTimer()
    {
        _elapsedBefore = 0;
        _runningSinceUtc = null;
    }

    public void DiscardWorkout()
    {
        workoutInProgress.Clear();
        ResetTimer();
        SaveDraft();   // empty workout clears the draft
    }

    // ---------------- draft save / restore ----------------

    private void SaveDraft()
    {
        if (!HasWorkout)
        {
            _ = PersistAsync(null);
            return;
        }

        // snapshot synchronously so what's saved matches this exact moment
        var draft = new WorkoutDraft
        {
            ElapsedBeforeSeconds = _elapsedBefore,
            RunningSinceUtc = _runningSinceUtc,
            Exercises = workoutInProgress.Values.Select(e => new DraftExercise
            {
                ExerciseId = e.Exercise.Id,
                Sets = e.Sets.Select(s => new DraftSet { Weight = s.Weight, Reps = s.Reps }).ToList()
            }).ToList()
        };

        _ = PersistAsync(JsonSerializer.Serialize(draft));
    }

    private async Task PersistAsync(string? json)
    {
        await _saveLock.WaitAsync();   // keeps writes in order
        try
        {
            if (json is null) await _db.ClearDraftAsync();
            else await _db.SaveDraftAsync(json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Draft save failed: {ex}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    // Safe to call from several places; the work only happens once.
    public Task RestoreDraftAsync() => _restoreTask ??= RestoreCoreAsync();

    private async Task RestoreCoreAsync()
    {
        if (workoutInProgress.Count > 0) return;

        string? json;
        try { json = await _db.GetDraftAsync(); }
        catch { return; }
        if (string.IsNullOrWhiteSpace(json)) return;

        WorkoutDraft? draft;
        try { draft = JsonSerializer.Deserialize<WorkoutDraft>(json); }
        catch { return; }
        if (draft is null) return;

        try
        {
            foreach (var de in draft.Exercises)
            {
                var exercise = await _db.GetExerciseByIdAsync(de.ExerciseId);
                if (exercise is null) continue;   // exercise was deleted, skip it

                workoutInProgress[exercise.Name] = new ExerciseEntry
                {
                    Exercise = exercise,
                    Sets = de.Sets.Select(s => new SetEntry { Weight = s.Weight, Reps = s.Reps }).ToList()
                };
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Draft restore failed: {ex}");
            return;
        }

        if (HasWorkout)
        {
            _elapsedBefore = draft.ElapsedBeforeSeconds;
            _runningSinceUtc = draft.RunningSinceUtc;
        }
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
        ResetTimer();

        // only reached if every save above worked, so a failed finish keeps the draft
        await _saveLock.WaitAsync();
        try { await _db.ClearDraftAsync(); }
        finally { _saveLock.Release(); }

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