using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    // Portal interaction: press opens the destination map, hold renames.
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

    // ------------------------------------------------------------------
    // Destination picker: our own marker layer on top of the large map.
    // Other mods' pins are never touched, so there is nothing to fight.
    // ------------------------------------------------------------------
    internal static class PortalGui
    {
        private static bool _open;
        private static Vector3 _sourcePos;
        private static GameObject? _root;
        private static readonly List<KeyValuePair<RectTransform, PortalSync.Entry>> _markers =
            new List<KeyValuePair<RectTransform, PortalSync.Entry>>();

        internal static void Open(TeleportWorld portal)
        {
            if (Minimap.instance == null || Player.m_localPlayer == null) return;
            _sourcePos = portal.transform.position;
            _open = true;
            PortalSync.OnPortalsUpdated = RebuildMarkers;
            PortalSync.Request();
            Minimap.instance.SetMapMode(Minimap.MapMode.Large);
            RebuildMarkers();
            Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                BifrostPlugin.T("Choose a destination portal", "Choisis un portail de destination"));
        }

        internal static void Close()
        {
            _open = false;
            PortalSync.OnPortalsUpdated = null;
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            _markers.Clear();
        }

        internal static void OnMapUpdate()
        {
            if (!_open) return;
            if (Minimap.instance == null || Minimap.instance.m_mode != Minimap.MapMode.Large)
            {
                Close();
                return;
            }
            foreach (KeyValuePair<RectTransform, PortalSync.Entry> marker in _markers)
            {
                if (marker.Key == null) continue;
                float mx, my;
                Minimap.instance.WorldToMapPoint(marker.Value.pos, out mx, out my);
                marker.Key.anchoredPosition = Minimap.instance.MapPointToLocalGuiPos(mx, my, Minimap.instance.m_mapImageLarge);
            }
        }

        private static void RebuildMarkers()
        {
            if (!_open || Minimap.instance == null) return;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _markers.Clear();

            _root = new GameObject("BifrostMarkers", typeof(RectTransform));
            RectTransform rootRt = _root.GetComponent<RectTransform>();
            rootRt.SetParent(Minimap.instance.m_mapImageLarge.transform, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = Vector2.zero;
            rootRt.offsetMax = Vector2.zero;

            Sprite? icon = null;
            foreach (Minimap.SpriteData sd in Minimap.instance.m_icons)
            {
                if (sd.m_name == Minimap.PinType.Icon4)
                {
                    icon = sd.m_icon;
                    break;
                }
            }

            foreach (PortalSync.Entry entry in PortalSync.Portals)
            {
                if (Vector3.Distance(entry.pos, _sourcePos) < 3f) continue;

                GameObject go = new GameObject("BifrostPortal", typeof(RectTransform), typeof(Image), typeof(Button));
                RectTransform rt = go.GetComponent<RectTransform>();
                rt.SetParent(rootRt, false);
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(30f, 30f);

                Image img = go.GetComponent<Image>();
                img.sprite = icon;
                img.color = Color.white;

                PortalSync.Entry captured = entry;
                go.GetComponent<Button>().onClick.AddListener(() => Teleport(captured));

                GameObject labelGo = new GameObject("label", typeof(RectTransform), typeof(TextMeshProUGUI));
                RectTransform lrt = labelGo.GetComponent<RectTransform>();
                lrt.SetParent(rt, false);
                lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
                lrt.anchoredPosition = new Vector2(0f, -12f);
                lrt.sizeDelta = new Vector2(160f, 20f);
                TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
                label.font = Minimap.instance.m_biomeNameLarge.font;
                label.fontSize = 15f;
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                label.text = string.IsNullOrEmpty(entry.tag) ? BifrostPlugin.T("(no name)", "(sans nom)") : entry.tag;

                _markers.Add(new KeyValuePair<RectTransform, PortalSync.Entry>(rt, entry));
            }
            OnMapUpdate();
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
            player.TeleportTo(target, rot, true);
            Minimap.instance.SetMapMode(Minimap.MapMode.Small);
        }
    }

    [HarmonyPatch(typeof(Minimap), nameof(Minimap.Update))]
    internal static class Minimap_Update_Patch
    {
        private static void Postfix()
        {
            PortalGui.OnMapUpdate();
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
