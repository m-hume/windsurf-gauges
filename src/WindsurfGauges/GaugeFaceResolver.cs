using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Config;

namespace WindsurfGauges;

/// <summary>What one gauge renders: level, formatted value, colour and resolved name.</summary>
public sealed record GaugeFace
{
	public const string Unavailable = "-";
	public const string WarningColor = "#ff3b30";

	public double Level { get; init; }
	public string Value { get; init; } = Unavailable;
	public string? Color { get; init; }
	public string Name { get; init; } = "";
}

/// <summary>
/// Resolves one gauge's face from the plugin's own variables or the host's, with the built-in
/// Gauges semantics: min/max bounds with an automatic span, colour thresholds, warn above/below.
/// </summary>
internal sealed partial class GaugeFaceResolver(
	WindsurfVariableProvider ownVariables,
	IVariableApi? hostVariables,
	Serilog.ILogger logger)
{
	private const double AutoSpan = 100;

	public async Task<GaugeFace> ResolveAsync(WindsurfGaugeConfig gauge, CancellationToken cancellationToken)
	{
		var handle = await FindVariableAsync(gauge.Variable, cancellationToken);
		var name = await ResolveTemplateAsync(gauge.Name, cancellationToken);
		if (handle is null || ToDouble(handle.Value) is not { } value)
			return new GaugeFace { Color = gauge.Color, Name = name };

		return new GaugeFace
		{
			Level = Level(value, gauge),
			Value = FormatValue(value, handle.DecimalPlaces),
			Color = ColorOf(value, gauge),
			Name = name
		};
	}

	public static double Level(double value, WindsurfGaugeConfig gauge)
	{
		var (min, max) = Bounds(gauge);
		var level = (value - min) / (max - min);
		return double.IsFinite(level) ? Math.Round(Math.Clamp(level, 0, 1), 4) : 0;
	}

	// The editor's number fields have no empty state, so a maximum of 0 stands for automatic.
	public static (double Min, double Max) Bounds(WindsurfGaugeConfig gauge)
	{
		var min = gauge.Min ?? 0;
		if (gauge.Max is { } max && max != 0 && max > min)
			return (min, max);
		return (min, min + AutoSpan);
	}

	public static string? ColorOf(double value, WindsurfGaugeConfig gauge)
	{
		if (!gauge.ThresholdsEnabled)
			return IsWarning(value, gauge) ? GaugeFace.WarningColor : gauge.Color;
		var (min, max) = Bounds(gauge);
		var thresholds = gauge.Thresholds ?? ThresholdDefaults.Defaults(min, max);
		return thresholds.ColorAt(value) ?? gauge.Color;
	}

	public static bool IsWarning(double value, WindsurfGaugeConfig gauge) =>
		gauge.WarnWhen switch
		{
			WindsurfGaugeConfig.WarnAbove => value >= (gauge.WarnAt ?? 0),
			WindsurfGaugeConfig.WarnBelow => value <= (gauge.WarnAt ?? 0),
			_ => false
		};

	public static string FormatValue(double value, int? decimalPlaces) =>
		value.ToString($"0.{new string('#', Math.Max(0, Math.Min(4, decimalPlaces ?? 2)))}",
			CultureInfo.InvariantCulture);

	private async Task<VariableHandle?> FindVariableAsync(string reference, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(reference))
			return null;

		var localId = OwnLocalId(reference);
		if (localId is not null)
		{
			var reading = await ownVariables.ReadAsync(localId, cancellationToken);
			if (reading.Value is not null)
				return new VariableHandle(Guid.Empty, reference, VariableType.Numeric, reading.Value, null)
				{
					DefinitionId = localId
				};
		}

		if (hostVariables is null)
			return null;

		try
		{
			var handle = await hostVariables.GetByNameAsync(reference);
			if (handle is not null)
				return handle;
		}
		catch (Exception)
		{
			return null;
		}

		try
		{
			var handles = await hostVariables.GetAllAsync();
			var local = reference.Contains("::", StringComparison.Ordinal)
				? reference[(reference.LastIndexOf(':') + 1)..]
				: reference;
			return handles.FirstOrDefault(x => string.Equals(x.Name, reference, StringComparison.OrdinalIgnoreCase)) ??
				handles.FirstOrDefault(x => string.Equals(x.DefinitionId, local, StringComparison.OrdinalIgnoreCase)) ??
				handles.FirstOrDefault(x => string.Equals(x.Name, local, StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception)
		{
			return null;
		}
	}

	// GetByNameAsync misses variables whose stored name differs in case or form, so fall back to
	// a scan of everything the host exposes.
	private async Task<VariableHandle?> FindHostVariableAsync(string reference, CancellationToken cancellationToken)
	{
		if (hostVariables is null || string.IsNullOrWhiteSpace(reference))
			return null;

		try
		{
			var direct = await hostVariables.GetByNameAsync(reference);
			if (direct is not null)
				return direct;
		}
		catch (Exception)
		{
			return null;
		}

		try
		{
			var handles = await hostVariables.GetAllAsync();
			var local = reference.Contains("::", StringComparison.Ordinal)
				? reference[(reference.LastIndexOf(':') + 1)..]
				: reference;
			return handles.FirstOrDefault(x => string.Equals(x.Name, reference, StringComparison.OrdinalIgnoreCase)) ??
				handles.FirstOrDefault(x => string.Equals(x.DefinitionId, local, StringComparison.OrdinalIgnoreCase)) ??
				handles.FirstOrDefault(x => string.Equals(x.Name, local, StringComparison.OrdinalIgnoreCase));
		}
		catch (Exception)
		{
			return null;
		}
	}

	private string? OwnLocalId(string reference)
	{
		var local = reference;
		var qualifier = PluginIds.Self + "::";
		if (local.StartsWith(qualifier, StringComparison.OrdinalIgnoreCase))
			local = local[qualifier.Length..];
		else if (local.Contains("::", StringComparison.Ordinal))
			return null;

		return ownVariables.Variables
			.FirstOrDefault(x =>
				string.Equals(x.Name, local, StringComparison.OrdinalIgnoreCase) ||
				string.Equals(x.Id, local, StringComparison.OrdinalIgnoreCase))
			?.Id;
	}

	public async Task<string> ResolveTemplateAsync(string? name, CancellationToken cancellationToken)
	{
		if (name is not { Length: > 0 })
			return "";
		if (!VariableTemplateRegex().IsMatch(name))
			return name;

		// A name is user written text that renders on every refresh; a broken template shows
		// itself instead of faulting the session.
		try
		{
			var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var variableName in VariableTemplateRegex().Matches(name)
				.Select(m => m.Groups[1].Value.Trim())
				.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var handle = await FindVariableAsync(variableName, cancellationToken);
				if (handle is null)
					logger.Debug("Template variable {Name} not found in the plugin's own variables or the host", variableName);
				values[variableName] = handle?.Value switch
				{
					null or "" => "",
					double number => number.ToString("0.##", CultureInfo.InvariantCulture),
					_ => handle.Value.ToString() ?? ""
				};
			}

			return VariableTemplateRegex().Replace(name,
				match => values.GetValueOrDefault(match.Groups[1].Value.Trim(), ""));
		}
		catch (Exception)
		{
			return name;
		}
	}

	[GeneratedRegex(@"\{\{\s*vars\.([A-Za-z0-9_]+)\s*\}\}")]
	private static partial Regex VariableTemplateRegex();

	private static readonly Regex TemplateResolver = VariableTemplateRegex();

	private static double? ToDouble(object? value) => value switch
	{
		double number when double.IsFinite(number) => number,
		float number when double.IsFinite(number) => number,
		int number => number,
		long number => number,
		decimal number => (double)number,
		string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			&& double.IsFinite(parsed) => parsed,
		bool flag => flag ? 1 : 0,
		_ => null
	};
}
