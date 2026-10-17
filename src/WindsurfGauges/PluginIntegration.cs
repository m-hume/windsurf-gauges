using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Surfaces;
using Serilog;

namespace WindsurfGauges;

/// <summary>
/// The plugin's one integration: it polls Windsurf usage, exposes it as variables, registers the
/// gauges widget type and serves its widget, preview and configuration surfaces.
/// </summary>
public sealed class PluginIntegration : IPluginIntegration, IVariableProvider, IWidgetTypeProvider, IUiProvider, IConfigFlowProvider, IAsyncDisposable
{
	private readonly ILogger _logger;
	private readonly WindsurfUsageService _usage;
	private readonly WindsurfVariableProvider _variables;
	private GaugeFaceResolver? _resolver;
	private IUiResourceRegistry? _uiResources;

	public PluginIntegration(ILogger logger, IHttpClientFactory httpClientFactory, IWindsurfApiKeySource apiKeySource)
	{
		_logger = logger.ForContext<PluginIntegration>();
		_usage = new WindsurfUsageService(httpClientFactory, apiKeySource.GetKeys, logger);
		_variables = new WindsurfVariableProvider(_usage);
	}

	public IReadOnlyList<IActionDefinition> Actions => [new RefreshUsageAction(_usage)];

	public IReadOnlyList<VariableDefinition> Variables => _variables.Variables;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken) =>
		_variables.ReadAsync(localId, cancellationToken);

	public string ProviderName => "Windsurf Gauges";

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [Descriptor()];

	public static WidgetTypeDescriptor Descriptor() => new(
		WindsurfGaugeWidget.WidgetTypeId,
		Strings.Widgets.Gauge.Name(),
		Strings.Widgets.Gauge.Description(),
		WindsurfGaugeWidget.DefaultData,
		WindsurfGaugeWidget.DataSchema,
		true,
		new Dictionary<string, string>())
	{
		SupportsFlows = true,
		DefaultShortPressAction = new WidgetDefaultAction("refresh-usage", new Dictionary<string, string>())
	};

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
	[
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new UiSurfaceDeclaration { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var surface = request.Surface;

		if (surface.Kind == UiSurfaceKinds.Config)
		{
			if (!IsWidgetConfig(surface, WindsurfGaugeWidget.WidgetTypeId))
				return Task.FromResult<IUiSession?>(null);

			var stored = ReadRawData(surface, UiConfigSurfaceAttributes.WidgetData);
			return Task.FromResult<IUiSession?>(
				new GaugeConfigSession(surface, WindsurfGaugesData.Parse(stored), stored));
		}

		if (surface.Kind != UiSurfaceKinds.Widget && surface.Kind != UiSurfaceKinds.Preview)
			return Task.FromResult<IUiSession?>(null);
		if (!IsWidget(surface, WindsurfGaugeWidget.WidgetTypeId) || _resolver is null)
			return Task.FromResult<IUiSession?>(null);

		var isSample = surface.Attributes.TryGetValue(UiWidgetSurfaceAttributes.Sample, out var sample) &&
			sample.ValueKind == JsonValueKind.True;
		var data = ReadRawData(surface, UiWidgetSurfaceAttributes.Data);
		var config = WindsurfGaugesData.Parse(data);
		return GaugeSession.CreateAsync(
			surface,
			config,
			AppearanceColor(data, "backgroundColor"),
			_resolver,
			_uiResources,
			_logger,
			live: !isSample,
			cancellationToken)
			.ContinueWith<IUiSession?>(task => task.Result, cancellationToken);
	}

	public bool AllowsMultipleConfigurations => false;

	public IConfigFlow CreateConfigFlow() => new PluginConfigFlow();

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_resolver = new GaugeFaceResolver(_variables, context.Variables, _logger);
		_uiResources = context.UiResources;
		_usage.SetPollSeconds(await ReadPollSecondsAsync(context));
		_logger.Information("Initialized, polling every {Seconds}s", await ReadPollSecondsAsync(context));
		await _usage.PollAsync();
	}

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken)
	{
		await context.RegisterWidgetTypeAsync(Descriptor(), cancellationToken);
	}

	public Task ShutdownAsync()
	{
		_usage.Dispose();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		_usage.Dispose();
		return ValueTask.CompletedTask;
	}

	private async Task<double> ReadPollSecondsAsync(IIntegrationContext context)
	{
		try
		{
			var entries = await context.Config.GetEntriesAsync(CancellationToken.None);
			if (entries.Count == 0)
				return WindsurfUsageService.DefaultPollSeconds;

			var raw = await context.Config.GetStringAsync(
				entries[0].Id, PluginConfigFlow.PollSecondsKey, CancellationToken.None);
			return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
				? seconds
				: WindsurfUsageService.DefaultPollSeconds;
		}
		catch (Exception exception)
		{
			_logger.Debug(exception, "Plugin configuration read failed, using default poll interval");
			return WindsurfUsageService.DefaultPollSeconds;
		}
	}

	private static string? AppearanceColor(JsonElement data, string key) =>
		data.ValueKind == JsonValueKind.Object &&
		data.TryGetProperty(key, out var element) &&
		element.ValueKind == JsonValueKind.String
			? element.GetString()
			: null;

	private static bool IsWidget(UiSurface surface, string widgetTypeId) =>
		TryGetType(surface, UiWidgetSurfaceAttributes.WidgetType, widgetTypeId);

	private static bool IsWidgetConfig(UiSurface surface, string widgetTypeId) =>
		TryGetType(surface, UiConfigSurfaceAttributes.EntryPoint, UiConfigEntryPoints.WidgetConfig) &&
		TryGetType(surface, UiConfigSurfaceAttributes.WidgetType, widgetTypeId);

	private static bool TryGetType(UiSurface surface, string attributeKey, string widgetTypeId)
	{
		if (!surface.Attributes.TryGetValue(attributeKey, out var element) || element.ValueKind != JsonValueKind.String)
			return false;
		var value = element.GetString();
		return value == widgetTypeId || value == WindsurfGaugeWidget.QualifiedWidgetTypeId;
	}

	private static JsonElement ReadRawData(UiSurface surface, string key) =>
		surface.Attributes.TryGetValue(key, out var data) && data.ValueKind == JsonValueKind.Object
			? data
			: default;
}
