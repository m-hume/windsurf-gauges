using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;

namespace WindsurfGauges;

/// <summary>
/// Refreshes the usage figures and opens the usage splash as a modal on the client that ran the
/// action. It is the widget's default short-press action, so tapping the tile shows the full
/// overview; headless runs still get the refresh.
/// </summary>
internal sealed class ShowUsageAction(WindsurfUsageService usage) : IActionDefinition
{
	public string Id => "show-usage";

	public LocalizedText Name => Strings.Actions.ShowUsage.Name();

	public LocalizedText Description => Strings.Actions.ShowUsage.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.All;

	public IActionExecutor CreateExecutor() => new Executor(usage);

	private sealed class Executor(WindsurfUsageService usage) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			// Refresh first so the splash opens on current figures; a stale splash is still more
			// useful than none, so a failed poll does not block the dialog.
			await usage.PollAsync();

			if (context.Ui is null || context.OriginClientId is not { Length: > 0 } clientId)
				return ActionResult.Success();

			var opened = await context.Ui.ShowModalAsync(
				clientId,
				new ModalDefinition
				{
					ViewId = UsageDetailSession.ViewId,
					Title = Strings.Dialog.Usage.Title()
				},
				context.CancellationToken);

			return opened
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.ShowUsage.Unavailable());
		}
	}
}
