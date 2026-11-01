using GymTracker.Avalonia.Models;
using GymTracker.Avalonia.Data;
using SQLite;

namespace GymTracker.Avalonia.Services;

public class DatabaseService
{
    private SQLiteAsyncConnection _db;

   private readonly SemaphoreSlim _initLock = new(1, 1);

private async Task Init()
{
    if (_db is not null)
        return;

    await _initLock.WaitAsync();
    try
    {
        if (_db is not null)
            return;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(appData, "gymtracker.db3");
        var conn = new SQLiteAsyncConnection(dbPath);

        await conn.CreateTableAsync<Exercise>();
        await conn.CreateTableAsync<WorkoutSession>();
        await conn.CreateTableAsync<ExerciseLog>();
        await conn.CreateTableAsync<SetEntry>();
        await conn.CreateTableAsync<AppSetting>();
        await SeedAsync(conn);   // seeded before anyone can use the db, so no screen ever sees an empty exercise list

        _db = conn;   // only visible to other callers once the tables exist and are seeded
    }
    finally
    {
        _initLock.Release();
    }
}

    private static async Task SeedAsync(SQLiteAsyncConnection conn)
    {
        if (await conn.Table<Exercise>().CountAsync() > 0)
            return; // already seeded, don't duplicate

        var seed = ExerciseLibrary.All.Select(ex => new Exercise
        {
            Name = ex.Name,
            MuscleGroup = ex.MuscleGroup,
            SubTarget = ex.SubTarget
        }).ToList();

        await conn.InsertAllAsync(seed);   // one transaction instead of one disk write per exercise
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
    
    // Seeding now happens inside Init(), so this just opens the db early (App calls it at startup).
    public Task SeedExercisesAsync() => Init();

    // Saves a whole workout in one transaction: all of it lands, or none of it does.
    // The draft is cleared in the same transaction, so a crash can never leave a saved
    // workout AND a draft that would be restored (and saved again) on the next launch.
    public async Task SaveWorkoutAsync(WorkoutSession session, List<(int ExerciseId, List<SetEntry> Sets)> logs)
    {
        await Init();

        await _db.RunInTransactionAsync(conn =>
        {
            conn.Insert(session);

            foreach (var (exerciseId, sets) in logs)
            {
                var log = new ExerciseLog { WorkoutSessionId = session.Id, ExerciseId = exerciseId };
                conn.Insert(log);

                foreach (var set in sets)
                {
                    set.ExerciseLogId = log.Id;
                    conn.Insert(set);
                }
            }

            conn.Delete<AppSetting>(DraftKey);
        });
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
    
    public async Task<WorkoutSession?> GetLatestSessionAsync()
    {
        await Init();
        return await _db.Table<WorkoutSession>()
            .OrderByDescending(s => s.Date)
            .FirstOrDefaultAsync();
    }

    public async Task<List<WorkoutSession>> GetSessionsInRangeAsync(DateTime from, DateTime to)
    {
        await Init();
        return await _db.Table<WorkoutSession>()
            .Where(s => s.Date >= from && s.Date < to)
            .OrderByDescending(s => s.Date)
            .ToListAsync();
    }

    // one query for many sessions, instead of one query per session
    public async Task<List<ExerciseLog>> GetLogsForSessionsAsync(List<int> sessionIds)
    {
        await Init();
        if (sessionIds.Count == 0) return new List<ExerciseLog>();
        return await _db.Table<ExerciseLog>()
            .Where(l => sessionIds.Contains(l.WorkoutSessionId))
            .ToListAsync();
    }

    public async Task<List<SetEntry>> GetSetsForLogsAsync(List<int> logIds)
    {
        await Init();
        if (logIds.Count == 0) return new List<SetEntry>();
        return await _db.Table<SetEntry>()
            .Where(s => logIds.Contains(s.ExerciseLogId))
            .ToListAsync();
    }

    public async Task<Exercise> GetExerciseByIdAsync(int id)
    {
        await Init();
        return await _db.Table<Exercise>().FirstOrDefaultAsync(e => e.Id == id);
    }
    
// GYM TRACKER — DatabaseService.cs additions



// one transaction, three statements: a half-deleted workout can't happen
public async Task DeleteSessionAsync(int sessionId)
{
    await Init();

    await _db.RunInTransactionAsync(conn =>
    {
        conn.Execute("DELETE FROM SetEntry WHERE ExerciseLogId IN " +
                     "(SELECT Id FROM ExerciseLog WHERE WorkoutSessionId = ?)", sessionId);
        conn.Execute("DELETE FROM ExerciseLog WHERE WorkoutSessionId = ?", sessionId);
        conn.Execute("DELETE FROM WorkoutSession WHERE Id = ?", sessionId);
    });
}
 
public async Task UpdateExerciseAsync(Exercise exercise)
{
    await Init();
    await _db.UpdateAsync(exercise);
}
 
public async Task DeleteExerciseAsync(Exercise exercise)
{
    await Init();
    await _db.DeleteAsync(exercise);
}
 
public async Task<int> CountLogsForExerciseAsync(int exerciseId)
{
    await Init();
    return await _db.Table<ExerciseLog>().Where(l => l.ExerciseId == exerciseId).CountAsync();
}



public async Task<List<(Exercise Exercise, SetEntry Set, DateTime Date)>> GetRecentPRsAsync(int daysBack = 7)
{
    await Init();

    var prs = new List<(Exercise Exercise, SetEntry Set, DateTime Date)>();
    var cutoff = DateTime.Now.AddDays(-daysBack);

    // The old version ran queries inside nested loops, and the Home tab runs this every time
    // it opens, so it got slower with every workout you logged. This one does 5 queries total,
    // no matter how much history there is.

    // sessions are tiny rows (no exercises or sets), so reading them all is cheap
    var allSessions = await _db.Table<WorkoutSession>().ToListAsync();
    var recent = allSessions.Where(x => x.Date >= cutoff).OrderBy(x => x.Date).ToList();
    if (recent.Count == 0) return prs;

    var recentLogs = await GetLogsForSessionsAsync(recent.Select(x => x.Id).ToList());
    if (recentLogs.Count == 0) return prs;

    // every log and set ever logged for the exercises trained this week (that's the "previous best" data)
    var exerciseIds = recentLogs.Select(l => l.ExerciseId).Distinct().ToList();
    var historyLogs = await _db.Table<ExerciseLog>()
        .Where(l => exerciseIds.Contains(l.ExerciseId))
        .ToListAsync();

    var idList = string.Join(",", exerciseIds);   // ints from our own table, safe to inline
    var historySets = await _db.QueryAsync<SetEntry>(
        "SELECT st.* FROM SetEntry st JOIN ExerciseLog l ON l.Id = st.ExerciseLogId " +
        $"WHERE l.ExerciseId IN ({idList})");

    var exercises = (await GetExercisesAsync()).ToDictionary(e => e.Id);

    var dateBySession = allSessions.ToDictionary(x => x.Id, x => x.Date);
    var logsByExercise = historyLogs.ToLookup(l => l.ExerciseId);
    var setsByLog = historySets.ToLookup(st => st.ExerciseLogId);

    foreach (var session in recent)
    {
        foreach (var log in recentLogs.Where(l => l.WorkoutSessionId == session.Id).OrderBy(l => l.Id))
        {
            if (!exercises.TryGetValue(log.ExerciseId, out var exercise))
                continue;

            // best weight lifted for this exercise in sessions before this one
            double previousBest = 0;
            foreach (var other in logsByExercise[log.ExerciseId])
            {
                if (!dateBySession.TryGetValue(other.WorkoutSessionId, out var otherDate) || otherDate >= session.Date)
                    continue;

                foreach (var old in setsByLog[other.Id])
                    previousBest = Math.Max(previousBest, old.Weight);
            }

            foreach (var set in setsByLog[log.Id].OrderBy(st => st.Id))
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

public async Task<int> GetWeeklyGoalAsync()
{
    await Init();
    var row = await _db.Table<AppSetting>().FirstOrDefaultAsync(s => s.Key == "WeeklyGoal");
    return row is not null && int.TryParse(row.Value, out var g) && g > 0 ? g : 3;
}
 
public async Task SetWeeklyGoalAsync(int goal)
{
    await Init();
    await _db.InsertOrReplaceAsync(new AppSetting { Key = "WeeklyGoal", Value = goal.ToString() });
}
 
//how many different days you trained this week
public async Task<(int Streak, int ThisWeek)> GetWeeklyStreakAsync(int goal)
{
    await Init();
    goal = Math.Max(1, goal);
 
    var sessions = await _db.Table<WorkoutSession>().ToListAsync();
    var counts = sessions
        .GroupBy(s => StartOfWeek(s.Date))
        .ToDictionary(g => g.Key, g => g.Select(s => s.Date.Date).Distinct().Count());
 
    var week = StartOfWeek(DateTime.Today);
    counts.TryGetValue(week, out int thisWeek);
 
    int streak = 0;
 
    // The current week only adds to the streak once the goal is hit,
    // and never breaks it while still in progress.
    if (thisWeek >= goal)
        streak++;
 
    week = week.AddDays(-7);
    while (counts.TryGetValue(week, out int c) && c >= goal)
    {
        streak++;
        week = week.AddDays(-7);
    }
 
    return (streak, thisWeek);
}
 
private static DateTime StartOfWeek(DateTime date)
{
    int diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
    return date.Date.AddDays(-diff);
}
    
private const string DraftKey = "DraftWorkout";

public async Task SaveDraftAsync(string json)
{
    await Init();
    await _db.InsertOrReplaceAsync(new AppSetting { Key = DraftKey, Value = json });
}

public async Task<string?> GetDraftAsync()
{
    await Init();
    var row = await _db.Table<AppSetting>().FirstOrDefaultAsync(s => s.Key == DraftKey);
    return row?.Value;
}

public async Task ClearDraftAsync()
{
    await Init();
    await _db.Table<AppSetting>().Where(s => s.Key == DraftKey).DeleteAsync();
}

    
}