using HealthBreak.Core.Models;

namespace HealthBreak.Core.Services;

public sealed record HealthScoreResult(int Score, string Status);

public static class HealthScore
{
    public static HealthScoreResult Calculate(DailyStatistics statistics, double currentSessionSeconds = 0)
    {
        // This is a habit indicator, not a measurement of health or a medical assessment.
        var longestMinutes = Math.Max(statistics.LongestSessionSeconds, currentSessionSeconds) / 60;
        var longSessionPenalty = Math.Min(35, Math.Max(0, longestMinutes - 45) * 0.4);
        var skippedPenalty = Math.Min(40, statistics.BreaksSkipped * 3);
        var snoozePenalty = Math.Min(12, statistics.Snoozes * 1.5);
        // Emergency actions are already included in BreaksSkipped, so this is only an extra weight.
        var emergencyPenalty = Math.Min(16, statistics.EmergencySkips * 4);
        var recoveryBonus = Math.Min(20, statistics.Breaks * 4)
            + Math.Min(20, statistics.ExercisesCompleted * 2);
        var points = 100 - longSessionPenalty - skippedPenalty - snoozePenalty
            - emergencyPenalty + recoveryBonus;
        var score = (int)Math.Round(Math.Clamp(points, 0, 100), MidpointRounding.AwayFromZero);
        return new(score, score >= 80 ? "Excellent" : score >= 60 ? "Good" : score >= 40 ? "Needs attention" : "Poor");
    }
}
