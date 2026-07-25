using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace Bifrost
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    public class BifrostPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "ashr4f.bifrost";
        public const string ModName = "Bifrost";
        public const string ModVersion = "1.0.0";

        internal static ManualLogSource Log = null!;

        private static readonly ConfigSync configSync = new ConfigSync(ModGuid)
        {
            DisplayName = ModName,
            CurrentVersion = ModVersion,
            MinimumRequiredVersion = ModVersion,
            ModRequired = false
        };

        private static ConfigEntry<bool> _serverConfigLocked = null!;
        internal static ConfigEntry<bool> Enabled = null!;
        internal static ConfigEntry<bool> QuickTravel = null!;
        internal static ConfigEntry<bool> IgnoreRestrictions = null!;
        internal static ConfigEntry<string> PortalPrefabs = null!;
        internal static ConfigEntry<bool> OpenOnEnter = null!;

        private ConfigEntry<TVal> BindSynced<TVal>(string group, string name, TVal value, string description)
        {
            ConfigEntry<TVal> entry = Config.Bind(group, name, value, description);
            configSync.AddConfigEntry(entry);
            return entry;
        }

        private void Awake()
        {
            Log = Logger;

            _serverConfigLocked = BindSynced("General", "Lock Configuration", true,
                "If on and the server runs Bifrost, the server configuration is enforced for everyone.");
            configSync.AddLockingConfigEntry(_serverConfigLocked);

            Enabled = BindSynced("General", "Enabled", true,
                "Master switch. If off, portals behave like vanilla.");

            QuickTravel = BindSynced("General", "Quick Travel", true,
                "Removes the artificial teleport wait. The screen stays faded until the destination area is ready, so the unloaded world is never shown.");

            IgnoreRestrictions = BindSynced("General", "Ignore Teleport Restrictions", false,
                "If on, items that normally cannot pass through portals no longer block travel.");

            PortalPrefabs = BindSynced("General", "Portal Prefabs", "portal_wood, portal_stone",
                "Comma-separated prefab names treated as portals.");

            OpenOnEnter = Config.Bind("General", "Open On Enter", true,
                "Walking into a portal opens the destination map. Pressing E on the portal always works.");

            new Harmony(ModGuid).PatchAll();
        }

        internal static string T(string en, string fr)
        {
            return Localization.instance != null && Localization.instance.GetSelectedLanguage() == "French" ? fr : en;
        }
    }

    // ------------------------------------------------------------------
    // Portal list sync: the server owns the full ZDO set, clients ask it.
    // ------------------------------------------------------------------
    internal static class PortalSync
    {
        internal struct Entry
        {
            public Vector3 pos;
            public float rotY;
            public string tag;
        }

        internal static readonly List<Entry> Portals = new List<Entry>();
        internal static Action? OnPortalsUpdated;

        internal static void RegisterRpcs()
        {
            ZRoutedRpc.instance.Register("Bifrost_RequestPortals", new Action<long>(OnRequest));
            ZRoutedRpc.instance.Register<ZPackage>("Bifrost_Portals", new Action<long, ZPackage>(OnReceive));
        }

        internal static void Request()
        {
            if (ZNet.instance == null) return;
            if (ZNet.instance.IsServer())
            {
                Portals.Clear();
                Portals.AddRange(Collect());
                OnPortalsUpdated?.Invoke();
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "Bifrost_RequestPortals");
            }
        }

        private static void OnRequest(long sender)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
            ZPackage pkg = new ZPackage();
            List<Entry> list = Collect();
            pkg.Write(list.Count);
            foreach (Entry e in list)
            {
                pkg.Write(e.pos);
                pkg.Write(e.rotY);
                pkg.Write(e.tag);
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, "Bifrost_Portals", pkg);
        }

        private static void OnReceive(long sender, ZPackage pkg)
        {
            Portals.Clear();
            int count = pkg.ReadInt();
            for (int i = 0; i < count; i++)
            {
                Entry e = new Entry
                {
                    pos = pkg.ReadVector3(),
                    rotY = pkg.ReadSingle(),
                    tag = pkg.ReadString()
                };
                Portals.Add(e);
            }
            OnPortalsUpdated?.Invoke();
        }

        private static List<Entry> Collect()
        {
            List<Entry> list = new List<Entry>();
            if (ZDOMan.instance == null) return list;
            foreach (string part in BifrostPlugin.PortalPrefabs.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string prefab = part.Trim();
                if (prefab.Length == 0) continue;
                List<ZDO> zdos = new List<ZDO>();
                int index = 0;
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, zdos, ref index)) { }
                foreach (ZDO zdo in zdos)
                {
                    list.Add(new Entry
                    {
                        pos = zdo.GetPosition(),
                        rotY = zdo.GetRotation().eulerAngles.y,
                        tag = zdo.GetString("tag")
                    });
                }
            }
            return list;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Start))]
    internal static class Game_Start_Patch
    {
        private static void Postfix()
        {
            PortalSync.RegisterRpcs();
        }
    }

    // ------------------------------------------------------------------
    // Portal interaction: press or step in to pick a destination on the
    // map, hold to rename. Vanilla tag pairing is replaced.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Interact))]
    internal static class TeleportWorld_Interact_Patch
    {
        private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, ref bool __result)
        {
            if (!BifrostPlugin.Enabled.Value) return true;
            if (human != Player.m_localPlayer) return true;

            if (hold)
            {
                if (!PrivateArea.CheckAccess(__instance.transform.position))
                {
                    __result = true;
                    return false;
                }
                TextInput.instance.RequestText(__instance, "$piece_portal_tag", 10);
                __result = true;
                return false;
            }

            PortalGui.Open(__instance);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.GetHoverText))]
    internal static class TeleportWorld_GetHoverText_Patch
    {
        private static void Postfix(TeleportWorld __instance, ref string __result)
        {
            if (!BifrostPlugin.Enabled.Value) return;
            string tag = __instance.GetText();
            if (string.IsNullOrEmpty(tag)) tag = BifrostPlugin.T("(no name)", "(sans nom)");
            __result = Localization.instance.Localize(
                "$piece_portal \"" + tag + "\"\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] " + BifrostPlugin.T("Choose destination", "Choisir la destination") + "\n" +
                "[<color=yellow><b>1s $KEY_Use</b></color>] $piece_portal_settag");
        }
    }

    // Walking into a portal opens the picker instead of the vanilla tag teleport.
    // Suppressed right after arrival so the destination portal does not reopen it.
    [HarmonyPatch(typeof(TeleportWorld), nameof(TeleportWorld.Teleport), typeof(Player))]
    internal static class TeleportWorld_Teleport_Patch
    {
        private static bool Prefix(TeleportWorld __instance, Player player)
        {
            if (!BifrostPlugin.Enabled.Value || !BifrostPlugin.OpenOnEnter.Value) return true;
            if (player == null || player != Player.m_localPlayer) return true;
            if (player.m_teleporting || Time.time < PortalGui.SuppressUntil) return false;
            if (Minimap.instance == null || Minimap.instance.m_mode == Minimap.MapMode.Large) return false;
            PortalGui.Open(__instance);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Destination picker built on the native pin system. Bifrost adds its
    // own temporary pins and removes them on close. Pins from other mods
    // are never touched, so there is nothing to fight.
    // ------------------------------------------------------------------
    internal static class PortalGui
    {
        private static bool _open;
        internal static float SuppressUntil;
        private static Vector3 _sourcePos;
        private static readonly List<KeyValuePair<Minimap.PinData, PortalSync.Entry>> _pins =
            new List<KeyValuePair<Minimap.PinData, PortalSync.Entry>>();

        internal static bool IsOpen => _open;

        internal static void Open(TeleportWorld portal)
        {
            if (Minimap.instance == null || Player.m_localPlayer == null) return;
            _sourcePos = portal.transform.position;
            _open = true;
            PortalSync.OnPortalsUpdated = RebuildPins;
            PortalSync.Request();
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            RebuildPins();
            Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                BifrostPlugin.T("Choose a destination portal", "Choisis un portail de destination"));
        }

        internal static void Close()
        {
            if (!_open) return;
            _open = false;
            PortalSync.OnPortalsUpdated = null;
            foreach (KeyValuePair<Minimap.PinData, PortalSync.Entry> pin in _pins)
            {
                Minimap.instance?.RemovePin(pin.Key);
            }
            _pins.Clear();
        }

        private static void RebuildPins()
        {
            if (!_open || Minimap.instance == null) return;
            foreach (KeyValuePair<Minimap.PinData, PortalSync.Entry> pin in _pins)
            {
                Minimap.instance.RemovePin(pin.Key);
            }
            _pins.Clear();

            foreach (PortalSync.Entry entry in PortalSync.Portals)
            {
                if (Vector3.Distance(entry.pos, _sourcePos) < 3f) continue;
                string name = string.IsNullOrEmpty(entry.tag) ? BifrostPlugin.T("(no name)", "(sans nom)") : entry.tag;
                Minimap.PinData? pin = AddPinCompat(entry.pos, name);
                if (pin != null) _pins.Add(new KeyValuePair<Minimap.PinData, PortalSync.Entry>(pin, entry));
            }
        }

        // AddPin's last parameter changed type across game versions (long, then
        // PlatformUserID from an assembly the game libs package does not ship).
        // Resolve the method at runtime and fill trailing parameters blind.
        private static MethodInfo? _addPin;

        private static Minimap.PinData? AddPinCompat(Vector3 pos, string name)
        {
            if (_addPin == null)
            {
                foreach (MethodInfo mi in typeof(Minimap).GetMethods(AccessTools.all))
                {
                    ParameterInfo[] ps = mi.GetParameters();
                    if (mi.Name == "AddPin" && ps.Length >= 5
                        && ps[0].ParameterType == typeof(Vector3)
                        && ps[2].ParameterType == typeof(string))
                    {
                        _addPin = mi;
                        break;
                    }
                }
                if (_addPin == null)
                {
                    BifrostPlugin.Log.LogWarning("Bifrost: Minimap.AddPin not found.");
                    return null;
                }
            }

            ParameterInfo[] pars = _addPin.GetParameters();
            object?[] args = new object?[pars.Length];
            args[0] = pos;
            args[1] = Minimap.PinType.Icon4;
            args[2] = name;
            args[3] = false;
            args[4] = false;
            for (int i = 5; i < pars.Length; i++)
            {
                Type pt = pars[i].ParameterType;
                args[i] = pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }
            return _addPin.Invoke(Minimap.instance, args) as Minimap.PinData;
        }

        internal static bool HandleMapClick()
        {
            if (!_open || Minimap.instance == null) return false;

            Vector3 click = Minimap.instance.ScreenToWorldPoint(Input.mousePosition);
            float radius = Minimap.instance.m_removeRadius * (Minimap.instance.m_largeZoom * 2f);

            bool found = false;
            PortalSync.Entry best = default;
            float bestDist = float.MaxValue;
            foreach (KeyValuePair<Minimap.PinData, PortalSync.Entry> pin in _pins)
            {
                Vector3 p = pin.Value.pos;
                float dist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(click.x, click.z));
                if (dist < radius && dist < bestDist)
                {
                    bestDist = dist;
                    best = pin.Value;
                    found = true;
                }
            }

            if (found) Teleport(best);
            return true;
        }

        private static void Teleport(PortalSync.Entry entry)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;

            if (!BifrostPlugin.IgnoreRestrictions.Value && !player.IsTeleportable())
            {
                player.Message(MessageHud.MessageType.Center,
                    BifrostPlugin.T("You carry something that cannot pass through", "Tu portes un objet qui ne passe pas le portail"));
                Minimap.instance.SetMapMode(Minimap.MapMode.Small);
                return;
            }

            Quaternion rot = Quaternion.Euler(0f, entry.rotY, 0f);
            Vector3 target = entry.pos + rot * Vector3.forward * 1.2f + Vector3.up * 0.5f;
            SuppressUntil = Time.time + 5f;
            player.TeleportTo(target, rot, true);
            Minimap.instance.SetMapMode(Minimap.MapMode.Small);
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
    internal static class Minimap_SetMapMode_Patch
    {
        private static void Postfix(Minimap.MapMode mode)
        {
            if (mode != Minimap.MapMode.Large) PortalGui.Close();
        }
    }

    [HarmonyPatch(typeof(Minimap), "OnMapLeftClick")]
    internal static class Minimap_OnMapLeftClick_Patch
    {
        private static bool Prefix()
        {
            return !PortalGui.HandleMapClick();
        }
    }

    // A double click would normally place a player pin, swallow it while picking.
    [HarmonyPatch(typeof(Minimap), "OnMapDblClick")]
    internal static class Minimap_OnMapDblClick_Patch
    {
        private static bool Prefix()
        {
            return !PortalGui.IsOpen;
        }
    }

    // ------------------------------------------------------------------
    // Quick travel: skip the artificial wait, never skip the load check.
    // The fade holds until the area is ready, so no unloaded world flash.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(Player), "UpdateTeleport")]
    internal static class Player_UpdateTeleport_Patch
    {
        private static void Prefix(Player __instance, ref float ___m_teleportTimer)
        {
            if (!BifrostPlugin.Enabled.Value || !BifrostPlugin.QuickTravel.Value) return;
            if (!__instance.m_teleporting || ___m_teleportTimer >= 8f) return;
            if (ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(__instance.m_teleportTargetPos))
            {
                ___m_teleportTimer = 8f;
            }
        }
    }
}
