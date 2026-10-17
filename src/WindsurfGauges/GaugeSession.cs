using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using Serilog;

namespace WindsurfGauges;

/// <summary>
/// The live half of the gauges widget: it opens the view, resolves every configured gauge's face
/// on a short timer and patches the face states the tree renders from. Icon handles are resolved
/// once per session against the host's icon store.
/// </summary>
internal sealed class GaugeSession : IUiSession, IDisposable, IAsyncDisposable
{
	private static readonly TimeSpan RefreshPeriod = TimeSpan.FromSeconds(5);

	private readonly UiView _view;
	private readonly UiState<string> _title = new("");
	private readonly WindsurfGaugesData _config;
	private readonly IReadOnlyList<UiState<GaugeFace>> _faces;
	private readonly GaugeFaceResolver _resolver;
	private readonly ILogger _logger;
	private readonly Timer? _timer;
	private readonly CancellationTokenSource _refresh = new();
	private bool _disposed;

	private GaugeSession(
		UiView view,
		UiState<string> title,
		WindsurfGaugesData config,
		IReadOnlyList<UiState<GaugeFace>> faces,
		GaugeFaceResolver resolver,
		ILogger logger,
		bool live)
	{
		_view = view;
		_title = title;
		_config = config;
		_faces = faces;
		_resolver = resolver;
		_logger = logger.ForContext<GaugeSession>();

		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault) =>
			Faulted?.Invoke(this, new UiSessionFaultedEventArgs("ui-handler-fault", fault.Exception));
		if (live)
			_timer = new Timer(OnTick, null, TimeSpan.Zero, RefreshPeriod);
	}

	private static readonly WindsurfGaugesData SampleConfig = new()
	{
		Title = "Windsurf",
		Style = WindsurfGaugesData.StyleArc,
		Gauges =
		[
			new WindsurfGaugeConfig { Id = "daily", Variable = "windsurf_daily_used", Name = "Daily", Max = 100, ValueSuffix = "%" },
			new WindsurfGaugeConfig { Id = "weekly", Variable = "windsurf_weekly_used", Name = "Weekly", Max = 100, ValueSuffix = "%" }
		]
	};

	public static async Task<GaugeSession> CreateAsync(
		UiSurface surface,
		WindsurfGaugesData config,
		string? backgroundColor,
		GaugeFaceResolver resolver,
		IUiResourceRegistry? uiResources,
		ILogger logger,
		bool live,
		CancellationToken cancellationToken)
	{
		// The picker card asks for a representative sample: when the stored data holds no gauges
		// (or carries none at all), render the sample pair instead of the empty hint.
		if (live == false && config.Shown.Count == 0)
			config = SampleConfig;
		var faces = config.Shown.Select(_ => new UiState<GaugeFace>(
			new GaugeFace { Color = null, Name = "" })).ToList();

		var icons = new Dictionary<string, UiResource>(StringComparer.Ordinal);
		foreach (var gauge in config.Shown)
		{
			if (gauge.Name is { Length: > 0 } || gauge.Icon is not { } reference ||
				!Guid.TryParse(reference.Reference, out var iconId))
				continue;
			try
			{
				if (uiResources is null)
					continue;
				icons[reference.Reference] = await uiResources.GetIconAsync(iconId, cancellationToken);
			}
			catch (Exception exception)
			{
				logger.Debug(exception, "Icon {IconId} could not be resolved", iconId);
			}
		}

		if (!live)
		{
			// The picker sample: representative values without touching any variable.
			var sampleValues = new[] { 32d, 68d, 18d, 86d, 12d, 55d, 74d, 41d };
			for (var i = 0; i < faces.Count; i++)
			{
				var gauge = config.Shown[i];
				var value = sampleValues[i % sampleValues.Length];
				faces[i].Set(new GaugeFace
				{
					Level = GaugeFaceResolver.Level(value, gauge),
					Value = GaugeFaceResolver.FormatValue(value, 0),
					Color = gauge.Color,
					Name = gauge.Name ?? ""
				});
			}
		}

		var titleState = new UiState<string>(config.Title ?? "");
		if (config.Title is { Length: > 0 })
			titleState.Set(await resolver.ResolveTemplateAsync(config.Title, cancellationToken));
		var view = new UiView(surface, WindsurfGaugeWidget.Build(
			config, titleState, faces, backgroundColor, icons));
		return new GaugeSession(view, titleState, config, faces, resolver, logger, live);
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
			if (_disposed || _refresh.IsCancellationRequested)
				return;

			if (_config.Title is { Length: > 0 } title)
			{
				var resolved = await _resolver.ResolveTemplateAsync(title, _refresh.Token);
				if (_title.Peek() != resolved)
					_title.Set(resolved);
			}

			var gauges = _config.Shown;
			for (var i = 0; i < gauges.Count && i < _faces.Count; i++)
			{
				var face = await _resolver.ResolveAsync(gauges[i], _refresh.Token);
				if (_faces[i].Value != face)
					_faces[i].Set(face);
			}
		}
		catch (Exception exception)
		{
			_logger.Debug(exception, "Gauges refresh failed");
		}
	}

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
		_timer?.Dispose();
		_refresh.Cancel();
		_refresh.Dispose();
	}
}
