using System;
using System.Collections.Generic;
using System.Linq;

namespace TipCL.Core.Launcher;

public class AccountManager
{
	public List<object> accounts = new List<object>();

	public object active;

	public readonly object _sync = new object();

	public static Func<PyDict, PyDict> MicrosoftRefreshHook = DefaultMicrosoftRefresh;

	public static Func<PyDict, PyDict> AuthlibRefreshHook = Authlib.Refresh;

	public static Func<PyDict, PyDict> Nide8RefreshHook = Nide8.Refresh;

	public AccountManager()
	{
		Load();
	}

	public void Load()
	{
		object obj = Utils.ReadJson(Auth.ACCOUNTS_FILE);
		if (!Py.Truthy(obj))
		{
			obj = new PyDict();
		}
		PyDict pyDict = obj as PyDict;
		if (pyDict == null)
		{
			pyDict = new PyDict();
		}
		List<object> source = (pyDict.Get("accounts", new List<object>()) as List<object>) ?? new List<object>();
		accounts = source.Where((object a) => a is PyDict).Select<object, object>((Func<object, object>)((object a) => Auth.OpenAccount((PyDict)a))).ToList();
		active = pyDict["active"];
	}

	public void Save()
	{
		List<object> list = ((IEnumerable<object>)accounts).Select((Func<object, object>)((object a) => Auth.SealAccount((PyDict)a))).ToList();
		Utils.WriteJson(Auth.ACCOUNTS_FILE, PyDict.Of("active", active, "accounts", list));
		Auth._restrict_accounts_file();
	}

	public PyDict AddAccount(PyDict account)
	{
		_remove_stored_secrets(account["name"]);
		accounts = accounts.Where((object a) => !Py.Eq(((PyDict)a)["name"], account["name"])).ToList();
		accounts.Add(account);
		active = account.Req("name");
		Save();
		return account;
	}

	public void RemoveAccount(object name)
	{
		_remove_stored_secrets(name);
		PyDict account = GetAccount(name);
		if (account != null && Py.Truthy(account["skin_file"]))
		{
			Skin.RemoveSkin(account);
		}
		accounts = accounts.Where((object a) => !Py.Eq(((PyDict)a)["name"], name)).ToList();
		if (Py.Eq(active, name))
		{
			active = ((accounts.Count > 0) ? ((PyDict)accounts[0]).Req("name") : null);
		}
		Save();
	}

	private void _remove_stored_secrets(object name)
	{
		if (!Py.Truthy(name))
		{
			return;
		}
		object obj = Utils.ReadJson(Auth.ACCOUNTS_FILE, new PyDict());
		if (!Py.Truthy(obj))
		{
			obj = new PyDict();
		}
		if (!(obj is PyDict pyDict) || !(pyDict.Get("accounts", new List<object>()) is List<object> list))
		{
			return;
		}
		foreach (object item in list)
		{
			if (item is PyDict pyDict2 && Py.Eq(pyDict2["name"], name))
			{
				string[] tOKEN_KEYS = Auth._TOKEN_KEYS;
				foreach (string key in tOKEN_KEYS)
				{
					Auth._delete_keyring_reference(Py.Or(pyDict2[key], ""));
				}
			}
		}
	}

	public PyDict GetAccount(object name)
	{
		foreach (PyDict account in accounts)
		{
			if (Py.Eq(account["name"], name))
			{
				return account;
			}
		}
		return null;
	}

	public PyDict GetActive()
	{
		if (Py.Truthy(active))
		{
			PyDict account = GetAccount(active);
			if (account != null)
			{
				return account;
			}
		}
		return null;
	}

	public object SetActive(object name)
	{
		if (Py.Truthy(name) && GetAccount(name) != null)
		{
			active = name;
			Save();
		}
		return active;
	}

	public PyDict OfflineAccount(object username0, object skin0 = null)
	{
		string text = Py.Strip(Py.AsStr(username0, "strip"));
		if (text.Length == 0)
		{
			text = "Player";
		}
		string text2 = Py.Lower(Py.AsStr(Py.Or(skin0, "default"), "lower"));
		string text3 = ((text2 == "steve") ? "8667ba71-b85a-4004-af54-457a9734eed7" : ((!(text2 == "alex")) ? Utils.OfflineUuid(text) : "ec561538-f3fd-461d-a7c9-7aa354f5bba9"));
		return PyDict.Of("type", "offline", "name", text, "uuid", text3, "skin", text2);
	}

	private static PyDict DefaultMicrosoftRefresh(PyDict acc)
	{
		return new MicrosoftAuthenticator(Py.Str(Py.Or(Config.CONFIG.Get("microsoft_client_id"), "00000000402b5328"))).Refresh(acc);
	}

	private static bool Expired(PyDict account)
	{
		return Py.Time() > Py.Float(Py.Or(account.Get("expires_at"), 0L));
	}

	public PyDict EnsureValid(PyDict account)
	{
		if (!Py.Truthy(account))
		{
			return account;
		}
		object a = account.Get("type");
		if (Py.Eq(a, "nide8"))
		{
			if (!Expired(account) && Py.Truthy(account.Get("access_token")))
			{
				return account;
			}
			Func<PyDict, PyDict> func = Nide8RefreshHook ?? new Func<PyDict, PyDict>(Nide8.Refresh);
			return AddAccount(func(account));
		}
		if (Py.Eq(a, "authlib"))
		{
			if (!Expired(account) && Py.Truthy(account.Get("access_token")))
			{
				return account;
			}
			Func<PyDict, PyDict> func2 = AuthlibRefreshHook ?? new Func<PyDict, PyDict>(Authlib.Refresh);
			return AddAccount(func2(account));
		}
		if (!Py.Eq(a, "microsoft"))
		{
			return account;
		}
		if (!Expired(account) && Py.Truthy(account.Get("access_token")))
		{
			return account;
		}
		if (!Py.Truthy(account.Get("refresh_token")))
		{
			throw new AuthError("正版令牌已过期且无法刷新，请重新登录。");
		}
		Func<PyDict, PyDict> obj = MicrosoftRefreshHook ?? new Func<PyDict, PyDict>(DefaultMicrosoftRefresh);
		object originalName = account.Get("name");
		PyDict pyDict = obj(new PyDict(account));
		if (!Py.Eq(pyDict.Req("name"), originalName))
		{
			if (GetAccount(pyDict["name"]) != null)
			{
				throw new AuthError("刷新后的角色名与已有账号冲突，请先处理同名账号。");
			}
			_remove_stored_secrets(originalName);
			accounts = accounts.Where((object obj2) => !Py.Eq(((PyDict)obj2).Get("name"), originalName)).ToList();
		}
		account.Update(pyDict);
		return AddAccount(account);
	}

	public PyDict LaunchProps(PyDict account)
	{
		object a = account.Get("type");
		if (Py.Eq(a, "microsoft"))
		{
			return PyDict.Of("name", account.Req("name"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), "token", Py.Or(account.Get("access_token"), "0"), "user_type", "msa", "xuid", Py.Or(account.Get("xuid"), ""));
		}
		if (Py.Eq(a, "authlib"))
		{
			return PyDict.Of("name", account.Get("name", "Player"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), "token", Py.Or(account.Get("access_token"), "0"), "user_type", "mojang", "xuid", "", "authlib_api", Py.Or(account.Get("api"), ""));
		}
		if (Py.Eq(a, "nide8"))
		{
			return PyDict.Of("name", account.Get("name", "Player"), "uuid", Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), "token", Py.Or(account.Get("access_token"), "0"), "user_type", "mojang", "xuid", "", "nide8_id", Py.Or(account.Get("server_id"), ""));
		}
		object obj = account.Get("name", "Player");
		return PyDict.Of("name", obj, "uuid", Py.Or(Utils.DashedUuid(Py.Str(Py.Or(account.Get("uuid"), ""))), Utils.OfflineUuid(Py.Str(obj))), "token", "0", "user_type", "legacy", "xuid", "", "skin_file", Py.Or(account.Get("skin_file"), ""), "skin_model", Py.Or(account.Get("skin_model"), ""));
	}
}
