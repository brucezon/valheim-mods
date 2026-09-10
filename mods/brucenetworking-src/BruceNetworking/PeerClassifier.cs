using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace BruceNetworking;

// Classifies each connected peer as LAN or remote (by the remote address of its Steam networking
// connection, or by name override) and keeps a smoothed ping from Steam's real-time connection status.
internal static class PeerClassifier
{
	internal sealed class PeerInfo
	{
		public long Uid;
		public string Name = "";
		public string Address = "?";
		public bool Lan;
		public bool Forced;
		public float Ping = -1f;          // ms, smoothed; -1 = unknown
		public float LastUpdate = -1000f;
		public bool SocketLogged;

		public string Class => Lan ? "LAN" : "remote";
		public override string ToString() => $"{Name}[{Class}{(Forced ? "*" : "")} {Address} {(Ping < 0 ? "?" : ((int)Ping).ToString())}ms]";
	}

	private struct Subnet
	{
		public uint Net;
		public uint Mask;
	}

	private static readonly Dictionary<long, PeerInfo> s_infos = new();
	private static readonly List<Subnet> s_subnets = new();
	private static readonly HashSet<string> s_forceLan = new(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<string> s_forceRemote = new(StringComparer.OrdinalIgnoreCase);
	private const float RefreshInterval = 2f;
	private const float PingSmoothing = 0.3f;

	internal static void Reload()
	{
		s_subnets.Clear();
		foreach (string part in Split(BruceNetworkingPlugin.LanSubnets.Value))
		{
			if (TryParseCidr(part, out Subnet s))
			{
				s_subnets.Add(s);
			}
			else
			{
				BruceNetworkingPlugin.Log.LogWarning($"Ignoring unparseable LAN subnet '{part}'");
			}
		}
		s_forceLan.Clear();
		foreach (string n in Split(BruceNetworkingPlugin.ForceLanPlayers.Value)) s_forceLan.Add(n);
		s_forceRemote.Clear();
		foreach (string n in Split(BruceNetworkingPlugin.ForceRemotePlayers.Value)) s_forceRemote.Add(n);
		s_infos.Clear(); // reclassify everyone on next lookup
	}

	internal static PeerInfo Get(ZNetPeer peer)
	{
		if (peer == null) return null;
		if (!s_infos.TryGetValue(peer.m_uid, out PeerInfo info))
		{
			info = new PeerInfo { Uid = peer.m_uid };
			s_infos[peer.m_uid] = info;
		}
		float now = Time.time;
		if (now - info.LastUpdate >= RefreshInterval)
		{
			info.LastUpdate = now;
			Refresh(peer, info);
		}
		return info;
	}

	internal static PeerInfo Lookup(long uid)
	{
		return s_infos.TryGetValue(uid, out PeerInfo info) ? info : null;
	}

	internal static void Forget(long uid)
	{
		s_infos.Remove(uid);
	}

	private static void Refresh(ZNetPeer peer, PeerInfo info)
	{
		info.Name = string.IsNullOrEmpty(peer.m_playerName) ? peer.m_uid.ToString() : peer.m_playerName;
		bool lan = false;
		bool addressKnown = false;
		float ping = -1f;

		if (!info.SocketLogged)
		{
			info.SocketLogged = true;
			BruceNetworkingPlugin.Log.LogInfo($"peer {info.Name}: socket chain {DescribeChain(peer.m_socket)}");
		}
		if (TryGetConnection(peer.m_socket, out HSteamNetConnection con))
		{
			try
			{
				if (SteamGameServerNetworkingSockets.GetConnectionInfo(con, out SteamNetConnectionInfo_t ci))
				{
					SteamNetworkingIPAddr addr = ci.m_addrRemote;
					if (addr.IsIPv4())
					{
						uint ip = addr.GetIPv4();
						info.Address = $"{(ip >> 24) & 255}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}";
						lan = InLan(ip);
						addressKnown = true;
					}
					else
					{
						addr.ToString(out string text, false);
						info.Address = string.IsNullOrEmpty(text) ? "ipv6/relay" : text;
					}
				}
				SteamNetConnectionRealTimeStatus_t st = default;
				SteamNetConnectionRealTimeLaneStatus_t lane = default;
				if (SteamGameServerNetworkingSockets.GetConnectionRealTimeStatus(con, ref st, 0, ref lane) == EResult.k_EResultOK)
				{
					ping = st.m_nPing;
				}
			}
			catch (Exception e)
			{
				BruceNetworkingPlugin.Log.LogWarning($"Peer probe failed for {info.Name}: {e.GetType().Name}: {e.Message}");
			}
		}
		else if (peer.m_socket != null)
		{
			info.Address = peer.m_socket.GetHostName();
		}

		info.Forced = false;
		if (s_forceLan.Contains(info.Name)) { lan = true; info.Forced = true; }
		else if (s_forceRemote.Contains(info.Name)) { lan = false; info.Forced = true; }
		else if (!addressKnown) { lan = false; }
		info.Lan = lan;

		if (ping >= 0f)
		{
			info.Ping = info.Ping < 0f ? ping : Mathf.Lerp(info.Ping, ping, PingSmoothing);
		}
	}

	private static readonly System.Reflection.FieldInfo s_conField = HarmonyLib.AccessTools.Field(typeof(ZSteamSocket), "m_con");

	// Finds the Steam connection handle behind a peer socket. ServerSync (one private copy per mod)
	// wraps the vanilla socket in a BufferingSocket during login and, with several such mods, leaves
	// the peer holding a chain of wrappers one shorter than the mod count. Each wrapper keeps the
	// inner socket in an ISocket-typed field, so walk those, generously deep.
	private const int MaxUnwrapDepth = 32;

	private static object InnerSocket(object wrapper)
	{
		foreach (System.Reflection.FieldInfo f in HarmonyLib.AccessTools.GetDeclaredFields(wrapper.GetType()))
		{
			if (typeof(ISocket).IsAssignableFrom(f.FieldType))
			{
				object inner = f.GetValue(wrapper);
				if (inner != null && !ReferenceEquals(inner, wrapper)) return inner;
			}
		}
		return null;
	}

	private static bool TryGetConnection(ISocket socket, out HSteamNetConnection con)
	{
		con = HSteamNetConnection.Invalid;
		object current = socket;
		for (int depth = 0; depth < MaxUnwrapDepth && current != null; depth++)
		{
			if (current is ZSteamSocket steam)
			{
				if (s_conField == null) return false;
				con = (HSteamNetConnection)s_conField.GetValue(steam);
				return con != HSteamNetConnection.Invalid;
			}
			current = InnerSocket(current);
		}
		return false;
	}

	private static string DescribeChain(ISocket socket)
	{
		if (socket == null) return "null";
		System.Text.StringBuilder sb = new();
		object current = socket;
		int depth = 0;
		while (current != null && depth < MaxUnwrapDepth)
		{
			if (depth > 0) sb.Append(" > ");
			sb.Append(current.GetType().Name);
			if (current is ZSteamSocket) break;
			current = InnerSocket(current);
			depth++;
		}
		return $"{sb} (depth {depth})";
	}

	// true when candidate should own things in preference to current.
	internal static bool IsBetter(PeerInfo candidate, PeerInfo current)
	{
		if (candidate == null || current == null) return false;
		if (candidate.Lan != current.Lan) return candidate.Lan;
		if (!BruceNetworkingPlugin.PingTiebreak.Value) return false;
		if (candidate.Ping < 0f || current.Ping < 0f) return false;
		return current.Ping - candidate.Ping >= BruceNetworkingPlugin.PingDelta.Value;
	}

	// Sort key: LAN first, then lowest ping (unknown ping sorts last within its class).
	internal static int Compare(PeerInfo a, PeerInfo b)
	{
		if (a.Lan != b.Lan) return a.Lan ? -1 : 1;
		float pa = a.Ping < 0f ? float.MaxValue : a.Ping;
		float pb = b.Ping < 0f ? float.MaxValue : b.Ping;
		return pa.CompareTo(pb);
	}

	private static bool InLan(uint ip)
	{
		foreach (Subnet s in s_subnets)
		{
			if ((ip & s.Mask) == s.Net) return true;
		}
		return false;
	}

	private static IEnumerable<string> Split(string value)
	{
		foreach (string part in (value ?? "").Split(',', ';'))
		{
			string t = part.Trim();
			if (t.Length > 0) yield return t;
		}
	}

	private static bool TryParseCidr(string text, out Subnet subnet)
	{
		subnet = default;
		string[] halves = text.Split('/');
		if (halves.Length > 2) return false;
		string[] octets = halves[0].Split('.');
		if (octets.Length != 4) return false;
		uint ip = 0;
		foreach (string o in octets)
		{
			if (!byte.TryParse(o.Trim(), out byte b)) return false;
			ip = (ip << 8) | b;
		}
		int bits = 32;
		if (halves.Length == 2 && (!int.TryParse(halves[1].Trim(), out bits) || bits < 0 || bits > 32)) return false;
		uint mask = bits == 0 ? 0u : uint.MaxValue << (32 - bits);
		subnet = new Subnet { Net = ip & mask, Mask = mask };
		return true;
	}
}
