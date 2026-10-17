using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

namespace WindsurfGauges;

/// <summary>
/// The widget's configuration, mirroring the built-in Gauges editor (title, style, gauge list with
/// add/remove, per-gauge variable/name/icon/colours/bounds/thresholds/warning, appearance, flows)
/// extended with this plugin's fields: the templated title, the inside label and value font sizes
/// and the template-aware value suffix.
/// </summary>
internal sealed class GaugeConfigSession : IUiSession
{
	private const string None = "none";

	private readonly UiView _view;

	public GaugeConfigSession(UiSurface surface, WindsurfGaugesData data, JsonElement storedData)
	{
		// Every read below expects an object; a widget placed before this schema existed may carry none.
		if (storedData.ValueKind != JsonValueKind.Object)
			storedData = JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

		var title = new UiState<string>(data.Title ?? "");
		var style = new UiState<string>(data.Style);
		var gauges = new UiState<List<JsonObject>>(ReadGauges(storedData));
		var selected = new UiState<string>(gauges.Value.Count > 0 ? IdOf(gauges.Value[0]) : "");
		var backgroundColor = new UiState<string>(ReadString(storedData, "backgroundColor") ?? "");
		var flows = new UiState<JsonElement>(storedData.ValueKind == JsonValueKind.Object &&
			storedData.TryGetProperty("flows", out var f)
				? f
				: JsonSerializer.SerializeToElement(Array.Empty<object>()));

		bool HasRoom() => gauges.Value.Count < WindsurfGaugesData.MaxGauges;

		string Selected() => gauges.Value.Any(gauge => IdOf(gauge) == selected.Value)
			? selected.Value
			: gauges.Value.Count > 0
				? IdOf(gauges.Value[0])
				: "";

		void Append(JsonObject gauge)
		{
			if (!HasRoom())
				return;
			var id = Guid.NewGuid().ToString("N");
			gauge["id"] = id;
			gauges.Value = [.. gauges.Value, gauge];
			selected.Value = id;
		}

		void RemoveSelected()
		{
			var id = Selected();
			var index = gauges.Value.FindIndex(gauge => IdOf(gauge) == id);
			if (index < 0)
				return;

			var remaining = gauges.Value.Where((_, position) => position != index).ToList();
			gauges.Value = remaining;
			selected.Value = remaining.Count > 0 ? IdOf(remaining[Math.Min(index, remaining.Count - 1)]) : "";
		}

		_view = new UiView(surface, new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiStringInput
					{
						Key = "title",
						Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Title.Label()),
						Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Title.Description()),
						Binding = Bind.To(title)
					},
					new UiChoiceInput
					{
						Key = "style",
						Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Style.Label()),
						Binding = Bind.To(style),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(WindsurfGaugesData.StyleRing, Strings.Widgets.Gauge.Config.Style.Ring()),
							UiOption.Of(WindsurfGaugesData.StyleArc, Strings.Widgets.Gauge.Config.Style.Arc())
						])
					},
					new UiHeading { Key = "presets-heading", Text = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Presets.Heading()) },
					new UiWhen
					{
						Key = "presets-when",
						Condition = HasRoom,
						Content = () => new UiConfigStack
						{
							Key = "presets",
							Direction = "horizontal",
							Children =
							[
								PresetButton("presetDaily",
									Strings.Widgets.Gauge.Config.Presets.Daily(),
									() => Append(Preset("windsurf_daily_used", "Daily used", "%"))),
								PresetButton("presetWeekly",
									Strings.Widgets.Gauge.Config.Presets.Weekly(),
									() => Append(Preset("windsurf_weekly_used", "Weekly used", "%"))),
							]
						}
					},
					new UiWhen
					{
						Key = "full-when",
						Condition = () => !HasRoom(),
						Content = () => new UiProse { Key = "full-hint", Text = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.FullHint()) }
					},
					new UiHeading { Key = "gauges-heading", Text = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.List.Heading()) },
					new UiConfigStack
					{
						Key = "gauge-row",
						Direction = "horizontal",
						Wrap = false,
						Children =
						[
							new UiWhen
							{
								Key = "selectedGauge-when",
								Condition = () => gauges.Value.Count > 0,
								Content = () => new UiChoiceInput
								{
									Key = "selectedGauge",
									Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.List.Heading()),
									HideLabel = true,
									RowWeight = 1,
									Transient = true,
									Binding = Bind.Custom(Selected, value => selected.Value = value),
									Options = UiValue.From(() => (IReadOnlyList<UiOption>)gauges.Value
										.Select((gauge, index) => UiOption.Of(IdOf(gauge), LabelOf(gauge, index)))
										.ToList())
								}
							},
							new UiWhen
							{
								Key = "addGauge-when",
								Condition = HasRoom,
								Content = () => new UiConfigButton
								{
									Key = "addGauge",
									Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.List.Add()),
									Icon = "plus",
									Events =
									[
										UiEventHandler.On(UiConfigEvents.Activate, () => Append(new JsonObject()))
									]
								}
							},
							new UiWhen
							{
								Key = "deleteGauge-when",
								Condition = () => gauges.Value.Count > 0,
								Content = () => new UiConfigButton
								{
									Key = "deleteGauge",
									Label = UiText.FromLocalized(() => MacroDeckStrings.Common.Delete()),
									Icon = "trash",
									Events = [UiEventHandler.On(UiConfigEvents.Activate, RemoveSelected)]
								}
							}
						]
					},
					new UiArrayInput
					{
						Key = "gauges",
						Binding = Bind.Custom(
							() => Serialize(gauges.Value),
							value => gauges.Value = Deserialize(value)),
						Children =
						[
							new UiRepeat<JsonObject>
							{
								Key = "gaugeItems",
								Items = UiValue.From(() => (IReadOnlyList<JsonObject>)gauges.Value),
								KeySelector = IdOf,
								Template = (gauge, _) => Item(gauges, IdOf(gauge), () => Selected() == IdOf(gauge))
							}
						]
					},
					// The appearance section adds its own heading; a second one would duplicate the key.
					UiWidgetAppearance.Section(
						storedData,
						UiWidgetAppearanceFields.BackgroundColor |
						UiWidgetAppearanceFields.Border)
				]
			},
			Editor = new UiWidgetEditor
			{
				Key = "editor",
				Children = [new UiActionsListEditor { Key = "flows", Binding = Bind.To(flows), CanRun = true }]
			}
		});

		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("ui-handler-fault", fault.Exception));
	}

	private static UiConfigButton PresetButton(string key, LocalizedText label, Action apply) => new()
	{
		Key = key,
		Label = label,
		Events = [UiEventHandler.On(UiConfigEvents.Activate, apply)]
	};

	// Preset names become stored, user-editable widget data, like ConfigFlowResult.Complete's
	// name: written in the default language and left alone.
	private static JsonObject Preset(string variable, string name, string suffix) => new()
	{
		["variable"] = variable,
		["name"] = name,
		["max"] = 100.0,
		["valueSize"] = 0.24,
		["labelSize"] = 0.36,
		["valueSuffix"] = suffix
	};

	private static UiObjectInput Item(UiState<List<JsonObject>> gauges, string id, Func<bool> isSelected) =>
		new()
		{
			Key = id,
			Children =
			[
				new UiWhen
				{
					Key = $"selected-when-{id}",
					Condition = isSelected,
					Content = () => new UiFragment { Key = $"fields-{id}", Children = Fields(gauges, id) }
				}
			]
		};

	private static UiElement[] Fields(UiState<List<JsonObject>> gauges, string id) =>
	[
		new UiVariablePickerInput
		{
			Key = "variable",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Variable()),
			Binding = StringField(gauges, id, "variable"),
			VariableTypes = UiValue.Of<IReadOnlyList<string>>(["numeric"])
		},
		new UiStringInput
		{
			Key = "name",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Name()),
			Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.NameHint()),
			Binding = StringField(gauges, id, "name")
		},
		new UiIconReferenceInput
		{
			Key = "icon",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Icon()),
			Binding = IconField(gauges, id)
		},
		new UiColorInput
		{
			Key = "iconColor",
			AllowVariables = true,
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.IconColor()),
			Binding = StringField(gauges, id, "iconColor"),
			SupportsReset = true,
			DefaultValue = ""
		},
		new UiNumberInput
		{
			Key = "min",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Minimum()),
			Binding = NumberField(gauges, id, "min")
		},
		new UiNumberInput
		{
			Key = "max",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Maximum()),
			Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.MaximumHint()),
			Binding = NumberField(gauges, id, "max")
		},
		new UiColorInput
		{
			Key = "color",
			AllowVariables = true,
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Color()),
			Binding = StringField(gauges, id, "color"),
			SupportsReset = true,
			DefaultValue = ""
		},
		new UiBooleanInput
		{
			Key = "thresholdsEnabled",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.ColorThresholds()),
			Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.ColorThresholdsDescription()),
			Binding = FlagField(gauges, id, "thresholdsEnabled")
		},
		new UiWhen
		{
			Key = $"thresholds-when-{id}",
			Condition = () => ThresholdsOn(gauges, id),
			Content = () => new UiThresholdsInput
			{
				Key = "thresholds",
				AllowVariables = true,
				Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.ColorThresholds()),
				HideLabel = true,
				Binding = ThresholdsField(gauges, id),
				Min = UiValue.From(() => GaugeFaceResolver.Bounds(ParseGauge(gauges, id)).Min),
				Max = UiValue.From(() => GaugeFaceResolver.Bounds(ParseGauge(gauges, id)).Max),
				DefaultValue = UiValue.From(() =>
				{
					var (min, max) = GaugeFaceResolver.Bounds(ParseGauge(gauges, id));
					return ThresholdDefaults.Defaults(min, max);
				}),
				SupportsReset = true
			}
		},
		new UiWhen
		{
			Key = $"warnWhen-when-{id}",
			Condition = () => !ThresholdsOn(gauges, id),
			Content = () => new UiChoiceInput
			{
				Key = "warnWhen",
				Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Warning()),
				Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.WarningHint()),
				Binding = StringField(gauges, id, "warnWhen", None),
				Options = UiValue.Of<IReadOnlyList<UiOption>>([
					UiOption.Of(None, Strings.Widgets.Gauge.Config.Fields.WarningOff()),
					UiOption.Of(WindsurfGaugeConfig.WarnAbove, Strings.Widgets.Gauge.Config.Fields.WarningAbove()),
					UiOption.Of(WindsurfGaugeConfig.WarnBelow, Strings.Widgets.Gauge.Config.Fields.WarningBelow())
				])
			}
		},
		new UiWhen
		{
			Key = $"warnAt-when-{id}",
			Condition = () => !ThresholdsOn(gauges, id) && Find(gauges.Value, id)?["warnWhen"] is JsonValue,
			Content = () => new UiNumberInput
			{
				Key = "warnAt",
				Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.Threshold()),
				Binding = NumberField(gauges, id, "warnAt")
			}
		},

		// This plugin's extensions to the built-in gauge fields.
		new UiNumberInput
		{
			Key = "valueSize",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.ValueSize()),
			Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.ValueSizeHint()),
			Min = UiValue.Of(1d),
			Max = UiValue.Of(60d),
			Step = UiValue.Of(1d),
			Binding = PercentField(gauges, id, "valueSize", WindsurfGaugesData.DefaultValueSize)
		},
		new UiNumberInput
		{
			Key = "labelSize",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.LabelSize()),
			Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.LabelSizeHint()),
			Min = UiValue.Of(1d),
			Max = UiValue.Of(60d),
			Step = UiValue.Of(1d),
			Binding = PercentField(gauges, id, "labelSize", WindsurfGaugesData.DefaultLabelSize)
		},
		new UiStringInput
		{
			Key = "valueSuffix",
			Label = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.ValueSuffix()),
			Description = UiText.FromLocalized(() => Strings.Widgets.Gauge.Config.Fields.ValueSuffixHint()),
			Binding = StringField(gauges, id, "valueSuffix")
		}
	];

	private static WindsurfGaugeConfig ParseGauge(UiState<List<JsonObject>> gauges, string id)
	{
		var stored = Find(gauges.Value, id);
		return WindsurfGaugeConfig.Parse(
			stored is null ? default : JsonSerializer.Deserialize<JsonElement>(stored.ToJsonString()),
			id);
	}

	private static bool ThresholdsOn(UiState<List<JsonObject>> gauges, string id) =>
		Find(gauges.Value, id)?["thresholdsEnabled"] is JsonValue value &&
		value.TryGetValue<bool>(out var enabled) &&
		enabled;

	private static LocalizedText LabelOf(JsonObject gauge, int index) =>
		gauge["name"] is JsonValue name && name.TryGetValue<string>(out var text) && text.Length > 0
			? LocalizedText.FromLiteral(text)
			: LocalizedText.FromLiteral($"{index + 1}");

	private static UiBinding<bool> FlagField(UiState<List<JsonObject>> gauges, string id, string field) =>
		Bind.Custom(
			() => Find(gauges.Value, id)?[field] is JsonValue value &&
				value.TryGetValue<bool>(out var flag) &&
				flag,
			value => Mutate(gauges, id, gauge =>
			{
				if (value)
					gauge[field] = true;
				else
					gauge.Remove(field);
			}));

	private static UiBinding<UiThresholds> ThresholdsField(UiState<List<JsonObject>> gauges, string id) =>
		Bind.Custom(
			() =>
			{
				if (Find(gauges.Value, id)?["thresholds"] is JsonObject stored &&
					UiThresholds.TryParse(
						JsonSerializer.Deserialize<JsonElement>(stored.ToJsonString()),
						out var parsed) &&
					parsed is not null)
					return parsed;
				var (min, max) = GaugeFaceResolver.Bounds(ParseGauge(gauges, id));
				return ThresholdDefaults.Defaults(min, max);
			},
			value => Mutate(gauges, id, gauge =>
			{
				if (value is null)
					gauge.Remove("thresholds");
				else
					gauge["thresholds"] = JsonNode.Parse(UiCanonicalJson.Serialize(value));
			}));

	private static JsonObject? Find(IReadOnlyList<JsonObject> gauges, string id) =>
		gauges.FirstOrDefault(gauge => IdOf(gauge) == id);

	private static void Mutate(UiState<List<JsonObject>> gauges, string id, Action<JsonObject> mutate)
	{
		gauges.Value = gauges.Value
			.Select(gauge =>
			{
				if (IdOf(gauge) != id)
					return gauge;
				var copy = (JsonObject)gauge.DeepClone();
				mutate(copy);
				return copy;
			})
			.ToList();
	}

	private static UiBinding<string> StringField(
		UiState<List<JsonObject>> gauges,
		string id,
		string field,
		string? unset = null) =>
		Bind.Custom(
			() => Find(gauges.Value, id)?[field] is JsonValue value &&
				value.TryGetValue<string>(out var text)
					? text
					: unset ?? "",
			value => Mutate(gauges, id, gauge =>
			{
				if (string.IsNullOrEmpty(value) || value == unset)
					gauge.Remove(field);
				else
					gauge[field] = value;
			}));

	private static UiBinding<double> NumberField(UiState<List<JsonObject>> gauges, string id, string field) =>
		Bind.Custom(
			() => Find(gauges.Value, id)?[field] is JsonValue value &&
				(value.TryGetValue<double>(out var number) ||
				 value.TryGetValue<int>(out var integer) && (number = integer) == integer ||
				 value.TryGetValue<long>(out var long_) && (number = long_) == long_)
					? number
					: 0,
			value => Mutate(gauges, id, gauge =>
			{
				if (double.IsFinite(value))
					gauge[field] = value;
			}));

	// Font sizes are authored as percent of the tile in the editor and stored as fractions.
	private static UiBinding<double> PercentField(
		UiState<List<JsonObject>> gauges,
		string id,
		string field,
		double defaultFraction) =>
		Bind.Custom(
			() => Find(gauges.Value, id)?[field] is JsonValue value &&
				value.TryGetValue<double>(out var fraction)
					? fraction * 100
					: defaultFraction * 100,
			value => Mutate(gauges, id, gauge =>
			{
				if (double.IsFinite(value) && value > 0)
					gauge[field] = Math.Clamp(value / 100, 0.005, 0.6);
			}));

	private static UiBinding<UiIconReference> IconField(UiState<List<JsonObject>> gauges, string id) =>
		Bind.Custom(
			() => Find(gauges.Value, id)?["icon"] is JsonObject stored &&
				stored["type"] is JsonValue type &&
				type.TryGetValue<string>(out var typeName) &&
				stored["reference"] is JsonValue reference &&
				reference.TryGetValue<string>(out var referenceValue)
					? new UiIconReference(typeName, referenceValue)
					: new UiIconReference("", ""),
			value => Mutate(gauges, id, gauge =>
			{
				if (value is null)
					gauge.Remove("icon");
				else
					gauge["icon"] = new JsonObject { ["type"] = value.Type, ["reference"] = value.Reference };
			}));

	private static List<JsonObject> ReadGauges(JsonElement data)
	{
		var stored = data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty("gauges", out var gauges) &&
			gauges.ValueKind == JsonValueKind.Array
				? gauges
				: default;

		if (stored.ValueKind != JsonValueKind.Array)
			return [];

		var result = new List<JsonObject>();
		foreach (var item in stored.EnumerateArray())
		{
			if (JsonNode.Parse(item.GetRawText()) is JsonObject gauge)
				result.Add(gauge);
		}

		return result;
	}

	private static JsonElement Serialize(IReadOnlyList<JsonObject> gauges) =>
		JsonSerializer.Deserialize<JsonElement>(
			new JsonArray([.. gauges.Select(gauge => gauge.DeepClone())]).ToJsonString());

	private static List<JsonObject> Deserialize(JsonElement value) =>
		value.ValueKind == JsonValueKind.Array
			? ReadGauges(JsonSerializer.Deserialize<JsonElement>(
				new JsonObject { ["gauges"] = JsonNode.Parse(value.GetRawText()) }.ToJsonString()))
			: [];

	private static string? ReadString(JsonElement data, string name) =>
		data.ValueKind == JsonValueKind.Object &&
		data.TryGetProperty(name, out var value) &&
		value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static string IdOf(JsonObject gauge) => gauge["id"]?.GetValue<string>() ?? "";

	public event EventHandler? Changed;
	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent e) => _view.Dispatch(e);

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
