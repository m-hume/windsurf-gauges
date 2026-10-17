using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;
using MacroDeck.Ui.Runtime;

namespace WindsurfGauges;

internal static class GaugeWidgetPreviews
{
	private static readonly UiState<GaugeFace> Daily = new(
		new GaugeFace { Level = 0.12, Value = "12", Color = null, Name = "Daily used" });

	private static readonly UiState<GaugeFace> Weekly = new(
		new GaugeFace { Level = 0.32, Value = "32", Color = null, Name = "Weekly used" });

	private static WindsurfGaugesData SampleConfig(bool arc) => new()
	{
		Title = "Windsurf",
		Style = arc ? WindsurfGaugesData.StyleArc : WindsurfGaugesData.StyleRing,
		Gauges =
		[
			new WindsurfGaugeConfig { Id = "daily", Variable = "windsurf_daily_used", Name = "Daily used", Max = 100, ValueSuffix = "%" },
			new WindsurfGaugeConfig { Id = "weekly", Variable = "windsurf_weekly_used", Name = "Weekly used", Max = 100, ValueSuffix = "%" }
		]
	};

	[UiPreview("Two gauges, ring", Profile = UiPreviewProfiles.Widget)]
	public static UiElement TwoRings() => WindsurfGaugeWidget.Build(
		SampleConfig(arc: false), new UiState<string>("Windsurf"), [Daily, Weekly], null);

	[UiPreview("Two gauges, arc", Profile = UiPreviewProfiles.Widget)]
	public static UiElement TwoArcs() => WindsurfGaugeWidget.Build(
		SampleConfig(arc: true), new UiState<string>("Windsurf"), [Daily, Weekly], null);

	[UiPreview("Single gauge, no label", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Single() => WindsurfGaugeWidget.Build(
		new WindsurfGaugesData
		{
			Gauges = [new WindsurfGaugeConfig { Id = "daily", Variable = "windsurf_daily_used", Max = 100 }]
		},
		null,
		[new UiState<GaugeFace>(new GaugeFace { Level = 0.12, Value = "12" })],
		null);

	[UiPreview("Empty", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Empty() => WindsurfGaugeWidget.Build(
		new WindsurfGaugesData(), null, [], null);

	[UiPreview("Warning colour", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Warning() => WindsurfGaugeWidget.Build(
		new WindsurfGaugesData
		{
			Gauges = [new WindsurfGaugeConfig { Id = "daily", Variable = "windsurf_daily_used", Name = "Daily used", Max = 100, ValueSuffix = "%" }]
		},
		new UiState<string>("Windsurf"),
		[new UiState<GaugeFace>(new GaugeFace { Level = 0.93, Value = "93", Color = GaugeFace.WarningColor, Name = "Daily used" })],
		null);
}
