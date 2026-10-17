using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;

namespace WindsurfGauges;

internal sealed class PluginConfigFlow : IConfigFlow
{
	public const string StepId = "polling";
	public const string PollSecondsKey = "pollSeconds";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken) =>
		Task.FromResult(ConfigFlowResult.Step(PollingStep()));

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (!string.Equals(stepId, StepId, StringComparison.Ordinal))
			return Task.FromResult(ConfigFlowResult.Step(PollingStep()));

		if (!input.TryGetValue(PollSecondsKey, out var raw) || !TryParseSeconds(raw, out var seconds))
			return Task.FromResult(ConfigFlowResult.Error(
				PollingStep(),
				Strings.Config.Polling.Field.Invalid(),
				new Dictionary<string, LocalizedText>
				{
					[PollSecondsKey] = Strings.Config.Polling.Field.Invalid()
				}));

		return Task.FromResult(ConfigFlowResult.Complete(
			"Windsurf Gauges",
			new Dictionary<string, ConfigFlowValue>
			{
				[PollSecondsKey] = ConfigFlowValue.Plain(seconds.ToString(CultureInfo.InvariantCulture))
			}));
	}

	private static ConfigFlowStep PollingStep() => new()
	{
		StepId = StepId,
		Title = Strings.Config.Polling.Title(),
		Description = Strings.Config.Polling.Description(),
		Fields =
		[
			ActionParameter.Number(
				PollSecondsKey,
				Strings.Config.Polling.Field.Label(),
				Strings.Config.Polling.Field.Description(),
				WindsurfUsageService.MinPollSeconds,
				WindsurfUsageService.MaxPollSeconds,
				1,
				WindsurfUsageService.DefaultPollSeconds,
				required: true)
		]
	};

	private static bool TryParseSeconds(object? raw, out double seconds)
	{
		seconds = raw switch
		{
			double number => number,
			float number => number,
			int number => number,
			long number => number,
			decimal number => (double)number,
			string text when double.TryParse(
				text,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parsed) => parsed,
			_ => double.NaN
		};
		if (!double.IsFinite(seconds) ||
			seconds < WindsurfUsageService.MinPollSeconds ||
			seconds > WindsurfUsageService.MaxPollSeconds)
		{
			seconds = 0;
			return false;
		}

		return true;
	}
}
