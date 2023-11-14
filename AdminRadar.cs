using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;

/*
    Fixed green X drawings in GroupLimitHightlighting
    Fixed TC / TCArrows argument
    Please read 4.8.0 notes
*/

namespace Oxide.Plugins
{
    [Info("Admin Radar", "nivex", "4.8.2")]
    [Description("Radar tool for Admins and Developers.")]
    class AdminRadar : RustPlugin
    {
        [PluginReference] private Plugin Vanish;

        private const string permAllowed = "adminradar.allowed";
        private const string permBypass = "adminradar.bypass";
        private const string permAuto = "adminradar.auto";
        private const string permBypassOverride = "adminradar.bypass.override";
        private const string genericPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string vendingPrefab = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
        private const float flickerDelay = 0.05f;
        private static AdminRadar ins;
        private StoredData storedData = new StoredData();
        private bool init; // don't use cache while false
        private static bool isUnloading;

        private static readonly List<string> tags = new List<string>
            {"ore", "cluster", "1", "2", "3", "4", "5", "6", "_", ".", "-", "deployed", "wooden", "large", "pile", "prefab", "collectable", "loot", "small"}; // strip these from names to reduce the size of the text and make it more readable

        private readonly Dictionary<ulong, Color> playersColor = new Dictionary<ulong, Color>();
        private readonly List<BasePlayer> accessList = new List<BasePlayer>();
        private readonly Dictionary<ulong, Timer> voices = new Dictionary<ulong, Timer>();
        private static readonly List<Radar> activeRadars = new List<Radar>();
        private readonly List<ulong> warnings = new List<ulong>();

        private static readonly Dictionary<ulong, float> cooldowns = new Dictionary<ulong, float>();
        private static Cache cache = new Cache();
        private Coroutine coroutine;

        public enum CupboardAction
        {
            Authorize,
            Clear,
            Deauthorize
        }

        private class Cache
        {
            public readonly HashSet<BaseNpc> Animals = new HashSet<BaseNpc>();
            public readonly HashSet<BaseEntity> CargoPlanes = new HashSet<BaseEntity>();
            public readonly HashSet<DroppedItemContainer> Backpacks = new HashSet<DroppedItemContainer>();
            public readonly Dictionary<Vector3, CachedInfo> Bags = new Dictionary<Vector3, CachedInfo>();
            public readonly HashSet<BaseEntity> Boats = new HashSet<BaseEntity>();
            public readonly HashSet<BradleyAPC> BradleyAPCs = new HashSet<BradleyAPC>();
            public readonly HashSet<BaseEntity> CargoShips = new HashSet<BaseEntity>();
            public readonly HashSet<BaseEntity> Cars = new HashSet<BaseEntity>();
            public readonly HashSet<BaseEntity> CH47 = new HashSet<BaseEntity>();
            public readonly HashSet<BuildingPrivlidge> Cupboards = new HashSet<BuildingPrivlidge>();
            public readonly Dictionary<Vector3, CachedInfo> Collectibles = new Dictionary<Vector3, CachedInfo>();
            public readonly HashSet<StorageContainer> Containers = new HashSet<StorageContainer>();
            public readonly Dictionary<PlayerCorpse, CachedInfo> Corpses = new Dictionary<PlayerCorpse, CachedInfo>();
            public readonly HashSet<BaseHelicopter> Helicopters = new HashSet<BaseHelicopter>();
            public readonly HashSet<BaseEntity> MiniCopter = new HashSet<BaseEntity>();
            public readonly HashSet<BasePlayer> NPCPlayers = new HashSet<BasePlayer>();
            public readonly Dictionary<Vector3, CachedInfo> Ores = new Dictionary<Vector3, CachedInfo>();
            public readonly HashSet<BaseEntity> RHIB = new HashSet<BaseEntity>();
            public readonly HashSet<BaseEntity> RidableHorse = new HashSet<BaseEntity>();
            public readonly HashSet<SupplyDrop> SupplyDrops = new HashSet<SupplyDrop>();
            public readonly Dictionary<Vector3, CachedInfo> Turrets = new Dictionary<Vector3, CachedInfo>();
            public readonly HashSet<Zombie> Zombies = new HashSet<Zombie>();
        }

        private class StoredData
        {
            public readonly List<string> Extended = new List<string>();
            public readonly Dictionary<string, List<string>> Filters = new Dictionary<string, List<string>>();
            public readonly List<string> Hidden = new List<string>();
            public readonly List<string> OnlineBoxes = new List<string>();
            public readonly List<string> Visions = new List<string>();
            public StoredData() { }
        }

        private class CachedInfo
        {
            public object Info;
            public string Name;
            public double Size;
        }

        public enum EntityType
        {
            Active,
            Airdrops,
            Animals,
            Bags,
            Backpacks,
            Boats,
            Bradley,
            Cars,
            CargoPlanes,
            CargoShips,
            CH47Helicopters,
            Containers,
            Collectibles,
            Cupboards,
            CupboardsArrow,
            Dead,
            GroupLimitHightlighting,
            Heli,
            MiniCopter,
            Npc,
            Ore,
            RidableHorses,
            RigidHullInflatableBoats,
            Sleepers,
            Source,
            Turrets,
            Zombies
        }

        private class Radar : FacepunchBehaviour
        {
            private readonly List<BasePlayer> distantPlayers = new List<BasePlayer>();
            private Dictionary<BuildingPrivlidge, PrivInfo> tcs = new Dictionary<BuildingPrivlidge, PrivInfo>();
            private int drawnObjects;
            private EntityType entityType;
            private int _inactiveSeconds;
            private int activeSeconds;
            public float invokeTime;
            public float maxDistance;
            public BasePlayer player;
            private Vector3 position;

            private bool setSource = true;
            public bool showBags;
            public bool showBoats;
            public bool showBox;
            public bool showBradley;
            public bool showCars;
            public bool showCargoPlanes;
            public bool showCargoShips;
            public bool showCH47;
            public bool showCollectible;
            public bool showDead;
            public bool showHeli;
            public bool showHT;
            public bool showLoot;
            public bool showMiniCopter;
            public bool showNPC;
            public bool showOre;
            public bool showRidableHorses;
            public bool showRHIB;
            public bool showSleepers;
            public bool showStash;
            public bool showTC;
            public bool showTCArrow;
            public bool showTurrets;
            public bool showAll;
            private BaseEntity source;
            private DateTime tick;
            private Dictionary<ulong, Ping> pings = new Dictionary<ulong, Ping>();
            private StringBuilder _cachedStringBuilder = new StringBuilder();

            private class Ping
            {
                public int AveragePing;
                public float Time;
            }

            private class PrivInfo
            {
                public float ProtectedMinutes;
                public float LastTime = float.MaxValue;
                public PrivInfo() { }
            }

            private void Awake()
            {
                activeRadars.Add(this);

                if (activeRadars.Count == 1 && (blockDamageAnimals || blockDamageBuildings || blockDamageNpcs || blockDamagePlayers || blockDamageOther))
                {
                    ins.Subscribe(nameof(OnEntityTakeDamage));
                }

                player = GetComponent<BasePlayer>();
                source = player;
                position = player.transform.position;
                
                if (inactiveSeconds > 0f || inactiveMinutes > 0)
                    InvokeRepeating(Activity, 0f, 1f);
            }

            public void Start()
            {
                CancelInvoke(DoRadar);
                Invoke(DoRadar, invokeTime);
                InvokeRepeating(DoRadar, 0f, invokeTime);
            }

            private void OnDestroy()
            {
                if (radarUI.Contains(player.UserIDString))
                    ins.DestroyUI(player);

                if (inactiveSeconds > 0 || inactiveMinutes > 0)
                    CancelInvoke(Activity);

                pings.Clear();
                CancelInvoke(DoRadar);
                activeRadars.Remove(this);

                if (activeRadars.Count == 0 && !isUnloading) ins.Unsubscribe(nameof(OnEntityTakeDamage));

                if (player != null && player.IsConnected)
                {
                    if (coolDown > 0f)
                    {
                        if (!cooldowns.ContainsKey(player.userID))
                            cooldowns.Add(player.userID, Time.realtimeSinceStartup + coolDown);
                        else cooldowns[player.userID] = Time.realtimeSinceStartup + coolDown;
                    }

                    player.ChatMessage(ins.msg("Deactivated", player.UserIDString));
                }

                Destroy(this);
            }

            public bool GetBool(string value)
            {
                switch (value)
                {
                    case "All":
                        return showAll;
                    case "Bags":
                        return showBags;
                    case "Boats":
                        return showBoats;
                    case "Box":
                        return showBox;
                    case "Bradley":
                        return showBradley;
                    case "CargoPlanes":
                        return showCargoPlanes;
                    case "CargoShips":
                        return showCargoShips;
                    case "Cars":
                        return showCars;
                    case "CH47":
                        return showCH47;
                    case "Collectibles":
                        return showCollectible;
                    case "Dead":
                        return showDead;
                    case "Heli":
                        return showHeli;
                    case "Loot":
                        return showLoot;
                    case "MiniCopter":
                        return showMiniCopter;
                    case "NPC":
                        return showNPC;
                    case "Ore":
                        return showOre;
                    case "RidableHorses":
                        return showRidableHorses;
                    case "RHIB":
                        return showRHIB;
                    case "Sleepers":
                        return showSleepers;
                    case "Stash":
                        return showStash;
                    case "TCArrows":
                        return showTCArrow;
                    case "TC":
                        return showTC;
                    case "Turrets":
                        return showTurrets;
                    default:
                        return false;
                }
            }

            private bool LatencyAccepted()
            {
                if (latencyMs > 0)
                {
                    double ms = (DateTime.Now - tick).TotalMilliseconds;

                    if (ms > latencyMs)
                    {
                        player.ChatMessage(ins.msg("DoESP", player.UserIDString, ms, latencyMs));
                        return false;
                    }
                }

                return true;
            }

            private void Activity()
            {
                if (source != player)
                {
                    _inactiveSeconds = 0;
                    return;
                }

                _inactiveSeconds = position == player.transform.position ? _inactiveSeconds + 1 : 0;
                position = player.transform.position;

                if (inactiveMinutes > 0 && ++activeSeconds / 60 > inactiveMinutes)
                    Destroy(this);
                else if (inactiveSeconds > 0 && _inactiveSeconds > inactiveSeconds)
                    Destroy(this);
            }

            private void DoRadar()
            {
                tick = DateTime.Now;
                bool isAdmin = player.IsAdmin;

                try
                {
                    if (!player.IsConnected)
                    {
                        Destroy(this);
                        return;
                    }

                    drawnObjects = 0;

                    if (!isAdmin && ins.permission.UserHasPermission(player.UserIDString, permAllowed))
                    {
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, true);
                        player.SendNetworkUpdateImmediate();
                    }

                    if (!SetSource())
                        return;

                    if (!ShowActive())
                        return;

                    if (!ShowSleepers())
                        return;

                    if (barebonesMode)
                        return;

                    if (!ShowEntity(EntityType.Cars, showCars, "C", cache.Cars))
                        return;

                    if (!ShowEntity(EntityType.CargoPlanes, showCargoPlanes, "CP", cache.CargoPlanes))
                        return;

                    if (!ShowEntity(EntityType.CargoShips, showCargoShips, "CS", cache.CargoShips))
                        return;

                    if (!ShowHeli())
                        return;

                    if (!ShowBradley())
                        return;

                    if (!ShowLimits())
                        return;

                    if (!ShowTC())
                        return;

                    if (!ShowContainers())
                        return;

                    if (!ShowBags())
                        return;

                    if (!ShowTurrets())
                        return;

                    if (!ShowDead())
                        return;

                    if (!ShowNPC())
                        return;

                    if (!ShowOre())
                        return;
                    
                    if (!ShowEntity(EntityType.CH47Helicopters, showCH47, "CH47", cache.CH47))
                        return;

                    if (!ShowEntity(EntityType.RigidHullInflatableBoats, showRHIB, "RHIB", cache.RHIB))
                        return;

                    if (!ShowEntity(EntityType.Boats, showBoats, "RB", cache.Boats))
                        return;

                    if (!ShowEntity(EntityType.MiniCopter, showMiniCopter, "MC", cache.MiniCopter))
                        return;

                    if (!ShowEntity(EntityType.RidableHorses, showRidableHorses, "RH", cache.RidableHorse))
                        return;

                    ShowCollectables();
                }
                catch (Exception ex)
                {
                    ins.Puts("Error @{0}: {1} --- {2}", Enum.GetName(typeof(EntityType), entityType), ex.Message, ex.StackTrace);
                    player.ChatMessage(ins.msg("Exception", player.UserIDString));

                    switch (entityType)
                    {
                        case EntityType.Active:
                            {
                                trackActive = false;
                            }
                            break;
                        case EntityType.Airdrops:
                            {
                                trackSupplyDrops = false;
                                cache.SupplyDrops.Clear();
                            }
                            break;
                        case EntityType.Animals:
                        case EntityType.Npc:
                        case EntityType.Zombies:
                            {
                                trackNPC = false;
                                showNPC = false;
                                uiBtnNPC = false;
                                cache.Animals.Clear();
                                cache.Zombies.Clear();
                                cache.NPCPlayers.Clear();
                            }
                            break;
                        case EntityType.Bags:
                            {
                                trackBags = false;
                                showBags = false;
                                uiBtnBags = false;
                                cache.Bags.Clear();
                            }
                            break;
                        case EntityType.Backpacks:
                            {
                                trackLoot = false;
                                showLoot = false;
                                uiBtnLoot = false;
                                cache.Backpacks.Clear();
                            }
                            break;
                        case EntityType.Bradley:
                            {
                                showBradley = false;
                                uiBtnBradley = false;
                                trackBradley = false;
                                cache.BradleyAPCs.Clear();
                            }
                            break;
                        case EntityType.Cars:
                            {
                                showCars = false;
                                uiBtnCars = false;
                                trackCars = false;
                                cache.Cars.Clear();
                            }
                            break;
                        case EntityType.CargoPlanes:
                            {
                                uiBtnCargoPlanes = false;
                                trackCargoPlanes = false;
                                showCargoPlanes = false;
                                cache.CargoPlanes.Clear();
                            }
                            break;
                        case EntityType.CargoShips:
                            {
                                trackCargoShips = false;
                                showCargoShips = false;
                                uiBtnCargoShips = false;
                                cache.CargoShips.Clear();
                            }
                            break;
                        case EntityType.CH47Helicopters:
                            {
                                showCH47 = false;
                                uiBtnCH47 = false;
                                trackCH47 = false;
                                cache.CH47.Clear();
                            }
                            break;
                        case EntityType.Containers:
                            {
                                showBox = false;
                                showLoot = false;
                                showStash = false;
                                uiBtnBox = false;
                                uiBtnLoot = false;
                                uiBtnStash = false;
                                trackBox = false;
                                trackLoot = false;
                                trackStash = false;
                                cache.Containers.Clear();
                            }
                            break;
                        case EntityType.Collectibles:
                            {
                                trackCollectibles = false;
                                showCollectible = false;
                                uiBtnCollectible = false;
                                cache.Collectibles.Clear();
                            }
                            break;
                        case EntityType.Cupboards:
                            {
                                trackTC = false;
                                showTC = false;
                                uiBtnTC = false;
                                cache.Cupboards.Clear();
                            }
                            break;
                        case EntityType.CupboardsArrow:
                            {
                                showTCArrow = false;
                                uiBtnTCArrow = false;
                            }
                            break;
                        case EntityType.Dead:
                            {
                                trackDead = false;
                                showDead = false;
                                uiBtnDead = false;
                                cache.Corpses.Clear();
                            }
                            break;
                        case EntityType.GroupLimitHightlighting:
                            {
                                drawX = false;
                            }
                            break;
                        case EntityType.Heli:
                            {
                                showHeli = false;
                                uiBtnHeli = false;
                                trackHeli = false;
                                cache.Helicopters.Clear();
                            }
                            break;
                        case EntityType.MiniCopter:
                            {
                                showMiniCopter = false;
                                uiBtnMiniCopter = false;
                                trackMiniCopter = false;
                                cache.MiniCopter.Clear();
                            }
                            break;
                        case EntityType.Ore:
                            {
                                trackOre = false;
                                showOre = false;
                                uiBtnOre = false;
                                cache.Ores.Clear();
                            }
                            break;
                        case EntityType.RidableHorses:
                            {
                                showRidableHorses = false;
                                uiBtnRidableHorses = false;
                                trackRidableHorses = false;
                                cache.RidableHorse.Clear();
                            }
                            break;
                        case EntityType.RigidHullInflatableBoats:
                            {
                                showRHIB = false;
                                uiBtnRHIB = false;
                                trackRigidHullInflatableBoats = false;
                                cache.RHIB.Clear();
                            }
                            break;
                        case EntityType.Boats:
                            {
                                showBoats = false;
                                uiBtnBoats = false;
                                trackBoats = false;
                                cache.Boats.Clear();
                            }
                            break;
                        case EntityType.Sleepers:
                            {
                                trackSleepers = false;
                                showSleepers = false;
                                uiBtnSleepers = false;
                            }
                            break;
                        case EntityType.Source:
                            {
                                setSource = false;
                            }
                            break;
                        case EntityType.Turrets:
                            {
                                trackTurrets = false;
                                showTurrets = false;
                                uiBtnTurrets = false;
                                cache.Turrets.Clear();
                            }
                            break;
                    }

                    uiBtnNames = new string[0];
                    uiButtons = null;
                }
                finally
                {
                    if (!isAdmin && player.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                    {
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, false);
                        player.SendNetworkUpdateImmediate();
                    }

                    if (!LatencyAccepted())
                    {
                        double ms = (DateTime.Now - tick).TotalMilliseconds;
                        string message = ins.msg("DoESP", player.UserIDString, ms, latencyMs);
                        ins.Puts("{0} for {1} ({2})", message, player.displayName, player.UserIDString);
                        Destroy(this);
                    }
                }
            }

            private Vector3 GetNearestCupboard(BasePlayer target)
            {
                var positions = new List<Vector3>();
                float distance = 0f;

                foreach (var tc in cache.Cupboards)
                {
                    if (tc.IsAuthed(target))
                    {
                        distance = Vector3.Distance(target.transform.position, tc.transform.position);

                        if (distance >= 5f && distance <= tcArrowsDistance)
                        {
                            positions.Add(tc.transform.position);
                        }
                    }
                }

                if (positions.Count == 0)
                {
                    return Vector3.zero;
                }

                if (positions.Count > 1)
                {
                    positions.Sort((x, y) => Vector3.Distance(x, target.transform.position).CompareTo(Vector3.Distance(y, target.transform.position)));
                }

                return positions[0];
            }

            private int GetAveragePing(BasePlayer target)
            {
                if (!pings.ContainsKey(target.userID))
                {
                    pings.Add(target.userID, new Ping());
                }

                var ping = pings[target.userID];
                
                if (ping.Time == 0f || Time.realtimeSinceStartup - ping.Time >= averagePingInterval)
                {
                    ping.Time = Time.realtimeSinceStartup;
                    ping.AveragePing = Network.Net.sv.GetAveragePing(target.Connection);
                }

                return ping.AveragePing;
            }

            private bool IsLetters(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                foreach (char c in value)
                {
                    if (!char.IsLetter(c))
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool IsNumeric(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }
                
                foreach (char c in value)
                {
                    if (!char.IsDigit(c))
                    {
                        return false;
                    }
                }

                return true;
            }

            private bool SetSource()
            {
                if (!setSource)
                {
                    source = player;
                    return true;
                }

                entityType = EntityType.Source;
                source = player;

                if (player.IsSpectating())
                {
                    var parentEntity = player.GetParentEntity();

                    if (parentEntity as BasePlayer != null)
                    {
                        var target = parentEntity as BasePlayer;

                        if (target.IsDead() && !target.IsConnected)
                            player.StopSpectating();
                        else source = parentEntity;
                    }
                }

                if (player == source && (player.IsDead() || player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot)))
                    return false;

                return true;
            }

            private bool ShowActive()
            {
                if (!trackActive)
                    return true;

                entityType = EntityType.Active;
                double currDistance;
                Color color;

                foreach (var target in BasePlayer.activePlayerList)
                {
                    if (target == null || target.transform == null || !target.IsConnected)
                    {
                        continue;
                    }

                    currDistance = Math.Floor(Vector3.Distance(target.transform.position, source.transform.position));

                    if (player == target || currDistance > maxDistance)
                    {
                        continue;
                    }

                    if (ins.permission.UserHasPermission(target.UserIDString, permBypass) && !ins.permission.UserHasPermission(player.UserIDString, permBypassOverride))
                    {
                        continue;
                    }

                    color = __(target.IsAlive() ? activeCC : activeDeadCC);

                    if (currDistance < playerDistance)
                    {
                        _cachedStringBuilder.Clear();

                        if (ins.storedData.Extended.Contains(player.UserIDString) && target.GetActiveItem() != null)
                        {
                            _cachedStringBuilder.Append(target.GetActiveItem().info.displayName.translated);
                            var itemList = target.GetActiveItem().contents?.itemList;

                            if (itemList?.Count > 0)
                            {
                                _cachedStringBuilder.Append(" (");

                                for (int index = 0; index < itemList.Count; index++)
                                {
                                    _cachedStringBuilder.Append(itemList[index].info.displayName.translated).Append("|");
                                }

                                _cachedStringBuilder.Replace("Weapon ", "");
                                _cachedStringBuilder.Replace("Simple Handmade ", "");
                                _cachedStringBuilder.Replace("Muzzle ", "");
                                _cachedStringBuilder.Replace("4x Zoom Scope", "4x");
                                _cachedStringBuilder.Append(")");
                            }
                        }

                        string vanished = ins.Vanish != null && ins.Vanish.Call<bool>("IsInvisible", target) ? "<color=#FF00FF>V</color>" : string.Empty;
                        string health = showHT && target.metabolism != null ? string.Format("{0} <color=#FFA500>{1}</color>:<color=#FFADD8E6>{2}</color>", Math.Floor(target.health), target.metabolism.calories.value.ToString("#0"), target.metabolism.hydration.value.ToString("#0")) : Math.Floor(target.health).ToString("#0");

                        if (averagePingInterval > 0) _cachedStringBuilder.Append(" ").Append(GetAveragePing(target)).Append("ms");
                        if (ins.storedData.Visions.Contains(player.UserIDString)) DrawVision(player, target, invokeTime);
                        if (drawArrows) player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, __(colorDrawArrows), target.transform.position + new Vector3(0f, target.transform.position.y + 10), target.transform.position, 1);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, color, target.transform.position + new Vector3(0f, 2f, 0f), string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>{5} {6}", ins.RemoveFormatting(target.displayName) ?? target.userID.ToString(), healthCC, health, distCC, currDistance, vanished, _cachedStringBuilder.ToString()));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, color, target.transform.position + new Vector3(0f, 1f, 0f), target.GetHeight(target.modelState.ducked));
                        if (ins.voices.ContainsKey(target.userID) && Vector3.Distance(target.transform.position, source.transform.position) <= voiceDistance)
                        {
                            player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, Color.yellow, target.transform.position + new Vector3(0f, 2.5f, 0f), target.transform.position, 1);
                        }
                        ShowCupboardArrows(target, EntityType.Active);
                    }
                    else if (drawX)
                        distantPlayers.Add(target);
                    else
                        player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, color, target.transform.position + new Vector3(0f, 1f, 0f), 5f);

                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit)
                        return false;
                }

                return LatencyAccepted();
            }

            private bool ShowSleepers()
            {
                if (!showSleepers || !trackSleepers)
                    return true;

                entityType = EntityType.Sleepers;
                double currDistance;
                Color color;
                
                foreach (var sleeper in BasePlayer.sleepingPlayerList)
                {
                    if (sleeper == null || sleeper.transform == null)
                        continue;

                    currDistance = Math.Floor(Vector3.Distance(sleeper.transform.position, source.transform.position));

                    if (currDistance > maxDistance)
                        continue;

                    if (currDistance < playerDistance)
                    {
                        string health = showHT && sleeper.metabolism != null ? string.Format("{0} <color=#FFA500>{1}</color>:<color=#FFADD8E6>{2}</color>", Math.Floor(sleeper.health), sleeper.metabolism.calories.value.ToString("#0"), sleeper.metabolism.hydration.value.ToString("#0")) : Math.Floor(sleeper.health).ToString("#0");
                        color = __(sleeper.IsAlive() ? sleeperCC : sleeperDeadCC);

                        if (drawArrows) player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, __(colorDrawArrows), sleeper.transform.position + new Vector3(0f, sleeper.transform.position.y + 10), sleeper.transform.position, 1);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, color, sleeper.transform.position, string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>", ins.RemoveFormatting(sleeper.displayName) ?? sleeper.userID.ToString(), healthCC, health, distCC, currDistance));
                        if (drawX) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, color, sleeper.transform.position + new Vector3(0f, 1f, 0f), "X");
                        else if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, color, sleeper.transform.position, GetScale(currDistance));
                        ShowCupboardArrows(sleeper, EntityType.Sleepers);
                    }
                    else player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, Color.cyan, sleeper.transform.position + new Vector3(0f, 1f, 0f), 5f);

                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                }

                return LatencyAccepted();
            }

            private void ShowCupboardArrows(BasePlayer target, EntityType lastType)
            {
                if (!barebonesMode && showTCArrow && uiBtnTCArrow && uiBtnTC)
                {
                    entityType = EntityType.CupboardsArrow;
                    var nearest = GetNearestCupboard(target);

                    if (nearest != Vector3.zero)
                    {
                        player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, __(tcCC), target.transform.position + new Vector3(0f, 0.115f, 0f), nearest, 0.25f);
                    }

                    entityType = lastType;
                }
            }

            private bool ShowHeli()
            {
                if (!showHeli || (!trackHeli && !uiBtnHeli))
                    return true;

                entityType = EntityType.Heli;
                double currDistance;
                
                if (cache.Helicopters.Count > 0)
                {
                    foreach (var heli in cache.Helicopters)
                    {
                        if (heli == null || heli.transform == null || heli.IsDestroyed)
                            continue;

                        currDistance = Math.Floor(Vector3.Distance(heli.transform.position, source.transform.position));
                        string heliHealth = heli.health > 1000 ? Math.Floor(heli.health).ToString("#,##0,K", CultureInfo.InvariantCulture) : Math.Floor(heli.health).ToString("#0");
                        string info = showHeliRotorHealth ? string.Format("<color={0}>{1}</color> (<color=#FFFF00>{2}</color>/<color=#FFFF00>{3}</color>)", healthCC, heliHealth, Math.Floor(heli.weakspots[0].health), Math.Floor(heli.weakspots[1].health)) : string.Format("<color={0}>{1}</color>", healthCC, heliHealth);

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(heliCC), heli.transform.position + new Vector3(0f, 2f, 0f), string.Format("H {0} <color={1}>{2}</color>", info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(heliCC), heli.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowBradley()
            {
                if (!showBradley || (!uiBtnBradley && !trackBradley))
                    return true;

                entityType = EntityType.Bradley;
                double currDistance;
                
                if (cache.BradleyAPCs.Count > 0)
                {
                    foreach (var bradley in cache.BradleyAPCs)
                    {
                        if (bradley == null || bradley.transform == null || bradley.IsDestroyed)
                            continue;

                        currDistance = Math.Floor(Vector3.Distance(bradley.transform.position, source.transform.position));
                        string info = string.Format("<color={0}>{1}</color>", healthCC, bradley.health > 1000 ? Math.Floor(bradley.health).ToString("#,##0,K", CultureInfo.InvariantCulture) : Math.Floor(bradley.health).ToString());

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(bradleyCC), bradley.transform.position + new Vector3(0f, 2f, 0f), string.Format("B {0} <color={1}>{2}</color>", info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(bradleyCC), bradley.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowLimits()
            {
                if (!drawX)
                    return true;

                entityType = EntityType.GroupLimitHightlighting;
                
                if (distantPlayers.Count > 0)
                {
                    distantPlayers.RemoveAll(p => p == null || p.transform == null || !p.IsConnected);

                    var groupedPlayers = new Dictionary<int, List<BasePlayer>>();
                    var players = new List<BasePlayer>();
                    bool foundPlayer = false;

                    foreach (var target in distantPlayers.ToArray())
                    {
                        players.Clear();

                        foreach (var player in distantPlayers)
                        {
                            if (Vector3.Distance(player.transform.position, target.transform.position) < groupRange)
                            {
                                foundPlayer = false;

                                foreach(var list in groupedPlayers.Values)
                                {
                                    if (list.Contains(player))
                                    {
                                        foundPlayer = true;
                                        break;
                                    }
                                }

                                if (!foundPlayer && !players.Contains(player))
                                {
                                    players.Add(player);
                                }
                            }
                        }
                        
                        if (players.Count >= groupLimit)
                        {
                            int index = 0;

                            while (groupedPlayers.ContainsKey(index))
                                index++;

                            groupedPlayers.Add(index, players);
                            distantPlayers.RemoveAll(x => players.Contains(x));
                        }
                    }

                    foreach (var target in distantPlayers)
                    {
                        player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, target.IsAlive() ? Color.green : Color.red, target.transform.position + new Vector3(0f, 1f, 0f), "X");
                    }

                    foreach (var entry in groupedPlayers)
                    {
                        foreach (var target in entry.Value)
                        {
                            player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(target.IsAlive() ? GetGroupColor(entry.Key) : groupColorDead), target.transform.position + new Vector3(0f, 1f, 0f), "X");
                        }

                        if (groupCountHeight != 0f)
                        {
                            player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, Color.magenta, entry.Value[0].transform.position + new Vector3(0f, groupCountHeight, 0f), entry.Value.Count.ToString());
                        }
                    }

                    distantPlayers.Clear();
                    groupedPlayers.Clear();
                }

                return LatencyAccepted();
            }

            private bool ShowTC()
            {
                if (!showTC || !trackTC)
                    return true;

                entityType = EntityType.Cupboards;
                double currDistance;
                int bags;
                float time;

                foreach (var tc in cache.Cupboards)
                {
                    if (tc == null || tc.transform == null || tc.IsDestroyed)
                    {
                        tcs.Remove(tc);
                        continue;
                    }

                    currDistance = Math.Floor(Vector3.Distance(tc.transform.position, source.transform.position));

                    if (currDistance < tcDistance && currDistance < maxDistance)
                    {
                        if (drawText)
                        {
                            bags = 0;

                            if (showTCBagCount)
                            {
                                var building = tc.GetBuilding();

                                if (building != null)
                                {
                                    foreach(var entity in building.decayEntities)
                                    {
                                        if (entity is SleepingBag)
                                        {
                                            bags++;
                                        }
                                    }
                                }
                            }

                            if (!tcs.ContainsKey(tc))
                            {
                                tcs.Add(tc, new PrivInfo());
                            }

                            time = Time.realtimeSinceStartup;

                            if (tcs[tc].LastTime == float.MaxValue || time - tcs[tc].LastTime > 60)
                            {
                                tcs[tc].ProtectedMinutes = tc.GetProtectedMinutes();
                                tcs[tc].LastTime = time;
                            }

                            string text = string.Empty;

                            if (bags > 0 && showTCAuthedCount) text = string.Format("TC <color={0}>{1}</color> <color={2}>{3}</color> <color={4}>{5}</color> <color={0}>{6}</color>", distCC, currDistance, bagCC, bags, tcCC, tc.authorizedPlayers.Count, tcs[tc].ProtectedMinutes);
                            else if (bags > 0) text = string.Format("TC <color={0}>{1}</color> <color={2}>{3}</color> <color={0}>{4}</color>", distCC, currDistance, bagCC, bags, tcs[tc].ProtectedMinutes);
                            else if (showTCAuthedCount) text = string.Format("TC <color={0}>{1}</color> <color={2}>{3}</color> <color={0}>{4}</color>", distCC, currDistance, tcCC, tc.authorizedPlayers.Count, tcs[tc].ProtectedMinutes);
                            else text = string.Format("TC <color={0}>{1} {2}</color>", distCC, currDistance, tcs[tc].ProtectedMinutes);

                            player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(tcCC), tc.transform.position + new Vector3(0f, 0.5f, 0f), text);
                        }

                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(tcCC), tc.transform.position + new Vector3(0f, 0.5f, 0f), 3f);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }
                        
            private bool ShowContainers()
            {
                if (!showBox && !showLoot && !showStash)
                {
                    return true;
                }

                double currDistance;
                bool isBox;
                bool isLoot;

                if (showLoot)
                {
                    entityType = EntityType.Backpacks;

                    foreach (var backpack in cache.Backpacks)
                    {
                        if (backpack == null || backpack.transform == null || backpack.IsDestroyed)
                            continue;

                        currDistance = Math.Floor(Vector3.Distance(backpack.transform.position, source.transform.position));

                        if (currDistance > maxDistance || currDistance > lootDistance)
                            continue;

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(backpackCC), backpack.transform.position + new Vector3(0f, 0.5f, 0f), string.Format("{0} {1}<color={2}>{3}</color>", string.IsNullOrEmpty(backpack._playerName) ? ins.msg("backpack", player.UserIDString) : backpack._playerName, GetContents(backpack), distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(backpackCC), backpack.transform.position + new Vector3(0f, 0.5f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                if (showBox && trackSupplyDrops)
                {
                    entityType = EntityType.Airdrops;

                    foreach (var drop in cache.SupplyDrops)
                    {
                        if (drop == null || drop.transform == null || drop.IsDestroyed)
                            continue;

                        currDistance = Math.Floor(Vector3.Distance(drop.transform.position, source.transform.position));

                        if (currDistance > maxDistance || currDistance > adDistance)
                            continue;

                        string text = showAirdropContents && drop.inventory.itemList.Count > 0 ? GetContents(drop.inventory.itemList) : string.Format("({0}) ", drop.inventory.itemList.Count);

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(airdropCC), drop.transform.position + new Vector3(0f, 0.5f, 0f), string.Format("{0} {1}<color={2}>{3}</color>", _(drop.ShortPrefabName), text, distCC, currDistance, lootCC));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(airdropCC), drop.transform.position + new Vector3(0f, 0.5f, 0f), GetScale(currDistance));
                    }
                }

                entityType = EntityType.Containers;

                foreach (var container in cache.Containers)
                {
                    if (container == null || container.transform == null || container.IsDestroyed)
                        continue;

                    currDistance = Math.Floor(Vector3.Distance(container.transform.position, source.transform.position));

                    if (currDistance > maxDistance)
                        continue;

                    isBox = IsBox(container.ShortPrefabName);
                    isLoot = IsLoot(container.ShortPrefabName);

                    if (container is StashContainer)
                    {
                        if (!showStash || currDistance > stashDistance || !trackStash)
                            continue;
                    }
                    else if (isBox)
                    {
                        if (!showBox || currDistance > boxDistance || !trackBox)
                            continue;
                    }
                    else if (isLoot)
                    {
                        if (!showLoot || currDistance > lootDistance || !trackLoot)
                            continue;
                    }

                    string colorHex = isBox ? boxCC : isLoot ? lootCC : stashCC;
                    _cachedStringBuilder.Clear();

                    if (ins.storedData.OnlineBoxes.Contains(player.UserIDString) && container.OwnerID.IsSteamId() && (container.name.Contains("box") || container.name.Contains("coffin")))
                    {
                        var owner = BasePlayer.FindByID(container.OwnerID);

                        if (owner == null || !owner.IsConnected)
                        {
                            continue;
                        }
                    }

                    string text = container.inventory?.itemList?.Count > 0 ? (isLoot && showLootContents || container is StashContainer && showStashContents ? GetContents(container.inventory.itemList) : string.Format("({0}) ", container.inventory.itemList.Count)) : string.Empty;

                    if (text.Length == 0 && !drawEmptyContainers) continue;

                    string shortname = _(container.ShortPrefabName).Replace("coffinstorage", "coffin");
                    if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(colorHex), container.transform.position + new Vector3(0f, 0.5f, 0f), string.Format("{0} {1}<color={2}>{3}</color>", shortname, text, distCC, currDistance));
                    if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(colorHex), container.transform.position + new Vector3(0f, 0.5f, 0f), GetScale(currDistance));
                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                }

                return LatencyAccepted();
            }

            private bool ShowBags()
            {
                if (!showBags || !trackBags)
                    return true;

                entityType = EntityType.Bags;
                double currDistance;

                foreach (var bag in cache.Bags)
                {
                    currDistance = Math.Floor(Vector3.Distance(bag.Key, source.transform.position));

                    if (currDistance < bagDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(bagCC), bag.Key, string.Format("bag <color={0}>{1}</color>", distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(bagCC), bag.Key, bag.Value.Size);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowTurrets()
            {
                if (!showTurrets || !trackTurrets)
                    return true;

                entityType = EntityType.Turrets;
                double currDistance;

                foreach (var turret in cache.Turrets)
                {
                    currDistance = Math.Floor(Vector3.Distance(turret.Key, source.transform.position));

                    if (currDistance < turretDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(atCC), turret.Key + new Vector3(0f, 0.5f, 0f), string.Format("AT ({0}) <color={1}>{2}</color>", turret.Value.Info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(atCC), turret.Key + new Vector3(0f, 0.5f, 0f), turret.Value.Size);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowDead()
            {
                if (!showDead || !trackDead)
                    return true;

                entityType = EntityType.Dead;
                double currDistance;

                foreach (var corpse in cache.Corpses)
                {
                    if (corpse.Key == null || corpse.Key.transform == null || corpse.Key.IsDestroyed)
                        continue;

                    currDistance = Math.Floor(Vector3.Distance(corpse.Key.transform.position, source.transform.position));

                    if (currDistance < corpseDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(corpseCC), corpse.Key.transform.position + new Vector3(0f, 0.25f, 0f), string.Format("{0} ({1})", corpse.Value.Name, corpse.Value.Info));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(corpseCC), corpse.Key, GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowNPC()
            {
                if (!showNPC || !trackNPC)
                    return true;

                entityType = EntityType.Zombies;
                double currDistance;
                float j, k = TerrainMeta.HeightMap.GetHeight(source.transform.position);

                foreach (var zombie in cache.Zombies)
                {
                    if (zombie == null || zombie.transform == null || zombie.IsDestroyed)
                        continue;

                    currDistance = Math.Floor(Vector3.Distance(zombie.transform.position, source.transform.position));

                    if (currDistance > maxDistance)
                        continue;

                    if (currDistance < playerDistance)
                    {
                        if (drawArrows) player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, __(zombieCC), zombie.transform.position + new Vector3(0f, zombie.transform.position.y + 10), zombie.transform.position, 1);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(zombieCC), zombie.transform.position + new Vector3(0f, 2f, 0f), string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>", ins.msg("Zombie", player.UserIDString), healthCC, Math.Floor(zombie.health), distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(zombieCC), zombie.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                    }
                    else player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(zombieCC), zombie.transform.position + new Vector3(0f, 1f, 0f), 5f);

                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                }

                entityType = EntityType.Npc;

                foreach (var target in cache.NPCPlayers)
                {
                    if (target == null || target.transform == null || target.IsDestroyed)
                        continue;

                    currDistance = Math.Floor(Vector3.Distance(target.transform.position, source.transform.position));

                    if (player == target || currDistance > maxDistance)
                        continue;

                    if (skipUnderworld)
                    {
                        j = TerrainMeta.HeightMap.GetHeight(target.transform.position);
                        
                        if (j > target.transform.position.y)
                        {
                            if (source.transform.position.y > k)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (source.transform.position.y < k)
                            {
                                continue;
                            }
                        }
                    }

                    string npcColor = target is HTNPlayer ? htnscientistCC : target.ShortPrefabName.Contains("peacekeeper") ? peacekeeperCC : target.name.Contains("scientist") ? scientistCC : target.ShortPrefabName == "murderer" ? murdererCC : npcCC;

                    if (currDistance < npcPlayerDistance)
                    {
                        string displayName = !string.IsNullOrEmpty(target.displayName) && IsLetters(target.displayName) ? target.displayName : target.ShortPrefabName == "scarecrow" ? ins.msg("scarecrow", player.UserIDString) : target.PrefabName.Contains("scientist") ? ins.msg("scientist", player.UserIDString) : target is NPCMurderer ? ins.msg("murderer", player.UserIDString) : ins.msg("npc", player.UserIDString);
                        if (ConVar.Server.hostname.Contains("Test Server")) displayName = target is HTNPlayer ? "htn" : target is NPCMurderer ? "murderer" : target is Scientist ? "scientist" :  target.ShortPrefabName;

                        if (drawArrows) player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, target.transform.position.y + 10), target.transform.position, 1);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, 2f, 0f), string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>", displayName, healthCC, Math.Floor(target.health), distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, 1f, 0f), target.GetHeight(target.modelState.ducked));
                    }
                    else player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, 1f, 0f), 5f);

                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                }

                entityType = EntityType.Animals;
                foreach (var npc in cache.Animals)
                {
                    if (npc == null || npc.transform == null || npc.IsDestroyed)
                        continue;

                    currDistance = Math.Floor(Vector3.Distance(npc.transform.position, source.transform.position));

                    if (currDistance < npcDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(npcCC), npc.transform.position + new Vector3(0f, 1f, 0f), string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>", npc.ShortPrefabName, healthCC, Math.Floor(npc.health), distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(npcCC), npc.transform.position + new Vector3(0f, 1f, 0f), npc.bounds.size.y);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowOre()
            {
                if (!showOre || !trackOre)
                    return true;

                entityType = EntityType.Ore;
                double currDistance;
                
                foreach (var ore in cache.Ores)
                {
                    currDistance = Math.Floor(Vector3.Distance(ore.Key, source.transform.position));

                    if (currDistance < oreDistance && currDistance < maxDistance)
                    {
                        string info = showResourceAmounts ? string.Format("({0})", ore.Value.Info) : string.Format("<color={0}>{1}</color>", distCC, currDistance);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(resourceCC), ore.Key + new Vector3(0f, 1f, 0f), string.Format("{0} {1}", ore.Value.Name, info));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(resourceCC), ore.Key + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowCollectables()
            {
                if (!showCollectible || !trackCollectibles)
                    return true;

                entityType = EntityType.Collectibles;
                double currDistance;
                
                foreach (var col in cache.Collectibles)
                {
                    currDistance = Math.Floor(Vector3.Distance(col.Key, source.transform.position));

                    if (currDistance < colDistance && currDistance < maxDistance)
                    {
                        string info = showResourceAmounts ? string.Format("({0})", col.Value.Info) : string.Format("<color={0}>{1}</color>", distCC, currDistance);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(colCC), col.Key + new Vector3(0f, 1f, 0f), string.Format("{0} {1}", _(col.Value.Name), info));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(colCC), col.Key + new Vector3(0f, 1f, 0f), col.Value.Size);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted();
            }

            private bool ShowEntity(EntityType entityType, bool track, string entityName, HashSet<BaseEntity> entities)
            {
                if (!track)
                    return true;

                this.entityType = entityType;

                if (entities.Count > 0)
                {
                    double currDistance;

                    foreach (var e in entities)
                    {
                        if (e == null || e.transform == null || e.IsDestroyed)
                            continue;

                        currDistance = Math.Floor(Vector3.Distance(e.transform.position, source.transform.position));

                        if (entityType == EntityType.Boats || entityType == EntityType.RigidHullInflatableBoats)
                        {
                            if (currDistance > boatDistance) continue;
                            if (!trackRigidHullInflatableBoats && !uiBtnRHIB) continue;
                        }
                        else if (entityType == EntityType.Cars)
                        {
                            if (currDistance > carDistance) continue;
                            if (!trackCars && !uiBtnCars) continue;
                        }
                        else if (entityType == EntityType.MiniCopter)
                        {
                            if (currDistance > mcDistance) continue;
                            if (!trackMiniCopter && !uiBtnMiniCopter) continue;
                        }
                        else if (entityType == EntityType.RidableHorses)
                        {
                            if (currDistance > rhDistance) continue;
                            if (!trackRidableHorses && !uiBtnRidableHorses) continue;
                        }
                        else if (entityType == EntityType.CH47Helicopters && !trackCH47 && !uiBtnCH47) continue;

                        string info = e.Health() <= 0 ? entityName : string.Format("{0} <color={1}>{2}</color>", entityName, healthCC, e.Health() > 1000 ? Math.Floor(e.Health()).ToString("#,##0,K", CultureInfo.InvariantCulture) : Math.Floor(e.Health()).ToString("#0"));

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(bradleyCC), e.transform.position + new Vector3(0f, 2f, 0f), string.Format("{0} <color={1}>{2}</color>", info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(bradleyCC), e.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }
                
                return LatencyAccepted();
            }

            private string GetContents(List<Item> itemList)
            {
                _cachedStringBuilder.Clear();
                _cachedStringBuilder.Append("(");

                for (int index = 0; index < itemList.Count; index++)
                {
                    var item = itemList[index];
                    _cachedStringBuilder.Append(item.info.displayName.translated).Append(" ");
                    _cachedStringBuilder.Append("(").Append(item.amount).Append("), ");
                }

                _cachedStringBuilder.Length -= 2;
                _cachedStringBuilder.Append(") ");

                return _cachedStringBuilder.ToString();
            }

            public static string GetContents(ItemContainer[] containers)
            {
                if (containers == null)
                {
                    return string.Empty;
                }
                
                var list = new List<string>();
                int count = 0;
                int amount = 0;

                foreach (var container in containers)
                {
                    if (container == null || container.itemList == null) continue;

                    count += container.itemList.Count;

                    foreach(var item in container.itemList)
                    {
                        list.Add(string.Format("{0} ({1})", item.info.displayName.translated.ToLower(), item.amount));

                        if (++amount >= corpseContentAmount)
                        {
                            break;
                        }
                    }
                }

                if (corpseContentAmount > 0 && list.Count > 0)
                {
                    return string.Format("{0} ({1})", string.Join(", ", list.ToArray()), count.ToString());
                }

                return count.ToString();
            }

            public static string GetContents(DroppedItemContainer backpack)
            {
                if (backpack?.inventory?.itemList == null)
                {
                    return string.Empty;
                }

                if (backpackContentAmount > 0 && backpack.inventory.itemList.Count > 0)
                {
                    var list = new List<string>();
                    int amount = 0;

                    foreach (var item in backpack.inventory.itemList)
                    {
                        list.Add(string.Format("{0} ({1})", item.info.displayName.translated.ToLower(), item.amount));

                        if (++amount >= backpackContentAmount)
                        {
                            break;
                        }
                    }

                    return string.Format("({0}) ({1}) ", string.Join(", ", list.ToArray()), backpack.inventory.itemList.Count.ToString());
                }

                return backpack.inventory.itemList.Count.ToString();
            }

            private static double GetScale(double value)
            {
                return value * 0.02;
            }
        }
        
        private bool IsRadar(string id)
        {
            foreach(var radar in activeRadars)
            {
                if (radar.player.UserIDString == id)
                {
                    return true;
                }
            }

            return false;
        }

        private void Init()
        {
            ins = this;
            Unsubscribe(nameof(OnEntityTakeDamage));
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnPlayerDisconnected));
            Unsubscribe(nameof(OnPlayerVoice));
            Unsubscribe(nameof(OnPlayerInit));
            Unsubscribe(nameof(OnPlayerSleepEnded));
        }

        private void Loaded()
        {
            isUnloading = false;
            cache = new Cache();
            permission.RegisterPermission(permAllowed, this);
            permission.RegisterPermission(permBypass, this);
            permission.RegisterPermission(permAuto, this);
            permission.RegisterPermission(permBypassOverride, this);
        }

        private void OnServerInitialized()
        {
            try
            {
                storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch { }

            if (storedData == null)
                storedData = new StoredData();

            LoadVariables();

            if (!drawBox && !drawText && !drawArrows)
            {
                Puts("Configuration does not have a chosen drawing method. Setting drawing method to text.");
                Config.Set("Drawing Methods", "Draw Text", true);
                Config.Save();
                drawText = true;
            }

            if (useVoiceDetection)
            {
                Subscribe(nameof(OnPlayerVoice));
                Subscribe(nameof(OnPlayerDisconnected));
            }

            init = true;

            if (barebonesMode)
            {
                return;
            }

            Subscribe(nameof(OnPlayerInit));
            Subscribe(nameof(OnEntityDeath));
            Subscribe(nameof(OnEntityKill));
            Subscribe(nameof(OnEntitySpawned));
            Subscribe(nameof(OnPlayerSleepEnded));

            coroutine = ServerMgr.Instance.StartCoroutine(FillCache());

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player != null && player.IsConnected && permission.UserHasPermission(player.UserIDString, permAllowed))
                {
                    cmdESP(player, "radar", new string[0]);
                }
            }
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (player == null)
                return;
            
            accessList.RemoveAll(p => p == null || p == player || !p.IsConnected);

            if (player.IsAdmin || HasAccess(player))
            {
                accessList.Add(player);
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player != null && player.IsConnected && player.GetComponent<Radar>() == null && permission.UserHasPermission(player.UserIDString, permAuto) && HasAccess(player))
            {
                cmdESP(player, "radar", new string[0]);
            }
        }

        private void OnPlayerVoice(BasePlayer player, Byte[] data)
        {
            ulong userId = player.userID;

            if (voices.ContainsKey(userId))
                voices[userId].Reset();
            else voices.Add(userId, timer.Once(voiceInterval, () => voices.Remove(userId)));
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (useVoiceDetection)
            {
                voices.Remove(player.userID);
            }

            accessList.RemoveAll(p => p == null || p == player || !p.IsConnected);
        }

        private void Unload()
        {
            isUnloading = true;
            StopFillCache();
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

            var radarObjects = UnityEngine.Object.FindObjectsOfType(typeof(Radar));

            if (radarObjects != null)
            {
                foreach (var gameObj in radarObjects)
                {
                    UnityEngine.Object.Destroy(gameObj);
                }
            }

            playersColor.Clear();
            tags.Clear();
            voices.Clear();
            activeRadars.Clear();
            cache = null;
            uiBtnNames = new string[0];
            uiButtons = null;
            authorized.Clear();
            itemExceptions.Clear();
            groupColors.Clear();
            cooldowns.Clear();
        }

        private void Warn(BasePlayer player, string message)
        {
            ulong playerId = player.userID;

            if (!warnings.Contains(playerId))
            {
                player.ChatMessage(msg(message, player.UserIDString));
                warnings.Add(playerId);
                timer.Once(5f, () => warnings.Remove(playerId));
            }
        }

        private bool BlockDamage(HitInfo info)
        {
            if (info != null)
            {
                info.damageTypes = new DamageTypeList();
                info.DidHit = false;
                info.HitEntity = null;
                info.Initiator = null;
                info.DoHitEffects = false;
                info.HitMaterial = 0;
                info.PointStart = Vector3.zero;
            }

            return true;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var attacker = info?.Initiator as BasePlayer;
            
            if (attacker == null || !attacker.IsConnected)
            {
                return null;
            }

            // Check if player is using radar
            if (!IsRadar(attacker.UserIDString))
            {
                return null;
            }

            // Block damage to animals
            if (entity is BaseNpc)
            {
                if (blockDamageAnimals)
                {
                    Warn(attacker, "CantHurtAnimals");
                    return BlockDamage(info);
                }
                else return null;
            }

            // Block damage to buildings
            if (entity is BuildingBlock)
            {
                if (blockDamageBuildings)
                {
                    Warn(attacker, "CantDamageBuilds");
                    return BlockDamage(info);
                }
                else return null;
            }

            // Block damage to players
            if (entity is BasePlayer && entity.ToPlayer().userID.IsSteamId())
            {
                if (blockDamagePlayers)
                {
                    Warn(attacker, "CantHurtPlayers");
                    return BlockDamage(info);
                }
                else return null;
            }

            // Block damage to all other npcs
            if (entity.IsNpc)
            {
                if (blockDamageNpcs)
                {
                    Warn(attacker, "CantHurtNpcs");
                    return BlockDamage(info);
                }
                else return null;
            }

            // Block damage to all other entities
            if (blockDamageOther)
            {
                Warn(attacker, "CantHurtOther");
                return BlockDamage(info);
            }

            return null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            RemoveFromCache(entity as BaseEntity);
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            RemoveFromCache(entity as BaseEntity);
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            AddToCache(entity as BaseEntity);
        }

        private static void DrawVision(BasePlayer player, BasePlayer target, float invokeTime)
        {
            RaycastHit hit;
            if (Physics.Raycast(target.eyes.HeadRay(), out hit, Mathf.Infinity))
            {
                player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, Color.red, target.eyes.position + new Vector3(0f, 0.115f, 0f), hit.point, 0.15f);
            }
        }

        private static bool IsHex(string value)
        {
            foreach (char c in value)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        private static Color __(string value)
        {
            Color color;
            if (!ColorUtility.TryParseHtmlString(IsHex(value) ? "#" + value : value, out color))
            {
                color = Color.white;
            }

            return color;
        }

        private static string _(string value)
        {
            var sb = new StringBuilder(value);

            foreach (string str in tags)
            {
                sb.Replace(str, string.Empty);
            }

            return sb.ToString();
        }

        private void StopFillCache()
        {
            if (coroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(coroutine);
            }
        }

        private WaitForEndOfFrame _cachedWaitForEndOfFrame = new WaitForEndOfFrame(); // 4.8.0 Credits @hoppel
        private System.Collections.IEnumerator FillCache()
        {
            var tick = DateTime.Now;
            int cached = 0, total = 0;

            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (AddToCache(e as BaseEntity))
                {
                    cached++;
                }

                total++;

                if (total % 50 == 0)
                {
                    yield return _cachedWaitForEndOfFrame;
                }
            }

            Puts("Cached {0}/{1} entities in {2} seconds!", cached, total, (DateTime.Now - tick).TotalSeconds);
            coroutine = null;
        }

        private bool AddToCache(BaseEntity entity)
        {
            if (entity == null || entity.transform == null || entity.IsDestroyed)
                return false;

            var position = entity.transform.position;

            if (trackNPC && entity.IsNpc)
            {
                if (entity is BaseNpc)
                {
                    return cache.Animals.Add(entity as BaseNpc);
                }
                else if (entity is BasePlayer)
                {
                    return cache.NPCPlayers.Add(entity as BasePlayer);
                }
                else if (entity is Zombie)
                {
                    return cache.Zombies.Add(entity as Zombie);
                }

                return false;
            }

            if (trackTC && entity is BuildingPrivlidge)
            {
                return cache.Cupboards.Add(entity as BuildingPrivlidge);
            }
            else if (entity is StorageContainer)
            {
                if (trackSupplyDrops && entity is SupplyDrop)
                {
                    return cache.SupplyDrops.Add(entity as SupplyDrop);
                }

                return IsBox(entity.ShortPrefabName) || IsLoot(entity.ShortPrefabName) ? cache.Containers.Add(entity as StorageContainer) : false;
            }
            else if (trackLoot && entity is DroppedItemContainer)
            {
                return cache.Backpacks.Add(entity as DroppedItemContainer);
            }
            else if (trackCollectibles && entity is CollectibleEntity)
            {
                if (!cache.Collectibles.ContainsKey(position))
                {
                    var itemList = entity.GetComponent<CollectibleEntity>().itemList;
                    int sum = 0;

                    if (itemList != null)
                    {
                        foreach(var item in itemList)
                        {
                            sum += (int)item.amount;
                        }
                    }

                    cache.Collectibles.Add(position, new CachedInfo { Name = _(entity.ShortPrefabName), Size = 0.5f, Info = sum });
                    return true;
                }

                return false;
            }
            else if (trackOre && entity is OreResourceEntity)
            {
                if (!cache.Ores.ContainsKey(position))
                {
                    float amount = 0;

                    foreach(var item in entity.GetComponentInParent<ResourceDispenser>().containedItems)
                    {
                        amount += item.amount;
                    }

                    cache.Ores.Add(position, new CachedInfo { Name = _(entity.ShortPrefabName), Info = amount });
                    return true;
                }

                return false;
            }
            else if (trackDead && entity is PlayerCorpse)
            {
                var corpse = entity as PlayerCorpse;

                if (!cache.Corpses.ContainsKey(corpse) && corpse.playerSteamID.IsSteamId())
                {
                    string contents = Radar.GetContents(corpse.containers);
                    cache.Corpses.Add(corpse, new CachedInfo { Name = corpse.parentEnt?.ToString() ?? corpse.playerSteamID.ToString(), Info = contents });
                    return true;
                }

                return false;
            }
            else if (trackBags && entity is SleepingBag)
            {
                if (!cache.Bags.ContainsKey(position))
                {
                    cache.Bags.Add(position, new CachedInfo { Size = 0.5f });
                    return true;
                }

                return false;
            }
            else if (trackCargoPlanes && entity is CargoPlane)
            {
                return cache.CargoPlanes.Add(entity as CargoPlane);
            }
            else if (trackHeli && entity is BaseHelicopter)
            {
                return cache.Helicopters.Add(entity as BaseHelicopter);
            }
            else if (trackBradley && entity is BradleyAPC)
            {
                return cache.BradleyAPCs.Add(entity as BradleyAPC);
            }
            else if (trackRidableHorses && entity is RidableHorse)
            {
                return cache.RidableHorse.Add(entity);
            }
            else if (trackRigidHullInflatableBoats && entity is RHIB)
            {
                return cache.RHIB.Add(entity);
            }
            else if (trackMiniCopter && entity is MiniCopter)
            {
                return cache.MiniCopter.Add(entity);
            }
            else if (trackBoats && entity is BaseBoat)
            {
                return cache.Boats.Add(entity);
            }
            else if (trackCH47 && entity is CH47Helicopter)
            {
                return cache.CH47.Add(entity);
            }
            if (trackCargoShips && entity is CargoShip)
            {
                return cache.CargoShips.Add(entity);
            }
            else if (trackCars && entity is BaseCar)
            {
                return cache.Cars.Add(entity);
            }
            else if (trackTurrets && entity.name.Contains("turret"))
            {
                if (!cache.Turrets.ContainsKey(position))
                {
                    int amount = -1;
                    var io = entity as ContainerIOEntity;

                    if (io?.inventory?.itemList?.Count > 0)
                    {
                        foreach (Item item in io.inventory.itemList)
                        {
                            amount += item.amount;
                        }
                    }

                    if (io == null)
                    {
                        var container = entity as StorageContainer;

                        if (container?.inventory?.itemList?.Count > 0)
                        {
                            foreach (Item item in container.inventory.itemList)
                            {
                                amount += item.amount;
                            }
                        }
                    }

                    cache.Turrets.Add(position, new CachedInfo { Size = 1f, Info = amount });
                    return true;
                }
            }

            return false;
        }

        private static bool IsBox(string str)
        {
            if (trackBox)
            {
                return str.Contains("box") || str.Equals("heli_crate") || str.Contains("coffin") || str.Contains("stash");
            }

            return false;
        }

        private static bool IsLoot(string str)
        {
            if (trackLoot)
            {
                return str.Contains("loot") || str.Contains("crate_") || str.Contains("trash") || str.Contains("hackable") || str.Contains("oil");
            }

            return false;
        }

        private static void RemoveFromCache(BaseEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            var position = entity.transform?.position ?? Vector3.zero;

            if (cache.Ores.ContainsKey(position))
                cache.Ores.Remove(position);
            else if (entity is StorageContainer)
                cache.Containers.Remove(entity as StorageContainer);
            else if (cache.Collectibles.ContainsKey(position))
                cache.Collectibles.Remove(position);
            else if (entity is BaseNpc)
                cache.Animals.Remove(entity as BaseNpc);
            else if (entity is PlayerCorpse)
                cache.Corpses.Remove(entity as PlayerCorpse);
            else if (cache.Bags.ContainsKey(position))
                cache.Bags.Remove(position);
            else if (entity is DroppedItemContainer)
                cache.Backpacks.Remove(entity as DroppedItemContainer);
            else if (entity is BaseHelicopter)
                cache.Helicopters.Remove(entity as BaseHelicopter);
            else if (cache.Turrets.ContainsKey(position))
                cache.Turrets.Remove(position);
            else if (entity is Zombie)
                cache.Zombies.Remove(entity as Zombie);
            else if (entity is CargoShip)
                cache.CargoShips.Remove(entity);
            else if (entity is BaseCar)
                cache.Cars.Remove(entity);
            else if (entity is CH47Helicopter)
                cache.CH47.Remove(entity);
            else if (entity is RHIB)
                cache.RHIB.Remove(entity);
            else if (entity is BaseBoat)
                cache.Boats.Remove(entity);
            else if (entity is RidableHorse)
                cache.RidableHorse.Remove(entity);
            else if (entity is MiniCopter)
                cache.MiniCopter.Remove(entity);
            else if (entity is CargoPlane)
                cache.CargoPlanes.Remove(entity);
        }

        private bool HasAccess(BasePlayer player)
        {
            if (player == null)
                return false;

            if (DeveloperList.Contains(player.userID))
                return true;

            if (authorized.Count > 0)
                return authorized.Contains(player.UserIDString);

            if (permission.UserHasPermission(player.UserIDString, permAllowed))
                return true;

            if (player.IsConnected && player.net.connection.authLevel >= authLevel)
                return true;

            return false;
        }

        [ConsoleCommand("espgui")]
        private void ccmdESPGUI(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs())
                return;

            var player = arg.Player();

            if (!player)
                return;

            cmdESP(player, "espgui", arg.Args);
        }
        
        private void cmdESP(BasePlayer player, string command, string[] args)
        {
            if (!HasAccess(player))
            {
                player.ChatMessage(msg("NotAllowed", player.UserIDString));
                return;
            }

            if (args.Length == 1)
            {
                switch (args[0].ToLower())
                {
                    case "drops":
                        {
                            bool hasDrops = false;
                            DroppedItem drop = null;
                            double currDistance;

                            foreach (var entity in BaseNetworkable.serverEntities)
                            {
                                if (entity is DroppedItem || entity is Landmine || entity is BearTrap || entity is DroppedItemContainer)
                                {
                                    drop = entity as DroppedItem;
                                    string shortname = drop?.item?.info.shortname ?? entity.ShortPrefabName;
                                    currDistance = Math.Floor(Vector3.Distance(entity.transform.position, player.transform.position));

                                    if (currDistance < lootDistance)
                                    {
                                        if (drawText) player.SendConsoleCommand("ddraw.text", 30f, Color.red, entity.transform.position, string.Format("{0} <color=#FFFF00>{1}</color>", shortname, currDistance));
                                        if (drawBox) player.SendConsoleCommand("ddraw.box", 30f, Color.red, entity.transform.position, 0.25f);
                                        hasDrops = true;
                                    }
                                }
                            }

                            if (!hasDrops)
                            {
                                player.ChatMessage(msg("NoDrops", player.UserIDString, lootDistance));
                            }
                        }
                        return;
                    case "online":
                        {
                            if (storedData.OnlineBoxes.Contains(player.UserIDString))
                                storedData.OnlineBoxes.Remove(player.UserIDString);
                            else
                                storedData.OnlineBoxes.Add(player.UserIDString);

                            player.ChatMessage(msg(storedData.OnlineBoxes.Contains(player.UserIDString) ? "BoxesOnlineOnly" : "BoxesAll", player.UserIDString));
                        }
                        return;
                    case "vision":
                        {
                            if (storedData.Visions.Contains(player.UserIDString))
                                storedData.Visions.Remove(player.UserIDString);
                            else
                                storedData.Visions.Add(player.UserIDString);

                            player.ChatMessage(msg(storedData.Visions.Contains(player.UserIDString) ? "VisionOn" : "VisionOff", player.UserIDString));
                        }
                        return;
                    case "ext":
                    case "extend":
                    case "extended":
                        {
                            if (storedData.Extended.Contains(player.UserIDString))
                                storedData.Extended.Remove(player.UserIDString);
                            else
                                storedData.Extended.Add(player.UserIDString);

                            player.ChatMessage(msg(storedData.Extended.Contains(player.UserIDString) ? "ExtendedPlayersOn" : "ExtendedPlayersOff", player.UserIDString));
                        }
                        return;
                }
            }

            var list = new List<string>();

            foreach (string arg in args)
            {
                list.Add(arg.ToLower());
            }

            if (!storedData.Filters.ContainsKey(player.UserIDString))
                storedData.Filters.Add(player.UserIDString, list);

            if (args.Length == 0 && player.GetComponent<Radar>())
            {
                UnityEngine.Object.Destroy(player.GetComponent<Radar>());
                return;
            }

            args = list.ToArray();

            if (args.Length >= 1)
            {
                if (args[0] == "setanchormin" && args.Length >= 1)
                {
                    if (args.Length == 3)
                    {
                        anchorMin = $"{args[1]} {args[2]}";

                        foreach (var radar in activeRadars)
                        {
                            if (radar.player == player)
                            {
                                DestroyUI(player);
                                CreateUI(player, radar, isArg(args, "all"));
                            }
                        }
                    }

                    player.ChatMessage(anchorMin);
                    return;
                }
                if (args[0] == "setanchormax" && args.Length >= 1)
                {
                    if (args.Length == 3)
                    {
                        anchorMax = $"{args[1]} {args[2]}";

                        foreach (var radar in activeRadars)
                        {
                            if (radar.player == player)
                            {
                                DestroyUI(player);
                                CreateUI(player, radar, isArg(args, "all"));
                            }
                        }
                    }

                    player.ChatMessage(anchorMax);
                    return;
                }
                if (args[0] == "anchors_save")
                {
                    Config.Set("GUI", "Anchor Min", anchorMin);
                    Config.Set("GUI", "Anchor Max", anchorMax);
                    player.ChatMessage($"Saved: {anchorMin} {anchorMax}");
                    return;
                }
                if (args[0] == "anchors_reset")
                {
                    anchorMin = anchorMinDefault;
                    anchorMax = anchorMaxDefault;
                    Config.Set("GUI", "Anchor Min", anchorMin);
                    Config.Set("GUI", "Anchor Max", anchorMax);
                    player.ChatMessage($"Reset: {anchorMin} {anchorMax}");
                    return;
                }
                if (args[0] == "tracker")
                {
                    player.ChatMessage("Feature removed. Use Player Tracker plugin.");
                    return;
                }
                if (args[0] == "help")
                {
                    player.ChatMessage(msg("Help1", player.UserIDString, string.Join(", ", GetButtonNames, "HT")));
                    player.ChatMessage(msg("Help2", player.UserIDString, szChatCommand, "online"));
                    player.ChatMessage(msg("Help3", player.UserIDString, szChatCommand, "ui"));
                    player.ChatMessage(msg("Help7", player.UserIDString, szChatCommand, "vision"));
                    player.ChatMessage(msg("Help8", player.UserIDString, szChatCommand, "ext"));
                    player.ChatMessage(msg("Help9", player.UserIDString, szChatCommand, lootDistance));
                    player.ChatMessage(msg("Help5", player.UserIDString, szChatCommand));
                    player.ChatMessage(msg("Help6", player.UserIDString, szChatCommand));
                    player.ChatMessage(msg("PreviousFilter", player.UserIDString, command));
                    return;
                }

                if (args[0].Contains("ui"))
                {
                    if (storedData.Filters[player.UserIDString].Contains(args[0]))
                        storedData.Filters[player.UserIDString].Remove(args[0]);

                    if (storedData.Hidden.Contains(player.UserIDString))
                    {
                        storedData.Hidden.Remove(player.UserIDString);
                        player.ChatMessage(msg("GUIShown", player.UserIDString));
                    }
                    else
                    {
                        storedData.Hidden.Add(player.UserIDString);
                        player.ChatMessage(msg("GUIHidden", player.UserIDString));
                    }

                    args = storedData.Filters[player.UserIDString].ToArray();
                }
                else if (args[0] == "list")
                {
                    var sb = new StringBuilder();

                    if (activeRadars.Count > 0)
                    {
                        foreach (var radar in activeRadars)
                        {
                            sb.Append(radar.player.displayName).Append(", ");
                        }

                        sb.Length -= 2;
                    }

                    player.ChatMessage(activeRadars.Count == 0 ? msg("NoActiveRadars", player.UserIDString) : msg("ActiveRadars", player.UserIDString, sb.ToString()));
                    return;
                }
                else if (args[0] == "f") 
                    args = storedData.Filters[player.UserIDString].ToArray();
            }

            if (command == "espgui")
            {
                string filter = storedData.Filters[player.UserIDString].Find(f => f.Equals(args[0])) ?? storedData.Filters[player.UserIDString].Find(f => f.Contains(args[0]) || args[0].Contains(f)) ?? args[0];

                if (storedData.Filters[player.UserIDString].Contains(filter))
                    storedData.Filters[player.UserIDString].Remove(filter);
                else
                    storedData.Filters[player.UserIDString].Add(filter);

                args = storedData.Filters[player.UserIDString].ToArray();
            }
            else
            {
                if (coolDown > 0f)
                {
                    float time = Time.realtimeSinceStartup;

                    if (cooldowns.ContainsKey(player.userID))
                    {
                        float cooldown = cooldowns[player.userID] - time;

                        if (cooldown > 0)
                        {
                            player.ChatMessage(msg("WaitCooldown", player.UserIDString, cooldown));
                            return;
                        }
                        else cooldowns.Remove(player.userID);
                    }
                }

                list.Clear();

                foreach(string arg in args)
                {
                    list.Add(arg);
                }

                storedData.Filters[player.UserIDString] = list;
            }

            var esp = player.GetComponent<Radar>() ?? player.gameObject.AddComponent<Radar>();
            float invokeTime, maxDistance, outTime, outDistance;

            if (args.Length > 0 && float.TryParse(args[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out outTime))
                invokeTime = outTime < 0.1f ? 0.1f : outTime;
            else
                invokeTime = defaultInvokeTime;

            if (args.Length > 1 && float.TryParse(args[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out outDistance))
                maxDistance = outDistance <= 0f ? defaultMaxDistance : outDistance;
            else
                maxDistance = defaultMaxDistance;

            bool showAll = isArg(args, "all");
            esp.showAll = showAll;
            esp.showBags = isArg(args, "bag") || showAll;
            esp.showBoats = isArg(args, "boats") || (!uiBtnBoats && trackBoats);
            esp.showBox = isArg(args, "box") || showAll;
            esp.showBradley = isArg(args, "bradley") || showAll || (!uiBtnBradley && trackBradley);
            esp.showCargoPlanes = isArg(args, "cargoplane") || showAll || (!uiBtnCargoPlanes && trackCargoPlanes);
            esp.showCargoShips = isArg(args, "cargoship") || showAll || (!uiBtnCargoShips && trackCargoShips);
            esp.showCars = isArg(args, "cars") || showAll || (!uiBtnCars && trackCars);
            esp.showCH47 = isArg(args, "ch47") || showAll || (!uiBtnCH47 && trackCH47);
            esp.showCollectible = isArg(args, "col") || showAll;
            esp.showDead = isArg(args, "dead") || showAll;
            esp.showHeli = isArg(args, "heli") || showAll || (!uiBtnHeli && trackHeli);
            esp.showLoot = isArg(args, "loot") || showAll;
            esp.showMiniCopter = isArg(args, "mini") || showAll || (!uiBtnMiniCopter && trackMiniCopter);
            esp.showNPC = isArg(args, "npc") || showAll;
            esp.showOre = isArg(args, "ore") || showAll;
            esp.showRidableHorses = isArg(args, "horse") || showAll || (!uiBtnRidableHorses && trackRidableHorses);
            esp.showRHIB = isArg(args, "rhib") || showAll || (!uiBtnRHIB && trackRigidHullInflatableBoats);
            esp.showSleepers = isArg(args, "sleeper") || showAll;
            esp.showStash = isArg(args, "stash") || showAll;
            esp.showTC = isArg(args, "tc", true) || showAll;
            esp.showTCArrow = isArg(args, "tcarrows", true) || showAll;
            esp.showTurrets = isArg(args, "turret") || showAll;
            esp.showHT = isArg(args, "ht");

            if (showUI && !barebonesMode)
            {
                if (radarUI.Contains(player.UserIDString))
                {
                    DestroyUI(player);
                }

                if (!storedData.Hidden.Contains(player.UserIDString))
                {
                    CreateUI(player, esp, showAll);
                }
            }

            esp.invokeTime = invokeTime;
            esp.maxDistance = maxDistance;
            esp.Start();

            if (command == "espgui")
                return;

            player.ChatMessage(msg("Activated", player.UserIDString, invokeTime, maxDistance, command));
        }

        private bool isArg(string[] args, string arg, bool equalTo = false)
        {
            foreach(string Arg in args)
            {
                if (equalTo)
                {
                    if (Arg.Equals(arg))
                    {
                        return true;
                    }
                }
                else if (Arg.Contains(arg) || arg.Contains(Arg))
                {
                    return true;
                }
            }

            return false;
        }

        #region UI

        private static string[] uiBtnNames = new string[0];
        private static Dictionary<int, Dictionary<string, string>> uiButtons;
        private static readonly List<string> radarUI = new List<string>();
        private readonly string UI_PanelName = "AdminRadar_UI";

        public void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_PanelName);
            radarUI.Remove(player.UserIDString);
        }

        private void CreateUI(BasePlayer player, Radar esp, bool showAll)
        {
            var buttonNames = GetButtonNames;
            var buttons = CreateButtons;
            string aMin = anchorMin;
            string aMax = anchorMax;

            if (buttons.Count > 12)
            {
                double anchorMinX;
                if (double.TryParse(anchorMin.Split(' ')[0], out anchorMinX))
                {
                    double anchorMinY;
                    if (double.TryParse(anchorMin.Split(' ')[1], out anchorMinY))
                    {
                        double anchorMaxX;
                        if (double.TryParse(anchorMax.Split(' ')[0], out anchorMaxX))
                        {
                            double anchorMaxY;
                            if (double.TryParse(anchorMax.Split(' ')[1], out anchorMaxY))
                            {
                                if (buttons.Count >= 13 && buttons.Count <= 16)
                                {
                                    anchorMinX += 0.010;
                                    anchorMinY += 0.0215;
                                    anchorMaxX -= 0.0135;
                                    anchorMaxY -= 0.0135;

                                    aMin = string.Format("{0} {1}", anchorMinX, anchorMinY);
                                    aMax = string.Format("{0} {1}", anchorMaxX, anchorMaxY);
                                }
                                else if (buttons.Count >= 17)
                                {
                                    anchorMinX -= 0.024;
                                    anchorMinY += 0.0175;
                                    anchorMaxX -= 0.0305;
                                    anchorMaxY -= 0.0305;

                                    aMin = string.Format("{0} {1}", anchorMinX, anchorMinY);
                                    aMax = string.Format("{0} {1}", anchorMaxX, anchorMaxY);
                                }
                            }
                        }
                    }
                }
            }

            var element = UI.CreateElementContainer(UI_PanelName, "0 0 0 0.0", aMin, aMax, false);
            int fontSize = buttons.Count > 12 ? 8 : 10;

            for (int x = 0; x < buttonNames.Length; x++)
            {
                UI.CreateButton(ref element, UI_PanelName, esp.GetBool(buttonNames[x]) ? uiColorOn : uiColorOff, msg(buttonNames[x], player.UserIDString), fontSize, buttons[x]["Anchor"], buttons[x]["Offset"], "espgui " + buttonNames[x]);
            }

            radarUI.Add(player.UserIDString);
            CuiHelper.AddUi(player, element);
        }

        public string[] GetButtonNames
        {
            get
            {
                if (uiBtnNames.Length == 0)
                {
                    var list = new List<string>() { "All" };

                    if (uiBtnBags) list.Add("Bags");
                    if (uiBtnBoats) list.Add("Boats");
                    if (uiBtnBox) list.Add("Box");
                    if (uiBtnBradley) list.Add("Bradley");
                    if (uiBtnCargoPlanes) list.Add("CargoPlanes");
                    if (uiBtnCargoShips) list.Add("CargoShips");
                    if (uiBtnCars) list.Add("Cars");
                    if (uiBtnCH47) list.Add("CH47");
                    if (uiBtnCollectible) list.Add("Collectibles");
                    if (uiBtnDead) list.Add("Dead");
                    if (uiBtnHeli) list.Add("Heli");
                    if (uiBtnLoot) list.Add("Loot");
                    if (uiBtnMiniCopter) list.Add("MiniCopter");
                    if (uiBtnNPC) list.Add("NPC");
                    if (uiBtnOre) list.Add("Ore");
                    if (uiBtnRidableHorses) list.Add("Horses");
                    if (uiBtnRHIB) list.Add("RHIB");
                    if (uiBtnSleepers) list.Add("Sleepers");
                    if (uiBtnStash) list.Add("Stash");
                    if (uiBtnTC) list.Add("TC");
                    if (uiBtnTCArrow) list.Add("TCArrows");
                    if (uiBtnTurrets) list.Add("Turrets");

                    uiBtnNames = list.ToArray();
                }

                return uiBtnNames;
            }
        }

        public Dictionary<int, Dictionary<string, string>> CreateButtons
        {
            get
            {
                if (uiButtons == null)
                {
                    uiButtons = new Dictionary<int, Dictionary<string, string>>();

                    int amount = uiBtnNames.Length;
                    double anchorMin = amount > 12 ? 0.011 : 0.017;
                    double anchorMax = amount > 12 ? 0.675 : 0.739;
                    double offsetMin = amount > 12 ? 0.275 : 0.331;
                    double offsetMax = amount > 12 ? 0.957 : 0.957;
                    double defaultAnchorMax = anchorMax;
                    double defaultOffsetMax = offsetMax;
                    int rowMax = 4;

                    for (int count = 0; count < amount; count++)
                    {
                        if (count > 0 && count % rowMax == 0)
                        {
                            anchorMax = defaultAnchorMax;
                            offsetMax = defaultOffsetMax;
                            anchorMin += (amount > 12 ? 0.280 : 0.326);
                            offsetMin += (amount > 12 ? 0.280 : 0.326);
                        }

                        uiButtons.Add(count, new Dictionary<string, string>()
                        {
                            ["Anchor"] = $"{anchorMin} {anchorMax}",
                            ["Offset"] = $"{offsetMin} {offsetMax}",
                        });

                        anchorMax -= (amount > 12 ? 0.329 : 0.239);
                        offsetMax -= (amount > 12 ? 0.329 : 0.239);
                    }
                }

                return uiButtons;
            }
        }

        public class UI // Credit: Absolut
        {
            public static CuiElementContainer CreateElementContainer(string panelName, string color, string aMin, string aMax, bool cursor = false, string parent = "Overlay")
            {
                var NewElement = new CuiElementContainer
                {
                    {
                        new CuiPanel
                        {
                            Image =
                            {
                                Color = color
                            },
                            RectTransform =
                            {
                                AnchorMin = aMin,
                                AnchorMax = aMax
                            },
                            CursorEnabled = cursor
                        },
                        new CuiElement().Parent = parent,
                        panelName
                    }
                };
                return NewElement;
            }

            public static void CreateButton(ref CuiElementContainer container, string panel, string color, string text, int size, string aMin, string aMax, string command, TextAnchor align = TextAnchor.MiddleCenter, string labelColor = "")
            {
                container.Add(new CuiButton
                {
                    Button =
                        {
                            Color = color,
                            Command = command,
                            FadeIn = 1.0f
                        },
                    RectTransform =
                        {
                            AnchorMin = aMin,
                            AnchorMax = aMax
                        },
                    Text =
                        {
                            Text = text,
                            FontSize = size,
                            Align = align,
                            Color = labelColor
                        }
                },
                    panel);
            }
        }

        #endregion

        #region Config

        private bool Changed;
        private static bool barebonesMode;
        private static int averagePingInterval;
        private static bool drawText = true;
        private static bool drawBox;
        private static bool drawArrows;
        private static string colorDrawArrows;
        private static bool drawX;
        private static int authLevel;
        private static float defaultInvokeTime;
        private static float defaultMaxDistance;

        private static float mcDistance;
        private static float carDistance;
        private static float boatDistance;
        private static float adDistance;
        private static float boxDistance;
        private static float playerDistance;
        private static float npcPlayerDistance;
        private static float tcDistance;
        private static float tcArrowsDistance;
        private static float stashDistance;
        private static float corpseDistance;
        private static float oreDistance;
        private static float rhDistance;
        private static float lootDistance;
        private static float colDistance;
        private static float bagDistance;
        private static float npcDistance;
        private static float turretDistance;
        private static float latencyMs;
        private static int objectsLimit;
        private static bool showLootContents;
        private static bool showAirdropContents;
        private static bool showStashContents;
        private static bool drawEmptyContainers;
        private static bool showResourceAmounts;
        private static bool showHeliRotorHealth;
        private static int backpackContentAmount;
        private static int corpseContentAmount;
        private static int groupLimit;
        private static float groupRange;
        private static float groupCountHeight;
        private static int inactiveSeconds;
        private static int inactiveMinutes;
        private static bool showUI;
        private static bool showTCAuthedCount;
        private static bool showTCBagCount;
        
        private static string distCC;
        private static string heliCC;
        private static string bradleyCC;
        private static string activeCC;
        private static string activeDeadCC;
        private static string corpseCC;
        private static string sleeperCC;
        private static string sleeperDeadCC;
        private static string healthCC;
        private static string backpackCC;
        private static string zombieCC;
        private static string scientistCC;
        private static string peacekeeperCC;
        private static string htnscientistCC;
        private static string murdererCC;
        private static string npcCC;
        private static string resourceCC;
        private static string colCC;
        private static string tcCC;
        private static string bagCC;
        private static string airdropCC;
        private static string atCC;
        private static string boxCC;
        private static string lootCC;
        private static string stashCC;
        private static string groupColorDead;
        private static string groupColorBasic;
        private string uiColorOn;
        private string uiColorOff;

        private static string szChatCommand;
        private static string szSecondaryCommand;
        private static List<object> authorized = new List<object>();
        private static List<string> itemExceptions = new List<string>();

        private static bool trackActive = true; // default tracking
        private static bool trackBags = true;
        private static bool trackBox = true;
        private static bool trackCollectibles = true;
        private static bool trackDead = true;
        private static bool trackLoot = true;
        private static bool trackNPC = true;
        private static bool trackOre = true;
        private static bool trackSleepers = true;
        private static bool trackStash = true;
        private static bool trackSupplyDrops = true;
        private static bool trackTC = true;
        private static bool trackTurrets = true;
        
        private static bool trackMiniCopter; // additional tracking
        private static bool trackHeli;
        private static bool trackBradley;
        private static bool trackCars;
        private static bool trackCargoPlanes;
        private static bool trackCargoShips;
        private static bool trackCH47;
        private static bool trackRidableHorses;
        private static bool trackRigidHullInflatableBoats;
        private static bool trackBoats;

        private const string anchorMinDefault = "0.667 0.020";
        private const string anchorMaxDefault = "0.810 0.148";
        private string anchorMin;
        private string anchorMax;
        private static bool uiBtnBags;
        private static bool uiBtnBoats;
        private static bool uiBtnBox;
        private static bool uiBtnBradley;
        private static bool uiBtnCars;
        private static bool uiBtnCargoPlanes;
        private static bool uiBtnCargoShips;
        private static bool uiBtnCH47;
        private static bool uiBtnCollectible;
        private static bool uiBtnDead;
        private static bool uiBtnHeli;
        private static bool uiBtnLoot;
        private static bool uiBtnMiniCopter;
        private static bool uiBtnNPC;
        private static bool uiBtnOre;
        private static bool uiBtnRidableHorses;
        private static bool uiBtnRHIB;
        private static bool uiBtnSleepers;
        private static bool uiBtnStash;
        private static bool uiBtnTC;
        private static bool uiBtnTCArrow;
        private static bool uiBtnTurrets;

        //static string voiceSymbol;
        private static bool useVoiceDetection;
        private static int voiceInterval;
        private static float voiceDistance;
        private static bool skipUnderworld;
        private static bool blockDamageBuildings;
        private static bool blockDamageAnimals;
        private static bool blockDamagePlayers;
        private static bool blockDamageNpcs;
        private static bool blockDamageOther;
        private static float coolDown;

        private List<object> ItemExceptions
        {
            get
            {
                return new List<object> { "bottle", "planner", "rock", "torch", "can.", "arrow." };
            }
        }

        private static bool useGroupColors;
        private static readonly Dictionary<int, string> groupColors = new Dictionary<int, string>();

        private static string GetGroupColor(int index)
        {
            if (useGroupColors && groupColors.ContainsKey(index))
                return groupColors[index];

            return groupColorBasic;
        }

        private void SetupGroupColors(List<object> list)
        {
            if (list != null && list.Count > 0)
            {
                groupColors.Clear();

                Dictionary<string, object> dict;
                string value;
                int key;

                foreach (var entry in list)
                {
                    if (entry is Dictionary<string, object>)
                    {
                        dict = (Dictionary<string, object>)entry;

                        foreach (var kvp in dict)
                        {
                            key = 0;
                            if (int.TryParse(kvp.Key, out key))
                            {
                                value = kvp.Value.ToString();

                                if (__(value) == Color.red)
                                {
                                    if (__(activeDeadCC) == Color.red || __(sleeperDeadCC) == Color.red)
                                    {
                                        groupColors[key] = "#FF00FF"; // magenta
                                        continue;
                                    }
                                }

                                if (IsHex(value))
                                {
                                    value = "#" + value;
                                }

                                groupColors[key] = value;
                            }
                        }
                    }
                }
            }
        }

        private List<object> DefaultGroupColors
        {
            get
            {
                return new List<object>
                {
                    new Dictionary<string, object>
                    {
                        ["0"] = "#FF00FF", // magenta
                        ["1"] = "#008000", // green
                        ["2"] = "#0000FF", // blue
                        ["3"] = "#FFA500", // orange
                        ["4"] = "#FFFF00" // yellow
                    }
                };
            }
        }

        private Dictionary<string, string> hexColors = new Dictionary<string, string>
        {
            ["<color=blue>"] = "<color=#0000FF>",
            ["<color=red>"] = "<color=#FF0000>",
            ["<color=yellow>"] = "<color=#FFFF00>",
            ["<color=lightblue>"] = "<color=#ADD8E6>",
            ["<color=orange>"] = "<color=#FFA500>",
            ["<color=silver>"] = "<color=#C0C0C0>",
            ["<color=magenta>"] = "<color=#FF00FF>",
            ["<color=green>"] = "<color=#008000>",
        };

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NotAllowed"] = "You are not allowed to use this command.",
                ["PreviousFilter"] = "To use your previous filter type <color=#FFA500>/{0} f</color>",
                ["Activated"] = "ESP Activated - {0}s refresh - {1}m distance. Use <color=#FFA500>/{2} help</color> for help.",
                ["Deactivated"] = "ESP Deactivated.",
                ["DoESP"] = "DoESP() took {0}ms (max: {1}ms) to execute!",
                ["Exception"] = "ESP Tool: An error occured. Please check the server console.",
                ["GUIShown"] = "GUI will be shown",
                ["GUIHidden"] = "GUI will now be hidden",
                ["InvalidID"] = "{0} is not a valid steam id. Entry removed.",
                ["BoxesAll"] = "Now showing all boxes.",
                ["BoxesOnlineOnly"] = "Now showing online player boxes only.",
                ["Help1"] = "<color=#FFA500>Available Filters</color>: {0}",
                ["Help2"] = "<color=#FFA500>/{0} {1}</color> - Toggles showing online players boxes only when using the <color=#FF0000>box</color> filter.",
                ["Help3"] = "<color=#FFA500>/{0} {1}</color> - Toggles quick toggle UI on/off",
                ["Help5"] = "e.g: <color=#FFA500>/{0} 1 1000 box loot stash</color>",
                ["Help6"] = "e.g: <color=#FFA500>/{0} 0.5 400 all</color>",
                ["VisionOn"] = "You will now see where players are looking.",
                ["VisionOff"] = "You will no longer see where players are looking.",
                ["ExtendedPlayersOn"] = "Extended information for players is now on.",
                ["ExtendedPlayersOff"] = "Extended information for players is now off.",
                ["Help7"] = "<color=#FFA500>/{0} {1}</color> - Toggles showing where players are looking.",
                ["Help8"] = "<color=#FFA500>/{0} {1}</color> - Toggles extended information for players.",
                ["backpack"] = "backpack",
                ["scientist"] = "scientist",
                ["npc"] = "npc",
                ["NoDrops"] = "No item drops found within {0}m",
                ["Help9"] = "<color=#FFA500>/{0} drops</color> - Show all dropped items within {1}m.",
                ["Zombie"] = "<color=#FF0000>Zombie</color>",
                ["NoActiveRadars"] = "No one is using Radar at the moment.",
                ["ActiveRadars"] = "Active radar users: {0}",
                ["All"] = "All",
                ["Bags"] = "Bags",
                ["Box"] = "Box",
                ["Collectibles"] = "Collectibles",
                ["Dead"] = "Dead",
                ["Loot"] = "Loot",
                ["NPC"] = "NPC",
                ["Ore"] = "Ore",
                ["Sleepers"] = "Sleepers",
                ["Stash"] = "Stash",
                ["TC"] = "TC",
                ["Turrets"] = "Turrets",
                ["bear"] = "Bear",
                ["boar"] = "Boar",
                ["chicken"] = "Chicken",
                ["wolf"] = "Wolf",
                ["stag"] = "Stag",
                ["horse"] = "Horse",
                ["My Base"] = "My Base",
                ["scarecrow"] = "scarecrow",
                ["murderer"] = "murderer",
                ["CantDamageBuilds"] = "You can't damage buildings while using radar",
                ["CantHurtAnimals"] = "You can't hurt animals while using radar",
                ["CantHurtPlayers"] = "You can't hurt players while using radar",
                ["CantHurtNpcs"] = "You can't hurt npcs while using radar",
                ["CantHurtOther"] = "You can't hurt this while using radar",
                ["WaitCooldown"] = "You must wait {0} seconds to use this command again."
            }, this);
        }

        private void LoadVariables()
        {
            barebonesMode = Convert.ToBoolean(GetConfig("Settings", "Barebones Performance Mode", false));
            authorized = GetConfig("Settings", "Restrict Access To Steam64 IDs", new List<object>()) as List<object>;

            foreach (var auth in authorized)
            {
                if (auth == null || !auth.ToString().IsSteamId())
                {
                    PrintWarning(msg("InvalidID", null, auth == null ? "null" : auth.ToString()));
                }
            }

            authLevel = authorized.Count == 0 ? Convert.ToInt32(GetConfig("Settings", "Restrict Access To Auth Level", 1)) : int.MaxValue;
            defaultMaxDistance = Convert.ToSingle(GetConfig("Settings", "Default Distance", 500.0));
            defaultInvokeTime = Convert.ToSingle(GetConfig("Settings", "Default Refresh Time", 5.0));
            latencyMs = Convert.ToInt32(GetConfig("Settings", "Latency Cap In Milliseconds (0 = no cap)", 1000.0));
            objectsLimit = Convert.ToInt32(GetConfig("Settings", "Objects Drawn Limit (0 = unlimited)", 250));
            var exceptions = GetConfig("Settings", "Dropped Item Exceptions", ItemExceptions) as List<object>;

            foreach(var exception in exceptions)
            {
                itemExceptions.Add(exception.ToString());
            }

            inactiveSeconds = Convert.ToInt32(GetConfig("Settings", "Deactivate Radar After X Seconds Inactive", 300));
            inactiveMinutes = Convert.ToInt32(GetConfig("Settings", "Deactivate Radar After X Minutes", 0));
            showUI = Convert.ToBoolean(GetConfig("Settings", "User Interface Enabled", true));
            averagePingInterval = Convert.ToInt32(GetConfig("Settings", "Show Average Ping Every X Seconds [0 = disabled]", 0));
            coolDown = Convert.ToSingle(GetConfig("Settings", "Re-use Cooldown, Seconds", 0f));

            blockDamageAnimals = Convert.ToBoolean(GetConfig("When Radar Is Active", "Block Damage To Animals", false));
            blockDamageBuildings = Convert.ToBoolean(GetConfig("When Radar Is Active", "Block Damage To Buildings", false));
            blockDamageNpcs = Convert.ToBoolean(GetConfig("When Radar Is Active", "Block Damage To Npcs", false));
            blockDamagePlayers = Convert.ToBoolean(GetConfig("When Radar Is Active", "Block Damage To Players", false));
            blockDamageOther = Convert.ToBoolean(GetConfig("When Radar Is Active", "Block Damage To Everything Else", false));

            showLootContents = Convert.ToBoolean(GetConfig("Options", "Show Barrel And Crate Contents", false));
            showAirdropContents = Convert.ToBoolean(GetConfig("Options", "Show Airdrop Contents", false));
            showStashContents = Convert.ToBoolean(GetConfig("Options", "Show Stash Contents", false));
            drawEmptyContainers = Convert.ToBoolean(GetConfig("Options", "Draw Empty Containers", true));
            showResourceAmounts = Convert.ToBoolean(GetConfig("Options", "Show Resource Amounts", true));
            backpackContentAmount = Convert.ToInt32(GetConfig("Options", "Show X Items In Backpacks [0 = amount only]", 3));
            corpseContentAmount = Convert.ToInt32(GetConfig("Options", "Show X Items On Corpses [0 = amount only]", 0));
            skipUnderworld = Convert.ToBoolean(GetConfig("Options", "Only Show NPCPlayers At World View", false));
            showTCAuthedCount = Convert.ToBoolean(GetConfig("Options", "Show Authed Count On Cupboards", true));
            showTCBagCount = Convert.ToBoolean(GetConfig("Options", "Show Bag Count On Cupboards", true));
            
            drawArrows = Convert.ToBoolean(GetConfig("Drawing Methods", "Draw Arrows On Players", false));
            drawBox = Convert.ToBoolean(GetConfig("Drawing Methods", "Draw Boxes", false));
            drawText = Convert.ToBoolean(GetConfig("Drawing Methods", "Draw Text", true));

            drawX = Convert.ToBoolean(GetConfig("Group Limit", "Draw Distant Players With X", true));
            groupLimit = Convert.ToInt32(GetConfig("Group Limit", "Limit", 4));
            groupRange = Convert.ToSingle(GetConfig("Group Limit", "Range", 50f));
            groupCountHeight = Convert.ToSingle(GetConfig("Group Limit", "Height Offset [0.0 = disabled]", 40f));
            
            mcDistance = Convert.ToSingle(GetConfig("Drawing Distances", "MiniCopter", 200f));
            boatDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Boats", 150f));
            carDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Cars", 500f));
            adDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Airdrop Crates", 400f));
            npcDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Animals", 200));
            bagDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Sleeping Bags", 250));
            boxDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Boxes", 100));
            colDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Collectibles", 100));
            corpseDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Player Corpses", 200));
            playerDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Players", 500));
            npcPlayerDistance = Convert.ToSingle(GetConfig("Drawing Distances", "NPC Players", 300));
            lootDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Loot Containers", 150));
            oreDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Resources (Ore)", 200));
            rhDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Ridable Horses", 250));
            stashDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Stashes", 250));
            tcDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Tool Cupboards", 150));
            tcArrowsDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Tool Cupboard Arrows", 250));
            turretDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Turrets", 100));

            trackBradley = Convert.ToBoolean(GetConfig("Additional Tracking", "Bradley APC", true));
            trackCars = Convert.ToBoolean(GetConfig("Additional Tracking", "Cars", false));
            trackCargoPlanes = Convert.ToBoolean(GetConfig("Additional Tracking", "CargoPlanes", false));
            trackCargoShips = Convert.ToBoolean(GetConfig("Additional Tracking", "CargoShips", false));
            trackMiniCopter = Convert.ToBoolean(GetConfig("Additional Tracking", "MiniCopter", false));
            trackHeli = Convert.ToBoolean(GetConfig("Additional Tracking", "Helicopters", true));
            showHeliRotorHealth = Convert.ToBoolean(GetConfig("Additional Tracking", "Helicopter Rotor Health", false));
            trackCH47 = Convert.ToBoolean(GetConfig("Additional Tracking", "CH47", false));
            trackRidableHorses = Convert.ToBoolean(GetConfig("Additional Tracking", "Ridable Horses", false));
            trackRigidHullInflatableBoats = Convert.ToBoolean(GetConfig("Additional Tracking", "RHIB", false));
            trackBoats = Convert.ToBoolean(GetConfig("Additional Tracking", "Boats", false));

            colorDrawArrows = Convert.ToString(GetConfig("Color-Hex Codes", "Player Arrows", "#000000"));
            distCC = Convert.ToString(GetConfig("Color-Hex Codes", "Distance", "#ffa500"));
            heliCC = Convert.ToString(GetConfig("Color-Hex Codes", "Helicopters", "#ff00ff"));
            bradleyCC = Convert.ToString(GetConfig("Color-Hex Codes", "Bradley", "#ff00ff"));
            activeCC = Convert.ToString(GetConfig("Color-Hex Codes", "Online Player", "#ffffff"));
            activeDeadCC = Convert.ToString(GetConfig("Color-Hex Codes", "Online Dead Player", "#ff0000"));
            sleeperCC = Convert.ToString(GetConfig("Color-Hex Codes", "Sleeping Player", "#00ffff"));
            sleeperDeadCC = Convert.ToString(GetConfig("Color-Hex Codes", "Sleeping Dead Player", "#ff0000"));
            healthCC = Convert.ToString(GetConfig("Color-Hex Codes", "Health", "#ff0000"));
            backpackCC = Convert.ToString(GetConfig("Color-Hex Codes", "Backpacks", "#c0c0c0"));
            zombieCC = Convert.ToString(GetConfig("Color-Hex Codes", "Zombies", "#ff0000"));
            scientistCC = Convert.ToString(GetConfig("Color-Hex Codes", "Scientists", "#ffff00"));
            peacekeeperCC = Convert.ToString(GetConfig("Color-Hex Codes", "Scientist Peacekeeper", "#ffff00"));
            htnscientistCC = Convert.ToString(GetConfig("Color-Hex Codes", "Scientist HTN", "#ff00ff"));
            murdererCC = Convert.ToString(GetConfig("Color-Hex Codes", "Murderers", "#000000"));
            npcCC = Convert.ToString(GetConfig("Color-Hex Codes", "Animals", "#0000ff"));
            resourceCC = Convert.ToString(GetConfig("Color-Hex Codes", "Resources", "#ffff00"));
            colCC = Convert.ToString(GetConfig("Color-Hex Codes", "Collectibles", "#ffff00"));
            tcCC = Convert.ToString(GetConfig("Color-Hex Codes", "Tool Cupboards", "#000000"));
            bagCC = Convert.ToString(GetConfig("Color-Hex Codes", "Sleeping Bags", "#ff00ff"));
            airdropCC = Convert.ToString(GetConfig("Color-Hex Codes", "Airdrops", "#ff00ff"));
            atCC = Convert.ToString(GetConfig("Color-Hex Codes", "AutoTurrets", "#ffff00"));
            corpseCC = Convert.ToString(GetConfig("Color-Hex Codes", "Corpses", "#ffff00"));
            boxCC = Convert.ToString(GetConfig("Color-Hex Codes", "Box", "#ff00ff"));
            lootCC = Convert.ToString(GetConfig("Color-Hex Codes", "Loot", "#ffff00"));
            stashCC = Convert.ToString(GetConfig("Color-Hex Codes", "Stash", "#ffffff"));

            anchorMin = Convert.ToString(GetConfig("GUI", "Anchor Min", "0.667 0.020"));
            anchorMax = Convert.ToString(GetConfig("GUI", "Anchor Max", "0.810 0.148"));
            uiColorOn = Convert.ToString(GetConfig("GUI", "Color On", "0.69 0.49 0.29 0.5"));
            uiColorOff = Convert.ToString(GetConfig("GUI", "Color Off", "0.29 0.49 0.69 0.5"));
            uiBtnBags = Convert.ToBoolean(GetConfig("GUI", "Show Button - Bags", true));
            uiBtnBoats = Convert.ToBoolean(GetConfig("GUI", "Show Button - Boats", false));
            uiBtnBradley = Convert.ToBoolean(GetConfig("GUI", "Show Button - Bradley", false));
            uiBtnBox = Convert.ToBoolean(GetConfig("GUI", "Show Button - Box", true));
            uiBtnCars = Convert.ToBoolean(GetConfig("GUI", "Show Button - Cars", false));
            uiBtnCargoPlanes = Convert.ToBoolean(GetConfig("GUI", "Show Button - CargoPlanes", false));
            uiBtnCargoShips = Convert.ToBoolean(GetConfig("GUI", "Show Button - CargoShips", false));
            uiBtnCH47 = Convert.ToBoolean(GetConfig("GUI", "Show Button - CH47", false));
            uiBtnCollectible = Convert.ToBoolean(GetConfig("GUI", "Show Button - Collectibles", true));
            uiBtnDead = Convert.ToBoolean(GetConfig("GUI", "Show Button - Dead", true));
            uiBtnHeli = Convert.ToBoolean(GetConfig("GUI", "Show Button - Heli", false));
            uiBtnLoot = Convert.ToBoolean(GetConfig("GUI", "Show Button - Loot", true));
            uiBtnMiniCopter = Convert.ToBoolean(GetConfig("GUI", "Show Button - MiniCopter", false));
            uiBtnNPC = Convert.ToBoolean(GetConfig("GUI", "Show Button - NPC", true));
            uiBtnOre = Convert.ToBoolean(GetConfig("GUI", "Show Button - Ore", true));
            uiBtnRidableHorses = Convert.ToBoolean(GetConfig("GUI", "Show Button - Ridable Horses", false));
            uiBtnRHIB = Convert.ToBoolean(GetConfig("GUI", "Show Button - RigidHullInflatableBoats", false));
            uiBtnSleepers = Convert.ToBoolean(GetConfig("GUI", "Show Button - Sleepers", true));
            uiBtnStash = Convert.ToBoolean(GetConfig("GUI", "Show Button - Stash", true));
            uiBtnTC = Convert.ToBoolean(GetConfig("GUI", "Show Button - TC", true));
            uiBtnTCArrow = Convert.ToBoolean(GetConfig("GUI", "Show Button - TC Arrow", true));
            uiBtnTurrets = Convert.ToBoolean(GetConfig("GUI", "Show Button - Turrets", true));

            if (!anchorMin.Contains(" ")) anchorMin = anchorMinDefault;
            if (!anchorMax.Contains(" ")) anchorMax = anchorMaxDefault;
            if (uiBtnBoats) trackBoats = true;
            if (uiBtnBradley) trackBradley = true;
            if (uiBtnCars) trackCars = true;
            if (uiBtnCargoPlanes) trackCargoPlanes = true;
            if (uiBtnCargoShips) trackCargoShips = true;
            if (uiBtnCH47) trackCH47 = true;
            if (uiBtnHeli) trackHeli = true;
            if (uiBtnMiniCopter) trackMiniCopter = true;
            if (uiBtnRidableHorses) trackRidableHorses = true;
            if (uiBtnRHIB) trackRigidHullInflatableBoats = true;

            useGroupColors = Convert.ToBoolean(GetConfig("Group Limit", "Use Group Colors Configuration", true));
            groupColorDead = Convert.ToString(GetConfig("Group Limit", "Dead Color", "#ff0000"));
            groupColorBasic = Convert.ToString(GetConfig("Group Limit", "Group Color Basic", "#ffff00"));

            var list = GetConfig("Group Limit", "Group Colors", DefaultGroupColors) as List<object>;

            if (list != null && list.Count > 0)
            {
                SetupGroupColors(list);
            }

            szChatCommand = Convert.ToString(GetConfig("Settings", "Chat Command", "radar"));
            szSecondaryCommand = Convert.ToString(GetConfig("Settings", "Second Command", "radar"));

            if (!string.IsNullOrEmpty(szChatCommand))
                cmd.AddChatCommand(szChatCommand, this, cmdESP);

            if (!string.IsNullOrEmpty(szSecondaryCommand) && szChatCommand != szSecondaryCommand)
                cmd.AddChatCommand(szSecondaryCommand, this, cmdESP);

            //voiceSymbol = Convert.ToString(GetConfig("Voice Detection", "Voice Symbol", "🔊"));
            useVoiceDetection = Convert.ToBoolean(GetConfig("Voice Detection", "Enabled", true));
            voiceInterval = Convert.ToInt32(GetConfig("Voice Detection", "Timeout After X Seconds", 3));
            voiceDistance = Convert.ToSingle(GetConfig("Voice Detection", "Detection Radius", 30f));

            if (voiceInterval < 1)
                useVoiceDetection = false;

            if (Changed)
            {
                SaveConfig();
                Changed = false;
            }
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            Config.Clear();
            LoadVariables();
        }

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                Changed = true;
            }
            object value;
            if (!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                Changed = true;
            }

            return value;
        }

        private string msg(string key, string id = null, params object[] args)
        {
            var sb = new StringBuilder(id == null ? RemoveFormatting(lang.GetMessage(key, this, id)) : lang.GetMessage(key, this, id));

            foreach(var entry in hexColors)
            {
                if (sb.ToString().Contains(entry.Key))
                {
                    sb.Replace(entry.Key, entry.Value);
                }
            }
            
            return args.Length > 0 ? string.Format(sb.ToString(), args) : sb.ToString();
        }

        private string RemoveFormatting(string source)
        {
            return source.Contains(">") ? Regex.Replace(source, "<.*?>", string.Empty) : source;
        }

        #endregion
    }
}