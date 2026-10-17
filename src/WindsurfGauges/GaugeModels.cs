using System.Text.Json;
using MacroDeck.Ui.Config;

namespace WindsurfGauges;

/// <summary>One gauge inside the widget: the built-in Gauges settings plus this plugin's extensions.</summary>
public sealed record WindsurfGaugeConfig
{
	public const string WarnAbove = "above";
	public const string WarnBelow = "below";

	public required string Id { get; init; }
	public string Variable { get; init; } = "";
	public string? Name { get; init; }
	public UiIconReference? Icon { get; init; }
	public string? IconColor { get; init; }
	public double? Min { get; init; }
	public double? Max { get; init; }
	public string? Color { get; init; }
	public string? WarnWhen { get; init; }
	public double? WarnAt { get; init; }
	public bool ThresholdsEnabled { get; init; }
	public UiThresholds? Thresholds { get; init; }

	// Extensions: the value and label drawn inside the arc, with independent font sizes, and a
	// template-aware suffix after the value.
	public double ValueSize { get; init; } = WindsurfGaugesData.DefaultValueSize;
	public double LabelSize { get; init; } = WindsurfGaugesData.DefaultLabelSize;
	public string? ValueSuffix { get; init; }

	public static WindsurfGaugeConfig Parse(JsonElement item, string id)
	{
		var warnWhen = ReadString(item, "warnWhen");
		return new WindsurfGaugeConfig
		{
			Id = id,
			Variable = ReadString(item, "variable") ?? "",
			Name = ReadString(item, "name") is { Length: > 0 } name ? name : null,
			Icon = ReadIcon(item),
			Min = ReadDouble(item, "min"),
			Max = ReadDouble(item, "max"),
			Color = ReadString(item, "color"),
			IconColor = ReadString(item, "iconColor"),
			WarnWhen = warnWhen is WarnAbove or WarnBelow ? warnWhen : null,
			WarnAt = ReadDouble(item, "warnAt"),
			ThresholdsEnabled = item.TryGetProperty("thresholdsEnabled", out var enabled) &&
				enabled.ValueKind == JsonValueKind.True,
			Thresholds = ReadThresholds(item),
			ValueSize = ReadFraction(item, "valueSize", WindsurfGaugesData.DefaultValueSize),
			LabelSize = ReadFraction(item, "labelSize", WindsurfGaugesData.DefaultLabelSize),
			ValueSuffix = ReadString(item, "valueSuffix")
		};
	}

	private static UiIconReference? ReadIcon(JsonElement item)
	{
		if (!item.TryGetProperty("icon", out var icon) || icon.ValueKind != JsonValueKind.Object)
			return null;
		var type = icon.TryGetProperty("type", out var typeElement) &&
			typeElement.ValueKind == JsonValueKind.String
				? typeElement.GetString()
				: null;
		var reference = icon.TryGetProperty("reference", out var referenceElement) &&
			referenceElement.ValueKind == JsonValueKind.String
				? referenceElement.GetString()
				: null;
		return type is { Length: > 0 } && reference is { Length: > 0 }
			? new UiIconReference(type, reference)
			: null;
	}

	private static UiThresholds? ReadThresholds(JsonElement item)
	{
		if (!item.TryGetProperty("thresholds", out var thresholds) ||
			thresholds.ValueKind != JsonValueKind.Object ||
			!UiThresholds.TryParse(thresholds, out var parsed))
			return null;
		return parsed;
	}

	private static string? ReadString(JsonElement data, string name) =>
		data.ValueKind == JsonValueKind.Object &&
		data.TryGetProperty(name, out var value) &&
		value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static double? ReadDouble(JsonElement data, string name) =>
		data.ValueKind == JsonValueKind.Object &&
		data.TryGetProperty(name, out var value) &&
		value.ValueKind == JsonValueKind.Number &&
		value.TryGetDouble(out var number) &&
		double.IsFinite(number)
			? number
			: null;

	private static double ReadFraction(JsonElement data, string name, double fallback) =>
		ReadDouble(data, name) is { } fraction && fraction > 0.005 && fraction <= 0.6
			? fraction
			: fallback;
}

/// <summary>The stored data shape of the widget.</summary>
public sealed record WindsurfGaugesData
{
	public const int MaxGauges = 8;
	public const string StyleRing = "ring";
	public const string StyleArc = "arc";
	public const double DefaultValueSize = 0.24;
	public const double DefaultLabelSize = 0.36;

	public string? Title { get; init; }
	public string Style { get; init; } = StyleRing;
	public IReadOnlyList<WindsurfGaugeConfig> Gauges { get; init; } = [];

	public bool IsArc => Style == StyleArc;

	public IReadOnlyList<WindsurfGaugeConfig> Shown =>
		Gauges.Count > MaxGauges ? Gauges.Take(MaxGauges).ToList() : Gauges;

	public static WindsurfGaugesData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
			return new WindsurfGaugesData();

		return new WindsurfGaugesData
		{
			Title = ReadString(data, "title") is { Length: > 0 } title ? title : null,
			Style = ReadString(data, "style") == StyleArc ? StyleArc : StyleRing,
			Gauges = ParseGauges(data.TryGetProperty("gauges", out var gauges) ? gauges : default)
		};
	}

	public static IReadOnlyList<WindsurfGaugeConfig> ParseGauges(JsonElement gauges)
	{
		if (gauges.ValueKind != JsonValueKind.Array)
			return [];

		var result = new List<WindsurfGaugeConfig>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var index = 0;

		foreach (var item in gauges.EnumerateArray())
		{
			index++;
			if (item.ValueKind != JsonValueKind.Object)
				continue;

			var id = ReadString(item, "id") is { } stored && IsUsableId(stored) && seen.Add(stored)
				? stored
				: null;
			if (id is null)
			{
				id = $"gauge-{index}";
				while (!seen.Add(id))
					id += "-";
			}

			result.Add(WindsurfGaugeConfig.Parse(item, id));
		}

		return result;
	}

	// Ids become node keys in the editor tree, which only accepts short identifiers.
	private static bool IsUsableId(string id) =>
		id.Length is > 0 and <= 32 &&
		id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

	private static string? ReadString(JsonElement data, string name) =>
		data.ValueKind == JsonValueKind.Object &&
		data.TryGetProperty(name, out var value) &&
		value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
}
