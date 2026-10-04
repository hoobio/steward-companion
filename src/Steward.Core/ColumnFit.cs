namespace Steward.Core;

public static class ColumnFit
{
    public static double TypicalMax(IEnumerable<double> widths)
    {
        var sorted = widths.Order().ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        var q1 = Quantile(sorted, 0.25);
        var q3 = Quantile(sorted, 0.75);
        var fence = q3 + 1.5 * (q3 - q1);
        return sorted.Last(width => width <= fence);
    }

    private static double Quantile(double[] sorted, double p)
    {
        var position = (sorted.Length - 1) * p;
        var lower = (int)position;
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}
