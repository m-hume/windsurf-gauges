using MacroDeck.Plugin.Testing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace WindsurfGauges.Tests;

/// <summary>
/// Behaviour tests through <see cref="PluginTestHarness"/>: the plugin's own capability handlers run,
/// but nothing crosses a socket. This is where you test what your integration does.
/// </summary>
[TestFixture]
public sealed class PluginIntegrationTests
{
	private static PluginTestHarness CreateHarness() =>
		PluginTestHarness.Create(builder =>
		{
			builder.UseLocalization(Strings.LocalizationCatalog);
			builder.Services.AddSingleton<IWindsurfApiKeySource>(new NullApiKeySource());
			builder.Services.AddSingleton<IHttpClientFactory>(new UnreachableHttpClientFactory());
			builder.RegisterIntegration<PluginIntegration>();
		});

	[Test]
	public async Task The_plugin_builds_and_initializes()
	{
		await using var harness = CreateHarness();

		Assert.DoesNotThrowAsync(harness.InitializeIntegrationsAsync);
	}

	[Test]
	public async Task The_refresh_action_polls_the_usage_service()
	{
		// No key source in the test environment, so the poll short-circuits before any network
		// call and the action reports failure rather than claiming success.
		await using var harness = PluginTestHarness.Create(builder =>
		{
			builder.UseLocalization(Strings.LocalizationCatalog);
			builder.Services.AddSingleton<IWindsurfApiKeySource>(new NullApiKeySource());
			builder.Services.AddSingleton<IHttpClientFactory>(new UnreachableHttpClientFactory());
			builder.RegisterIntegration<PluginIntegration>();
		});
		await harness.InitializeIntegrationsAsync();

		var outcome = await harness.Actions.ExecuteAsync(
			"refresh-usage",
			new Dictionary<string, object?>());

		Assert.That(outcome.Succeeded, Is.False);
	}

	private sealed class NullApiKeySource : IWindsurfApiKeySource
	{
		public string? GetKey() => null;
	}

	private sealed class UnreachableHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new(new HttpClientHandler
		{
			ServerCertificateCustomValidationCallback = (_, _, _, _) => false
		})
		{
			BaseAddress = new Uri("http://127.0.0.1:1/"),
			Timeout = TimeSpan.FromSeconds(2)
		};
	}

	[Test]
	public async Task The_title_template_resolves_against_the_plugins_own_variables()
	{
		var logger = new Serilog.LoggerConfiguration().CreateLogger();
		var service = new WindsurfUsageService(new FakeHttpClientFactory(), () => null, logger);
		var provider = new WindsurfVariableProvider(service);
		var resolver = new WindsurfGauges.GaugeFaceResolver(provider, null, logger);

		var resolved = await resolver.ResolveTemplateAsync("${{ vars.windsurf_extra_balance }}", CancellationToken.None);

		Assert.That(resolved, Is.EqualTo("$"));
	}

	[Test]
	public void The_usage_variables_are_declared()
	{
		var logger = new Serilog.LoggerConfiguration().CreateLogger();
		var service = new WindsurfUsageService(new FakeHttpClientFactory(), () => null, logger);
		var provider = new WindsurfVariableProvider(service);

		var names = provider.Variables.Select(v => v.Name).ToArray();

		Assert.That(names, Does.Contain("windsurf_daily_used"));
		Assert.That(names, Does.Contain("windsurf_weekly_used"));
		Assert.That(names, Does.Contain("windsurf_extra_balance"));
		Assert.That(names, Does.Contain("windsurf_plan"));
		Assert.That(names, Does.Contain("windsurf_last_update"));
		Assert.That(names, Does.Contain("windsurf_plan_reset"));
		Assert.That(names, Does.Contain("windsurf_plan_days_left"));
		Assert.That(names, Does.Contain("windsurf_daily_reset"));
		Assert.That(names, Does.Contain("windsurf_weekly_reset"));
		Assert.That(names, Does.Contain("windsurf_email"));
	}

	private sealed class FakeHttpClientFactory : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => new();
	}
}

public sealed class GaugeViewTests
{
	private static readonly System.Text.Json.JsonElement EmptyObject =
		System.Text.Json.JsonSerializer.SerializeToElement(new Dictionary<string, object?>());

	[Test]
	public void The_widget_config_view_builds_without_throwing()
	{
		var surface = new MacroDeck.Ui.Model.Surfaces.UiSurface
		{
			Kind = MacroDeck.Ui.Model.Surfaces.UiSurfaceKinds.Config,
			SessionMode = MacroDeck.Ui.Model.Surfaces.UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, System.Text.Json.JsonElement>
			{
				[MacroDeck.Ui.Model.Surfaces.UiConfigSurfaceAttributes.EntryPoint] =
					System.Text.Json.JsonSerializer.SerializeToElement(
						MacroDeck.Ui.Model.Surfaces.UiConfigEntryPoints.WidgetConfig),
				[MacroDeck.Ui.Model.Surfaces.UiConfigSurfaceAttributes.WidgetType] =
					System.Text.Json.JsonSerializer.SerializeToElement("gauge"),
				[MacroDeck.Ui.Model.Surfaces.UiConfigSurfaceAttributes.WidgetData] = EmptyObject
			}
		};

		var session = new WindsurfGauges.GaugeConfigSession(
			surface, WindsurfGauges.WindsurfGaugesData.Parse(EmptyObject), EmptyObject);

		Assert.That(session.BuildTree(), Is.Not.Null);
	}

	[Test]
	public void The_widget_view_builds_without_throwing()
	{
		var surface = new MacroDeck.Ui.Model.Surfaces.UiSurface
		{
			Kind = MacroDeck.Ui.Model.Surfaces.UiSurfaceKinds.Widget,
			SessionMode = MacroDeck.Ui.Model.Surfaces.UiSessionModes.Shared
		};

		var config = new WindsurfGauges.WindsurfGaugesData
		{
			Title = "Windsurf",
			Gauges =
			[
				new WindsurfGauges.WindsurfGaugeConfig { Id = "daily", Variable = "windsurf_daily_used", Name = "Daily", Max = 100 }
			]
		};
		var faces = config.Shown.Select(_ => new MacroDeck.Ui.Runtime.UiState<WindsurfGauges.GaugeFace>(
			new WindsurfGauges.GaugeFace { Level = 0.3, Value = "30", Name = "Daily" })).ToList();

		var view = new MacroDeck.Ui.Runtime.UiView(
			surface, WindsurfGauges.WindsurfGaugeWidget.Build(
				config, new MacroDeck.Ui.Runtime.UiState<string>("Windsurf"), faces, null));

		Assert.That(view.Tree, Is.Not.Null);
	}
}

/// <summary>
/// The localization set is generated from <c>Localization/*.resx</c>, so these guard the wiring rather
/// than any wording: a missing catalog registration leaves every label showing its raw key.
/// </summary>
[TestFixture]
public sealed class LocalizationTests
{
	[Test]
	public void The_catalog_is_scoped_to_the_plugin_id()
	{
		Assert.That(Strings.LocalizationCatalog.Scope, Is.EqualTo("plugin:com.m-hume.windsurf-gauges"));
	}

	[Test]
	public void English_is_the_default_culture()
	{
		Assert.That(Strings.LocalizationCatalog.DefaultCulture, Is.EqualTo("en"));
		Assert.That(Strings.LocalizationCatalog.Cultures, Does.Contain("en"));
	}

	[Test]
	public void The_action_strings_come_from_the_catalog()
	{
		Assert.That(Strings.LocalizationCatalog.KeysOf("en"), Does.Contain("Actions.RefreshUsage.Name"));
	}

	[Test]
	public void Every_key_the_default_culture_declares_resolves_to_text()
	{
		foreach (var key in Strings.LocalizationCatalog.KeysOf("en"))
		{
			Assert.That(Strings.LocalizationCatalog.TryGetTemplate("en", key, out var text), Is.True);
			Assert.That(text, Is.Not.Empty);
		}
	}
}
