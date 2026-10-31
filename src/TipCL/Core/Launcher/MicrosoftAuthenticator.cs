using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TipCL.Core.Launcher;

public class MicrosoftAuthenticator
{
	public readonly string client_id;

	public readonly double timeout;

	private const string UA = "TipCL/1.0";

	public MicrosoftAuthenticator(string client_id = "00000000402b5328", double timeout = 15.0)
	{
		this.client_id = client_id;
		this.timeout = timeout;
	}

	private AuthResponse PostForm(string url, PyDict form)
	{
		return AuthHttp.Send("POST", url, form, null, hasJson: false, null, timeout, "TipCL/1.0");
	}

	private AuthResponse PostJson(string url, object json)
	{
		return AuthHttp.Send("POST", url, null, json, hasJson: true, null, timeout, "TipCL/1.0");
	}

	private AuthResponse GetBearer(string url, object token)
	{
		return AuthHttp.Send("GET", url, null, null, hasJson: false, new Dictionary<string, string> { 
		{
			"Authorization",
			"Bearer " + Py.Str(token)
		} }, timeout, "TipCL/1.0");
	}

	public PyDict GetDeviceCode()
	{
		AuthResponse authResponse = PostForm("https://login.live.com/oauth20_connect.srf", PyDict.Of("client_id", client_id, "scope", "service::user.auth.xboxlive.com::MBI_SSL", "response_type", "device_code"));
		if (authResponse.StatusCode != 200)
		{
			throw new AuthError($"获取设备码失败 (HTTP {authResponse.StatusCode}): {authResponse.Text200}");
		}
		object obj = authResponse.Json();
		return PyDict.Of("device_code", Auth.Idx(obj, "device_code"), "user_code", Auth.Idx(obj, "user_code"), "verification_uri", Auth.Idx(obj, "verification_uri"), "expires_in", Py.Int(Auth.Idx(obj, "expires_in")), "interval", Py.Int((obj is PyDict pyDict) ? pyDict.Get("interval", 5L) : Authlib.PyGet(obj, "interval")));
	}

	public PyDict PollToken(object device_code, long interval = 5L, long expires_in = 900L, Action<string> on_status = null)
	{
		double num = Py.Time() + (double)expires_in;
		while (Py.Time() < num)
		{
			AuthResponse authResponse;
			object obj;
			try
			{
				authResponse = PostForm("https://login.live.com/oauth20_token.srf", PyDict.Of("grant_type", "urn:ietf:params:oauth:grant-type:device_code", "client_id", client_id, "device_code", device_code));
				try
				{
					obj = authResponse.Json();
				}
				catch (ValueError)
				{
					obj = new PyDict();
				}
			}
			catch (AuthHttpException)
			{
				on_status?.Invoke("网络错误，重试中…");
				Sleep(interval);
				continue;
			}
			if (authResponse.StatusCode == 200)
			{
				return PyDict.Of("access_token", Auth.Idx(obj, "access_token"), "refresh_token", Authlib.PyGet(obj, "refresh_token"));
			}
			object obj2 = ((obj is PyDict pyDict) ? pyDict.Get("error", "") : Authlib.PyGet(obj, "error"));
			if (Py.Eq(obj2, "authorization_pending"))
			{
				on_status?.Invoke("等待授权中…");
			}
			else
			{
				if (!Py.Eq(obj2, "slow_down"))
				{
					if (Py.Eq(obj2, "expired_token") || Py.Eq(obj2, "authorization_declined") || Py.Eq(obj2, "bad_verification_code"))
					{
						throw new AuthError("授权已过期或被拒绝，请重试。");
					}
					object o = ((PyDict)obj).Get("error_description", "");
					throw new AuthError("轮询令牌失败: " + Py.Str(obj2) + " " + Py.Str(o));
				}
				interval += 5;
			}
			Sleep(interval);
		}
		throw new AuthError("授权超时。");
	}

	private static void Sleep(long seconds)
	{
		if (seconds > 0)
		{
			Thread.Sleep(TimeSpan.FromSeconds(seconds));
		}
	}

	public object XblAuthenticate(object ms_token0)
	{
		string text = Py.Str(ms_token0);
		// login.live.com 设备码流程拿到的令牌要以 "d=" 开头，先试它，省掉一次必然失败的往返
		List<string> obj = ((text.StartsWith("d=", StringComparison.Ordinal) || text.StartsWith("t=", StringComparison.Ordinal)) ? new List<string> { text } : new List<string>
		{
			"d=" + text,
			text
		});
		AuthResponse authResponse = null;
		foreach (string item in obj)
		{
			authResponse = PostJson("https://user.auth.xboxlive.com/user/authenticate", PyDict.Of("Properties", PyDict.Of("AuthMethod", "RPS", "SiteName", "user.auth.xboxlive.com", "RpsTicket", item), "RelyingParty", "http://auth.xboxlive.com", "TokenType", "JWT"));
			if (authResponse.StatusCode == 200)
			{
				return Auth.Idx(authResponse.Json(), "Token");
			}
		}
		throw new AuthError($"Xbox Live 认证失败 (HTTP {authResponse.StatusCode})");
	}

	public object[] XstsAuthenticate(object xbl_token)
	{
		AuthResponse authResponse = PostJson("https://xsts.auth.xboxlive.com/xsts/authorize", PyDict.Of("Properties", PyDict.Of("SandboxId", "RETAIL", "UserTokens", new List<object> { xbl_token }), "RelyingParty", "rp://api.minecraftservices.com/", "TokenType", "JWT"));
		if (authResponse.StatusCode == 401)
		{
			object obj = Authlib.PyGet(authResponse.Json(), "XErr");
			string value = null;
			if (obj is long key)
			{
				Auth.XSTS_ERRORS.TryGetValue(key, out value);
			}
			else if (obj is bool flag)
			{
				Auth.XSTS_ERRORS.TryGetValue(flag ? 1 : 0, out value);
			}
			else if (obj is double num && num == Math.Floor(num) && Math.Abs(num) < 9E+18)
			{
				Auth.XSTS_ERRORS.TryGetValue((long)num, out value);
			}
			throw new AuthError("XSTS 认证失败: " + (value ?? ("未知错误 " + Py.Str(obj))));
		}
		if (authResponse.StatusCode != 200)
		{
			throw new AuthError($"XSTS 认证失败 (HTTP {authResponse.StatusCode})");
		}
		object obj2 = authResponse.Json();
		object obj3 = Auth.Idx(obj2, "Token");
		object obj4 = Auth.Idx(Auth.Idx(Auth.Idx(Auth.Idx(obj2, "DisplayClaims"), "xui"), 0L), "uhs");
		return new object[2] { obj3, obj4 };
	}

	public object McLogin(object uhs, object xsts_token)
	{
		AuthResponse authResponse = PostJson("https://api.minecraftservices.com/authentication/login_with_xbox", PyDict.Of("identityToken", "XBL3.0 x=" + Py.Str(uhs) + ";" + Py.Str(xsts_token)));
		if (authResponse.StatusCode != 200)
		{
			throw new AuthError($"Minecraft 登录失败 (HTTP {authResponse.StatusCode}): {authResponse.Text200}");
		}
		return Auth.Idx(authResponse.Json(), "access_token");
	}

	public bool CheckEntitlements(object mc_token)
	{
		AuthResponse bearer = GetBearer("https://api.minecraftservices.com/entitlements/mcstore", mc_token);
		if (bearer.StatusCode != 200)
		{
			throw new AuthError($"检查正版资格失败 (HTTP {bearer.StatusCode})");
		}
		object o = Py.Or(Authlib.PyGet(bearer.Json(), "items"), new List<object>());
		if (!Py.Truthy(o))
		{
			throw new AuthError("该账号尚未购买正版 Minecraft，或 Xbox Game Pass 已到期。");
		}
		List<object> list = new List<object>();
		foreach (object item2 in Py.ListCtor(o))
		{
			if (item2 is PyDict pyDict)
			{
				object n = pyDict.Get("name");
				if (!list.Any((object x) => Py.Eq(x, n)))
				{
					list.Add(n);
				}
			}
		}
		if (!list.Any((object obj) => obj is string item && Auth._OWNED_ITEMS.Contains(item)))
		{
			Utils.log.Warning("entitlements 未包含已知 Java 项，改由档案接口判定: %s", "{" + string.Join(", ", list.Select(Py.Repr)) + "}");
		}
		return true;
	}

	public PyDict GetProfile(object mc_token)
	{
		AuthResponse bearer = GetBearer("https://api.minecraftservices.com/minecraft/profile", mc_token);
		if (bearer.StatusCode == 404)
		{
			throw new AuthError("该账号尚未创建 Minecraft 档案（没有游戏角色名）。");
		}
		if (bearer.StatusCode != 200)
		{
			throw new AuthError($"获取玩家档案失败 (HTTP {bearer.StatusCode})");
		}
		object obj = bearer.Json();
		return PyDict.Of("uuid", Utils.DashedUuid(Py.Str(Auth.Idx(obj, "id"))), "name", Auth.Idx(obj, "name"));
	}

	public PyDict Login(Action<object, object, object> on_code = null, Action<string> on_status = null, bool open_browser = true)
	{
		PyDict deviceCode = GetDeviceCode();
		AuthHttp.Warmup("https://user.auth.xboxlive.com/", "https://xsts.auth.xboxlive.com/", "https://api.minecraftservices.com/");
		if (open_browser)
		{
			try
			{
				Auth.OpenBrowser(Py.Str(deviceCode["verification_uri"]));
			}
			catch (Exception)
			{
			}
		}
		on_code?.Invoke(deviceCode["user_code"], deviceCode["verification_uri"], deviceCode["expires_in"]);
		PyDict pyDict = PollToken(deviceCode["device_code"], (long)deviceCode["interval"], (long)deviceCode["expires_in"], on_status);
		object xbl_token = XblAuthenticate(pyDict["access_token"]);
		object[] array = XstsAuthenticate(xbl_token);
		object obj = McLogin(array[1], array[0]);
		System.Threading.Tasks.Task<bool> entitlements = System.Threading.Tasks.Task.Run(() => CheckEntitlements(obj));
		// 正版资格与玩家档案互不依赖，并行请求；报错优先级与原先串行时一致（先资格后档案）
		PyDict profile = null;
		Exception profileError = null;
		try
		{
			profile = GetProfile(obj);
		}
		catch (Exception ex)
		{
			profileError = ex;
		}
		entitlements.GetAwaiter().GetResult();
		if (profileError != null)
		{
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(profileError).Throw();
		}
		return PyDict.Of("type", "microsoft", "name", profile["name"], "uuid", profile["uuid"], "access_token", obj, "refresh_token", pyDict.Get("refresh_token"), "xuid", array[1], "expires_at", Py.Time() + 72000.0, "updated_at", Py.Time());
	}

	public PyDict Refresh(PyDict account)
	{
		if (!Py.Truthy(account.Get("refresh_token")))
		{
			throw new AuthError("缺少刷新令牌，需要重新登录。");
		}
		AuthResponse authResponse = PostForm("https://login.live.com/oauth20_token.srf", PyDict.Of("grant_type", "refresh_token", "client_id", client_id, "refresh_token", account.Req("refresh_token"), "scope", "service::user.auth.xboxlive.com::MBI_SSL"));
		if (authResponse.StatusCode != 200)
		{
			throw new AuthError("刷新令牌失败，需要重新登录。");
		}
		object obj = authResponse.Json();
		object ms_token = Auth.Idx(obj, "access_token");
		object obj2 = Py.Or(Authlib.PyGet(obj, "refresh_token"), account.Req("refresh_token"));
		object xbl_token = XblAuthenticate(ms_token);
		object[] array = XstsAuthenticate(xbl_token);
		object obj3 = McLogin(array[1], array[0]);
		PyDict profile = GetProfile(obj3);
		account.Update(PyDict.Of("name", profile["name"], "uuid", profile["uuid"], "access_token", obj3, "refresh_token", obj2, "xuid", array[1], "expires_at", Py.Time() + 72000.0, "updated_at", Py.Time()));
		return account;
	}
}
