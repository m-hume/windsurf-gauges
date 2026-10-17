using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace WindsurfGauges;

/// <summary>
/// Forces an immediate usage poll, for a button the user can press when the five-minute cadence
/// is too slow to wait out.
/// </summary>
internal sealed class RefreshUsageAction(WindsurfUsageService usage) : IActionDefinition
{
	public string Id => "refresh-usage";

	public LocalizedText Name => Strings.Actions.RefreshUsage.Name();

	public LocalizedText Description => Strings.Actions.RefreshUsage.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;

	public IActionExecutor CreateExecutor() => new Executor(usage);

	private sealed class Executor(WindsurfUsageService usage) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var ok = await usage.PollAsync();
			return ok
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.RefreshUsage.Failure());
		}
	}
}
