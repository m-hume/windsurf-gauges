using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
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
	private readonly Func<string?> _apiKeyAccessor;
	private readonly ILogger _logger;
	private readonly Timer _timer;
	private readonly CancellationTokenSource _stopping = new();
	private readonly SemaphoreSlim _pollGate = new(1, 1);
	private UsageSnapshot? _snapshot;
	private bool _disposed;

	public WindsurfUsageService(IHttpClientFactory httpClientFactory, Func<string?> apiKeyAccessor, ILogger logger)
	{
		_httpClientFactory = httpClientFactory;
		_apiKeyAccessor = apiKeyAccessor;
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
			var apiKey = _apiKeyAccessor();
			if (string.IsNullOrWhiteSpace(apiKey))
			{
				_logger.Information(
					"No API key: the plugin settings hold no key and none was found under the JetBrains options folder");
				return false;
			}

			var snapshot = await FetchAsync(apiKey, _stopping.Token).ConfigureAwait(false);
			if (snapshot is null)
			{
				_logger.Information("Usage poll failed: GetUserStatus did not return a usable status");
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
/// Reads the Windsurf API key the JetBrains plugin stores in each IDE's options/other.xml, so the
/// plugin works without configuration on a machine that has the plugin installed and signed in.
/// </summary>
internal static partial class JetBrainsApiKeyReader
{
	[GeneratedRegex("sk-ws-[A-Za-z0-9_-]+")]
	private static partial Regex ApiKeyRegex();

	public static string? ReadKey(ILogger logger)
	{
		if (!OperatingSystem.IsWindows())
			return null;
		try
		{
			var options = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"JetBrains");
			if (!Directory.Exists(options))
			{
				logger.Information("API key lookup: {Path} does not exist", options);
				return null;
			}

			// IgnoreInaccessible: one unreadable subfolder must not abort the whole scan.
			var files = Directory.EnumerateFiles(
				options, "other.xml",
				new EnumerationOptions
				{
					IgnoreInaccessible = true,
					RecurseSubdirectories = true,
					MatchCasing = MatchCasing.CaseInsensitive
				}).ToList();
			logger.Information("API key lookup: scanning {Path}, found {Count} options files", options, files.Count);

			foreach (var file in files)
			{
				var match = ApiKeyRegex().Match(File.ReadAllText(file));
				if (match.Success)
				{
					logger.Information("API key found in {File}", file);
					return match.Value;
				}
			}

			logger.Information("API key lookup: no key in {Count} options files", files.Count);
		}
		catch (Exception exception)
		{
			logger.Information("API key lookup failed: {Message}", exception.Message);
			return null;
		}
		return null;
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
