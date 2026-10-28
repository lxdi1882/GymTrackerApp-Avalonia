using GymTracker.Avalonia.Models;
using GymTracker.Avalonia.Data;
using SQLite;

namespace GymTracker.Avalonia.Services;

public class DatabaseService
{
    private SQLiteAsyncConnection _db;

    private async Task Init()
    {
        if (_db is not null)
            return;
        
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(appData, "gymtracker.db3");
        _db = new SQLiteAsyncConnection(dbPath);
        
        await _db.CreateTableAsync<Exercise>();
        await _db.CreateTableAsync<WorkoutSession>();
        await _db.CreateTableAsync<ExerciseLog>();
        await _db.CreateTableAsync<SetEntry>();

    }

    public async Task<List<Exercise>> GetExercisesAsync()
    {
        await Init();
        return await _db.Table<Exercise>().ToListAsync();
    }

    public async Task<int> AddExerciseAsync(Exercise exercise)
    {
        await Init();
        return await _db.InsertAsync(exercise);
    }
    
    public async Task SeedExercisesAsync()
    {
        await Init();

        var existing = await _db.Table<Exercise>().ToListAsync();
        if (existing.Count > 0)
            return; // already seeded, don't duplicate

        foreach (var ex in ExerciseLibrary.All)
        {
            await _db.InsertAsync(new Exercise
            {
                Name = ex.Name,
                MuscleGroup = ex.MuscleGroup,
                SubTarget = ex.SubTarget
            });
        }
    }
    
    public async Task AddWorkoutSessionAsync(WorkoutSession session)
    {
        await Init();
        await _db.InsertAsync(session);
    }
    
    public async Task<Exercise> GetExerciseByNameAsync(string name)
    {
        await Init();
        return await _db.Table<Exercise>().FirstOrDefaultAsync(e => e.Name == name);
    }
    
    public async Task AddExerciseLogAsync(ExerciseLog log)
    {
        await Init();
        await _db.InsertAsync(log);
    }
    
    public async Task AddSetEntryAsync(SetEntry set)
    {
        await Init();
        await _db.InsertAsync(set);
    }
    
    public async Task<List<WorkoutSession>> GetAllSessionsAsync()
    {
        await Init();
        return await _db.Table<WorkoutSession>().ToListAsync();
    }
    
    public async Task<List<ExerciseLog>> GetLogsForSessionAsync(int sessionId)
    {
        await Init();
        return await _db.Table<ExerciseLog>()
            .Where(log => log.WorkoutSessionId == sessionId)
            .ToListAsync();
    }
    
    public async Task<List<SetEntry>> GetSetsForLogAsync(int logId)
    {
        await Init();
        return await _db.Table<SetEntry>()
            .Where(set => set.ExerciseLogId == logId)
            .ToListAsync();
    }
    
    public async Task<Exercise> GetExerciseByIdAsync(int id)
    {
        await Init();
        return await _db.Table<Exercise>().FirstOrDefaultAsync(e => e.Id == id);
    }
    
// GYM TRACKER — DatabaseService.cs additions

public async Task<List<(Exercise Exercise, SetEntry Set, DateTime Date)>> GetRecentPRsAsync(int daysBack = 7)
{
    await Init();

    var cutoff = DateTime.Now.AddDays(-daysBack);
    var sessions = await _db.Table<WorkoutSession>()
        .Where(s => s.Date >= cutoff)
        .ToListAsync();

    var prs = new List<(Exercise, SetEntry, DateTime)>();

    foreach (var session in sessions)
    {
        var logs = await GetLogsForSessionAsync(session.Id);

        foreach (var log in logs)
        {
            var exercise = await GetExerciseByIdAsync(log.ExerciseId);
            var sets = await GetSetsForLogAsync(log.Id);
            double previousBest = await GetBestWeightBeforeDateAsync(log.ExerciseId, session.Date);

            foreach (var set in sets)
            {
                if (set.Weight > previousBest)
                {
                    prs.Add((exercise, set, session.Date));
                    previousBest = set.Weight; // don't re-flag smaller sets in the same session
                }
            }
        }
    }

    return prs;
}

private async Task<double> GetBestWeightBeforeDateAsync(int exerciseId, DateTime beforeDate)
{
    await Init();

    var logs = await _db.Table<ExerciseLog>()
        .Where(l => l.ExerciseId == exerciseId)
        .ToListAsync();

    double best = 0;

    foreach (var log in logs)
    {
        var session = await _db.Table<WorkoutSession>()
            .FirstOrDefaultAsync(s => s.Id == log.WorkoutSessionId);

        if (session == null || session.Date >= beforeDate)
            continue;

        var sets = await GetSetsForLogAsync(log.Id);
        if (sets.Any())
            best = Math.Max(best, sets.Max(s => s.Weight));
    }

    return best;
}

public async Task<int> GetStreakAsync()
{
    await Init();

    var trainingDays = (await _db.Table<WorkoutSession>().ToListAsync())
        .Select(s => s.Date.Date)
        .Distinct()
        .OrderByDescending(d => d)
        .ToList();

    if (trainingDays.Count == 0)
        return 0;

    var expected = DateTime.Today;
    if (!trainingDays.Contains(expected))
        expected = expected.AddDays(-1); // no workout today yet, still counts from yesterday

    int streak = 0;
    foreach (var day in trainingDays)
    {
        if (day == expected)
        {
            streak++;
            expected = expected.AddDays(-1);
        }
        else if (day < expected)
        {
            break;
        }
    }

    return streak;
}
    
    
}