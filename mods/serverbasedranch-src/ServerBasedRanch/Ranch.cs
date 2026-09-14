using System;
using System.Collections.Generic;
using UnityEngine;

namespace ServerBasedRanch;

internal static class Ranch
{
	// Shared with BruceQoL's client-side replay: whoever ticks an animal stamps this, so the other side
	// finds (almost) no elapsed time and never double-counts.
	private static readonly int KeyLastSim = "BQ_lastSim".GetStableHashCode();
	private const float Step = 10f; // Procreation.m_updateInterval; taming is linear so it does not care

	private sealed class Species
	{
		public string Name;
		public float TamingTime, FedDuration;
		public HashSet<int> Food = new();
		public bool Breeds;
		public float PregnancyChance, PregnancyDuration, PartnerRange, TotalRange, SpawnOffset, SpawnOffsetMax;
		public bool RandomDir;
		public int RequiredLove, MinOffspringLevel;
		public int OffspringHash, PartnerHash, NoPartnerOffspringHash; // PartnerHash 0 = same species
	}

	private sealed class Flags
	{
		public bool Persistent, Distant;
		public ZDO.ObjectType Type;
	}

	private static Dictionary<int, Species> s_species;
	private static HashSet<int> s_young;    // offspring prefabs, counted toward the pen cap
	private static HashSet<int> s_allFood;
	private static readonly Dictionary<int, Flags> s_flags = new();

	private static void BuildSpecies()
	{
		s_species = new Dictionary<int, Species>();
		s_young = new HashSet<int>();
		s_allFood = new HashSet<int>();
		foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
		{
			Tameable t = prefab.GetComponent<Tameable>();
			if (t == null) continue;
			Species sp = new Species { Name = prefab.name, TamingTime = t.m_tamingTime, FedDuration = t.m_fedDuration };
			MonsterAI ai = prefab.GetComponent<MonsterAI>();
			if (ai != null && ai.m_consumeItems != null)
			{
				foreach (ItemDrop food in ai.m_consumeItems)
				{
					if (food == null) continue;
					int h = food.gameObject.name.GetStableHashCode();
					sp.Food.Add(h);
					s_allFood.Add(h);
				}
			}
			Procreation p = prefab.GetComponent<Procreation>();
			if (p != null && p.m_offspring != null)
			{
				sp.Breeds = true;
				sp.PregnancyChance = p.m_pregnancyChance;
				sp.PregnancyDuration = p.m_pregnancyDuration;
				sp.PartnerRange = p.m_partnerCheckRange;
				sp.TotalRange = p.m_totalCheckRange;
				sp.SpawnOffset = p.m_spawnOffset;
				sp.SpawnOffsetMax = p.m_spawnOffsetMax;
				sp.RandomDir = p.m_spawnRandomDirection;
				sp.RequiredLove = p.m_requiredLovePoints;
				sp.MinOffspringLevel = p.m_minOffspringLevel;
				sp.OffspringHash = p.m_offspring.name.GetStableHashCode();
				sp.PartnerHash = p.m_seperatePartner != null ? p.m_seperatePartner.name.GetStableHashCode() : 0;
				sp.NoPartnerOffspringHash = p.m_noPartnerOffspring != null ? p.m_noPartnerOffspring.name.GetStableHashCode() : 0;
				s_young.Add(sp.OffspringHash);
				if (sp.NoPartnerOffspringHash != 0) s_young.Add(sp.NoPartnerOffspringHash);
			}
			s_species[prefab.name.GetStableHashCode()] = sp;
		}
		ServerBasedRanchPlugin.Log.LogInfo($"ServerBasedRanch: tracking {s_species.Count} tameable species, {s_allFood.Count} food items.");
	}

	private static Flags GetFlags(int prefabHash)
	{
		if (s_flags.TryGetValue(prefabHash, out Flags f)) return f;
		f = new Flags { Persistent = true, Distant = false, Type = ZDO.ObjectType.Default };
		GameObject prefab = ZNetScene.instance.GetPrefab(prefabHash);
		ZNetView view = prefab != null ? prefab.GetComponent<ZNetView>() : null;
		if (view != null)
		{
			f.Persistent = view.m_persistent;
			f.Distant = view.m_distant;
			f.Type = view.m_type;
		}
		s_flags[prefabHash] = f;
		return f;
	}

	internal static void Tick()
	{
		if (s_species == null) BuildSpecies();
		if (s_species.Count == 0) return;
		DateTime now = ZNet.instance.GetTime();
		long session = ZDOMan.GetSessionID();
		List<ZNetPeer> peers = ZNet.instance.GetPeers();

		// One pass over the world: candidates, and per-sector lists of animals (for the pen census) and food.
		List<ZDO> candidates = new();
		Dictionary<Vector2s, List<ZDO>> animals = new();
		Dictionary<Vector2s, List<ZDO>> food = new();
		foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
		{
			int prefab = zdo.GetPrefab();
			if (s_species.ContainsKey(prefab))
			{
				Add(animals, zdo);
				if (zdo.GetBool(ZDOVars.s_tamed) || zdo.GetLong(ZDOVars.s_tameLastFeeding, 0L) != 0L) candidates.Add(zdo);
			}
			else if (s_young.Contains(prefab)) Add(animals, zdo);
			else if (s_allFood.Contains(prefab)) Add(food, zdo);
		}

		Stats st = new();
		foreach (ZDO zdo in candidates)
		{
			if (!zdo.IsValid()) continue;
			// Leave loaded zones (and anything a connected client owns) to that client.
			if (IsLoadedOrClientOwned(zdo, peers, session)) { st.Loaded++; continue; }
			Species sp = s_species[zdo.GetPrefab()];
			try
			{
				Simulate(zdo, sp, now, session, peers, animals, food, st);
			}
			catch (Exception e)
			{
				ServerBasedRanchPlugin.Log.LogWarning($"ServerBasedRanch: {sp.Name} at {zdo.GetPosition()} skipped: {e.GetType().Name}: {e.Message}");
			}
		}
		if (candidates.Count == 0) return;
		bool active = st.Ate > 0 || st.Tamed > 0 || st.Born > 0 || st.Love > 0 || st.Pregnant > 0;
		if (ServerBasedRanchPlugin.LogActivity.Value && (active || ServerBasedRanchPlugin.LogDetails.Value))
		{
			ServerBasedRanchPlugin.Log.LogInfo($"ServerBasedRanch: {candidates.Count} tracked: {st.Loaded} loaded (client's), {st.Waiting} waiting for a step, {st.Advanced} advanced (ate {st.Ate}, tamed {st.Tamed}, love +{st.Love}, conceived {st.Pregnant}, born {st.Born}); blocked: {st.HungryNoFood} hungry without food in reach, {st.NoPartner} without a tame partner within {ServerBasedRanchPlugin.PartnerRadius.Value:0} m, {st.PenFull} pen full.");
		}
	}

	private sealed class Stats
	{
		public int Loaded, Waiting, Advanced, Ate, Tamed, Love, Pregnant, Born, HungryNoFood, NoPartner, PenFull;
	}

	// "Loaded" is the game's own synced active-area rule (a box of 1.5 zones, 1 zone at the lowest
	// setting, around the player's zone) plus a 32 m margin, because the server learns a player's position
	// a moment late. Anything a connected client owns is theirs too. The margin must stay small: a pen
	// inside the margin but outside the real active area is ticked by nobody.
	private const float LoadedMargin = 32f;

	private static bool IsLoadedOrClientOwned(ZDO zdo, List<ZNetPeer> peers, long session)
	{
		Vector3 pos = zdo.GetPosition();
		pos.y = 0f;
		float zoneSize = ZoneSystem.instance.m_zoneSize;
		float box = (ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance == 1 ? 1f : 1.5f) * zoneSize + LoadedMargin;
		long owner = zdo.GetOwner();
		foreach (ZNetPeer peer in peers)
		{
			if (owner != 0L && owner != session && peer.m_uid == owner) return true;
			if (!peer.IsReady()) continue;
			if (ZNetScene.InActiveArea(pos, peer.m_refPos)) return true;
			Vector3 zonePos = ZoneSystem.GetZonePos(ZoneSystem.GetZone(peer.m_refPos));
			zonePos.y = 0f;
			if (Utils.ChebyshevDistance(zonePos, pos) <= box) return true;
		}
		return false;
	}

	private static void Add(Dictionary<Vector2s, List<ZDO>> map, ZDO zdo)
	{
		Vector2s sector = zdo.GetSector();
		if (!map.TryGetValue(sector, out List<ZDO> list)) map[sector] = list = new List<ZDO>();
		list.Add(zdo);
	}

	private static IEnumerable<ZDO> Near(Dictionary<Vector2s, List<ZDO>> map, Vector2s sector)
	{
		for (int dx = -1; dx <= 1; dx++)
			for (int dy = -1; dy <= 1; dy++)
				if (map.TryGetValue(new Vector2s(sector.x + dx, sector.y + dy), out List<ZDO> list))
					foreach (ZDO z in list) yield return z;
	}

	// Mirrors Tameable.TamingUpdate + Procreation.Procreate on saved fields. Returns true if anything ran.
	private static void Simulate(ZDO zdo, Species sp, DateTime now, long session, List<ZNetPeer> peers, Dictionary<Vector2s, List<ZDO>> animals,
		Dictionary<Vector2s, List<ZDO>> foodMap, Stats st)
	{
		long lastSim = zdo.GetLong(KeyLastSim, 0L);
		if (lastSim == 0L)
		{
			zdo.Set(KeyLastSim, now.Ticks);
			st.Waiting++;
			return;
		}
		double real = (now - new DateTime(lastSim)).TotalSeconds;
		double cap = Math.Max(0f, ServerBasedRanchPlugin.MaxHours.Value) * 3600.0;
		bool capped = real > cap;
		if (capped) real = cap;
		// Unloaded time counts at a fraction of real time (default half). Only whole steps are simulated;
		// the stamp advances by the real time those steps used, so the remainder carries over.
		double speed = Math.Max(0f, ServerBasedRanchPlugin.UnloadedSpeed.Value) / 100.0;
		int steps = (int)(real * speed / Step);
		if (steps < 1) { st.Waiting++; return; }
		double elapsed = steps * Step;
		long newStamp = capped || speed <= 0 ? now.Ticks : Math.Min(now.Ticks, lastSim + TimeSpan.FromSeconds(elapsed / speed).Ticks);
		DateTime t0 = now.AddSeconds(-elapsed);
		Vector3 pos = zdo.GetPosition();
		Vector2s sector = zdo.GetSector();

		// Food within reach, nearest first. A pile in a zone some client has loaded (pen straddling a zone
		// edge) is that client's to run, so it is left alone and the animal simply stays hungry this tick.
		float radius = Math.Max(1f, ServerBasedRanchPlugin.FeedRadius.Value);
		List<ZDO> food = new();
		foreach (ZDO f in Near(foodMap, sector))
		{
			if (!f.IsValid() || !sp.Food.Contains(f.GetPrefab()) || Vector3.Distance(f.GetPosition(), pos) > radius) continue;
			if (IsLoadedOrClientOwned(f, peers, session)) continue;
			food.Add(f);
		}
		food.Sort((a, b) => Vector3.Distance(a.GetPosition(), pos).CompareTo(Vector3.Distance(b.GetPosition(), pos)));

		// Pen census. Unloaded animals do not move, so the partner test uses a pen-sized radius instead of
		// vanilla's 3 m (which vanilla re-checks every 10 s while they wander around each other).
		int penCount = 0, partners = 0;
		int myHash = zdo.GetPrefab();
		int partnerHash = sp.PartnerHash != 0 ? sp.PartnerHash : myHash;
		float partnerRadius = Math.Max(sp.PartnerRange, ServerBasedRanchPlugin.PartnerRadius.Value);
		if (sp.Breeds)
		{
			foreach (ZDO a in Near(animals, sector))
			{
				if (!a.IsValid()) continue;
				int h = a.GetPrefab();
				float d = Vector3.Distance(a.GetPosition(), pos);
				if ((h == myHash || h == sp.OffspringHash || h == sp.NoPartnerOffspringHash) && d < sp.TotalRange) penCount++;
				if (h == partnerHash && a.GetBool(ZDOVars.s_tamed) && d < partnerRadius) partners++;
			}
		}
		bool partnerOk = sp.Breeds && (sp.NoPartnerOffspringHash != 0 || (sp.PartnerHash != 0 ? partners >= 1 : partners >= 2));
		int penCap = ServerBasedRanchPlugin.MaxPerPen.Value > 0 ? ServerBasedRanchPlugin.MaxPerPen.Value : 4;

		DateTime lastFeed = new DateTime(zdo.GetLong(ZDOVars.s_tameLastFeeding, 0L));
		bool tamed = zdo.GetBool(ZDOVars.s_tamed);
		float timeLeft = zdo.GetFloat(ZDOVars.s_tameTimeLeft, sp.TamingTime);
		int love = zdo.GetInt(ZDOVars.s_lovePoints);
		long pregnant = zdo.GetLong(ZDOVars.s_pregnant, 0L);
		bool tameNow = false;
		int fed = 0, loveGained = 0, conceived = 0;
		bool hungryNoFood = false, noPartner = false, penFull = false;
		List<DateTime> births = new();

		for (DateTime t = t0.AddSeconds(Step); t <= now; t = t.AddSeconds(Step))
		{
			bool hungry = (t - lastFeed).TotalSeconds > sp.FedDuration;
			if (hungry && food.Count > 0)
			{
				if (Consume(food[0], session)) { fed++; lastFeed = t; hungry = false; }
				if (!food[0].IsValid()) food.RemoveAt(0);
			}
			if (hungry) hungryNoFood = true;
			if (!tamed)
			{
				if (!hungry)
				{
					timeLeft -= Step;
					if (timeLeft <= 0f) { timeLeft = 0f; tamed = true; tameNow = true; }
				}
				continue;
			}
			if (!sp.Breeds) continue;
			if (pregnant != 0L)
			{
				if ((t - new DateTime(pregnant)).TotalSeconds > sp.PregnancyDuration)
				{
					pregnant = 0L;
					births.Add(t);
					penCount++;
				}
				continue;
			}
			if (hungry) continue;
			if (penCount >= penCap) { penFull = true; continue; }
			if (!partnerOk) { noPartner = true; continue; }
			if (UnityEngine.Random.value <= sp.PregnancyChance) continue;
			love++;
			loveGained++;
			if (love >= sp.RequiredLove) { love = 0; pregnant = t.Ticks; conceived++; }
		}

		if (fed > 0) zdo.Set(ZDOVars.s_tameLastFeeding, lastFeed.Ticks);
		if (!zdo.GetBool(ZDOVars.s_tamed)) zdo.Set(ZDOVars.s_tameTimeLeft, timeLeft);
		if (tameNow)
		{
			zdo.Set(ZDOVars.s_tamed, true);
			zdo.Set(ZDOVars.s_despawnInDay, false);
			zdo.Set(ZDOVars.s_eventCreature, false);
			st.Tamed++;
		}
		if (sp.Breeds)
		{
			zdo.Set(ZDOVars.s_lovePoints, love, true);
			zdo.Set(ZDOVars.s_pregnant, pregnant);
			foreach (DateTime birth in births)
			{
				SpawnOffspring(zdo, sp, partners, birth, animals);
				st.Born++;
			}
		}
		zdo.Set(KeyLastSim, newStamp);
		st.Advanced++;
		st.Ate += fed;
		st.Love += loveGained;
		st.Pregnant += conceived;
		if (hungryNoFood) st.HungryNoFood++;
		if (noPartner) st.NoPartner++;
		if (penFull) st.PenFull++;
	}

	// Take one item from a dropped stack (the stack lives inside the item's saved ItemData package).
	private static bool Consume(ZDO item, long session)
	{
		GameObject prefab = ZNetScene.instance.GetPrefab(item.GetPrefab());
		ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
		if (drop == null) return false;
		ItemDrop.ItemData data = drop.m_itemData.Clone();
		ItemDrop.LoadFromZDO(data, item);
		if (data.m_stack <= 1)
		{
			item.SetOwner(session);
			ZDOMan.instance.DestroyZDO(item);
			return true;
		}
		data.m_stack--;
		ItemDrop.SaveToZDO(data, item);
		return true;
	}

	// Mirrors the birth half of Procreation.Procreate, as a saved object with the fields the game reads on load.
	private static void SpawnOffspring(ZDO mother, Species sp, int partners, DateTime birth, Dictionary<Vector2s, List<ZDO>> animals)
	{
		int hash = sp.OffspringHash;
		if (sp.NoPartnerOffspringHash != 0)
		{
			bool noPartner = sp.PartnerHash != 0 ? partners < 1 : partners < 2;
			if (noPartner) hash = sp.NoPartnerOffspringHash;
		}
		if (ZNetScene.instance.GetPrefab(hash) == null) return;
		Quaternion rot = mother.GetRotation();
		Vector3 forward = rot * Vector3.forward;
		Vector3 dir = forward;
		if (sp.RandomDir)
		{
			float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
			dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
		}
		float off = sp.SpawnOffsetMax > 0f ? UnityEngine.Random.Range(sp.SpawnOffset, sp.SpawnOffsetMax) : sp.SpawnOffset;
		Vector3 pos = mother.GetPosition() - dir * off;
		// Never below the ground: the mother's height, or the world's base terrain there if that is higher
		// (a pen on a slope), plus a small lift so the creature settles instead of clipping in.
		if (WorldGenerator.instance != null)
		{
			float ground = WorldGenerator.instance.GetHeight(pos.x, pos.z);
			if (ground > pos.y) pos.y = ground;
		}
		pos.y += 0.3f;
		int level = Mathf.Max(sp.MinOffspringLevel, mother.GetInt(ZDOVars.s_level, 1));

		Flags flags = GetFlags(hash);
		ZDO child = ZDOMan.instance.CreateNewZDO(pos, hash);
		child.Persistent = flags.Persistent;
		child.Type = flags.Type;
		child.Distant = flags.Distant;
		child.SetPrefab(hash);
		child.SetRotation(Quaternion.LookRotation(-forward, Vector3.up));
		child.Set(ZDOVars.s_tamed, mother.GetBool(ZDOVars.s_tamed));
		child.Set(ZDOVars.s_level, level, true);
		child.Set(ZDOVars.s_quality, level, true); // for egg-type offspring (ItemDrop)
		child.Set(ZDOVars.s_spawnTime, birth.Ticks);
		child.Set(KeyLastSim, birth.Ticks);
		child.SetOwner(0L); // nobody's until a client loads the zone
		Add(animals, child);
	}
}
