using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Serilog;
using Microsoft.Extensions.DependencyInjection;
using WindsurfGauges;

// Identity, description and icon are not set here: they come from manifest.json at the content root.
// Strings is generated from Localization/*.resx, so UseLocalization is what makes every LocalizedString
// below resolve in the user's language rather than falling back to its key.
var plugin = MacroDeckPlugin.CreatePlugin(args)
	.UseMacroDeckLogging()
	.UseLocalization(Strings.LocalizationCatalog)
	.ConfigureServices((_, services) => services.AddSingleton<IWindsurfApiKeySource, WindsurfApiKeySource>())
	.RegisterIntegration<PluginIntegration>()
	.Build();

await plugin.RunAsync();
