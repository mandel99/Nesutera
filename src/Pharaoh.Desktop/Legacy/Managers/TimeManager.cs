using System.Diagnostics;
using Pharaoh.Engine.Simulation;
using Pharaoh.Engine.ReverseEngineering;

namespace Pharaoh.Desktop.Legacy.Managers;

/// <summary>Owns monotonic UI deadlines, camera repeats and the city simulation clock.</summary>
internal sealed class TimeManager
{
    private long nextCameraMoveAt;
    private long simulationTimestamp;
    private int simulationStartYear;
    private int simulationStartMonth;
    private bool manuallyPaused;
    private bool inspectionPaused;

    public DateTime NameCaretDeadline { get; set; } = DateTime.UtcNow.AddMilliseconds(500);
    public DateTime TransitionDeadline { get; set; }
    public DateTime LoadingDeadline { get; set; }
    public DateTime TitleMouseBlockedUntil { get; set; }
    public LegacySimulationClock SimulationClock { get; } = new();
    public bool IsSimulationPaused => manuallyPaused || inspectionPaused;
    public LegacyCalendarTime Calendar => SimulationClock.GetCalendar(simulationStartYear, simulationStartMonth);

    public LegacySimulationAdvance UpdateSimulation(int gameSpeed, bool enabled)
    {
        long now = Stopwatch.GetTimestamp();
        if (simulationTimestamp == 0)
        {
            simulationTimestamp = now;
            return SimulationClock.Advance(TimeSpan.Zero, gameSpeed, IsSimulationPaused, enabled);
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(simulationTimestamp, now);
        simulationTimestamp = now;
        return SimulationClock.Advance(elapsed, gameSpeed, IsSimulationPaused, enabled);
    }

    public LegacySimulationAdvance AdvanceSimulation(TimeSpan elapsed, int gameSpeed, bool enabled = true) =>
        SimulationClock.Advance(elapsed, gameSpeed, IsSimulationPaused, enabled);

    public bool ToggleSimulationPause()
    {
        manuallyPaused = !manuallyPaused;
        simulationTimestamp = Stopwatch.GetTimestamp();
        return IsSimulationPaused;
    }

    [RewriteOnly("Keeps the inspection pause reason independent of manual P pause and resets the wall-clock baseline")]
    public void SetInspectionPause(bool paused)
    {
        inspectionPaused=paused;
        simulationTimestamp=Stopwatch.GetTimestamp();
    }

    public void ResetSimulation(int startYear = 0, int startMonth = 0)
    {
        SimulationClock.Reset();
        simulationStartYear = startYear;
        simulationStartMonth = startMonth;
        manuallyPaused = inspectionPaused = false;
        simulationTimestamp = Stopwatch.GetTimestamp();
    }

    public bool IsCameraStepDue(int intervalMilliseconds)
    {
        long now = Environment.TickCount64;
        if (now < nextCameraMoveAt)
            return false;
        nextCameraMoveAt = now + intervalMilliseconds;
        return true;
    }

    public void ResetCameraRepeat() => nextCameraMoveAt = 0;
}
