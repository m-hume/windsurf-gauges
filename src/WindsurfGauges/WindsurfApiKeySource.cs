using Serilog;

namespace WindsurfGauges;

/// <summary>Where the poller gets its Windsurf API keys from.</summary>
public interface IWindsurfApiKeySource
{
	IReadOnlyList<string> GetKeys();
}

/// <summary>
/// Reads the key the JetBrains Windsurf plugin stores in each IDE's options/other.xml, so the
/// plugin works without configuration on a machine that has the IDE plugin signed in. A plugin
/// configuration field can override this later.
/// </summary>
public sealed class WindsurfApiKeySource(ILogger logger) : IWindsurfApiKeySource
{
	public IReadOnlyList<string> GetKeys() => ApiKeyReader.ReadKeys(logger);
}
