namespace WindsurfGauges;

/// <summary>Ported from the built-in Gauges widget's layout: how many columns of rings a tile shape fits.</summary>
internal sealed record GaugeLayoutSegment(int Columns, int Rows, double? MinAspect, double? MaxAspect)
{
	public bool Contains(double aspect) =>
		(MinAspect is not { } min || aspect >= min) && (MaxAspect is not { } max || aspect < max);

	// The plugin never learns the real tile, so sizes assume the deck tile shape this layout most likely fills.
	public double RingScale
	{
		get
		{
			var aspect = TypicalAspect();
			var width = Math.Max(aspect, 1);
			var height = Math.Max(1 / aspect, 1);
			return Math.Min(width / Columns, height / (GaugeLayout.CellHeightRatio * Rows));
		}
	}

	private double TypicalAspect()
	{
		var typical = GaugeLayout.TileAspects.Where(Contains).ToList();
		return typical.Count > 0
			? typical.MinBy(aspect => Math.Abs(Math.Log(aspect)))
			: Math.Sqrt((MinAspect ?? 0.2) * (MaxAspect ?? 8));
	}
}

internal static class GaugeLayout
{
	public const double CellHeightRatio = 1.45;

	// Content boxes of 1x3, 1x2, 1x1, 2x1 and 3x1 tiles once the safe-area padding is taken off.
	public static readonly double[] TileAspects = [0.27, 0.42, 1, 2.35, 3.7];

	private const double AspectStep = 0.01;
	private const double LowestAspect = 0.2;
	private const double HighestAspect = 8;

	public static IReadOnlyList<GaugeLayoutSegment> For(int count)
	{
		if (count <= 1)
			return [new GaugeLayoutSegment(1, 1, null, null)];

		var segments = new List<GaugeLayoutSegment>();
		int? current = null;
		double? start = null;

		for (var step = 0; ; step++)
		{
			var aspect = Math.Round(LowestAspect + step * AspectStep, 2);
			if (aspect > HighestAspect)
				break;

			var columns = Best(count, aspect);
			if (columns == current)
				continue;

			if (current is { } previous)
				segments.Add(new GaugeLayoutSegment(previous, RowsFor(count, previous), start, aspect));

			current = columns;
			start = step == 0 ? null : aspect;
		}

		segments.Add(new GaugeLayoutSegment(current!.Value, RowsFor(count, current.Value), start, null));
		return segments;
	}

	public static int RowsFor(int count, int columns) => (count + columns - 1) / columns;

	private static int Best(int count, double aspect)
	{
		var best = 1;
		var bestScore = double.MinValue;

		for (var columns = 1; columns <= count; columns++)
		{
			var score = Math.Min(aspect / columns, 1 / (CellHeightRatio * RowsFor(count, columns)));
			if (score > bestScore + 1e-9)
			{
				best = columns;
				bestScore = score;
			}
		}

		return best;
	}
}
