using System.Globalization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace WindsurfGauges;

/// <summary>
/// The dialog session behind the usage splash. The show-usage action asks the host to open the
/// view on the client that pressed; the session then mirrors the usage snapshot onto it on a
/// short timer, so a left-open splash keeps tracking the figures.
/// </summary>
internal sealed class UsageDetailSession : IUiSession, IDisposable, IAsyncDisposable
{
	public const string ViewId = "usage-detail";

	private static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(5);

	private static readonly WindsurfGaugeConfig DailyGauge = new()
	{
		Id = "daily",
		Variable = "windsurf_daily_used",
		Max = 100,
		ThresholdsEnabled = true
	};

	private static readonly WindsurfGaugeConfig WeeklyGauge = new()
	{
		Id = "weekly",
		Variable = "windsurf_weekly_used",
		Max = 100,
		ThresholdsEnabled = true
	};

	private readonly UiView _view;
	private readonly UiState<GaugeFace> _daily = new(new GaugeFace { Name = "" });
	private readonly UiState<GaugeFace> _weekly = new(new GaugeFace { Name = "" });
	private readonly UiState<UsageDetailText> _text = new(new UsageDetailText());
	private readonly WindsurfUsageService _usage;
	private readonly GaugeFaceResolver _resolver;
	private readonly ILogger _logger;
	private readonly Timer _timer;
	private readonly CancellationTokenSource _stopping = new();
	private bool _disposed;

	public UsageDetailSession(
		UiSurface surface,
		WindsurfUsageService usage,
		GaugeFaceResolver resolver,
		ILogger logger)
	{
		_usage = usage;
		_resolver = resolver;
		_logger = logger.ForContext<UsageDetailSession>();
		_view = new UiView(surface, UsageDetailView.Build(_daily, _weekly, _text));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("ui-handler-fault", fault.Exception));
		_timer = new Timer(OnTick, null, TimeSpan.Zero, RefreshPeriod);
	}

	public event EventHandler? Changed;
	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent e) => _view.Dispatch(e);

	private void OnTick(object? _) => _ = RefreshAsync();

	private async Task RefreshAsync()
	{
		try
		{
			if (_disposed || _stopping.IsCancellationRequested)
				return;

			var daily = await _resolver.ResolveAsync(DailyGauge, _stopping.Token);
			if (_daily.Value != daily)
				_daily.Set(daily);

			var weekly = await _resolver.ResolveAsync(WeeklyGauge, _stopping.Token);
			if (_weekly.Value != weekly)
				_weekly.Set(weekly);

			var text = Describe(_usage.Snapshot);
			if (_text.Value != text)
				_text.Set(text);
		}
		catch (Exception exception)
		{
			_logger.Debug(exception, "Usage detail refresh failed");
		}
	}

	private static string FormatDate(DateTimeOffset? date) =>
		date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ??
		GaugeFace.Unavailable;

	private static UsageDetailText Describe(UsageSnapshot? snapshot) =>
		snapshot is null
			? new UsageDetailText()
			: new UsageDetailText
			{
				Plan = snapshot.Plan,
				PlanReset = snapshot.PlanEnd is { } end
					? $"{FormatDate(end)} · {Math.Ceiling((end - DateTimeOffset.Now).TotalDays)}d"
					: GaugeFace.Unavailable,
				ExtraBalance = $"${snapshot.ExtraBalance.ToString("0.##", CultureInfo.InvariantCulture)}",
				DailyReset = FormatDate(snapshot.DailyReset),
				WeeklyReset = FormatDate(snapshot.WeeklyReset),
				Account = snapshot.Email is { Length: > 0 } email ? email : GaugeFace.Unavailable,
				Updated = snapshot.Updated.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
			};

	public void Dispose()
	{
		DisposeCore();
		GC.SuppressFinalize(this);
	}

	public ValueTask DisposeAsync()
	{
		DisposeCore();
		return ValueTask.CompletedTask;
	}

	private void DisposeCore()
	{
		if (_disposed)
			return;
		_disposed = true;
		_timer.Dispose();
		_stopping.Cancel();
		_stopping.Dispose();
		_view.Dispose();
	}
}
