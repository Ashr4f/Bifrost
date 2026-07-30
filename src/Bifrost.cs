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
        public const string ModVersion = "1.0.9";

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
        internal static ConfigEntry<bool> HideOtherPins = null!;
        internal static ConfigEntry<string> KeepPinTypes = null!;
        internal static ConfigEntry<KeyboardShortcut> MapToggleKey = null!;
        internal static ConfigEntry<bool> ShowWorldWhileLoading = null!;
        internal static ConfigEntry<bool> SkipLoadingObjects = null!;
        internal static ConfigEntry<bool> SkipLoadingArea = null!;
        internal static ConfigEntry<float> SettleSeconds = null!;

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

            HideOtherPins = Config.Bind("General", "Hide Other Pins", true,
                "While choosing a destination, resource and custom pins are hidden so nothing covers the portals. Visual only and per frame, other mods keep their pins untouched.");

            KeepPinTypes = Config.Bind("General", "Always Visible Pins", "Ping, Shout, Death, Bed, Player, EventArea, RandomEvent, Boss",
                "Comma-separated pin types that stay visible while choosing a destination, so pings, other players and death markers are never hidden.");

            MapToggleKey = Config.Bind("General", "Map Toggle Key", new KeyboardShortcut(KeyCode.P),
                "Key to show or hide every portal on the large map at any time.");

            ShowWorldWhileLoading = Config.Bind("General", "Show World While Loading", false,
                "If on, the screen is not held black while the destination loads. Feels faster but shows the half loaded world.");

            SkipLoadingObjects = Config.Bind("General", "Skip Loading Objects", false,
                "Arrive once the terrain is ready without waiting for every object. Warning: you can land on a lower floor of a building.");

            SkipLoadingArea = Config.Bind("General", "Skip Loading Area", false,
                "Instant arrival, the world loads around you. Warning: you arrive before the world exists.");

            SettleSeconds = Config.Bind("General", "Extra Load Wait", 0f,
                "Extra seconds to wait after the destination has stopped receiving objects from the server. Normally not needed: arrival already waits for the transfer itself, this is only a margin for a slow connection.\n" +
                "Also used as the plain wait on game versions where the transfer cannot be watched. Destinations already loaded when leaving are always instant.");

            new Harmony(ModGuid).PatchAll();
        }

        internal static string T(string en, string fr)
        {
            return Localization.instance != null && Localization.instance.GetSelectedLanguage() == "French" ? fr : en;
        }

        // Holds the screen black while the destination is still loading, so the
        // half loaded world is never visible even with all waits removed.
        private static FieldInfo? _blackField;
        private static bool _blackSearched;

        private void LateUpdate()
        {
            // Last pass of the frame: anything a mod re-activated on the map
            // since our patches ran gets hidden again before rendering.
            if (Minimap.instance != null) PortalGui.AfterUpdatePins(Minimap.instance);

            if (!Enabled.Value || !QuickTravel.Value || ShowWorldWhileLoading.Value) return;
            Player p = Player.m_localPlayer;
            if (p == null || !p.m_teleporting || Hud.instance == null) return;
            // Same gate as the arrival itself, so the screen stays black for
            // exactly as long as the arrival is held back.
            if (TravelGate.ArrivalAllowed(p)) return;

            if (!_blackSearched)
            {
                _blackSearched = true;
                _blackField = HudVeil.Find();
            }
            if (_blackField == null) return;
            HudVeil.Keep(_blackField.GetValue(Hud.instance));
        }
    }

    // ------------------------------------------------------------------
    // The loading veil has changed name and type across game versions, so it
    // is looked up by hand: no logging from the patch library when a name is
    // absent, and both a canvas group and a plain object are accepted.
    // ------------------------------------------------------------------
    internal static class HudVeil
    {
        internal static FieldInfo? Find()
        {
            foreach (string name in new[] { "m_blackScreen", "m_loadingScreen", "m_loadingScreenPanel", "m_black" })
            {
                FieldInfo? f = typeof(Hud).GetField(name, AccessTools.all);
                if (f != null && IsUsable(f.FieldType)) return f;
            }
            foreach (FieldInfo f in typeof(Hud).GetFields(AccessTools.all))
            {
                string n = f.Name.ToLowerInvariant();
                if ((n.Contains("black") || n.Contains("loading")) && IsUsable(f.FieldType)) return f;
            }
            BifrostPlugin.Log.LogWarning("Bifrost: loading veil not found, the world stays visible while a destination loads.");
            return null;
        }

        private static bool IsUsable(Type type)
        {
            return typeof(CanvasGroup).IsAssignableFrom(type)
                || typeof(GameObject).IsAssignableFrom(type)
                || typeof(Component).IsAssignableFrom(type);
        }

        // The veil is held opaque through a canvas group, added when the object
        // does not carry one, which survives whatever the game sets meanwhile.
        internal static void Keep(object? value)
        {
            GameObject? go = null;
            if (value is CanvasGroup group && group) go = group.gameObject;
            else if (value is GameObject g && g) go = g;
            else if (value is Component c && c) go = c.gameObject;
            if (go == null) return;

            if (!go.activeSelf) go.SetActive(true);
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            if (cg.alpha != 1f) cg.alpha = 1f;
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
            SectorAck.RegisterRpcs();
            PortalGui.ResetAll();
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
                __result = true;
                return false;
            }

            // Shift+E renames like vanilla, E opens the destination map.
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
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
            string title = string.IsNullOrEmpty(tag) ? "$piece_portal" : "$piece_portal \"" + tag + "\"";
            __result = Localization.instance.Localize(
                title + "\n" +
                "[<color=yellow><b>$KEY_Use</b></color>] " + BifrostPlugin.T("Choose destination", "Choisir la destination") + "\n" +
                "[<color=yellow><b>Shift + $KEY_Use</b></color>] $piece_portal_settag");
        }
    }

    // Portals only light up when they have a tag paired target in vanilla.
    // Bifrost makes pairing pointless, so the connected state is forced and
    // every portal keeps its flames and glow.
    [HarmonyPatch]
    internal static class TeleportWorld_Target_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "HaveTarget", "TargetFound" })
            {
                MethodInfo? method = AccessTools.Method(typeof(TeleportWorld), name);
                if (method != null && method.ReturnType == typeof(bool)) yield return method;
            }
        }

        private static void Postfix(ref bool __result)
        {
            if (BifrostPlugin.Enabled.Value) __result = true;
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
        private static bool _browse;
        internal static float SuppressUntil;
        private static Vector3 _sourcePos;
        private static readonly List<KeyValuePair<Minimap.PinData, PortalSync.Entry>> _pins =
            new List<KeyValuePair<Minimap.PinData, PortalSync.Entry>>();
        private static readonly List<Minimap.PinData> _browsePins = new List<Minimap.PinData>();

        internal static bool IsOpen => _open;

        internal static void ResetAll()
        {
            _open = false;
            _browse = false;
            _pins.Clear();
            _browsePins.Clear();
            _hidden.Clear();
        }

        internal static void Open(TeleportWorld portal)
        {
            if (Minimap.instance == null || Player.m_localPlayer == null) return;
            _sourcePos = portal.transform.position;
            _open = true;
            PortalSync.OnPortalsUpdated = OnData;
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
            foreach (KeyValuePair<Minimap.PinData, PortalSync.Entry> pin in _pins)
            {
                Minimap.instance?.RemovePin(pin.Key);
            }
            _pins.Clear();
            RestoreHidden();
        }

        internal static void ToggleBrowse()
        {
            _browse = !_browse;
            if (_browse)
            {
                PortalSync.OnPortalsUpdated = OnData;
                PortalSync.Request();
                RebuildBrowse();
            }
            else
            {
                ClearBrowse();
            }
            Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
                _browse ? BifrostPlugin.T("Portals shown", "Portails affichés")
                        : BifrostPlugin.T("Portals hidden", "Portails masqués"));
        }

        private static void OnData()
        {
            if (_open) RebuildPins();
            if (_browse) RebuildBrowse();
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
                Minimap.PinData? pin = AddPinDirect(entry.pos, entry.tag ?? "");
                if (pin != null) _pins.Add(new KeyValuePair<Minimap.PinData, PortalSync.Entry>(pin, entry));
            }
        }

        private static void ClearBrowse()
        {
            foreach (Minimap.PinData pin in _browsePins)
            {
                Minimap.instance?.RemovePin(pin);
            }
            _browsePins.Clear();
        }

        private static void RebuildBrowse()
        {
            if (!_browse || Minimap.instance == null) return;
            ClearBrowse();
            foreach (PortalSync.Entry entry in PortalSync.Portals)
            {
                Minimap.PinData? pin = AddPinDirect(entry.pos, entry.tag ?? "");
                if (pin != null) _browsePins.Add(pin);
            }
        }

        // Visual only, per frame, after the vanilla pin update. Other mods can
        // recreate or restamp their pins freely, they just stay off screen
        // while the picker is open. Every GameObject a pin references is
        // swept generically (icon, name label, checked mark), field names
        // are never assumed so game updates cannot break the hiding.
        private static readonly List<FieldInfo> _pinGoFields = new List<FieldInfo>();
        private static readonly List<KeyValuePair<FieldInfo, FieldInfo>> _pinNestedGoFields =
            new List<KeyValuePair<FieldInfo, FieldInfo>>();
        private static bool _fieldsSearched;

        internal static void AfterUpdatePins(Minimap map)
        {
            if (!_open || !BifrostPlugin.HideOtherPins.Value) return;
            // Browse pins are hidden too while picking, otherwise they stack
            // on top of the picker pins. They come back when the picker closes.
            HashSet<Minimap.PinData> ours = new HashSet<Minimap.PinData>();
            foreach (KeyValuePair<Minimap.PinData, PortalSync.Entry> kv in _pins) ours.Add(kv.Key);

            foreach (Minimap.PinData pin in map.m_pins)
            {
                if (ours.Contains(pin)) continue;
                if (IsAlwaysVisible(pin.m_type)) continue;
                try
                {
                    HideAllVisuals(pin);
                }
                catch
                {
                    // One broken pin must never abort the hiding pass.
                }
            }
        }

        // Pings, other players and death markers must keep working while the
        // picker is open, only resource and custom pins are in the way.
        private static string _keepRaw = "";
        private static readonly HashSet<string> _keepTypes = new HashSet<string>();

        private static bool IsAlwaysVisible(Minimap.PinType type)
        {
            string raw = BifrostPlugin.KeepPinTypes.Value;
            if (raw != _keepRaw)
            {
                _keepRaw = raw;
                _keepTypes.Clear();
                foreach (string part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string p = part.Trim();
                    if (p.Length > 0) _keepTypes.Add(p.ToLowerInvariant());
                }
            }
            return _keepTypes.Contains(type.ToString().ToLowerInvariant());
        }

        private static void CachePinFields()
        {
            _fieldsSearched = true;
            foreach (FieldInfo f in typeof(Minimap.PinData).GetFields(AccessTools.all))
            {
                if (typeof(GameObject).IsAssignableFrom(f.FieldType) || typeof(Component).IsAssignableFrom(f.FieldType))
                {
                    _pinGoFields.Add(f);
                }
                else if (f.FieldType.IsClass && f.FieldType != typeof(string)
                    && !typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
                {
                    foreach (FieldInfo nested in f.FieldType.GetFields(AccessTools.all))
                    {
                        if (typeof(GameObject).IsAssignableFrom(nested.FieldType) || typeof(Component).IsAssignableFrom(nested.FieldType))
                            _pinNestedGoFields.Add(new KeyValuePair<FieldInfo, FieldInfo>(f, nested));
                    }
                }
            }
        }

        private static void HideAllVisuals(Minimap.PinData pin)
        {
            if (!_fieldsSearched) CachePinFields();
            foreach (FieldInfo f in _pinGoFields) Deactivate(f.GetValue(pin));
            foreach (KeyValuePair<FieldInfo, FieldInfo> kv in _pinNestedGoFields)
            {
                object? mid = kv.Key.GetValue(pin);
                if (mid != null) Deactivate(kv.Value.GetValue(mid));
            }
        }

        // Hidden through a CanvasGroup at alpha 0 instead of SetActive: zooming
        // and other mods keep reactivating pin elements, but the component we
        // planted on them survives and keeps them invisible. Restored on close.
        private static readonly HashSet<CanvasGroup> _hidden = new HashSet<CanvasGroup>();

        private static void Deactivate(object? value)
        {
            // Unity fake null: destroyed objects still exist as managed wrappers,
            // touching them throws. The implicit bool operator filters them out.
            GameObject? go = null;
            if (value is GameObject g && g) go = g;
            else if (value is Component c && c) go = c.gameObject;
            if (go == null) return;

            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            if (cg.alpha != 0f)
            {
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
            }
            _hidden.Add(cg);
        }

        private static void RestoreHidden()
        {
            foreach (CanvasGroup cg in _hidden)
            {
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
            }
            _hidden.Clear();
        }

        // Pins are inserted straight into the pin list instead of through
        // Minimap.AddPin: other mods patch AddPin (exploration reveal around
        // shared pins, pin syncing) and Bifrost's temporary markers must not
        // trigger any of that.
        private static Minimap.PinData? AddPinDirect(Vector3 pos, string name)
        {
            if (Minimap.instance == null) return null;
            try
            {
                Minimap.PinData pin = new Minimap.PinData();
                pin.m_type = Minimap.PinType.Icon4;
                pin.m_name = name;
                pin.m_pos = pos;
                pin.m_save = false;
                pin.m_checked = false;
                foreach (Minimap.SpriteData sd in Minimap.instance.m_icons)
                {
                    if (sd.m_name == Minimap.PinType.Icon4)
                    {
                        pin.m_icon = sd.m_icon;
                        break;
                    }
                }
                try
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        FieldInfo? nameField = AccessTools.Field(typeof(Minimap.PinData), "m_NamePinData");
                        if (nameField != null)
                            nameField.SetValue(pin, Activator.CreateInstance(nameField.FieldType, pin));
                    }
                }
                catch
                {
                    // No label support in this game version, the pin still works.
                }
                Minimap.instance.m_pins.Add(pin);
                return pin;
            }
            catch (Exception e)
            {
                BifrostPlugin.Log.LogWarning($"Bifrost: direct pin failed ({e.Message}), falling back to AddPin.");
                return AddPinCompat(pos, name);
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

    [HarmonyPatch(typeof(Minimap), "Update")]
    internal static class Minimap_Update_Patch
    {
        private static void Postfix(Minimap __instance)
        {
            if (!BifrostPlugin.Enabled.Value) return;
            PortalGui.AfterUpdatePins(__instance);
            if (__instance.m_mode != Minimap.MapMode.Large) return;
            if (Minimap.InTextInput()) return;
            if (PortalGui.IsOpen) return;
            if (BifrostPlugin.MapToggleKey.Value.IsDown()) PortalGui.ToggleBrowse();
        }
    }

    [HarmonyPatch(typeof(Minimap), "UpdatePins")]
    internal static class Minimap_UpdatePins_Patch
    {
        private static void Postfix(Minimap __instance)
        {
            PortalGui.AfterUpdatePins(__instance);
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

    // The picker can open while walking into a portal on a cliff edge, so the
    // character must stop dead: no movement, no jump, no auto run behind the map.
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class Player_SetControls_Patch
    {
        private static void Prefix(Player __instance, ref Vector3 movedir, ref bool jump, ref bool autoRun)
        {
            if (!PortalGui.IsOpen || __instance != Player.m_localPlayer) return;
            movedir = Vector3.zero;
            jump = false;
            autoRun = false;
        }
    }

    // ------------------------------------------------------------------
    // Single gate deciding when arriving is allowed. Terrain is generated
    // locally so it reports ready almost immediately, while server objects
    // are still on their way: a settle delay after readiness is what stops
    // arrivals in an empty world. The skip options relax this on purpose.
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    // Destination handshake. The server is the only one that knows how many
    // objects a sector really holds, so it is asked directly at departure
    // instead of guessing from the client side. Arrival then waits for the
    // transfer to be complete rather than for a timer.
    // ------------------------------------------------------------------
    internal static class SectorAck
    {
        // Objects are sent by priority and by distance, so the last few can
        // lag well behind the rest. Close enough is what completes.
        private const float Ratio = 0.95f;

        private static Vector2i _pending;
        private static Vector2i _answered;
        private static bool _waiting;
        private static int _expected = -1;

        internal static void RegisterRpcs()
        {
            ZRoutedRpc.instance.Register<ZPackage>("Bifrost_AskSector", new Action<long, ZPackage>(OnAsk));
            ZRoutedRpc.instance.Register<ZPackage>("Bifrost_TellSector", new Action<long, ZPackage>(OnTell));
        }

        internal static void Reset()
        {
            _waiting = false;
            _expected = -1;
        }

        // Asked as soon as the destination is known. The answer travels while
        // the terrain is still generating, so it costs no travel time.
        internal static void Ask(Vector3 pos)
        {
            Reset();
            if (ZNet.instance == null || ZoneSystem.instance == null || ZRoutedRpc.instance == null) return;

            Vector2i zone = ZoneSystem.GetZone(pos);
            _pending = zone;

            // Solo or host: the count is available right here, no round trip.
            if (ZNet.instance.IsServer())
            {
                _answered = zone;
                _expected = SectorStream.Count(pos);
                return;
            }

            _waiting = true;
            ZPackage pkg = new ZPackage();
            pkg.Write(zone.x);
            pkg.Write(zone.y);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), "Bifrost_AskSector", pkg);
        }

        // Number of objects the destination sector holds, or -1 while the
        // server has not answered, which is also the case when it does not
        // run Bifrost.
        internal static int Expected(Vector3 pos)
        {
            if (_expected < 0 || ZoneSystem.instance == null) return -1;
            Vector2i zone = ZoneSystem.GetZone(pos);
            return zone.x == _answered.x && zone.y == _answered.y ? _expected : -1;
        }

        internal static bool Complete(Vector3 pos, int known)
        {
            int expected = Expected(pos);
            if (expected < 0) return false;
            if (expected == 0) return true;
            return known >= Mathf.CeilToInt(expected * Ratio);
        }

        private static void OnAsk(long sender, ZPackage pkg)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null) return;
            int x = pkg.ReadInt();
            int y = pkg.ReadInt();
            Vector3 pos = ZoneSystem.GetZonePos(new Vector2i(x, y));

            ZPackage answer = new ZPackage();
            answer.Write(x);
            answer.Write(y);
            answer.Write(SectorStream.Count(pos));
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, "Bifrost_TellSector", answer);
        }

        private static void OnTell(long sender, ZPackage pkg)
        {
            int x = pkg.ReadInt();
            int y = pkg.ReadInt();
            int count = pkg.ReadInt();
            if (!_waiting || x != _pending.x || y != _pending.y) return;
            _waiting = false;
            _answered = new Vector2i(x, y);
            _expected = count;
        }
    }

    // ------------------------------------------------------------------
    // Counts the objects the client currently knows for a sector. Growing
    // count means the server is still sending, a stable count means the
    // destination is delivered. Method names differ between game versions, so
    // they are probed once and the whole thing degrades to -1 when absent.
    // ------------------------------------------------------------------
    internal static class SectorStream
    {
        private static bool _searched;
        private static MethodInfo? _findObjects;
        private static MethodInfo? _findSector;
        private static readonly List<ZDO> _buffer = new List<ZDO>();

        internal static int Count(Vector3 pos)
        {
            if (!_searched)
            {
                _searched = true;
                foreach (MethodInfo m in typeof(ZDOMan).GetMethods(AccessTools.all))
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (m.Name == "FindObjects" && ps.Length == 2) _findObjects = m;
                    else if (m.Name == "FindSectorObjects" && ps.Length >= 4) _findSector = m;
                }
                if (_findObjects == null && _findSector == null)
                    BifrostPlugin.Log.LogWarning("Bifrost: sector contents cannot be watched on this version, arrival falls back to the timed wait.");
            }

            if (ZDOMan.instance == null || ZoneSystem.instance == null) return -1;
            if (_findObjects == null && _findSector == null) return -1;

            _buffer.Clear();
            try
            {
                Vector2i zone = ZoneSystem.GetZone(pos);
                if (_findObjects != null) _findObjects.Invoke(ZDOMan.instance, new object[] { zone, _buffer });
                else _findSector!.Invoke(ZDOMan.instance, new object[] { zone, 1, 0, _buffer, null });
            }
            catch (Exception e)
            {
                BifrostPlugin.Log.LogWarning($"Bifrost: sector contents unreadable ({e.Message}), arrival falls back to the timed wait.");
                _findObjects = null;
                _findSector = null;
                return -1;
            }
            return _buffer.Count;
        }
    }

    internal static class TravelGate
    {
        private const float MaxHold = 12f;
        // The destination is considered delivered once its object count stops
        // moving. A sector that never receives anything is simply empty.
        private const float StableSeconds = 0.25f;
        private const float EmptyGrace = 1.2f;

        private static float _readySince = -1f;
        private static float _holdSince = -1f;
        private static Vector3 _target;
        private static bool _wasWarm;
        private static int _lastCount = -1;
        private static float _stableSince = -1f;

        internal static void Reset()
        {
            _readySince = -1f;
            _holdSince = -1f;
            _target = Vector3.zero;
            _lastCount = -1;
            _stableSince = -1f;
            SectorAck.Reset();
        }

        // True when the destination was already in memory on departure, so the
        // travel needs no loading time at all.
        internal static bool IsWarm => _wasWarm;

        internal static bool ArrivalAllowed(Player player)
        {
            if (!BifrostPlugin.Enabled.Value) return true;
            if (BifrostPlugin.SkipLoadingArea.Value) return true;

            Vector3 pos = player.m_teleportTargetPos;
            if (pos != _target)
            {
                _target = pos;
                _readySince = -1f;
                _lastCount = -1;
                _stableSince = -1f;
                _holdSince = Time.time;
                SectorAck.Ask(pos);
                // Destination already in memory when leaving: nothing to load,
                // nothing to wait for. Only cold destinations get the settle delay.
                _wasWarm = ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(pos)
                    && ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(pos);
            }

            if (_wasWarm) return true;

            // Hard safety net: the view is never held longer than this, whatever
            // the loading state reports.
            if (_holdSince > 0f && Time.time - _holdSince >= MaxHold) return true;

            bool zoneLoaded = ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(pos);
            if (!zoneLoaded) return false;

            if (BifrostPlugin.SkipLoadingObjects.Value) return true;

            if (ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(pos))
            {
                _readySince = -1f;
                return false;
            }

            // IsAreaReady only means "everything I know about is built". On a
            // sector the client has never received, it knows nothing, so it
            // answers yes on an empty world. What actually matters is whether
            // the server is still sending objects for that sector.
            int count = SectorStream.Count(pos);
            if (count < 0)
            {
                // No way to watch the stream on this game version: fall back to
                // the plain wait, which is what the setting is there for.
                if (_readySince < 0f) _readySince = Time.time;
                return Time.time - _readySince >= Mathf.Max(0f, BifrostPlugin.SettleSeconds.Value);
            }

            // The server answered how many objects the sector holds, so the
            // transfer has a real end condition and nothing has to be guessed.
            int expected = SectorAck.Expected(pos);
            if (expected >= 0)
            {
                if (!SectorAck.Complete(pos, count)) return false;
                if (_readySince < 0f) _readySince = Time.time;
                return Time.time - _readySince >= Mathf.Max(0f, BifrostPlugin.SettleSeconds.Value);
            }

            // No answer: the server does not run Bifrost, or it has not replied
            // yet. Falls back to watching the count until it stops moving.
            if (count != _lastCount)
            {
                _lastCount = count;
                _stableSince = Time.time;
                return false;
            }

            // Nothing received at all: either the transfer has not started or
            // the sector is genuinely empty, so a short grace decides.
            if (count == 0) return Time.time - _holdSince >= EmptyGrace;

            if (Time.time - _stableSince < StableSeconds) return false;

            if (_readySince < 0f) _readySince = Time.time;
            return Time.time - _readySince >= Mathf.Max(0f, BifrostPlugin.SettleSeconds.Value);
        }
    }

    // The teleport itself is never held back: the player must actually move
    // for the destination to start loading, otherwise the wait deadlocks.
    // Only the view is held, by the black screen in LateUpdate.
    [HarmonyPatch(typeof(Player), "UpdateTeleport")]
    internal static class Player_Teleport_Reset_Patch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Player __instance)
        {
            if (!__instance.m_teleporting) TravelGate.Reset();
        }
    }

    // ------------------------------------------------------------------
    // Quick travel: skip the artificial wait, never skip the load check.
    // The fade holds until the area is ready, so no unloaded world flash.
    // The two skip options relax the readiness definition itself, which
    // drives the vanilla arrival gate, our fast forward and the black
    // screen all at once.
    // ------------------------------------------------------------------
    [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.IsAreaReady))]
    internal static class ZNetScene_IsAreaReady_Patch
    {
        private static void Postfix(Vector3 point, ref bool __result)
        {
            if (__result) return;
            if (!BifrostPlugin.Enabled.Value || !BifrostPlugin.QuickTravel.Value) return;
            Player p = Player.m_localPlayer;
            if (p == null || !p.m_teleporting) return;
            if (Vector3.Distance(point, p.m_teleportTargetPos) > 4f) return;

            if (BifrostPlugin.SkipLoadingArea.Value)
            {
                __result = true;
            }
            else if (BifrostPlugin.SkipLoadingObjects.Value && ZoneSystem.instance != null
                && ZoneSystem.instance.IsZoneLoaded(point))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(Player), "UpdateTeleport")]
    internal static class Player_UpdateTeleport_Patch
    {
        private const float ColdFloor = 0.35f;

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(Player __instance, ref float ___m_teleportTimer)
        {
            if (!BifrostPlugin.Enabled.Value || !BifrostPlugin.QuickTravel.Value) return;
            if (!__instance.m_teleporting) return;

            bool allowed = TravelGate.ArrivalAllowed(__instance);

            // A cold destination must be requested before any readiness check
            // means anything, so a short floor is kept. It is dropped as soon as
            // the zone is loaded, and a destination already in memory never waits.
            if (!TravelGate.IsWarm && ___m_teleportTimer < ColdFloor
                && !(ZoneSystem.instance != null && ZoneSystem.instance.IsZoneLoaded(__instance.m_teleportTargetPos))) return;

            if (___m_teleportTimer < 20f && allowed) ___m_teleportTimer = 20f;
        }
    }
}
