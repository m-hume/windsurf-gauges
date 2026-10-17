using MacroDeck.Ui.Config;

namespace WindsurfGauges;

/// <summary>
/// The built-in Gauges widget's default colour bands, ported: green to half, yellow to three
/// quarters, orange to ninety percent, red above.
/// </summary>
internal static class ThresholdDefaults
{
	public const string Green = "#34c759";
	public const string Yellow = "#ffcc00";
	public const string Orange = "#ff9500";
	public const string Red = "#ff3b30";

	private static readonly (string Id, string Color, double Fraction)[] Bands =
	[
		("green", Green, 0),
		("yellow", Yellow, 0.5),
		("orange", Orange, 0.75),
		("red", Red, 0.9)
	];

	public static UiThresholds Defaults(double min, double max)
	{
		var low = Math.Min(min, max);
		var span = Math.Abs(max - min);
		if (!double.IsFinite(low) || !double.IsFinite(span) || span <= 0)
		{
			low = 0;
			span = 100;
		}

		return new UiThresholds(
		[
			.. Bands.Select((band, index) => new UiThresholdBand(band.Id,
				band.Color,
				index == 0 ? null : low + band.Fraction * span))
		]);
	}
}
