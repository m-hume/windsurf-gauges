using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Serilog;

namespace WindsurfGauges;

/// <summary>The latest usage reading the poller holds.</summary>
internal sealed record UsageSnapshot(
	double DailyUsedPercent,
	double WeeklyUsedPercent,
	double ExtraBalance,
	string Plan,
	string Email,
	DateTimeOffset? PlanEnd,
	DateTimeOffset? DailyReset,
	DateTimeOffset? WeeklyReset,
	DateTimeOffset Updated);

/// <summary>
/// Polls Codeium's cloud GetUserStatus endpoint with the plugin API key and keeps the latest
/// reading. The key comes from the plugin configuration (secret); when unset on Windows it falls
/// back to the key the JetBrains Windsurf plugin stores in its options files.
/// </summary>
internal sealed class WindsurfUsageService : IDisposable
{
	public const string ApiUrl = "https://server.codeium.com/exa.seat_management_pb.SeatManagementService/GetUserStatus";
	public const double DefaultPollSeconds = 300;
	public const double MinPollSeconds = 30;
	public const double MaxPollSeconds = 3600;

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly Func<IReadOnlyList<string>> _keysAccessor;
	private readonly ILogger _logger;
	private readonly Timer _timer;
	private readonly CancellationTokenSource _stopping = new();
	private readonly SemaphoreSlim _pollGate = new(1, 1);
	private UsageSnapshot? _snapshot;
	private bool _disposed;

	public WindsurfUsageService(IHttpClientFactory httpClientFactory, Func<IReadOnlyList<string>> keysAccessor, ILogger logger)
	{
		_httpClientFactory = httpClientFactory;
		_keysAccessor = keysAccessor;
		_logger = logger.ForContext<WindsurfUsageService>();
		_timer = new Timer(OnTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(DefaultPollSeconds));
	}

	/// <summary>Applies a configured poll period; called again on every (re)initialization.</summary>
	public void SetPollSeconds(double seconds)
	{
		var clamped = double.IsFinite(seconds)
			? Math.Clamp(seconds, MinPollSeconds, MaxPollSeconds)
			: DefaultPollSeconds;
		_timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(clamped));
	}

	public UsageSnapshot? Snapshot => _snapshot;

	public void Dispose()
	{
		if (_disposed)
			return;
		_disposed = true;
		_timer.Dispose();
		_stopping.Cancel();
		_stopping.Dispose();
		_pollGate.Dispose();
		GC.SuppressFinalize(this);
	}

	private void OnTick(object? _) => _ = PollAsync();

	public async Task<bool> PollAsync()
	{
		if (_disposed)
			return false;
		if (!await _pollGate.WaitAsync(0).ConfigureAwait(false))
			return true;
		try
		{
			var keys = _keysAccessor();
			if (keys.Count == 0)
			{
				_logger.Information(
					"No API key: install and sign in to a Windsurf JetBrains plugin, Devin Desktop or the Windsurf IDE");
				return false;
			}

			UsageSnapshot? snapshot = null;
			foreach (var apiKey in keys)
			{
				snapshot = await FetchAsync(apiKey, _stopping.Token).ConfigureAwait(false);
				if (snapshot is not null)
					break;
			}
			if (snapshot is null)
			{
				_logger.Information("Usage poll failed: no candidate key authenticated GetUserStatus");
				return false;
			}
			_snapshot = snapshot;
			_logger.Information(
				"Usage polled: daily {Daily}% weekly {Weekly}% extra {Extra}$ plan {Plan}",
				snapshot.DailyUsedPercent, snapshot.WeeklyUsedPercent, snapshot.ExtraBalance, snapshot.Plan);
			return true;
		}
		catch (Exception exception)
		{
			_logger.Information("Usage poll failed: {Message}", exception.Message);
			return false;
		}
		finally
		{
			_pollGate.Release();
		}
	}

	private async Task<UsageSnapshot?> FetchAsync(string apiKey, CancellationToken cancellationToken)
	{
		var client = _httpClientFactory.CreateClient("windsurf");
		var body = JsonSerializer.Serialize(new RequestBodyHolder.Envelope
		{
			Metadata = new RequestBodyHolder.Metadata
			{
				ApiKey = apiKey,
				IdeName = "jetbrains",
				IdeVersion = "0.0.0",
				ExtensionName = "windsurf",
				ExtensionVersion = "0.0.0",
				Locale = "en"
			}
		});
		using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
		{
			Content = new StringContent(body, new MediaTypeHeaderValue("application/json"))
		};
		request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");

		using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
		if (!response.IsSuccessStatusCode)
		{
			var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
			_logger.Information(
				"GetUserStatus returned {StatusCode}: {Detail}",
				response.StatusCode,
				detail.Length > 300 ? detail[..300] : detail);
			return null;
		}

		using var document = await JsonDocument.ParseAsync(
			await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
			cancellationToken: cancellationToken).ConfigureAwait(false);

		var status = document.RootElement
			.GetProperty("userStatus")
			.GetProperty("planStatus");

		var planInfo = status.GetProperty("planInfo");
		var dailyUsed = 100 - ReadNumber(status, "dailyQuotaRemainingPercent");
		var weeklyUsed = 100 - ReadNumber(status, "weeklyQuotaRemainingPercent");
		var extra = ReadNumber(status, "overageBalanceMicros") / 1_000_000.0;
		var plan = planInfo.TryGetProperty("planName", out var planName) &&
			planName.ValueKind == JsonValueKind.String
				? planName.GetString() ?? "unknown"
				: "unknown";

		var email = status.TryGetProperty("email", out var emailElement) &&
			emailElement.ValueKind == JsonValueKind.String
				? emailElement.GetString() ?? ""
				: "";

		return new UsageSnapshot(
			dailyUsed,
			weeklyUsed,
			Math.Round(extra, 2),
			plan,
			email,
			ReadDate(status, "planEnd"),
			ReadUnix(status, "dailyQuotaResetAtUnix"),
			ReadUnix(status, "weeklyQuotaResetAtUnix"),
			DateTimeOffset.UtcNow);
	}

	private static DateTimeOffset? ReadDate(JsonElement element, string property)
	{
		var text = element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
		return text is not null &&
			DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
				? parsed
				: null;
	}

	private static DateTimeOffset? ReadUnix(JsonElement element, string property)
	{
		var seconds = ReadNumber(element, property);
		return seconds > 0
			? DateTimeOffset.FromUnixTimeSeconds((long)seconds)
			: null;
	}

	private static double ReadNumber(JsonElement element, string property)
	{
		if (element.TryGetProperty(property, out var value))
		{
			if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
				return number;
			if (value.ValueKind == JsonValueKind.String &&
				double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
				return parsed;
		}
		return 0;
	}
}

/// <summary>
/// Collects the API keys the Windsurf-family clients store on disk: the JetBrains plugin's
/// options/other.xml files, and the Devin Desktop / Windsurf IDE global storage databases. The
/// JetBrains and Devin tokens are different schemes but both authenticate the same GetUserStatus
/// endpoint, so every candidate is tried until one answers.
/// </summary>
internal static partial class ApiKeyReader
{
	[GeneratedRegex("\"apiKey\"\\s*:\\s*\"([^\"]+)\"")]
	private static partial Regex DbApiKeyRegex();

	[GeneratedRegex("sk-ws-[A-Za-z0-9_-]+")]
	private static partial Regex XmlApiKeyRegex();

	public static IReadOnlyList<string> ReadKeys(ILogger logger)
	{
		var keys = new List<string>();
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

		var jetbrains = Path.Combine(appData, "JetBrains");
		if (Directory.Exists(jetbrains))
		{
			var files = Directory.EnumerateFiles(
				jetbrains, "other.xml",
				new EnumerationOptions
				{
					IgnoreInaccessible = true,
					RecurseSubdirectories = true,
					MatchCasing = MatchCasing.CaseInsensitive
				});
			foreach (var file in files)
			{
				var match = XmlApiKeyRegex().Match(File.ReadAllText(file));
				if (match.Success && !keys.Contains(match.Value))
				{
					keys.Add(match.Value);
					logger.Information("API key found in {File}", file);
				}
			}
		}
		else
		{
			logger.Information("API key lookup: {Path} does not exist", jetbrains);
		}

		// Devin Desktop and Windsurf IDE: the token lives in the global storage database's
		// windsurfAuthStatus JSON. Read from the file bytes directly: no SQLite dependency, and
		// the value is stored as plain text inside the database pages.
		foreach (var app in new[] { "Devin", "Windsurf" })
		{
			var db = Path.Combine(appData, app, "User", "globalStorage", "state.vscdb");
			if (!File.Exists(db))
				continue;
			var match = DbApiKeyRegex().Match(File.ReadAllText(db, Encoding.Latin1));
			if (match.Success && !keys.Contains(match.Groups[1].Value))
			{
				keys.Add(match.Groups[1].Value);
				logger.Information("API key found in {Db}", db);
			}
		}

		logger.Information("API key lookup: {Count} candidate key(s)", keys.Count);
		return keys;
	}
}

/// <summary>The GetUserStatus request body. The API key is the only variable part.</summary>
internal static class RequestBodyHolder
{
	public sealed record Envelope
	{
		[JsonPropertyName("metadata")]
		public Metadata Metadata { get; init; } = new();
	}

	public sealed record Metadata
	{
		[JsonPropertyName("apiKey")]
		public string ApiKey { get; init; } = "";

		[JsonPropertyName("ideName")]
		public string IdeName { get; init; } = "";

		[JsonPropertyName("ideVersion")]
		public string IdeVersion { get; init; } = "";

		[JsonPropertyName("extensionName")]
		public string ExtensionName { get; init; } = "";

		[JsonPropertyName("extensionVersion")]
		public string ExtensionVersion { get; init; } = "";

		[JsonPropertyName("locale")]
		public string Locale { get; init; } = "";
	}
}
