using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;

namespace WindsurfGauges;

/// <summary>
/// The widget view: the built-in Gauges layout (title, responsive grid of rings) with this
/// plugin's twist - the value and its label are drawn inside the ring where the built-in puts the
/// icon, and the icon only shows when no label is set.
/// </summary>
internal static class WindsurfGaugeWidget
{
	public const string WidgetTypeId = "gauge";
	public static readonly string QualifiedWidgetTypeId = $"{PluginIds.Self}::{WidgetTypeId}";

	private const double FullTurn = 360;
	private const double RingThickness = 0.11;
	private const double PaddedShare = 0.8;
	private const double TitledShare = 0.85;

	private static readonly UiSize TitleSize = UiSize.Capped(0.11, 13);
	private static readonly UiSize Gap = UiSize.Capped(0.02, 3);

	public const string DefaultData =
		"""{"title":"Windsurf usage (${{ vars.windsurf_extra_balance }})","style":"arc","gauges":[{"variable":"windsurf_daily_used","name":"Daily","max":100,"id":"2ffdb9ac399347a58dda0f98b80f4485","thresholdsEnabled":true,"labelSize":0.36,"valueSize":0.24,"valueSuffix":"%"},{"variable":"windsurf_weekly_used","name":"Weekly","max":100,"id":"0cc69335353346539c7e79f42b5955f1","thresholdsEnabled":true,"valueSize":0.24,"labelSize":0.36,"valueSuffix":"%"}]}""";

	public const string DataSchema =
		"""{"type":"object","properties":{"title":{"type":"string"},"style":{"type":"string"},"gauges":{"type":"array"},"flows":{"type":"array"},"backgroundColor":{"type":"string"},"border":{"type":"object"},"borderStyle":{"type":"string"},"borderColor":{"type":"string"}},"required":[]}""";

	private static readonly Dictionary<string, UiResource> NoIcons = new();

	public static UiElement Build(
		WindsurfGaugesData config,
		UiState<string>? titleState,
		IReadOnlyList<UiState<GaugeFace>> faces,
		string? backgroundColor,
		IReadOnlyDictionary<string, UiResource>? icons = null,
		int cornerRadius = 22)
	{
		var gauges = config.Shown;
		if (faces.Count != gauges.Count)
			throw new ArgumentException("Every shown gauge needs exactly one face.", nameof(faces));

		var children = new List<UiElement>();

		if (config.Title is { Length: > 0 } && titleState is not null)
		{
			children.Add(new UiTextRun
			{
				Key = "title",
				Text = UiText.From(() => titleState.Value),
				Size = TitleSize,
				Weight = UiValue.Of(UiComponentTextWeights.SemiBold),
				Role = UiValue.Of(UiComponentTextRoles.Primary),
				MaxLines = 1
			});
		}

		var resources = icons ?? NoIcons;
		var segments = GaugeLayout.For(gauges.Count);

		children.Add(gauges.Count == 0
			? EmptyHint()
			: segments.Count == 1
				? Layout(segments[0], config, gauges, faces, resources) with { Fill = true }
				: Responsive(segments, config, gauges, faces, resources));

		return new UiStack
		{
			Key = "gauges",
			Direction = UiComponentDirections.Vertical,
			Align = UiValue.Of(UiComponentAlignments.Start),
			Gap = Gap,
			Padding = SafeArea(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Children = children
		};
	}

	// The host's WidgetSafeArea.For insets content by the corner radius; approximate it.
	private static UiSize SafeArea(int cornerRadius) => UiSize.Capped(0.04, cornerRadius / 2.0);

	private static UiStack EmptyHint() => new()
	{
		Key = "emptyHint",
		Fill = true,
		Justify = UiValue.Of(UiComponentJustify.Center),
		Align = UiValue.Of(UiComponentAlignments.Center),
		Children =
		[
			new UiTextRun
			{
				Key = "emptyText",
				Text = UiText.FromLocalized(() => Strings.Widgets.Gauge.EmptyHint()),
				Size = UiSize.Capped(0.09, 11),
				Role = UiValue.Of(UiComponentTextRoles.Muted),
				Align = UiValue.Of(UiComponentAlignments.Center),
				Wrap = true
			}
		]
	};

	private static UiResponsive Responsive(
		IReadOnlyList<GaugeLayoutSegment> segments,
		WindsurfGaugesData config,
		IReadOnlyList<WindsurfGaugeConfig> gauges,
		IReadOnlyList<UiState<GaugeFace>> faces,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		var main = segments.FirstOrDefault(segment => segment.Contains(1)) ?? segments[0];
		var others = segments
			.Where(segment => segment != main)
			.Select(segment => new UiResponsiveVariant
			{
				MinAspect = segment.MinAspect,
				MaxAspect = segment.MaxAspect,
				Content = Layout(segment, config, gauges, faces, icons)
			})
			.ToList();

		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = Layout(main, config, gauges, faces, icons),
			Variants = others
		};
	}

	private static UiGrid Layout(
		GaugeLayoutSegment segment,
		WindsurfGaugesData config,
		IReadOnlyList<WindsurfGaugeConfig> gauges,
		IReadOnlyList<UiState<GaugeFace>> faces,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		var prefix = $"c{segment.Columns}";
		var scale = segment.RingScale * PaddedShare * (config.Title is null ? 1 : TitledShare);

		return new UiGrid
		{
			Key = $"{prefix}-grid",
			Columns = segment.Columns,
			Rows = segment.Rows,
			Gap = UiSize.FromBasis(0.12 * scale),
			Children = gauges
				.Select((gauge, index) => Cell($"g{index}", scale, config, gauge, faces[index], icons))
				.ToList()
		};
	}

	private static UiStack Cell(
		string key,
		double scale,
		WindsurfGaugesData config,
		WindsurfGaugeConfig gauge,
		UiState<GaugeFace> face,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Justify = UiValue.Of(UiComponentJustify.Center),
			Align = UiValue.Of(UiComponentAlignments.Center),
			Children =
			[
				new UiLayer
				{
					Key = "dial",
					Fill = true,
					Children = [Ring(scale, config, gauge, face), Centre(scale, gauge, face, icons)]
				}
			]
		};
	}

	private static UiGauge Ring(
		double scale,
		WindsurfGaugesData config,
		WindsurfGaugeConfig gauge,
		UiState<GaugeFace> face)
	{
		return new UiGauge
		{
			Key = "ring",
			Fill = true,
			Thickness = UiSize.FromBasis(RingThickness * scale),
			StartAngle = config.IsArc ? UiValue.Of(-135d) : UiValue.Of(0d),
			EndAngle = config.IsArc ? UiValue.Of(135d) : UiValue.Of(FullTurn),
			Level = UiValue.From(() => face.Value.Level),
			LevelColor = UiValue.Optional(() => face.Value.Color is { } color
				? UiValue.Of(color)
				: UiValue.None<string>()),
			Fallback = new UiRangeBar
			{
				Key = "ringFallback",
				Thickness = UiSize.FromBasis(0.05 * scale),
				Start = 0d,
				End = UiValue.From(() => face.Value.Level)
			}
		};
	}

	/// <summary>
	/// The middle of the ring: the value with its suffix, the label above it when one is set, or -
	/// only when no label is set - the configured icon, as on the built-in gauge.
	/// </summary>
	private static UiStack Centre(
		double scale,
		WindsurfGaugeConfig gauge,
		UiState<GaugeFace> face,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		var children = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "value",
				Text = UiText.From(() => face.Value.Value + (gauge.ValueSuffix ?? "")),
				Size = UiSize.FromBasis(gauge.ValueSize, 0.5),
				Weight = UiValue.Of(UiComponentTextWeights.SemiBold),
				Role = UiValue.Of(UiComponentTextRoles.Primary),
				Align = UiValue.Of(UiComponentAlignments.Center),
				MaxLines = 1
			}
		};

		if (gauge.Name is { Length: > 0 })
		{
			children.Insert(0, new UiTextRun
			{
				Key = "name",
				Text = UiText.From(() => face.Value.Name),
				Size = UiSize.FromBasis(gauge.LabelSize, 0.5),
				Role = UiValue.Of(UiComponentTextRoles.Muted),
				Align = UiValue.Of(UiComponentAlignments.Center),
				MaxLines = 1
			});
		}
		else if (gauge.Icon is { } icon && icons.TryGetValue(icon.Reference, out var resource))
		{
			var size = UiSize.FromBasis(0.36 * scale, 0.36);
			children.Insert(0, gauge.IconColor is { } tint
				? new UiButton
					{ Key = "icon", Source = UiValue.Of(resource), Tint = tint, MainSize = size, Background = "transparent" }
				: new UiImage { Key = "icon", Source = UiValue.Of(resource), Size = size });
		}

		return new UiStack
		{
			Key = "centre",
			Fill = true,
			Justify = UiValue.Of(UiComponentJustify.Center),
			Align = UiValue.Of(UiComponentAlignments.Center),
			Gap = UiSize.FromBasis(0.02 * scale),
			Children = children
		};
	}
}
