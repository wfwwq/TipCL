using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using TipCL.Models;

namespace TipCL.Services;

public sealed class BridgeClient : IDisposable
{
	public const string TokenHeader = "X-TipCL-Bridge-Token";

	public static readonly JsonSerializerOptions JsonOpt = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		NumberHandling = JsonNumberHandling.AllowReadingFromString
	};

	private readonly HttpClient _http;

	private readonly CancellationTokenSource _cts = new CancellationTokenSource();

	private readonly string _token;

	private int _id;

	private int _disposed;

	public Uri BaseUri { get; }

	public bool StreamConnected { get; private set; }

	public string LastStreamError { get; private set; } = "";

	public event EventHandler<BridgeEvent>? EventReceived;

	public event EventHandler<bool>? StreamStateChanged;

	public BridgeClient(Uri baseUri, string token)
	{
		bool flag = baseUri.Scheme != Uri.UriSchemeHttp || baseUri.Host != "127.0.0.1";
		if (!flag)
		{
			int port = baseUri.Port;
			flag = ((port < 1 || port > 65535) ? true : false);
		}
		if (flag)
		{
			throw new ArgumentException(I18n.L("仅允许回环地址"), "baseUri");
		}
		if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
		{
			throw new ArgumentException(I18n.L("令牌无效"), "token");
		}
		BaseUri = baseUri;
		_token = token;
		_http = new HttpClient
		{
			BaseAddress = baseUri,
			Timeout = TimeSpan.FromMinutes(20.0)
		};
		_http.DefaultRequestHeaders.TryAddWithoutValidation("X-TipCL-Bridge-Token", _token);
	}

	public void ConnectEvents()
	{
		Task.Run((Func<Task>)ReadSseLoop);
	}

	public async Task<bool> IsHealthyAsync(CancellationToken ct = default(CancellationToken))
	{
		using HttpResponseMessage httpResponseMessage = await _http.GetAsync("/health", ct).ConfigureAwait(continueOnCapturedContext: false);
		return httpResponseMessage.IsSuccessStatusCode;
	}

	public async Task<JsonElement> CallAsync(string method, object? args = null, CancellationToken ct = default(CancellationToken))
	{
		Dictionary<string, object> value = new Dictionary<string, object>
		{
			["jsonrpc"] = "2.0",
			["id"] = Interlocked.Increment(ref _id),
			["method"] = method,
			["params"] = args ?? new Dictionary<string, object>()
		};
		using StringContent content = new StringContent(JsonSerializer.Serialize(value, JsonOpt), Encoding.UTF8, "application/json");
		using HttpResponseMessage resp = await _http.PostAsync("/rpc", content, ct).ConfigureAwait(continueOnCapturedContext: false);
		string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
		using JsonDocument jsonDocument = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
		JsonElement rootElement = jsonDocument.RootElement;
		if (rootElement.TryGetProperty("error", out var value2) && value2.ValueKind != JsonValueKind.Null)
		{
			JsonElement value3;
			string text2 = ((value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("message", out value3)) ? value3.GetString() : ((value2.ValueKind == JsonValueKind.String) ? value2.GetString() : value2.ToString()));
			throw new BridgeCallException(method, text2 ?? I18n.L("调用失败"));
		}
		JsonElement value4;
		return rootElement.TryGetProperty("result", out value4) ? value4.Clone() : default(JsonElement);
	}

	public async Task<T?> CallAsync<T>(string method, object? args = null, CancellationToken ct = default(CancellationToken))
	{
		JsonElement jsonElement = await CallAsync(method, args, ct).ConfigureAwait(continueOnCapturedContext: false);
		JsonValueKind valueKind = jsonElement.ValueKind;
		if ((valueKind == JsonValueKind.Undefined || valueKind == JsonValueKind.Null) ? true : false)
		{
			return default(T);
		}
		if (typeof(T) == typeof(string) && jsonElement.ValueKind == JsonValueKind.String)
		{
			return (T)(object)(jsonElement.GetString() ?? "");
		}
		try
		{
			return JsonSerializer.Deserialize<T>(jsonElement.GetRawText(), JsonOpt);
		}
		catch (JsonException)
		{
			return default(T);
		}
	}

	public async Task<T?> TryCallAsync<T>(string method, object? args = null, T? fallback = default(T?))
	{
		try
		{
			T val = await CallAsync<T>(method, args).ConfigureAwait(continueOnCapturedContext: false);
			return (T)((val != null) ? ((object)val) : ((object)fallback));
		}
		catch
		{
			return fallback;
		}
	}

	public async Task<string> StartTaskAsync(string method, object? args = null, CancellationToken ct = default(CancellationToken))
	{
		JsonElement jsonElement = await CallAsync(method, args, ct).ConfigureAwait(continueOnCapturedContext: false);
		return (jsonElement.ValueKind == JsonValueKind.String) ? (jsonElement.GetString() ?? "") : jsonElement.ToString();
	}

	private async Task ReadSseLoop()
	{
		int attempt = 0;
		while (!_cts.IsCancellationRequested)
		{
			try
			{
				await ReadSseOnceAsync().ConfigureAwait(continueOnCapturedContext: false);
				attempt = 0;
				LastStreamError = I18n.L("事件流被服务端关闭");
			}
			catch (OperationCanceledException)
			{
				break;
			}
			catch (Exception ex2)
			{
				LastStreamError = ex2.GetType().Name + ": " + ex2.Message;
			}
			finally
			{
				SetStreamState(connected: false);
				if (Environment.GetEnvironmentVariable("TIPCL_WPF_DEBUG") == "1")
				{
					try
					{
						File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "tipcl-wpf-sse.log"), $"[{DateTime.Now:HH:mm:ss}] attempt={attempt} {LastStreamError}\n");
					}
					catch
					{
					}
				}
			}
			if (!_cts.IsCancellationRequested)
			{
				attempt = Math.Min(attempt + 1, 5);
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << attempt - 1, 15)), _cts.Token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				continue;
			}
			break;
		}
	}

	private async Task ReadSseOnceAsync()
	{
		using HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Get, "/events");
		req.Headers.TryAddWithoutValidation("X-TipCL-Bridge-Token", _token);
		using HttpResponseMessage resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, _cts.Token).ConfigureAwait(continueOnCapturedContext: false);
		resp.EnsureSuccessStatusCode();
		using Stream stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false);
		using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
		SetStreamState(connected: true);
		string ev = null;
		StringBuilder data = new StringBuilder();
		while (!_cts.IsCancellationRequested)
		{
			string text = await reader.ReadLineAsync().ConfigureAwait(continueOnCapturedContext: false);
			if (text != null)
			{
				if (text.StartsWith("event:", StringComparison.Ordinal))
				{
					ev = text.Substring(6).Trim();
				}
				else if (text.StartsWith("data:", StringComparison.Ordinal))
				{
					data.Append(text.Substring(5).Trim());
				}
				else if (text.Length == 0 && data.Length > 0)
				{
					Dispatch(ev ?? "message", data.ToString());
					ev = null;
					data.Clear();
				}
				continue;
			}
			break;
		}
	}

	private void Dispatch(string name, string json)
	{
		BridgeEvent bridgeEvent = new BridgeEvent
		{
			Event = name
		};
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(json);
			JsonElement rootElement = jsonDocument.RootElement;
			bridgeEvent.Payload = rootElement.Clone();
			bridgeEvent.TaskId = Str(rootElement, "task_id");
			bridgeEvent.Message = Str(rootElement, "message");
			bridgeEvent.Text = Str(rootElement, "text");
			bridgeEvent.Title = Str(rootElement, "title");
			bridgeEvent.Code = Str(rootElement, "code");
			bridgeEvent.Uri = Str(rootElement, "uri");
			bridgeEvent.Kind = Str(rootElement, "kind");
			bridgeEvent.Label = Str(rootElement, "label");
			bridgeEvent.Name = Str(rootElement, "name");
			bridgeEvent.Current = Num(rootElement, "current");
			bridgeEvent.Total = Num(rootElement, "total");
			bridgeEvent.Count = Num(rootElement, "count");
			bridgeEvent.Success = Bool(rootElement, "success");
			bridgeEvent.Stopped = Bool(rootElement, "stopped");
			if (name == "crash")
			{
				try
				{
					bridgeEvent.Crash = JsonSerializer.Deserialize<CrashReport>(json, JsonOpt);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		this.EventReceived?.Invoke(this, bridgeEvent);
	}

	private static string Str(JsonElement e, string k)
	{
		if (!e.TryGetProperty(k, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}

	private static int Num(JsonElement e, string k)
	{
		if (!e.TryGetProperty(k, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var value2))
		{
			return 0;
		}
		return value2;
	}

	private static bool Bool(JsonElement e, string k)
	{
		if (e.TryGetProperty(k, out var value))
		{
			return value.ValueKind == JsonValueKind.True;
		}
		return false;
	}

	private void SetStreamState(bool connected)
	{
		if (StreamConnected == connected)
		{
			return;
		}
		StreamConnected = connected;
		try
		{
			this.StreamStateChanged?.Invoke(this, connected);
		}
		catch
		{
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			try
			{
				_cts.Cancel();
			}
			catch
			{
			}
			_http.Dispose();
			_cts.Dispose();
		}
	}
}
