namespace RestaurantMenu.Helpers;

/// <summary>Round axis ticks (0, 5, 10, 15 / 0, 200, 400…) for the server-rendered charts.</summary>
public static class ChartScale
{
    /// <summary>The axis maximum and step for values up to <paramref name="max"/>, about <paramref name="ticks"/> steps.</summary>
    public static (int Max, int Step) Nice(int max, int ticks = 4)
    {
        if (max <= 0) return (ticks, 1);
        var raw = (double)max / ticks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 5, 10 }.Select(m => m * magnitude).First(s => s >= raw);
        var intStep = Math.Max(1, (int)Math.Ceiling(step));
        return ((int)Math.Ceiling((double)max / intStep) * intStep, intStep);
    }
}
