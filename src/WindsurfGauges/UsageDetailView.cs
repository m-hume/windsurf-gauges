using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace WindsurfGauges;

/// <summary>
/// The usage splash shown as a modal when the widget is pressed: two large arc gauges for the
/// daily and weekly quota plus a row per remaining account figure. Lengths are fractions of the
/// dialog box, so the same tree lays itself out on a desktop window and a phone.
/// </summary>
internal static class UsageDetailView
{
	private const double ArcStart = -135;
	private const double ArcEnd = 135;
	private const double RingThickness = 0.045;

	private static readonly UiSize NameSize = UiSize.FromBasis(0.05);
	private static readonly UiSize ValueSize = UiSize.FromBasis(0.13);
	private static readonly UiSize LabelSize = UiSize.FromBasis(0.042);
	private static readonly UiSize DetailSize = UiSize.FromBasis(0.045);

	public static UiElement Build(
		UiState<GaugeFace> daily,
		UiState<GaugeFace> weekly,
		UiState<UsageDetailText> text) => new UiStack
	{
		Key = "detail",
		Direction = UiComponentDirections.Vertical,
		Fill = true,
		Padding = UiSize.FromBasis(0.045),
		Gap = UiSize.FromBasis(0.035),
		Children =
		[
			new UiGrid
			{
				Key = "gauges",
				Columns = UiValue.Of(2),
				Fill = true,
				MinCellSize = UiSize.FromBasis(0.38),
				Gap = UiSize.FromBasis(0.05),
				Children =
				[
					GaugeCell("daily", daily, Strings.Dialog.Usage.Daily()),
					GaugeCell("weekly", weekly, Strings.Dialog.Usage.Weekly())
				]
			},
			new UiStack
			{
				Key = "info",
				Direction = UiComponentDirections.Vertical,
				Gap = UiSize.FromBasis(0.014),
				Children =
				[
					InfoRow("plan", Strings.Dialog.Usage.Plan(), () => text.Value.Plan),
					InfoRow("planReset", Strings.Dialog.Usage.PlanReset(), () => text.Value.PlanReset),
					InfoRow("extra", Strings.Dialog.Usage.ExtraBalance(), () => text.Value.ExtraBalance),
					InfoRow("dailyReset", Strings.Dialog.Usage.DailyReset(), () => text.Value.DailyReset),
					InfoRow("weeklyReset", Strings.Dialog.Usage.WeeklyReset(), () => text.Value.WeeklyReset),
					InfoRow("account", Strings.Dialog.Usage.Account(), () => text.Value.Account),
					InfoRow("updated", Strings.Dialog.Usage.Updated(), () => text.Value.Updated)
				]
			}
		]
	};

	private static UiLayer GaugeCell(string key, UiState<GaugeFace> face, MacroDeck.Localization.LocalizedText name) =>
		new UiLayer
		{
			Key = key,
			Fill = true,
			Children =
			[
				new UiGauge
				{
					Key = "ring",
					Fill = true,
					Thickness = UiSize.FromBasis(RingThickness),
					StartAngle = UiValue.Of(ArcStart),
					EndAngle = UiValue.Of(ArcEnd),
					Level = UiValue.From(() => face.Value.Level),
					LevelColor = UiValue.Optional(() => face.Value.Color is { } color
						? UiValue.Of(color)
						: UiValue.None<string>()),
					Fallback = new UiRangeBar
					{
						Key = "ringFallback",
						Thickness = UiSize.FromBasis(0.02),
						Start = 0d,
						End = UiValue.From(() => face.Value.Level)
					}
				},
				new UiStack
				{
					Key = "centre",
					Fill = true,
					Justify = UiValue.Of(UiComponentJustify.Center),
					Align = UiValue.Of(UiComponentAlignments.Center),
					Gap = UiSize.FromBasis(0.005),
					Children =
					[
						new UiTextRun
						{
							Key = "name",
							Text = name,
							Size = NameSize,
							Role = UiValue.Of(UiComponentTextRoles.Muted),
							Align = UiValue.Of(UiComponentAlignments.Center),
							MaxLines = 1
						},
						new UiTextRun
						{
							Key = "value",
							Text = UiText.From(() => face.Value.Value + "%"),
							Size = ValueSize,
							Weight = UiValue.Of(UiComponentTextWeights.SemiBold),
							Role = UiValue.Of(UiComponentTextRoles.Primary),
							Align = UiValue.Of(UiComponentAlignments.Center),
							MaxLines = 1
						}
					]
				}
			]
		};

	// A label on the left, the figure on the right; both fill halves of the row.
	private static UiStack InfoRow(string key, MacroDeck.Localization.LocalizedText label, Func<string> text) => new()
	{
		Key = key,
		Direction = UiComponentDirections.Horizontal,
		Children =
		[
			new UiTextRun
			{
				Key = "label",
				Text = label,
				Size = LabelSize,
				Role = UiValue.Of(UiComponentTextRoles.Muted),
				Fill = true,
				MaxLines = 1
			},
			new UiTextRun
			{
				Key = "value",
				Text = UiText.From(text),
				Size = DetailSize,
				Role = UiValue.Of(UiComponentTextRoles.Primary),
				Fill = true,
				Align = UiValue.Of(UiComponentAlignments.End),
				MaxLines = 1
			}
		]
	};
}

/// <summary>The resolved text of every info row, swapped as one state so rows update together.</summary>
internal sealed record UsageDetailText
{
	public string Plan { get; init; } = GaugeFace.Unavailable;
	public string PlanReset { get; init; } = GaugeFace.Unavailable;
	public string ExtraBalance { get; init; } = GaugeFace.Unavailable;
	public string DailyReset { get; init; } = GaugeFace.Unavailable;
	public string WeeklyReset { get; init; } = GaugeFace.Unavailable;
	public string Account { get; init; } = GaugeFace.Unavailable;
	public string Updated { get; init; } = GaugeFace.Unavailable;
}
