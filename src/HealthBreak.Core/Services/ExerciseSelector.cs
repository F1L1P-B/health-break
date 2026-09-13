using System.Text.Json;
using System.Text.Json.Serialization;
using HealthBreak.Core.Models;

namespace HealthBreak.Core.Services;

public static class ExerciseCatalog
{
    public static IReadOnlyList<Exercise> Load()
    {
        using var stream = typeof(ExerciseCatalog).Assembly.GetManifestResourceStream("HealthBreak.Core.Data.exercises.json")
            ?? throw new InvalidOperationException("The bundled exercise catalog is missing.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Deserialize<List<Exercise>>(stream, options)
            ?? throw new InvalidOperationException("The bundled exercise catalog could not be read.");
    }
}

public sealed class ExerciseSelector(IReadOnlyList<Exercise>? catalog = null)
{
    private readonly IReadOnlyList<Exercise> _catalog = catalog ?? ExerciseCatalog.Load();

    public IReadOnlyList<Exercise> Select(BreakType type, double activeSeconds,
        IReadOnlyList<ExerciseHistoryRecord> history, IReadOnlyList<BreakRecord>? recentBreaks = null, int? count = null)
    {
        var limit = Math.Clamp(count ?? (type == BreakType.Quick ? 2 : type == BreakType.Short ? 3 : 4), 1, 6);
        var recent = history.OrderByDescending(h => h.Timestamp).ToArray();
        var breakIds = recentBreaks?.OrderByDescending(b => b.StartTime).Take(3).Select(b => b.Id).ToArray()
            ?? recent.Select(h => h.BreakId).Distinct().Take(3).ToArray();
        var byId = _catalog.ToDictionary(e => e.Id);
        var repeatedMovement = breakIds.Length >= 3 && breakIds.All(id => recent.Any(h => h.BreakId == id &&
            byId.TryGetValue(h.ExerciseId, out var exercise) && exercise.Category == ExerciseCategory.Movement));
        var selected = new List<Exercise>();

        // Required categories remain present even when recent movement has a repetition penalty.
        Pick(ExerciseCategory.Eyes);
        if (activeSeconds >= 60 * 60 || type != BreakType.Quick) Pick(ExerciseCategory.Movement);
        if (activeSeconds >= 90 * 60 || type == BreakType.Full)
            Pick(BestCategory(ExerciseCategory.Neck, ExerciseCategory.Shoulders));
        while (selected.Count < limit)
        {
            var next = _catalog.Where(e => selected.All(s => s.Id != e.Id))
                .OrderByDescending(Score).ThenBy(e => e.Id).FirstOrDefault();
            if (next is null) break;
            selected.Add(next);
        }
        return selected;

        void Pick(ExerciseCategory category)
        {
            if (selected.Count >= limit) return;
            var next = _catalog.Where(e => e.Category == category && selected.All(s => s.Id != e.Id))
                .OrderByDescending(Score).ThenBy(e => e.Id).FirstOrDefault();
            if (next is not null) selected.Add(next);
        }

        ExerciseCategory BestCategory(ExerciseCategory first, ExerciseCategory second)
        {
            var best = _catalog.Where(e => e.Category == first || e.Category == second)
                .OrderByDescending(Score).ThenBy(e => e.Id).First();
            return best.Category;
        }

        double Score(Exercise exercise)
        {
            var uses = recent.Where(h => h.ExerciseId == exercise.Id).ToArray();
            var lastIndex = Array.FindIndex(recent, h => h.ExerciseId == exercise.Id);
            double score = 100;
            score += lastIndex < 0 ? 50 : Math.Min(40, lastIndex * 5);
            score -= uses.Take(5).Count(h => !h.Completed) * 14;
            if (lastIndex is >= 0 and < 6) score -= 80 - lastIndex * 8;
            if (selected.Any(e => e.Category == exercise.Category)) score -= 45;
            if (exercise.Category == ExerciseCategory.Eyes && activeSeconds >= 45 * 60) score += 25;
            if (exercise.Category == ExerciseCategory.Movement && activeSeconds >= 60 * 60) score += 25;
            if (exercise.Category == ExerciseCategory.Movement && repeatedMovement) score -= 45;
            if (type == BreakType.Quick && exercise.DurationSeconds > 30) score -= 35;
            return score;
        }
    }
}
