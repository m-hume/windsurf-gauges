using System.Globalization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;

namespace WindsurfGauges;

/// <summary>
/// Exposes the latest usage reading as variables the deck can bind to, so the built-in Gauges
/// widget and this plugin's own gauge widget both work off the same source.
/// </summary>
internal sealed class WindsurfVariableProvider(WindsurfUsageService usage) : IVariableProvider
{
	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager("windsurf_daily_used", VariableType.Numeric) with
		{
			Id = "daily-used",
			Unit = "%",
			DisplayName = Strings.Variables.DailyUsed.DisplayName(),
			Description = Strings.Variables.DailyUsed.Description()
		},
		VariableDefinition.Eager("windsurf_weekly_used", VariableType.Numeric) with
		{
			Id = "weekly-used",
			Unit = "%",
			DisplayName = Strings.Variables.WeeklyUsed.DisplayName(),
			Description = Strings.Variables.WeeklyUsed.Description()
		},
		VariableDefinition.Eager("windsurf_extra_balance", VariableType.Numeric) with
		{
			Id = "extra-balance",
			Unit = "$",
			DisplayName = Strings.Variables.ExtraBalance.DisplayName(),
			Description = Strings.Variables.ExtraBalance.Description()
		},
		VariableDefinition.Eager("windsurf_plan", VariableType.Text) with
		{
			Id = "plan",
			DisplayName = Strings.Variables.Plan.DisplayName(),
			Description = Strings.Variables.Plan.Description()
		},
		VariableDefinition.Eager("windsurf_last_update", VariableType.Text) with
		{
			Id = "last-update",
			DisplayName = Strings.Variables.LastUpdate.DisplayName(),
			Description = Strings.Variables.LastUpdate.Description()
		},
		VariableDefinition.Eager("windsurf_plan_reset", VariableType.Text) with
		{
			Id = "plan-reset",
			DisplayName = Strings.Variables.PlanReset.DisplayName(),
			Description = Strings.Variables.PlanReset.Description()
		},
		VariableDefinition.Eager("windsurf_plan_days_left", VariableType.Numeric) with
		{
			Id = "plan-days-left",
			Unit = "d",
			DisplayName = Strings.Variables.PlanDaysLeft.DisplayName(),
			Description = Strings.Variables.PlanDaysLeft.Description()
		},
		VariableDefinition.Eager("windsurf_daily_reset", VariableType.Text) with
		{
			Id = "daily-reset",
			DisplayName = Strings.Variables.DailyReset.DisplayName(),
			Description = Strings.Variables.DailyReset.Description()
		},
		VariableDefinition.Eager("windsurf_weekly_reset", VariableType.Text) with
		{
			Id = "weekly-reset",
			DisplayName = Strings.Variables.WeeklyReset.DisplayName(),
			Description = Strings.Variables.WeeklyReset.Description()
		},
		VariableDefinition.Eager("windsurf_email", VariableType.Text) with
		{
			Id = "email",
			DisplayName = Strings.Variables.Email.DisplayName(),
			Description = Strings.Variables.Email.Description()
		}
	];

	// Shown in the machine's local time, not the API's UTC.
	private static string FormatDate(DateTimeOffset? date) =>
		date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "";

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		var snapshot = usage.Snapshot;
		if (snapshot is null)
			return ValueTask.FromResult(VariableReading.Unavailable);

		VariableReading reading = localId switch
		{
			"daily-used" => VariableReading.Of(snapshot.DailyUsedPercent),
			"weekly-used" => VariableReading.Of(snapshot.WeeklyUsedPercent),
			"extra-balance" => VariableReading.Of(snapshot.ExtraBalance),
			"plan" => VariableReading.Of(snapshot.Plan),
			"last-update" => VariableReading.Of(snapshot.Updated.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
			"plan-reset" => VariableReading.Of(FormatDate(snapshot.PlanEnd)),
			"plan-days-left" => snapshot.PlanEnd is { } end
				? VariableReading.Of(Math.Ceiling((end - DateTimeOffset.Now).TotalDays))
				: VariableReading.Unavailable,
			"daily-reset" => VariableReading.Of(FormatDate(snapshot.DailyReset)),
			"weekly-reset" => VariableReading.Of(FormatDate(snapshot.WeeklyReset)),
			"email" => VariableReading.Of(snapshot.Email),
			_ => VariableReading.Unavailable
		};
		return ValueTask.FromResult(reading);
	}
}
