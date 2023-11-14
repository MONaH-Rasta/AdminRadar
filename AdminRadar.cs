//#define DEBUG
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using Oxide.Game.Rust.Libraries;
using Oxide.Plugins.AdminRadarExtensionMethods;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Admin Radar", "nivex", "5.3.1")]
    [Description("Radar tool for Admins and Developers.")]
    internal class AdminRadar : RustPlugin
    {
        [PluginReference] Plugin Clans, Backpacks, DiscordMessages;

        [Flags] public enum DrawFlags { None = 0, Arrow = 1 << 1, Box = 1 << 2, Text = 1 << 3, }
        public enum EntityType { Active, Airdrop, Bag, Backpack, Boat, Bradley, Car, CargoPlane, CargoShip, CCTV, CH47, Box, Col, TC, TCArrow, Dead, Limit, Loot, Heli, Mini, MLRS, Npc, Ore, Horse, RHIB, Sleeper, Stash, Trap, Turret }
        private List<string> _tags = new List<string> { "ore", "cluster", "1", "2", "3", "4", "5", "6", "_", ".", "-", "deployed", "wooden", "large", "pile", "prefab", "collectable", "loot", "small" };
        private List<EntityType> _errorTypes = new List<EntityType>();
        private List<Radar> _radars = new List<Radar>();
        private List<BaseEntity> _spawnedEntities = new List<BaseEntity>();
        private Dictionary<NetworkableId, Vector3> _despawnedEntities = new Dictionary<NetworkableId, Vector3>();
        private Dictionary<NetworkableId, BaseEntity> _allEntities = new Dictionary<NetworkableId, BaseEntity>();
        private Dictionary<string, float> _cooldowns = new Dictionary<string, float>();
        private Dictionary<ulong, string> _clans = new Dictionary<ulong, string>();
        private Dictionary<ulong, string> _teamColors = new Dictionary<ulong, string>();
        private Dictionary<string, string> _clanColors = new Dictionary<string, string>();
        private Dictionary<ulong, Timer> _voices = new Dictionary<ulong, Timer>();
        private Array _allEntityTypes = Enum.GetValues(typeof(EntityType));
        private CoroutineTimer _coroutineTimer = new CoroutineTimer(1.0f);
        private Stack<Coroutine> _coroutines = new Stack<Coroutine>();
        private StoredData data = new StoredData();
        private const bool True = true;
        private const bool False = false;
        private bool _isPopulatingCache;
        private bool isUnloading;
        private Cache cache;

        private class StoredData
        {
            public readonly Dictionary<ulong, UiOffsets> Offsets = new Dictionary<ulong, UiOffsets>();
            public readonly List<string> Extended = new List<string>();
            public readonly Dictionary<string, List<string>> Filters = new Dictionary<string, List<string>>();
            public readonly List<string> Hidden = new List<string>();
            public readonly List<string> OnlineBoxes = new List<string>();
            public readonly List<string> Visions = new List<string>();
            public readonly List<string> Active = new List<string>();
            public StoredData() { }
        }

        private class Cache
        {
            public Cache(AdminRadar instance)
            {
                config = instance.config;
                this.instance = instance;
            }

            public Configuration config;
            public AdminRadar instance;
            internal EntityType entityType;
            public Dictionary<NetworkableId, EntityInfo> Airdrops { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Animals { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Backpacks { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Bags { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Boats { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> BradleyAPCs { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> CargoPlanes { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> CargoShips { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Cars { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> CCTV { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> CH47 { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Cupboards { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Collectibles { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Containers { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Corpses { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Helicopters { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> MiniCopter { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> MLRS { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> NPCPlayers { get; set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Ores { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> RHIB { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> RidableHorse { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Turrets { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();
            public Dictionary<NetworkableId, EntityInfo> Traps { get; private set; } = new Dictionary<NetworkableId, EntityInfo>();

            public bool Add(BaseEntity entity)
            {
                if (entity.IsKilled())
                {
                    return False;
                }
                if (entity is BasePlayer)
                {
                    var player = entity as BasePlayer;
                    Interface.Oxide.NextTick(() =>
                    {
                        if (!player.IsValid() || player.IsDestroyed)
                        {
                            return;
                        }
                        if (player.userID.IsSteamId())
                        {
                            instance._radars.ForEach(radar => radar.TryCacheOnlinePlayer(player));
                        }
                        else if (config.Core.NPCPlayer)
                        {
                            EntityInfo ei;
                            NPCPlayers[entity.net.ID] = ei = new EntityInfo(player, EntityType.Npc, config.Distance.Get);
                            instance._radars.ForEach(radar => radar.TryCacheByType(EntityType.Npc, ei));
                        }
                    });
                    return False;
                }
                if (config.Core.Dead && entity is PlayerCorpse)
                {
                    PlayerCorpse corpse = entity as PlayerCorpse;
                    Interface.Oxide.NextTick(() =>
                    {
                        if (corpse.IsValid() && !corpse.IsDestroyed && corpse.playerSteamID.IsSteamId())
                        {
                            EntityInfo ei;
                            Corpses[entity.net.ID] = ei = new EntityInfo(entity, EntityType.Dead, config.Distance.Get);
                            ei.name = corpse.parentEnt?.ToString() ?? corpse.playerSteamID.ToString();
                            instance._radars.ForEach(radar => radar.TryCacheByType(EntityType.Dead, ei));
                        }
                    });
                    return False;
                }
                if (config.Additional.Traps && IsTrap(entity) && Add_Internal<BaseEntity, EntityInfo>(Traps, entity, EntityType.Trap))
                {
                    return True;
                }
                if (config.Additional.RH && Add_Internal<RidableHorse, EntityInfo>(RidableHorse, entity, EntityType.Horse))
                {
                    return True;
                }
                if (config.Core.Animals && Add_Internal<BaseNpc, EntityInfo>(Animals, entity, EntityType.Npc))
                {
                    return True;
                }
                if (config.Core.Animals && Add_Internal<SimpleShark, EntityInfo>(Animals, entity, EntityType.Npc))
                {
                    return True;
                }
                if ((config.Core.Loot || config.Core.Box) && entity is StorageContainer && TryGetContainerType(entity, out entityType) && Add_Internal<StorageContainer, EntityInfo>(Containers, entity, entityType))
                {
                    return True;
                }
                if (config.Core.Col && entity is CollectibleEntity && Add_Internal<CollectibleEntity, EntityInfo>(Collectibles, entity, EntityType.Col))
                {
                    return True;
                }
                if (config.Core.Ore && entity is OreResourceEntity && Add_Internal<OreResourceEntity, EntityInfo>(Ores, entity, EntityType.Ore))
                {
                    return True;
                }
                if (config.Additional.Cars && Add_Internal<BasicCar, EntityInfo>(Cars, entity, EntityType.Car) || Add_Internal<ModularCar, EntityInfo>(Cars, entity, EntityType.Car))
                {
                    return True;
                }
                if (config.Additional.CP && entity.prefabID == 2383782438 && Add_Internal<BaseEntity, EntityInfo>(CargoPlanes, entity, EntityType.CargoPlane))
                {
                    return True;
                }
                if (config.Core.Bags && Add_Internal<SleepingBag, EntityInfo>(Bags, entity, EntityType.Bag))
                {
                    return True;
                }
                if (config.Core.TC && Add_Internal<BuildingPrivlidge, EntityInfo>(Cupboards, entity, EntityType.TC))
                {
                    return True;
                }
                if (config.Additional.CCTV && Add_Internal<CCTV_RC, EntityInfo>(CCTV, entity, EntityType.CCTV))
                {
                    return True;
                }
                if (config.Core.Airdrop && Add_Internal<SupplyDrop, EntityInfo>(Airdrops, entity, EntityType.Airdrop))
                {
                    return True;
                }
                if (config.Core.Loot && Add_Internal<DroppedItemContainer, EntityInfo>(Backpacks, entity, EntityType.Backpack))
                {
                    return True;
                }
                if (config.Additional.Heli && Add_Internal<BaseHelicopter, EntityInfo>(Helicopters, entity, EntityType.Heli))
                {
                    return True;
                }
                if (config.Additional.Bradley && Add_Internal<BradleyAPC, EntityInfo>(BradleyAPCs, entity, EntityType.Bradley))
                {
                    return True;
                }
                if (config.Additional.RHIB && Add_Internal<RHIB, EntityInfo>(RHIB, entity, EntityType.RHIB))
                {
                    return True;
                }
                if (config.Additional.Boats && Add_Internal<BaseBoat, EntityInfo>(Boats, entity, EntityType.Boat))
                {
                    return True;
                }
                if (config.Additional.MC && Add_Internal<MiniCopter, EntityInfo>(MiniCopter, entity, EntityType.Mini))
                {
                    return True;
                }
                if (config.Additional.CH47 && Add_Internal<CH47Helicopter, EntityInfo>(CH47, entity, EntityType.CH47))
                {
                    return True;
                }
                if (config.Additional.CS && Add_Internal<CargoShip, EntityInfo>(CargoShips, entity, EntityType.CargoShip))
                {
                    return True;
                }
                if (config.Core.Turrets && Add_Internal<AutoTurret, EntityInfo>(Turrets, entity, EntityType.Turret))
                {
                    return True;
                }
                if (config.Additional.MLRS && Add_Internal<MLRSRocket, EntityInfo>(MLRS, entity, EntityType.MLRS))
                {
                    return True;
                }
                return False;
            }

            private bool TryGetContainerType(BaseEntity entity, out EntityType type)
            {
                if (entity is LockedByEntCrate || IsLoot(entity)) { type = EntityType.Loot; return True; }
                if (IsBox(entity)) { type = EntityType.Box; return True; }
                type = (EntityType)0;
                return False;
            }

            public bool Remove(NetworkableId nid, Vector3 entityPos)
            {
                instance._radars.ForEach(radar => radar.RemoveByNetworkId(nid));
                if (Remove_Internal(Airdrops, nid)) return True;
                if (Remove_Internal(Animals, nid)) return True;
                if (Remove_Internal(Backpacks, nid)) return True;
                if (Remove_Internal(Bags, nid)) return True;
                if (Remove_Internal(Boats, nid)) return True;
                if (Remove_Internal(BradleyAPCs, nid)) return True;
                if (Remove_Internal(CargoPlanes, nid)) return True;
                if (Remove_Internal(CargoShips, nid)) return True;
                if (Remove_Internal(Cars, nid)) return True;
                if (Remove_Internal(CCTV, nid)) return True;
                if (Remove_Internal(CH47, nid)) return True;
                if (Remove_Internal(Collectibles, nid)) return True;
                if (Remove_Internal(Containers, nid)) return True;
                if (Remove_Internal(Corpses, nid)) return True;
                if (Remove_Internal(Cupboards, nid)) return True;
                if (Remove_Internal(Helicopters, nid)) return True;
                if (Remove_Internal(MiniCopter, nid)) return True;
                if (Remove_Internal(MLRS, nid)) return True;
                if (Remove_Internal(NPCPlayers, nid)) return True;
                if (Remove_Internal(Ores, nid)) return True;
                if (Remove_Internal(RHIB, nid)) return True;
                if (Remove_Internal(RidableHorse, nid)) return True;
                if (Remove_Internal(Traps, nid)) return True;
                if (Remove_Internal(Turrets, nid)) return True;
                return False;
            }

            private bool Add_Internal<TLookFor, TTargetType>(Dictionary<NetworkableId, EntityInfo> cachedList, BaseEntity entity, EntityType type)
            {
                if (entity is TLookFor && entity.net != null && !cachedList.ContainsKey(entity.net.ID))
                {
                    var ei = new EntityInfo(entity, type, config.Distance.Get, instance.StripTags);
                    cachedList.Add(entity.net.ID, ei);
                    instance._radars.ForEach(radar => radar.TryCacheByType(type, ei));
                    return True;
                }
                return False;
            }

            private bool Remove_Internal<TType, TKeyType>(Dictionary<TKeyType, TType> cachedList, TKeyType key)
            {
                return cachedList.Remove(key);
            }

#if DEBUG
            public void Print()
            {
                StringBuilder sb = StringBuilderCache.Acquire();

                sb.AppendLine($"{nameof(Airdrops)}: {Airdrops.Count}");
                sb.AppendLine($"{nameof(Animals)}: {Animals.Count}");
                sb.AppendLine($"{nameof(Backpacks)}: {Backpacks.Count}");
                sb.AppendLine($"{nameof(Bags)}: {Bags.Count}");
                sb.AppendLine($"{nameof(Boats)}: {Boats.Count}");
                sb.AppendLine($"{nameof(BradleyAPCs)}: {BradleyAPCs.Count}");
                sb.AppendLine($"{nameof(CargoPlanes)}: {CargoPlanes.Count}");
                sb.AppendLine($"{nameof(CargoShips)}: {CargoShips.Count}");
                sb.AppendLine($"{nameof(Cars)}: {Cars.Count}");
                sb.AppendLine($"{nameof(CCTV)}: {CCTV.Count}");
                sb.AppendLine($"{nameof(CH47)}: {CH47.Count}");
                sb.AppendLine($"{nameof(Collectibles)}: {Collectibles.Count}");
                sb.AppendLine($"{nameof(Containers)}: {Containers.Count}");
                sb.AppendLine($"{nameof(Corpses)}: {Corpses.Count}");
                sb.AppendLine($"{nameof(Cupboards)}: {Cupboards.Count}");
                sb.AppendLine($"{nameof(Helicopters)}: {Helicopters.Count}");
                sb.AppendLine($"{nameof(MiniCopter)}: {MiniCopter.Count}");
                sb.AppendLine($"{nameof(MLRS)}: {MLRS.Count}");
                sb.AppendLine($"{nameof(NPCPlayers)}: {NPCPlayers.Count}");
                sb.AppendLine($"{nameof(Ores)}: {Ores.Count}");
                sb.AppendLine($"{nameof(RHIB)}: {RHIB.Count}");
                sb.AppendLine($"{nameof(RidableHorse)}: {RidableHorse.Count}");
                sb.AppendLine($"{nameof(Traps)}: {Traps.Count}");
                sb.AppendLine($"{nameof(Turrets)}: {Turrets.Count}");

                instance.Puts(StringBuilderCache.GetStringAndRelease(sb));
            }
#endif

            public bool IsTrap(BaseNetworkable entity)
            {
                return entity is BaseTrap || config.Options.AdditionalTraps.Exists(entity.ShortPrefabName.Contains);
            }

            public bool IsLoot(BaseNetworkable entity)
            {
                if (config.Core.Loot)
                {
                    return entity.ShortPrefabName == "campfire"
                            || entity.ShortPrefabName.Contains("loot", CompareOptions.IgnoreCase)
                            || entity.ShortPrefabName.Contains("crate_", CompareOptions.IgnoreCase)
                            || entity.ShortPrefabName.Contains("trash", CompareOptions.IgnoreCase)
                            || entity.ShortPrefabName.Contains("hackable", CompareOptions.IgnoreCase)
                            || entity.ShortPrefabName.Contains("oil", CompareOptions.IgnoreCase);
                }
                return False;
            }

            public bool IsBox(BaseNetworkable entity)
            {
                if (config.Core.Box)
                {
                    return entity.ShortPrefabName == "vendingmachine.deployed"
                            || entity.ShortPrefabName == "woodbox_deployed"
                            || entity.ShortPrefabName == "box.wooden.large"
                            || entity.ShortPrefabName == "dropbox.deployed"
                            || entity.ShortPrefabName == "coffinstorage"
                            || entity.ShortPrefabName == "small_stash_deployed"
                            || entity.ShortPrefabName == "mailbox.deployed"
                            || entity.ShortPrefabName == "missionstash"
                            || entity.ShortPrefabName.Equals("heli_crate", StringComparison.OrdinalIgnoreCase);
                }
                return False;
            }
        }

        private class CoroutineTimer
        {
            private readonly Stopwatch stopwatch = new Stopwatch();
            private float _maxDurationMs;
            private bool _isRunning;
            //public bool ForceStop { get; set; }
            public double Elapsed => stopwatch.Elapsed.TotalMilliseconds;

            public CoroutineTimer(float maxDurationMs)
            {
                _maxDurationMs = maxDurationMs;
            }

            public void Start()
            {
                stopwatch.Start();
                _isRunning = True;
            }

            public bool ShouldYield()
            {
                _isRunning = stopwatch.Elapsed.TotalMilliseconds < _maxDurationMs;

                if (!_isRunning)
                {
#if DEBUG
                    double milliseconds = Elapsed;
                    if (stopwatch.Elapsed.TotalMilliseconds > 2f * _maxDurationMs)
                    {
                        Interface.Oxide.LogInfo($"[AdminRadar] Time slice took {Elapsed} when it should be around {_maxDurationMs}");
                    }
#endif
                }
                return !_isRunning;
            }

            public void ResetIfYielded()
            {
                if (_isRunning) return;

                stopwatch.Restart();
                _isRunning = True;
            }
        }

        internal static class StringBuilderCache
        {
            [ThreadStatic]
            private static StringBuilder builder;

            public static StringBuilder Acquire(string text = "")
            {
                StringBuilder cached = builder;
                if (cached != null)
                {
                    builder = null;
                    cached.Clear();
                    cached.Append(text);
                    return cached;
                }
                return new StringBuilder(text);
            }

            public static void Clear()
            {
                if (builder != null)
                {
                    builder.Clear();
                    builder = null;
                }
            }

            public static string GetStringAndRelease(StringBuilder sb)
            {
                string result = sb.ToString();
                builder = sb;
                return result;
            }
        }

        public class EntityInfo
        {
            public BuildingPrivlidge priv;
            public BaseEntity entity;
            public EntityType type;
            public Color color;
            public Vector3 _from;
            public Vector3 to;
            public Vector3 offset;
            public string name;
            public object info;
            public float dist;
            public float size = 0.5f;
            public Network.Visibility.Group group => entity?.net?.group;
            public Vector3 from => (IsInvalid ? _from : _from = entity.transform.position) + offset;
            public bool IsInvalid
            {
                get
                {
                    if (entity == null || entity.net == null || entity.IsDestroyed)
                    {
                        return true;
                    }
                    _from = entity.transform.position;
                    return false;
                }
            }
            public EntityInfo() { }
            public EntityInfo(BaseEntity entity, EntityType type, Func<EntityType, BaseEntity, float> getDistance, Func<string, string> stripTags = null)
            {
                if (stripTags != null)
                {
                    name = stripTags(entity.ShortPrefabName);
                }
                if (getDistance != null)
                {
                    dist = getDistance(type, entity);
                }
                if (type == EntityType.TC)
                {
                    priv = entity as BuildingPrivlidge;
                }
                this.type = type;
                this.entity = entity;
                _from = entity.transform.position;
            }
        }

        private class Radar : FacepunchBehaviour
        {
            private static Func<char, bool> abbr = c => char.IsUpper(c) || char.IsDigit(c);

            public class DataObject
            {
                public EntityInfo ei;
                public Action action;
                public DrawFlags flags;
                public bool disabled;
                public DataObject()
                {
                }
                public bool HasFlag(DrawFlags flag)
                {
                    return ((flags & flag) == flag);
                }
                public void SetEnabled(Network.Visibility.Group group, Vector3 to, float max)
                {
                    disabled = ei.group != group && (ei.from - to).magnitude > Mathf.Max(max, ei.dist);
                }
                public bool IsOfType(EntityType type)
                {
                    if (type != EntityType.Loot)
                    {
                        return ei.type == type;
                    }
                    return ei.type == EntityType.Loot || ei.type == EntityType.Backpack;
                }
                public void Reset()
                {
                    ei = null;
                    action = null;
                    disabled = false;
                    flags = DrawFlags.None;
                }
            }

            internal class DistantPlayer
            {
                public Vector3 pos;
                public bool alive;
                public DistantPlayer()
                {
                }
            }

            internal bool setSource = True, canGetExistingBackpacks = True, isEnabled = True, canBypassOverride, hasPermAllowed, isAdmin, showHT, showAll;
            internal int inactiveSeconds, activatedSeconds, checks;
            internal float currDistance, invokeTime, maxDistance;
            internal string username, userid;
            internal RaycastHit hit;
            internal EntityType currType;
            internal Vector3 position;
            internal BaseEntity source;
            internal BasePlayer player;
            internal AdminRadar instance;
            internal Network.Visibility.Group group;
            internal ItemContainer _backpackItemContainer;
            internal Coroutine _radarCo, _updateCo, _groupCo;
            internal List<ulong> exclude = Pool.Get<List<ulong>>();
            internal List<NetworkableId> removeByNetworkId = Pool.Get<List<NetworkableId>>();
            internal List<EntityType> entityTypes = Pool.Get<List<EntityType>>();
            internal List<DistantPlayer> distant = Pool.Get<List<DistantPlayer>>();
            internal List<EntityType> removeByEntityType = Pool.Get<List<EntityType>>();
            internal Dictionary<NetworkableId, DataObject> data = Pool.Get<Dictionary<NetworkableId, DataObject>>();
            internal Dictionary<EntityType, Action> filters = Pool.Get<Dictionary<EntityType, Action>>();
            internal Dictionary<ulong, ItemContainer> backpacks = Pool.Get<Dictionary<ulong, ItemContainer>>();

            internal Cache Cache => instance.cache;
            internal Configuration config => instance.config;
            internal float delay => invokeTime + 0.05f;
            internal float Distance(Vector3 a) => Mathf.CeilToInt((a - position).magnitude);

            public Vector3 limitUp;
            public Vector3 halfUp = new Vector3(0f, 0.5f);
            public Vector3 twoHalfUp = new Vector3(0f, 2.5f);
            public Vector3 twoUp = new Vector3(0f, 2f);
            public Vector3 fiveUp = new Vector3(0f, 5f);

            private void Awake()
            {
                source = player = GetComponent<BasePlayer>();
                isAdmin = player.IsAdmin;
                userid = player.UserIDString;
                username = player.displayName;
                position = player.transform.position;
                exclude.Add(player.userID);
            }

            private void OnDestroy()
            {
                isEnabled = false;
                Interface.CallHook("OnRadarDeactivated", player, username, userid, position);
                if (config.Settings.Cooldown > 0f)
                {
                    instance._cooldowns[userid] = Time.realtimeSinceStartup + config.Settings.Cooldown;
                }
                if (config.Settings.ShowToggle)
                {
                    instance.Message(player, "Deactivated");
                }
                if (instance._radars.Remove(this) && instance._radars.Count == 0 && !instance.isUnloading)
                {
                    instance.Unsubscribe(nameof(OnPlayerRespawned));
                }
                instance.DestroyUI(player);
                ResetToPool();
                StopAll();
            }

            private void ResetToPool()
            {
                for (int i = distant.Count - 1; i >= 0; i--)
                {
                    var obj = distant[i];
                    if (obj != null)
                    {
                        Pool.Free(ref obj);
                    }
                    distant[i] = null;
                }
                for (int i = data.Count - 1; i >= 0; i--)
                {
                    var obj = data.ElementAt(i);
                    if (obj.Value != null)
                    {
                        var value = obj.Value;
                        Pool.Free(ref value);
                    }
                    data[obj.Key] = null;
                }
                data.ResetToPool();
                exclude.ResetToPool();
                distant.ResetToPool();
                filters.ResetToPool();
                backpacks.ResetToPool();
                entityTypes.ResetToPool();
                removeByNetworkId.ResetToPool();
                removeByEntityType.ResetToPool();
            }

            public bool Add(EntityType type)
            {
                if (entityTypes.Contains(type))
                {
                    return False;
                }
                entityTypes.Add(type);
                return True;
            }

            public void Init(AdminRadar instance)
            {
                this.instance = instance;
                this.limitUp = new Vector3(0f, config.Limit.Height);
                this.instance._radars.Add(this);
                canBypassOverride = HasPermission(userid, "adminradar.bypass.override");
                hasPermAllowed = HasPermission(userid, "adminradar.allowed");
                InvokeRepeating(Activity, 0f, 1f);
                Interface.CallHook("OnRadarActivated", player, username, userid, position);
                if (instance._radars.Count == 1)
                {
                    instance.Subscribe(nameof(OnPlayerRespawned));
                }
            }

            public void StopAll()
            {
                if (_radarCo != null)
                {
                    StopCoroutine(_radarCo);
                    _radarCo = null;
                }
                if (_updateCo != null)
                {
                    StopCoroutine(_updateCo);
                    _updateCo = null;
                }
                if (_groupCo != null)
                {
                    StopCoroutine(_groupCo);
                    _groupCo = null;
                }
                try { CancelInvoke(Activity); } catch { }
                try { RemoveAdminFlag(); } catch { }
            }

            public bool GetBool(EntityType type)
            {
                return entityTypes.Contains(type);
            }

            private void Activity()
            {
                inactiveSeconds = position == player.transform.position ? inactiveSeconds + 1 : 0;
                position = source.transform.position;
                if (source != player)
                {
                    inactiveSeconds = 0;
                    return;
                }
                if (config.Settings.DeactivateSeconds > 0 && ++activatedSeconds >= config.Settings.DeactivateSeconds)
                {
                    isEnabled = false;
                    Destroy(this);
                }
                if (config.Settings.InactiveSeconds > 0 && inactiveSeconds >= config.Settings.InactiveSeconds)
                {
                    isEnabled = false;
                    Destroy(this);
                }
            }

            private void SetupFilter(EntityType type, Action action)
            {
                if (entityTypes.Contains(type))
                {
                    if (_radarCo != null)
                    {
                        action();
                    }
                    filters[type] = action;
                }
                else if (filters.Remove(type))
                {
                    RemoveByEntityType(type);
                }
            }

            public void SetupFilters(bool barebones)
            {
                if (_updateCo != null)
                {
                    StopCoroutine(_updateCo);
                }
                SetupFilter(EntityType.Active, ShowActive);
                SetupFilter(EntityType.Sleeper, ShowSleepers);
                if (!barebones)
                {
                    SetupFilters();
                }
                if (_radarCo == null)
                {
                    CheckNetworkGroupChange();

                    _radarCo = StartCoroutine(DoRadarRoutine());
                }
                if (_groupCo == null && config.Limit.Enabled)
                {
                    _groupCo = StartCoroutine(DoGroupLimitRoutine());
                }
                DoRemoves();
            }

            private void SetupFilters()
            {
                SetupFilter(EntityType.TC, ShowTC);
                SetupFilter(EntityType.Bag, ShowBags);
                SetupFilter(EntityType.Box, ShowBox);
                SetupFilter(EntityType.Ore, ShowOre);
                SetupFilter(EntityType.Npc, ShowNPC);
                SetupFilter(EntityType.CCTV, ShowCCTV);
                SetupFilter(EntityType.Dead, ShowDead);
                SetupFilter(EntityType.Heli, ShowHeli);
                SetupFilter(EntityType.Loot, ShowLoot);
                SetupFilter(EntityType.Stash, ShowStash);
                SetupFilter(EntityType.Turret, ShowTurrets);
                SetupFilter(EntityType.Bradley, ShowBradley);
                SetupFilter(EntityType.Col, ShowCollectibles);
                SetupFilter(EntityType.Airdrop, ShowSupplyDrops);
                SetupFilter(EntityType.Car, () => ShowEntity(EntityType.Car, Cache.Cars));
                SetupFilter(EntityType.CH47, () => ShowEntity(EntityType.CH47, Cache.CH47));
                SetupFilter(EntityType.MLRS, () => ShowEntity(EntityType.MLRS, Cache.MLRS));
                SetupFilter(EntityType.RHIB, () => ShowEntity(EntityType.RHIB, Cache.RHIB));
                SetupFilter(EntityType.Boat, () => ShowEntity(EntityType.Boat, Cache.Boats));
                SetupFilter(EntityType.Trap, () => ShowEntity(EntityType.Trap, Cache.Traps));
                SetupFilter(EntityType.Mini, () => ShowEntity(EntityType.Mini, Cache.MiniCopter));
                SetupFilter(EntityType.Horse, () => ShowEntity(EntityType.Horse, Cache.RidableHorse));
                SetupFilter(EntityType.CargoShip, () => ShowEntity(EntityType.CargoShip, Cache.CargoShips));
                SetupFilter(EntityType.CargoPlane, () => ShowEntity(EntityType.CargoPlane, Cache.CargoPlanes));
            }

            private IEnumerator DoGroupLimitRoutine()
            {
                while (isEnabled)
                {
                    ShowGroupLimits();

                    yield return CoroutineEx.waitForSeconds(invokeTime);
                }
            }

            private IEnumerator DoUpdateRoutine()
            {
                SetEnabledDataObjects();

                foreach (var filter in filters)
                {
                    filter.Value();

                    if (checks >= 100)
                    {
                        checks = 0;
                        yield return CoroutineEx.waitForFixedUpdate;
                    }
                }
            }

            private IEnumerator DoRadarRoutine()
            {
                while (isEnabled)
                {
                    if (player == null || !player.IsConnected || instance.isUnloading)
                    {
                        isEnabled = false;
                        Destroy(this);
                        yield break;
                    }

                    if (!SetSource())
                    {
                        yield return CoroutineEx.waitForSeconds(0.1f);
                        continue;
                    }

                    DoRemoves();
                    SetAdminFlag();
                    DirectDrawAll();
                    RemoveAdminFlag();
                    CheckNetworkGroupChange();

                    checks = 0;

                    yield return CoroutineEx.waitForSeconds(invokeTime);
                }
            }

            private void DoRemoves()
            {
                if (removeByEntityType.Count != 0)
                {
                    foreach (var type in removeByEntityType)
                    {
                        RemoveByEntityType(type);
                        entityTypes.Remove(type);
                        filters.Remove(type);
                    }
                    removeByEntityType.Clear();
                }
                if (removeByNetworkId.Count != 0)
                {
                    foreach (var nid in removeByNetworkId)
                    {
                        RemoveByNetworkId(nid);
                    }
                    removeByNetworkId.Clear();
                }
            }

            private void CheckNetworkGroupChange()
            {
                if (group == player.net.group)
                {
                    return;
                }
                group = player.net.group;
                if (_updateCo != null)
                {
                    StopCoroutine(_updateCo);
                }
                _updateCo = StartCoroutine(DoUpdateRoutine());
            }

            public void SetEnabledDataObjects()
            {
                foreach (var pair in data)
                {
                    pair.Value.SetEnabled(group, position, maxDistance);
                }
            }

            public void RemoveByEntityType(EntityType type)
            {
                foreach (var pair in data)
                {
                    if (pair.Value.IsOfType(type))
                    {
                        removeByNetworkId.Add(pair.Key);
                        pair.Value.disabled = true;
                    }
                }
            }

            public void RemoveByNetworkId(NetworkableId nid)
            {
                DataObject obj;
                if (data.TryGetValue(nid, out obj))
                {
                    if (obj != null)
                    {
                        obj.Reset();
                        Pool.Free(ref obj);
                    }
                    data[nid] = null;
                    data.Remove(nid);
                }
            }

            public void TryCacheByType(EntityType type, EntityInfo ei)
            {
                try
                {
                    if (!entityTypes.Contains(type) || !ei.entity.IsValid() || ei.entity.net.group != group && Distance(ei._from) > ei.dist)
                    {
                        return;
                    }
                    switch (type)
                    {
                        case EntityType.Airdrop: { CacheAirdrop(ei); break; }
                        case EntityType.Backpack: { CacheBackpack(ei); break; }
                        case EntityType.Bag: { CacheSleepingBag(ei); break; }
                        case EntityType.Box: { CacheContainer(ei); break; }
                        case EntityType.Bradley: { CacheBradley(ei); break; }
                        case EntityType.CCTV: { CacheCCTV(ei); break; }
                        case EntityType.Col: { CacheCol(ei); break; }
                        case EntityType.Dead: { CacheDead(ei); break; }
                        case EntityType.Heli: { CacheHeli(ei); break; }
                        case EntityType.Loot: { CacheLoot(ei); break; }
                        case EntityType.Npc: { CacheNpc(ei); break; }
                        case EntityType.Ore: { CacheOre(ei); break; }
                        case EntityType.Stash: { CacheStash(ei); break; }
                        case EntityType.TC: { CacheTC(ei); break; }
                        case EntityType.Turret: { CacheTurret(ei); break; }
                        default: { CacheEntity(ei, type); break; }
                    }
                }
                catch (Exception ex)
                {
                    currType = type;
                    HandleException(ex);
                }
            }

            private void DirectDrawAll()
            {
                float delay = this.delay;

                foreach (var obj in data.Values)
                {
                    if (obj.disabled)
                    {
                        continue;
                    }
                    if (obj.HasFlag(DrawFlags.Arrow))
                    {
                        player.SendConsoleCommand("ddraw.arrow", delay, obj.ei.color, obj.ei.from, obj.ei.to, obj.ei.size);
                    }
                    if (obj.HasFlag(DrawFlags.Box))
                    {
                        player.SendConsoleCommand("ddraw.box", delay, obj.ei.color, obj.ei.from, obj.ei.size);
                    }
                    if (obj.HasFlag(DrawFlags.Text))
                    {
                        try { obj.action(); } catch (Exception ex) { if (HandleException(obj, ex)) continue; else break; }
                        if (obj.ei.info == null) { continue; }
                        player.SendConsoleCommand("ddraw.text", delay, obj.ei.color, obj.ei.from, obj.ei.info);
                    }
                }
            }

            private void DrawVisionArrow(BasePlayer target, float dist)
            {
                if (dist <= 150f && instance.data.Visions.Contains(userid) && Physics.Raycast(target.eyes.HeadRay(), out hit, Mathf.Infinity))
                {
                    DrawArrow(Color.red, target.eyes.position + new Vector3(0f, 0.115f, 0f), hit.point, 0.15f, True);
                }
            }

            private void DrawVoiceArrow(BasePlayer target, Vector3 a, float dist)
            {
                if (config.Voice.Enabled && dist <= config.Voice.Distance && instance._voices.ContainsKey(target.userID))
                {
                    DrawArrow(Color.yellow, a + fiveUp, a + twoHalfUp, 0.5f, True);
                }
            }

            private void DrawArrow(Color color, Vector3 from, Vector3 to, float size, bool @override = False)
            {
                if (config.Methods.Arrow || @override)
                {
                    player.SendConsoleCommand("ddraw.arrow", delay, color, from, to, size);
                }
            }

            private void DrawPlayerText(Color color, Vector3 position, object prefix, object text, bool @override = False)
            {
                if (config.Methods.Text || @override)
                {
                    player.SendConsoleCommand("ddraw.text", delay, color, position, Format(prefix, text, false));
                }
            }

            private void DrawBox(Color color, Vector3 position, float size, bool @override = False)
            {
                if (config.Methods.Box || @override)
                {
                    player.SendConsoleCommand("ddraw.box", delay, color, position, size);
                }
            }

            private void CacheArrow(DataObject obj, Color color, Vector3 offset, Vector3 to, float size, bool @override = False)
            {
                if (config.Methods.Arrow || @override)
                {
                    obj.ei.color = color;
                    obj.ei._from = obj.ei.from;
                    obj.ei.offset = offset;
                    obj.ei.to = to;
                    obj.ei.size = size;
                    obj.flags |= DrawFlags.Arrow;
                }
            }

            private void CacheBox(DataObject obj, Color color, Vector3 offset, float size, bool @override = False)
            {
                if (config.Methods.Box || @override)
                {
                    obj.ei.color = color;
                    obj.ei._from = obj.ei.from;
                    obj.ei.offset = offset;
                    obj.ei.size = size;
                    obj.flags |= DrawFlags.Box;
                }
            }

            private void CacheText(DataObject obj, Color color, Vector3 offset, Action action, bool @override = False)
            {
                if (config.Methods.Text || @override)
                {
                    obj.ei.color = color;
                    obj.ei._from = obj.ei.from;
                    obj.ei.offset = offset;
                    obj.flags |= DrawFlags.Text;
                    obj.action = action;
                }
            }

            private bool HandleException(DataObject obj, Exception ex)
            {
                currType = obj.ei.type;
                HandleException(ex);
                return player.IsAdmin;
            }

            private void HandleException(Exception ex)
            {
                RemoveAdminFlag();
                removeByEntityType.Add(currType);
                instance._errorTypes.Add(currType);
                instance.Message(player, "Exception");
                instance.Puts("Error @{0}: {1}", currType, ex);
                RemoveByEntityType(currType);
            }

            private void SetAdminFlag()
            {
                if (!isAdmin && hasPermAllowed && data.Count > 0 && !player.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, True);
                    player.SendNetworkUpdateImmediate();
                }
            }

            private void RemoveAdminFlag()
            {
                if (!isAdmin && hasPermAllowed && player.HasPlayerFlag(BasePlayer.PlayerFlags.IsAdmin))
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, False);
                    player.SendNetworkUpdateImmediate();
                }
            }

            private bool API_GetExistingBackpacks(ulong userid)
            {
                if (canGetExistingBackpacks && instance.Backpacks != null)
                {
                    canGetExistingBackpacks = False;
                    instance.timer.Once(60f, () => canGetExistingBackpacks = True);
                    backpacks = instance.Backpacks?.Call("API_GetExistingBackpacks") as Dictionary<ulong, ItemContainer>;
                }

                return backpacks != null && backpacks.TryGetValue(userid, out _backpackItemContainer) && _backpackItemContainer != null && !_backpackItemContainer.IsEmpty();
            }

            private string Format(object prefix, object text, bool entity = True)
            {
                if (entity)
                {
                    return $"<size={config.Settings.EntityNameSize}>{prefix}</size> <size={config.Settings.EntityTextSize}>{text}</size>";
                }
                return $"<size={config.Settings.PlayerNameSize}>{prefix}</size> <size={config.Settings.PlayerTextSize}>{text}</size>";
            }

            private string Format(BasePlayer target, bool s)
            {
                if (target.metabolism == null)
                {
                    return $"{Mathf.CeilToInt(target.health)}";
                }
                if (s)
                {
                    return $"{Mathf.CeilToInt(target.health)} {Mathf.CeilToInt(target.metabolism.calories.value)}:{Mathf.CeilToInt(target.metabolism.hydration.value)}";
                }
                return $"{Mathf.CeilToInt(target.health)} <color=#FFA500>{Mathf.CeilToInt(target.metabolism.calories.value)}</color>:<color=#FFADD8E6>{Mathf.CeilToInt(target.metabolism.hydration.value)}</color>";
            }

            private bool HasPermission(string userid, string perm) => instance.permission.UserHasPermission(userid, perm);

            private string GetContents(List<Item> itemList, int num)
            {
                if (num <= 0 || itemList.Count == 0)
                {
                    return $"({itemList.Count})";
                }
                var sb = StringBuilderCache.Acquire();
                foreach (Item item in itemList.Take(num))
                {
                    sb.Append(instance.m(config.Options.Abbr ? string.Concat(item.info.displayName.english.Where(abbr)) : item.info.displayName.english, userid)).Append($": {item.amount}, ");
                }
                sb.Length -= 2;
                return $"({StringBuilderCache.GetStringAndRelease(sb)}) ({itemList.Count})";
            }

            private string GetContents(ItemContainer[] containers, int num)
            {
                if (containers.IsNullOrEmpty())
                {
                    return string.Empty;
                }
                List<Item> itemList = Pool.GetList<Item>();
                foreach (ItemContainer container in containers)
                {
                    itemList.AddRange(container.itemList);
                }
                string contents = GetContents(itemList, num);
                Pool.FreeList(ref itemList);
                return contents;
            }

            private bool SetSource()
            {
                if (!setSource)
                {
                    source = player;
                    return True;
                }

                source = player;

                if (player.IsSpectating())
                {
                    var target = player.GetParentEntity() as BasePlayer;

                    if (target != null)
                    {
                        if (target.IsDead() && !target.IsConnected)
                        {
                            player.StopSpectating();
                        }
                        else
                        {
                            source = target;
                        }
                    }
                }

                if (player == source && (player.IsDead() || player.IsSleeping() || player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot)))
                {
                    RemoveAdminFlag();
                    return False;
                }

                return True;
            }

            private float SetDistance(Vector3 a)
            {
                return currDistance = Distance(a);
            }

            private DataObject SetDataObject(EntityInfo ei)
            {
                DataObject obj = Pool.Get<DataObject>();
                data[ei.entity.net.ID] = obj;
                obj.ei = ei;
                return obj;
            }

            private bool HasDataObject(BaseEntity entity) => data.ContainsKey(entity.net.ID);

            private bool IsValid(EntityInfo ei, float dist)
            {
                if (ei.IsInvalid || HasDataObject(ei.entity))
                {
                    return False;
                }
                if (ei.type == EntityType.Bradley || ei.type == EntityType.Heli)
                {
                    return SetDistance(ei._from) <= dist || currDistance <= maxDistance;
                }
                return SetDistance(ei._from) <= dist && currDistance <= maxDistance;
            }

            private void ShowActive()
            {
                try
                {
                    currType = EntityType.Active;

                    foreach (var target in BasePlayer.activePlayerList)
                    {
                        TryCacheOnlinePlayer(target);

                        checks++;
                    }
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            public void TryCacheOnlinePlayer(BasePlayer target)
            {
                currType = EntityType.Active;

                if (target.IsKilled() || !GetBool(EntityType.Active) || exclude.Contains(target.userID) || !canBypassOverride && HasPermission(target.UserIDString, "adminradar.bypass"))
                {
                    return;
                }

                var nid = target.net.ID;

                DataObject obj;
                if (!data.TryGetValue(nid, out obj))
                {
                    data[nid] = obj = Pool.Get<DataObject>();
                }

                obj.Reset();
                obj.ei = new EntityInfo(target, EntityType.Active, config.Distance.Get);

                CacheText(obj, Color.green, Vector3.zero, () =>
                {
                    if (target.IsKilled() || !target.IsConnected)
                    {
                        TryCacheSleepingPlayer(target);
                        return;
                    }

                    var dist = Distance(obj.ei.from);
                    var color = GetColor(target, obj.ei._from);

                    if (config.Methods.Box && dist > maxDistance)
                    {
                        DrawBox(GetColor(target, obj.ei._from), obj.ei._from + Vector3.up, GetScale(dist));
                    }
                    else if (dist < config.Distance.Players && dist < maxDistance)
                    {
                        DrawArrow(__(config.Hex.Arrows), obj.ei._from + new Vector3(0f, obj.ei._from.y + 10), obj.ei._from, 1);
                        DrawBox(color, obj.ei._from + Vector3.up, target.GetHeight());
                        DrawCupboardArrows(target, EntityType.Active);
                        DrawAppendedText(target, obj.ei._from, twoUp, color);
                        DrawVoiceArrow(target, obj.ei._from, dist);
                        DrawVisionArrow(target, dist);
                    }
                    else if (config.Limit.Enabled && config.Limit.Range > 0f && dist < maxDistance)
                    {
                        var obj2 = Pool.Get<DistantPlayer>();
                        obj2.alive = target.IsAlive();
                        obj2.pos = obj.ei._from;
                        distant.Add(obj2);
                    }
                    else if (config.Options.DrawX && dist < maxDistance)
                    {
                        DrawPlayerText(color, obj.ei._from, "X", string.Empty, True);
                    }
                }, True);
            }

            private void ShowSleepers()
            {
                try
                {
                    currType = EntityType.Sleeper;

                    foreach (var target in BasePlayer.sleepingPlayerList)
                    {
                        TryCacheSleepingPlayer(target);

                        checks++;
                    }
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void TryCacheSleepingPlayer(BasePlayer target)
            {
                if (target.IsKilled() || !GetBool(EntityType.Sleeper) || target.transform.position.y < config.Distance.MinY || !canBypassOverride && HasPermission(target.UserIDString, "adminradar.bypass"))
                {
                    return;
                }

                var nid = target.net.ID;

                DataObject obj;
                if (!data.TryGetValue(nid, out obj))
                {
                    data[nid] = obj = Pool.Get<DataObject>();
                }

                obj.Reset();
                obj.ei = new EntityInfo(target, EntityType.Sleeper, config.Distance.Get);

                CacheText(obj, Color.cyan, Vector3.zero, () =>
                {
                    if (target.IsKilled() || target.IsConnected)
                    {
                        TryCacheOnlinePlayer(target);
                        return;
                    }

                    if (target.transform.position.y < config.Distance.MinY)
                    {
                        removeByNetworkId.Add(nid);
                        return;
                    }

                    var color = __(target.IsAlive() ? config.Hex.Sleeper : config.Hex.SleeperDead);
                    var dist = Distance(obj.ei.from);

                    if (dist < config.Distance.Players)
                    {
                        DrawArrow(__(config.Hex.Arrows), obj.ei._from + new Vector3(0f, obj.ei._from.y + 10), obj.ei._from, 1, False);
                        DrawCupboardArrows(target, EntityType.Sleeper);
                        DrawAppendedText(target, obj.ei._from, halfUp, color);
                    }
                    else if (dist < maxDistance)
                    {
                        DrawPlayerText(Color.cyan, obj.ei._from, "X", string.Empty, config.Options.DrawX);
                        DrawBox(Color.cyan, obj.ei._from + Vector3.up, GetScale(dist));
                    }
                }, True);
            }

            private Color GetColor(BasePlayer target, Vector3 a)
            {
                if (target.health <= 0f)
                {
                    return __(config.Hex.OnlineDead);
                }
                if (canBypassOverride && (target.IsAdmin || target.IsDeveloper))
                {
                    return Color.magenta;
                }
                if (!target.IsOnGround() || target.IsFlying)
                {
                    return __(config.Hex.Flying);
                }
                if (a.y + 1f < TerrainMeta.HeightMap.GetHeight(a))
                {
                    return __(config.Hex.Underground);
                }
                return __(config.Hex.Online);
            }

            private void DrawAppendedText(BasePlayer target, Vector3 a, Vector3 offset, Color color)
            {
                var sb = StringBuilderCache.Acquire();
                if (instance.data.Extended.Contains(userid))
                {
                    Item item = target.GetActiveItem();

                    if (item != null)
                    {
                        sb.Append(instance.m(config.Options.Abbr ? string.Concat(item.info.displayName.english.Where(abbr)) : item.info.displayName.english, userid));

                        if (item.contents?.itemList?.Count > 0)
                        {
                            sb.Append(" (");
                            item.contents.itemList.ForEach(con =>
                            {
                                sb.Append(instance.m(config.Options.Abbr ? string.Concat(con.info.displayName.english.Where(abbr)) : con.info.displayName.english, userid)).Append("|");
                            });
                            sb.Length -= 1;
                            sb.Append(")");
                        }
                    }
                }
                if (config.Settings.AveragePingInterval > 0)
                {
                    sb.Append($" {target.IPlayer?.Ping ?? -1}ms");
                }
                if (config.Additional.BackpackPlugin && API_GetExistingBackpacks(target.userID))
                {
                    sb.Append("*");
                }
                if (!string.IsNullOrEmpty(config.Settings.New) && instance.permission.UserHasGroup(target.UserIDString, config.Settings.New))
                {
                    sb.Append(config.Settings.NewText);
                }
                string clan;
                if (instance._clanColors.TryGetValue(instance.GetClanOf(target.userID), out clan) && !config.Settings.ApplySameColor)
                {
                    clan = $" <color={clan}>C</color>";
                }
                string team;
                if (instance._teamColors.TryGetValue(target.currentTeam, out team) && !config.Settings.ApplySameColor)
                {
                    team = $"<color={team}>T</color>";
                }
                string health = showHT && target.metabolism != null ? Format(target, config.Settings.ApplySameColor) : $"{Mathf.CeilToInt(target.health)}";
                if (config.Settings.ApplySameColor && !string.IsNullOrEmpty(clan ?? team))
                {
                    DrawPlayerText(color, a + offset, $"{GetCheats(target)}<color={clan ?? team}>{r(target.displayName)}</color>", $"<color={clan ?? team}>{health} {Distance(a)} {StringBuilderCache.GetStringAndRelease(sb)}</color>");
                }
                else
                {
                    DrawPlayerText(color, a + offset, $"{GetCheats(target)}{r(target.displayName)}", $"<color={config.Hex.Health}>{health}</color> <color={config.Hex.Dist}>{Distance(a)}</color> {StringBuilderCache.GetStringAndRelease(sb)}{clan}{team}");
                }
            }

            private string GetCheats(BasePlayer target)
            {
                var sb = StringBuilderCache.Acquire();
                if (config.Track.Radar && instance.IsRadar(target.UserIDString)) sb.Append(config.Track.RadarText).Append("|");
                if (config.Track.God && target.IsGod()) sb.Append(config.Track.GodText).Append("|");
                if (config.Track.GodPlugin && target.metabolism?.calories?.min == 500) sb.Append(config.Track.GodPluginText).Append("|");
                if (config.Track.Vanish && target.limitNetworking) sb.Append(config.Track.VanishText).Append("|");
                if (config.Track.NoClip && target.IsFlying) sb.Append(config.Track.NoClipText).Append("|");
                if (sb.Length > 0) { sb.Length -= 1; sb.Insert(0, "(").Append(") "); }
                return StringBuilderCache.GetStringAndRelease(sb);
            }

            private void ShowGroupLimits()
            {
                if (distant.Count == 0)
                {
                    return;
                }

                currType = EntityType.Limit;

                var groups = Pool.Get<Dictionary<int, List<DistantPlayer>>>();

                int j = 0, k, i;

                try
                {
                    float sqrMagnitude = config.Limit.Range * config.Limit.Range;

                    for (; j < distant.Count; j++)
                    {
                        for (k = distant.Count - 1; k >= 0; k--)
                        {
                            if (j != k && (distant[j].pos - distant[k].pos).sqrMagnitude <= sqrMagnitude)
                            {
                                List<DistantPlayer> group = null;

                                foreach (var value in groups.Values)
                                {
                                    if (value.Contains(distant[j]) || value.Contains(distant[k]))
                                    {
                                        group = value;
                                        break;
                                    }
                                }

                                if (group == null)
                                {
                                    groups.Add(groups.Count, group = new List<DistantPlayer>());
                                }

                                if (!group.Contains(distant[j]))
                                {
                                    group.Add(distant[j]);
                                }

                                if (!group.Contains(distant[k]))
                                {
                                    group.Add(distant[k]);
                                }
                            }
                        }
                    }

                    j = 0;

                    var dead = __(config.Limit.Dead);
                    var drawAtOffset = config.Limit.Height != 0f;

                    foreach (var group in groups.Values)
                    {
                        var alive = __(instance.GetGroupColor(j));

                        k = 0;

                        for (i = group.Count - 1; i >= 0; i--)
                        {
                            var target = group[i];

                            if (distant.Remove(target))
                            {
                                if (k++ == 0 && drawAtOffset)
                                {
                                    DrawPlayerText(Color.black, target.pos + limitUp, group.Count, string.Empty, True);
                                }

                                DrawPlayerText(target.alive ? alive : dead, target.pos, "X", string.Empty, True);

                                Pool.Free(ref target);

                                group[i] = null;
                            }
                        }

                        if (k != 0 && ++j > config.Limit.Colors.Count)
                        {
                            j = 0;
                        }
                    }

                    for (j = 0; j < distant.Count; ++j)
                    {
                        DrawPlayerText(distant[j].alive ? Color.green : Color.red, distant[j].pos, "X", string.Empty, True);
                    }
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
                finally
                {
                    groups.ResetToPool();

                    for (i = distant.Count - 1; i >= 0; i--)
                    {
                        var obj = distant[i];
                        Pool.Free(ref obj);
                        distant[i] = null;
                    }

                    distant.Clear();
                }
            }

            private void DrawCupboardArrows(BasePlayer target, EntityType lastType)
            {
                try
                {
                    if (entityTypes.Contains(EntityType.TCArrow))
                    {
                        currType = EntityType.TCArrow;

                        foreach (var ei in Cache.Cupboards.Values)
                        {
                            if (IsValid(ei, config.Distance.TCArrows) && ei.priv.IsAuthed(target))
                            {
                                DrawArrow(__(config.Hex.TC), target.transform.position + new Vector3(0f, 0.115f, 0f), ei._from, 0.25f, True);
                            }
                        }

                        currType = lastType;
                    }
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowHeli()
            {
                if (Cache.Helicopters.Count > 0)
                {
                    foreach (var heli in Cache.Helicopters.Values)
                    {
                        CacheHeli(heli);
                    }
                }
            }

            private void CacheHeli(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Heli;

                    if (IsValid(ei, 9999f))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.Heli);
                        var name = instance.m("H", userid);
                        var weakspots = (ei.entity as BaseHelicopter).weakspots;

                        CacheText(obj, color, twoUp, () =>
                        {
                            string heliHealth = ei.entity.Health() > 1000 ? Mathf.CeilToInt(ei.entity.Health()).ToString("#,##0,K", CultureInfo.InvariantCulture) : Mathf.CeilToInt(ei.entity.Health()).ToString();
                            string info = config.Additional.RotorHealth ? $"<color={config.Hex.Health}>{heliHealth}</color> (<color=#FFFF00>{Mathf.CeilToInt(weakspots[0].health)}</color>/<color=#FFFF00>{Mathf.CeilToInt(weakspots[1].health)}</color>)" : $"<color={config.Hex.Health}>{heliHealth}</color>";
                            ei.info = Format(name, $"{info} <color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, Vector3.up, GetScale(Distance(ei.from)));
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowBradley()
            {
                if (Cache.BradleyAPCs.Count > 0)
                {
                    foreach (var bradley in Cache.BradleyAPCs.Values)
                    {
                        CacheBradley(bradley);
                    }
                }
            }

            private void CacheBradley(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Bradley;

                    if (IsValid(ei, 9999f))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.Bradley);
                        var name = instance.m("B", userid);

                        CacheText(obj, color, twoUp, () =>
                        {
                            string health = ei.entity.Health() > 1000 ? Mathf.CeilToInt(ei.entity.Health()).ToString("#,##0,K", CultureInfo.InvariantCulture) : Mathf.CeilToInt(ei.entity.Health()).ToString();
                            ei.info = Format(name, $"<color={config.Hex.Health}>{health}</color> <color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, Vector3.up, GetScale(currDistance));
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowTC()
            {
                foreach (var tc in Cache.Cupboards)
                {
                    CacheTC(tc.Value);
                }
            }

            private void CacheTC(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.TC;

                    if (IsValid(ei, config.Distance.TC))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.TC);

                        if (config.Methods.Text)
                        {
                            var decayEntities = config.Options.TCBags && ei.priv.buildingID != 0 ? ei.priv.GetBuilding()?.decayEntities : null;
                            var name = instance.m("TC", userid);

                            CacheText(obj, color, halfUp, () =>
                            {
                                var sb = StringBuilderCache.Acquire($"<color={config.Hex.Dist}>{Distance(ei.from)}</color>");

                                if (decayEntities != null)
                                {
                                    sb.Append($" <color={config.Hex.Bag}>{decayEntities.Sum(e => e is SleepingBag ? 1 : 0)}</color>");
                                }

                                if (config.Options.TCAuthed)
                                {
                                    sb.Append($" <color={config.Hex.TC}>{ei.priv.authorizedPlayers.Count}</color>");
                                }

                                sb.Append($" <color={config.Hex.Dist}>{ei.priv.GetProtectedMinutes()}</color>");

                                ei.info = Format(name, StringBuilderCache.GetStringAndRelease(sb));
                            });
                        }

                        CacheBox(obj, color, halfUp, 3f);
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowSupplyDrops()
            {
                foreach (var drop in Cache.Airdrops.Values)
                {
                    CacheAirdrop(drop);
                }
            }

            private void CacheAirdrop(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Airdrop;

                    if (IsValid(ei, config.Distance.Airdrop))
                    {
                        var obj = SetDataObject(ei);
                        var drop = ei.entity as SupplyDrop;
                        var color = __(config.Hex.AD);

                        CacheText(obj, color, halfUp, () =>
                        {
                            string text = config.Options.AirdropContentAmount > 0 ? GetContents(drop.inventory.itemList, config.Options.AirdropContentAmount) : $"({drop.inventory.itemList.Count}) ";
                            ei.info = Format(ei.name, $"<color={config.Hex.Loot}>{text}</color><color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, halfUp, GetScale(currDistance));
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowContainer(EntityInfo ei, EntityType type)
            {
                checks++;

                if (instance.data.OnlineBoxes.Contains(userid) && ei.entity.OwnerID.IsSteamId() && (ei.entity.PrefabName.Contains("box") || ei.entity.PrefabName.Contains("coffin")))
                {
                    var owner = BasePlayer.FindAwakeOrSleeping(ei.entity.OwnerID.ToString());

                    if (owner == null || !owner.IsConnected)
                    {
                        return;
                    }
                }

                var obj = SetDataObject(ei);
                var amount = config.Options.Get(type);
                var container = ei.entity as StorageContainer;
                var itemList = container.inventory.itemList;
                var color = __(container is LockedByEntCrate || container is VendingMachine ? config.Hex.Heli : type == EntityType.Box ? config.Hex.Box : type == EntityType.Loot ? config.Hex.Loot : config.Hex.Stash);

                ei.name = instance.m(instance.StripTags(container.ShortPrefabName).Replace("coffinstorage", "coffin").Replace("vendingmachine", "VM"), userid);

                CacheText(obj, color, halfUp, () =>
                {
                    ei.info = null;
                    if (itemList == null || !config.Options.DrawEmptyContainers && itemList.Count == 0) return;
                    string text = amount > 0 ? GetContents(itemList, amount) : $"({itemList.Count}) ";
                    ei.info = Format(ei.name, $"{text}<color={config.Hex.Dist}>{Distance(ei._from)}</color>");
                });

                CacheBox(obj, color, halfUp, GetScale(currDistance));
            }

            private void ShowBox()
            {
                foreach (var container in Cache.Containers.Values)
                {
                    CacheContainer(container);
                }
            }

            private void CacheContainer(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Box;

                    if (ei.type == EntityType.Box && IsValid(ei, config.Distance.Get(currType, ei.entity)))
                    {
                        ShowContainer(ei, currType);
                    }
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowStash()
            {
                foreach (var container in Cache.Containers.Values)
                {
                    CacheStash(container);
                }
            }

            private void CacheStash(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Stash;

                    if (ei.type == EntityType.Stash && IsValid(ei, config.Distance.Stash))
                    {
                        ShowContainer(ei, currType);
                    }
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowLoot()
            {
                foreach (var backpack in Cache.Backpacks.Values)
                {
                    CacheBackpack(backpack);
                }

                foreach (var container in Cache.Containers.Values)
                {
                    CacheLoot(container);
                }
            }

            private void CacheBackpack(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Backpack;

                    if (IsValid(ei, config.Distance.Loot))
                    {
                        var obj = SetDataObject(ei);
                        var backpack = ei.entity as DroppedItemContainer;
                        var color = __(config.Hex.Backpack);

                        CacheText(obj, color, halfUp, () =>
                        {
                            string prefix = string.IsNullOrEmpty(backpack._playerName) ? instance.m("backpack", userid) : backpack._playerName;
                            ei.info = Format(prefix, $"{GetContents(backpack.inventory.itemList, config.Options.BackpackContentAmount)}<color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, halfUp, GetScale(currDistance));
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void CacheLoot(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Loot;

                    if (ei.type == EntityType.Loot && IsValid(ei, config.Distance.Loot))
                    {
                        ShowContainer(ei, currType);
                    }
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowBags()
            {
                foreach (var bag in Cache.Bags.Values)
                {
                    CacheSleepingBag(bag);
                }
            }

            private void CacheSleepingBag(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Bag;

                    if (IsValid(ei, config.Distance.Bag))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.Bag);
                        var name = instance.m("bag", userid);

                        CacheText(obj, color, Vector3.zero, () =>
                        {
                            ei.info = Format(name, $"<color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, Vector3.zero, 0.5f);
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowTurrets()
            {
                foreach (var turret in Cache.Turrets.Values)
                {
                    CacheTurret(turret);
                }
            }

            private void CacheTurret(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Turret;

                    if (IsValid(ei, config.Distance.Turret))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.AT);
                        var itemList = (ei.entity as AutoTurret)?.inventory?.itemList;
                        var name = instance.m("AT", userid);

                        CacheText(obj, color, halfUp, () =>
                        {
                            ei.info = Format(name, $"({itemList?.Count}) <color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, halfUp, 1f);
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowDead()
            {
                foreach (var ci in Cache.Corpses.Values)
                {
                    CacheDead(ci);
                }
            }

            private void CacheDead(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Dead;

                    if (IsValid(ei, config.Distance.Corpse))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.Corpse);
                        var containers = (ei.entity as PlayerCorpse).containers;

                        CacheText(obj, color, halfUp, () =>
                        {
                            ei.info = Format(ei.name, $"{GetContents(containers, config.Options.CorpseContentAmount)}");
                        });

                        CacheBox(obj, color, Vector3.zero, GetScale(currDistance));
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowNPC()
            {
                if (config.Core.NPCPlayer)
                {
                    foreach (var target in Cache.NPCPlayers.Values)
                    {
                        CacheNpc(target);
                    }
                }

                if (config.Core.Animals)
                {
                    foreach (var npc in Cache.Animals.Values)
                    {
                        CacheAnimal(npc);
                    }
                }
            }

            private void CacheAnimal(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Npc;

                    if (IsValid(ei, 9999f))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.Animal);
                        var name = instance.m(ei.entity.ShortPrefabName, userid);
                        var _players = ei.entity.HasBrain ? ei.entity.GetComponent<BaseAIBrain>()?.Senses?.Players : null;

                        CacheText(obj, color, Vector3.up, () =>
                        {
                            ei.info = null;
                            float dist = Distance(ei.from);
                            if (dist < config.Distance.Animal && dist < maxDistance && IsAtView(ei))
                            {
                                if (config.Options.DrawTargetsVictim && _players != null && _players.Count > 0)
                                    DrawVictim(_players.Find(x => x) as BasePlayer, ei._from, new Vector3(0f, 1.25f + dist * 0.03f), color);

                                ei.info = Format(name, $"<color={config.Hex.Health}>{Mathf.CeilToInt(ei.entity.Health())}</color> <color={config.Hex.Dist}>{dist}</color>");
                            }
                        });

                        CacheBox(obj, color, Vector3.up, ei.entity.bounds.size.y);
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void CacheNpc(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Npc;

                    if (!(ei.entity is BasePlayer))
                    {
                        CacheAnimal(ei);
                        return;
                    }

                    if (IsValid(ei, 9999f))
                    {
                        var obj = SetDataObject(ei);

                        if (SetDistance(ei.from) < config.Distance.NPC)
                        {
                            var target = ei.entity as BasePlayer;
                            var _players = ei.entity.HasBrain ? ei.entity.GetComponent<BaseAIBrain>()?.Senses?.Players : null;
                            var color = __(target.IsHoldingEntity<BaseMelee>() ? config.Hex.Murderer : target.ShortPrefabName.Contains("peacekeeper") ? config.Hex.Peacekeeper : target.name.Contains("scientist") ? config.Hex.Scientist : target.ShortPrefabName == "murderer" ? config.Hex.Murderer : config.Hex.Animal);
                            var displayName = !string.IsNullOrEmpty(target.displayName) && target.displayName != target.UserIDString ? target.displayName : target.ShortPrefabName == "scarecrow" ? instance.m("scarecrow", userid) : target.PrefabName.Contains("scientist") ? instance.m("scientist", userid) : instance.m(target.ShortPrefabName, userid);

                            CacheText(obj, color, twoUp, () =>
                            {
                                ei.info = null;

                                float dist = Distance(ei.from);
                                if (dist > maxDistance || dist > config.Distance.NPC || !IsAtView(ei))
                                    return;

                                if (config.Options.DrawTargetsVictim && _players != null && _players.Count > 0)
                                    DrawVictim(_players.Find(x => x) as BasePlayer, ei._from, new Vector3(0f, 2f + Distance(ei._from) * 0.03f), color);

                                ei.info = Format(displayName, $"<color={config.Hex.Health}>{Mathf.CeilToInt(target.health)}</color> <color={config.Hex.Dist}>{dist}</color>");
                            });

                            CacheArrow(obj, color, new Vector3(0f, ei.from.y + 10), ei.from, 1, False);
                            CacheBox(obj, color, Vector3.up, target.GetHeight(target.modelState.ducked));
                        }
                        else CacheBox(obj, Color.blue, Vector3.up, 5f, True);
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void DrawVictim(BasePlayer victim, Vector3 from, Vector3 offset, Color color)
            {
                if (victim != null)
                {
                    string text = $"<color={(victim.IsSleeping() ? config.Hex.Sleeper : victim.IsAlive() ? "#00ff00" : config.Hex.OnlineDead)}>{victim.displayName}</color>";
                    player.SendConsoleCommand("ddraw.text", delay, color, from + offset, $"<size={config.Settings.PlayerTextSize}>T: {text}</size>");
                }
            }

            private bool IsAtView(EntityInfo ei)
            {
                if (config.Options.WorldView)
                {
                    if (ei.entity is SimpleShark)
                    {
                        return true;
                    }
                    if (position.y > 0f && ei._from.y < 0f)
                    {
                        return false;
                    }
                    if (ei._from.y > 0f && position.y < 0f)
                    {
                        return false;
                    }
                }
                return true;
            }

            private void ShowOre()
            {
                foreach (var ore in Cache.Ores.Values)
                {
                    CacheOre(ore);
                }
            }

            private void CacheOre(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Ore;

                    if (IsValid(ei, config.Distance.Ore))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.Resource);
                        var containedItems = ei.entity.GetComponent<ResourceDispenser>().containedItems;

                        CacheText(obj, color, Vector3.up, () =>
                        {
                            ei.info = Format(ei.name, config.Options.ResourceAmounts ? $"({containedItems.Sum(i => i.amount)})" : $"<color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, Vector3.up, GetScale(currDistance));
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowCCTV()
            {
                foreach (var cctv in Cache.CCTV.Values)
                {
                    CacheCCTV(cctv);
                }
            }

            private void CacheCCTV(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.CCTV;

                    if (IsValid(ei, config.Distance.CCTV))
                    {
                        var obj = SetDataObject(ei);
                        var cctv = ei.entity as CCTV_RC;
                        var name = instance.m("CCTV", userid);

                        CacheText(obj, Color.magenta, new Vector3(0f, 0.3f, 0f), () =>
                        {
                            ei.color = ei.entity.HasFlag(BaseEntity.Flags.Reserved5) ? Color.green : cctv.IsPowered() || cctv.IsStatic() ? Color.cyan : Color.red;
                            ei.info = Format(name, $"<color={config.Hex.Dist}>{Distance(ei.from)}</color> {cctv.ViewerCount}");
                        });

                        CacheBox(obj, Color.magenta, Vector3.zero, 0.25f);
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowCollectibles()
            {
                foreach (var col in Cache.Collectibles.Values)
                {
                    CacheCol(col);
                }
            }

            private void CacheCol(EntityInfo ei)
            {
                try
                {
                    currType = EntityType.Col;

                    if (IsValid(ei, config.Distance.Col))
                    {
                        var obj = SetDataObject(ei);
                        var color = __(config.Hex.Col);
                        var itemList = ((CollectibleEntity)ei.entity).itemList;

                        CacheText(obj, color, Vector3.up, () =>
                        {
                            ei.info = Format(ei.name, config.Options.ResourceAmounts ? $"({itemList.Sum(i => i.amount)})" : $"<color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                        });

                        CacheBox(obj, color, Vector3.up, ei.size);
                    }

                    checks++;
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private void ShowEntity(EntityType entityType, Dictionary<NetworkableId, EntityInfo> entities)
            {
                if (entities.Count > 0)
                {
                    foreach (var ei in entities.Values)
                    {
                        CacheEntity(ei, entityType);
                    }
                }
            }

            private void CacheEntity(EntityInfo ei, EntityType entityType)
            {
                this.currType = entityType;

                try
                {
                    checks++;

                    if (ei.IsInvalid || HasDataObject(ei.entity))
                    {
                        return;
                    }

                    var offset = Vector3.up;
                    var entityName = $"{entityType}";

                    SetDistance(ei.from);

                    if (entityType == EntityType.Boat)
                    {
                        if (currDistance > config.Distance.Boat || !config.Additional.Boats && !config.GUI.Boats) return;
                    }
                    else if (entityType == EntityType.RHIB)
                    {
                        if (currDistance > config.Distance.Boat || !config.Additional.RHIB && !config.GUI.RHIB) return;
                    }
                    else if (entityType == EntityType.Car)
                    {
                        if (currDistance > config.Distance.Car || !config.Additional.Cars && !config.GUI.Cars) return;
                    }
                    else if (entityType == EntityType.Mini)
                    {
                        if (currDistance > config.Distance.MC || !config.Additional.MC && !config.GUI.MC) return;
                    }
                    else if (entityType == EntityType.MLRS)
                    {
                        if (currDistance > config.Distance.MLRS || !config.Additional.MLRS && !config.GUI.MLRS) return;
                    }
                    else if (entityType == EntityType.Horse)
                    {
                        if (currDistance > config.Distance.RH || !config.Additional.RH && !config.GUI.Horse) return;
                    }
                    else if (entityType == EntityType.Trap)
                    {
                        if (currDistance > config.Distance.Traps || !config.Additional.Traps && !config.GUI.Traps) return;
                        else if (ei.entity is FlameTurret) offset = new Vector3(0f, 1.3f);
                        else if (ei.entity is Landmine) offset = new Vector3(0f, 0.25f);
                        else if (ei.entity is BearTrap) offset = halfUp;
                    }
                    else if (entityType == EntityType.CH47)
                    {
                        if (!config.Additional.CH47 && !config.GUI.CH47) return;
                    }

                    entityName = instance.m(ei.entity is ScrapTransportHelicopter ? "STH" : ei.entity is BaseSubmarine ? "SUB" : ei.entity is BaseBoat ? "RB" : string.Concat(entityName.Where(char.IsUpper)), userid);
                    var color = __(ei.entity is ScrapTransportHelicopter ? config.Hex.STH : config.Hex.Get(entityType));
                    var obj = SetDataObject(ei);

                    CacheText(obj, color, offset, () =>
                    {
                        string health = ei.entity.Health() > 1000 ? Mathf.CeilToInt(ei.entity.Health()).ToString("#,##0,K", CultureInfo.InvariantCulture) : Mathf.CeilToInt(ei.entity.Health()).ToString();
                        string info = $"{entityName} <color={config.Hex.Health}>{health}</color>";
                        ei.info = Format(info, $"<color={config.Hex.Dist}>{Distance(ei.from)}</color>");
                    });

                    CacheBox(obj, color, offset, GetScale(currDistance));
                }
                catch (Exception ex)
                {
                    HandleException(ex);
                }
            }

            private static float GetScale(float value)
            {
                return value * 0.02f;
            }
        }

        private void StopFillCache()
        {
            //Puts("StopFillCache");

            if (_coroutines.Count > 0)
            {
                while (_coroutines.Count > 0)
                {
                    var co = _coroutines.Pop();
                    if (co == null) continue;
                    ServerMgr.Instance.StopCoroutine(co);
                }
            }
        }

        private IEnumerator FillOnEntitySpawned()
        {
            while (!isUnloading)
            {
                foreach (var entity in _spawnedEntities)
                {
                    cache.Add(entity);
                }

                _spawnedEntities.Clear();

                yield return CoroutineEx.waitForSeconds(0.1f);

                foreach (var element in _despawnedEntities)
                {
                    cache.Remove(element.Key, element.Value);
                }

                _despawnedEntities.Clear();

                yield return CoroutineEx.waitForSeconds(0.1f);
            }
        }

        private IEnumerator FillCache()
        {
            var tick = DateTime.Now;
            int cached = 0, total = 0;

            _coroutineTimer.Start();
            _allEntities = new Dictionary<NetworkableId, BaseEntity>(total = BaseNetworkable.serverEntities.Count);

            if (_coroutineTimer.ShouldYield())
            {
                yield return CoroutineEx.waitForEndOfFrame;
                _coroutineTimer.ResetIfYielded();
            }

            var checks = 0;
            foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
            {
                if (entity is BaseEntity)
                {
                    _allEntities.Add(entity.net.ID, entity as BaseEntity);
                }

                if (++checks % 10 == 0 && _coroutineTimer.ShouldYield())
                {
                    yield return CoroutineEx.waitForEndOfFrame;
                    _coroutineTimer.ResetIfYielded();
                }
            }

            if (_coroutineTimer.ShouldYield())
            {
                yield return CoroutineEx.waitForEndOfFrame;
                _coroutineTimer.ResetIfYielded();
            }

            yield return CreateCoroutine(RemoveElementsFromList<StabilityEntity>(_coroutineTimer));
            yield return CreateCoroutine(RemoveElementsFromList<TreeEntity>(_coroutineTimer));
            yield return CreateCoroutine(RemoveElementsFromList<HeldEntity>(_coroutineTimer));
            yield return CreateCoroutine(RemoveElementsFromList<BushEntity>(_coroutineTimer));
            yield return CreateCoroutine(RemoveElementsFromList<ProjectileWeaponMod>(_coroutineTimer));

            if (config.Core.Bags)
            {
                yield return CreateCoroutine(AddElementsToCache<SleepingBag>(_coroutineTimer, cache.Bags, EntityType.Bag));
                cached += cache.Bags.Count;
            }

            if (config.Core.TC)
            {
                yield return CreateCoroutine(AddElementsToCache<BuildingPrivlidge>(_coroutineTimer, cache.Cupboards, EntityType.TC));
                cached += cache.Cupboards.Count;
            }

            if (config.Core.NPCPlayer)
            {
                if (config.Additional.RH)
                {
                    yield return CreateCoroutine(AddElementsToCache<RidableHorse>(_coroutineTimer, cache.RidableHorse, EntityType.Horse));
                    cached += cache.RidableHorse.Count;
                }

                yield return CreateCoroutine(AddElementsToCache<BaseNpc>(_coroutineTimer, cache.Animals, EntityType.Npc));
                yield return CreateCoroutine(AddElementsToCache<SimpleShark>(_coroutineTimer, cache.Animals, EntityType.Npc));
                cached += cache.Animals.Count;

                Func<BaseEntity, bool> validNPC = entity =>
                {
                    return entity is BasePlayer && !entity.IsDestroyed && !((BasePlayer)entity).userID.IsSteamId();
                };
                yield return CreateCoroutine(AddElementsToCache<BasePlayer>(_coroutineTimer, cache.NPCPlayers, EntityType.Npc, validNPC));
                cached += cache.NPCPlayers.Count;
            }

            if (config.Additional.CCTV)
            {
                yield return CreateCoroutine(AddElementsToCache<CCTV_RC>(_coroutineTimer, cache.CCTV, EntityType.CCTV));
                cached += cache.CCTV.Count;
            }

            if (config.Core.Airdrop)
            {
                yield return CreateCoroutine(AddElementsToCache<SupplyDrop>(_coroutineTimer, cache.Airdrops, EntityType.Airdrop));
                cached += cache.Airdrops.Count;
            }

            if (config.Core.Loot || config.Core.Box)
            {
                Func<BaseEntity, EntityInfo> getCachedInfo = entity =>
                {
                    var type = cache.IsLoot(entity) ? EntityType.Loot : entity is StashContainer ? EntityType.Stash : EntityType.Box;
                    return new EntityInfo(entity, type, config.Distance.Get, StripTags);
                };

                Func<BaseEntity, bool> condition = entity =>
                {
                    return cache.IsLoot(entity) || cache.IsBox(entity);
                };

                yield return CreateCoroutine(AddElementsToCacheWithInfo<StorageContainer>(_coroutineTimer, cache.Containers, getCachedInfo, condition));
                cached += cache.Containers.Count;
            }

            if (config.Core.Loot)
            {
                yield return CreateCoroutine(AddElementsToCache<DroppedItemContainer>(_coroutineTimer, cache.Backpacks, EntityType.Backpack));
                cached += cache.Backpacks.Count;
            }

            if (config.Core.Col)
            {
                Func<BaseEntity, EntityInfo> getCachedInfo = entity =>
                {
                    return new EntityInfo(entity, EntityType.Col, config.Distance.Get, StripTags);
                };
                yield return CreateCoroutine(AddElementsToCacheWithInfo<CollectibleEntity>(_coroutineTimer, cache.Collectibles, getCachedInfo));
                cached += cache.Collectibles.Count;
            }

            if (config.Core.Ore)
            {
                Func<BaseEntity, EntityInfo> getCachedInfo = entity =>
                {
                    return new EntityInfo(entity, EntityType.Ore, config.Distance.Get, StripTags);
                };
                yield return CreateCoroutine(AddElementsToCacheWithInfo<OreResourceEntity>(_coroutineTimer, cache.Ores, getCachedInfo));
                cached += cache.Ores.Count;
            }

            if (config.Core.Dead)
            {
                foreach (BaseNetworkable entity in BaseNetworkable.serverEntities)
                {
                    if (entity is PlayerCorpse && !entity.IsDestroyed)
                    {
                        PlayerCorpse corpse = entity as PlayerCorpse;
                        if (corpse.playerSteamID.IsSteamId())
                        {
                            EntityInfo ei;
                            cache.Corpses[corpse.net.ID] = ei = new EntityInfo(corpse, EntityType.Dead, config.Distance.Get);
                            ei.name = corpse.parentEnt?.ToString() ?? corpse.playerSteamID.ToString();
                        }
                    }
                }
                cached += cache.Corpses.Count;
            }

            if (config.Additional.Heli)
            {
                yield return CreateCoroutine(AddElementsToCache<BaseHelicopter>(_coroutineTimer, cache.Helicopters, EntityType.Heli));
                cached += cache.Helicopters.Count;
            }

            if (config.Additional.Bradley)
            {
                yield return CreateCoroutine(AddElementsToCache<BradleyAPC>(_coroutineTimer, cache.BradleyAPCs, EntityType.Bradley));
                cached += cache.BradleyAPCs.Count;
            }

            if (config.Additional.RHIB)
            {
                yield return CreateCoroutine(AddElementsToCache<RHIB>(_coroutineTimer, cache.RHIB, EntityType.RHIB));
                yield return CreateCoroutine(AddElementsToCache<BaseSubmarine>(_coroutineTimer, cache.RHIB, EntityType.RHIB));
                cached += cache.RHIB.Count;
            }

            if (config.Additional.Boats)
            {
                yield return CreateCoroutine(AddElementsToCache<BaseBoat>(_coroutineTimer, cache.Boats, EntityType.Boat));
                cached += cache.Boats.Count;
            }

            if (config.Additional.MC)
            {
                yield return CreateCoroutine(AddElementsToCache<MiniCopter>(_coroutineTimer, cache.MiniCopter, EntityType.Mini));
                yield return CreateCoroutine(AddElementsToCache<Drone>(_coroutineTimer, cache.MiniCopter, EntityType.Mini));
                cached += cache.MiniCopter.Count;
            }

            if (config.Additional.CH47)
            {
                yield return CreateCoroutine(AddElementsToCache<CH47Helicopter>(_coroutineTimer, cache.CH47, EntityType.CH47));
                cached += cache.CH47.Count;
            }

            if (config.Additional.CS)
            {
                yield return CreateCoroutine(AddElementsToCache<CargoShip>(_coroutineTimer, cache.CargoShips, EntityType.CargoShip));
                cached += cache.CargoShips.Count;
            }

            if (config.Additional.Cars)
            {
                yield return CreateCoroutine(AddElementsToCache<BasicCar>(_coroutineTimer, cache.Cars, EntityType.Car));
                yield return CreateCoroutine(AddElementsToCache<ModularCar>(_coroutineTimer, cache.Cars, EntityType.Car));
                cached += cache.Cars.Count;
            }

            if (config.Core.Turrets)
            {
                yield return CreateCoroutine(AddElementsToCache<AutoTurret>(_coroutineTimer, cache.Turrets, EntityType.Turret));
                cached += cache.Turrets.Count;
            }

            if (config.Additional.Traps)
            {
                Func<BaseEntity, bool> condition = entity =>
                {
                    return cache.IsTrap(entity);
                };

                yield return CreateCoroutine(AddElementsToCache<BaseEntity>(_coroutineTimer, cache.Traps, EntityType.Trap, condition));
                cached += cache.Traps.Count;
            }

            if (config.Additional.MLRS)
            {
                yield return CreateCoroutine(AddElementsToCache<MLRSRocket>(_coroutineTimer, cache.MLRS, EntityType.MLRS));
                cached += cache.MLRS.Count;
            }

            if (config.Additional.CP)
            {
                Func<BaseEntity, bool> condition = entity =>
                {
                    return entity.prefabID == 2383782438;
                };
                yield return CreateCoroutine(AddElementsToCache<CargoPlane>(_coroutineTimer, cache.CargoPlanes, EntityType.CargoPlane, condition));
                cached += cache.CargoPlanes.Count;
            }
#if DEBUG
            cache.Print();
#else
            Puts("Cached {0}/{1} entities in {2} seconds!", cached, total, (DateTime.Now - tick).TotalSeconds);
#endif
            _isPopulatingCache = False;
            _allEntities.Clear();
            //Puts("FillCache");
            _coroutines.Pop();
            _coroutines.Push(ServerMgr.Instance.StartCoroutine(FillOnEntitySpawned()));
        }

        private Coroutine CreateCoroutine(IEnumerator operation)
        {
            Coroutine tmp = ServerMgr.Instance.StartCoroutine(operation);

            _coroutines.Push(tmp);
            return tmp;
        }

        private IEnumerator RemoveElementsFromList<TType>(CoroutineTimer timer)
        {
            if (timer.ShouldYield())
            {
                yield return CoroutineEx.waitForEndOfFrame;
                timer.ResetIfYielded();
            }

            if (timer.ShouldYield())
            {
                yield return CoroutineEx.waitForEndOfFrame;
                timer.ResetIfYielded();
            }

#if DEBUG
            Puts($"Start Remove {typeof(TType)}");
#endif
            List<NetworkableId> toRemove = Pool.GetList<NetworkableId>();
            var checks = 0;
            foreach (var pair in _allEntities)
            {
                if (pair.Value.IsKilled() || pair.Value is TType)
                {
                    toRemove.Add(pair.Key);
                }
                if (checks % 10 == 0 && timer.ShouldYield())
                {
                    yield return CoroutineEx.waitForEndOfFrame;
                    timer.ResetIfYielded();
                }
                checks++;
            }

            foreach (var id in toRemove)
            {
                _allEntities.Remove(id);

                if (checks % 10 == 0 && timer.ShouldYield())
                {
                    yield return CoroutineEx.waitForEndOfFrame;
                    timer.ResetIfYielded();
                }
                checks++;
            }

#if DEBUG
            Puts($"End Remove {typeof(TType)}");
#endif
            Pool.FreeList(ref toRemove);
            //Puts("RemoveElementsFromList");
            _coroutines.Pop();
        }

        private IEnumerator AddElementsToCacheWithInfo<TType>(CoroutineTimer timer, Dictionary<NetworkableId, EntityInfo> cachedList, Func<BaseEntity, EntityInfo> getCacheInfoFunc, Func<BaseEntity, bool> condition = null)
        {
            if (timer.ShouldYield())
            {
                yield return CoroutineEx.waitForEndOfFrame;
                timer.ResetIfYielded();
            }

#if DEBUG
            Puts($"Start Caching {typeof(TType)}");
#endif

            List<NetworkableId> idsToRemove = Pool.GetList<NetworkableId>();
            var checks = 0;
            foreach (var pair in _allEntities)
            {
                if (pair.Value.IsKilled() || !pair.Value.IsValid())
                {
                    idsToRemove.Add(pair.Key);
                }
                else if (pair.Value is TType && !cachedList.ContainsKey(pair.Key) && (condition == null || condition(pair.Value)))
                {
                    var ei = getCacheInfoFunc(pair.Value);
                    cachedList.Add(pair.Key, ei);
                    idsToRemove.Add(pair.Key);
                    _radars.ForEach(radar => radar.TryCacheByType(ei.type, ei));
                }
                if (checks % 10 == 0 && timer.ShouldYield())
                {
                    yield return CoroutineEx.waitForEndOfFrame;
                    timer.ResetIfYielded();
                }
                checks++;
            }

            foreach (var key in idsToRemove)
            {
                _allEntities.Remove(key);
                if (checks % 10 == 0 && timer.ShouldYield())
                {
                    yield return CoroutineEx.waitForEndOfFrame;
                    timer.ResetIfYielded();
                }
                checks++;
            }

#if DEBUG
            Puts($"End Caching {typeof(TType)}");
#endif
            Pool.FreeList(ref idsToRemove);
            //Puts("AddElementsToCacheWithInfo");
            _coroutines.Pop();
        }

        private IEnumerator AddElementsToCache<TLookFor>(CoroutineTimer timer, Dictionary<NetworkableId, EntityInfo> cachedList, EntityType type, Func<BaseEntity, bool> condition = null) where TLookFor : BaseEntity
        {
            if (timer.ShouldYield())
            {
                yield return CoroutineEx.waitForEndOfFrame;
                timer.ResetIfYielded();
            }

#if DEBUG
            Puts($"Start Caching {typeof(TLookFor)}");
#endif
            List<NetworkableId> idsToRemove = Pool.GetList<NetworkableId>();
            var checks = 0;
            foreach (var pair in _allEntities)
            {
                if (pair.Value.IsKilled())
                {
                    idsToRemove.Add(pair.Key);
                }
                else if (pair.Value is TLookFor && !cachedList.ContainsKey(pair.Key) && (condition == null || condition(pair.Value)))
                {
                    var ei = new EntityInfo(pair.Value, type, config.Distance.Get, StripTags);
                    cachedList.Add(pair.Key, ei);
                    idsToRemove.Add(pair.Key);
                    _radars.ForEach(radar => radar.TryCacheByType(ei.type, ei));
                }
                if (checks % 10 == 0 && timer.ShouldYield())
                {
                    yield return CoroutineEx.waitForEndOfFrame;
                    timer.ResetIfYielded();
                }
                checks++;
            }

            foreach (var key in idsToRemove)
            {
                _allEntities.Remove(key);
                if (checks % 10 == 0 && timer.ShouldYield())
                {
                    yield return CoroutineEx.waitForEndOfFrame;
                    timer.ResetIfYielded();
                }
                checks++;
            }

#if DEBUG
            Puts($"End Caching {typeof(TLookFor)}");
#endif
            Pool.FreeList(ref idsToRemove);
            //Puts("AddElementsToCache");
            _coroutines.Pop();
        }

        [ConsoleCommand("espgui")]
        private void ccmdESPGUI(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs())
                return;

            var player = arg.Player();

            if (!player || !HasAccess(player))
                return;

            if (arg.Args.Contains(config.GUI.Arrow) || arg.Args.Contains("move"))
            {
                ccmdMovePosition(player, "espgui", arg.Args);
                return;
            }

            RadarCommandX(player, "espgui", arg.Args);
        }

        private void ccmdMovePosition(BasePlayer player, string command, string[] args)
        {
            var radar = _radars.Find(x => x.player == player);
            if (args.Length == 1)
            {
                ShowRadarUi(player, radar, False);
                ShowMoveUi(player, True);
                return;
            }
            UiOffsets offsets;
            if (!data.Offsets.TryGetValue(player.userID, out offsets))
            {
                return;
            }
            string[] offsetMin = offsets.Min.Split(' ');
            string[] offsetMax = offsets.Max.Split(' ');
            int n = player.serverInput.IsDown(BUTTON.DUCK) ? 1 : player.serverInput.IsDown(BUTTON.SPRINT) ? 50 : 15;
            switch (args[1])
            {
                case "left":
                    offsets.Min = $"{Convert.ToSingle(offsetMin[0]) - n} {offsetMin[1]}";
                    offsets.Max = $"{Convert.ToSingle(offsetMax[0]) - n} {offsetMax[1]}";
                    break;
                case "right":
                    offsets.Min = $"{Convert.ToSingle(offsetMin[0]) + n} {offsetMin[1]}";
                    offsets.Max = $"{Convert.ToSingle(offsetMax[0]) + n} {offsetMax[1]}";
                    break;
                case "up":
                    offsets.Min = $"{offsetMin[0]} {Convert.ToSingle(offsetMin[1]) + n}";
                    offsets.Max = $"{offsetMax[0]} {Convert.ToSingle(offsetMax[1]) + n}";
                    break;
                case "down":
                    offsets.Min = $"{offsetMin[0]} {Convert.ToSingle(offsetMin[1]) - n}";
                    offsets.Max = $"{offsetMax[0]} {Convert.ToSingle(offsetMax[1]) - n}";
                    break;
            }
            offsets.changed = True;
            data.Offsets[player.userID] = offsets;
            ShowRadarUi(player, radar, True);
        }

        private void RadarCommand(IPlayer user, string command, string[] args)
        {
            var player = user.Object as BasePlayer;

            if (!player)
            {
                user.Message("Not a player!");
                return;
            }

            RadarCommandX(player, command, args);
        }

        private void RadarCommandX(BasePlayer player, string command, string[] args)
        {
            if (args.Contains("list") && permission.UserHasPermission(player.UserIDString, "adminradar.list"))
            {
                Message(player, "ActiveRadars", string.Join(", ", _radars.Select(x => x.username)));
                return;
            }

            if (!HasAccess(player))
            {
                Message(player, player.Connection.authLevel > 0 ? "NotAllowed" : $"Unknown command: {command}");
                return;
            }

            args = args.ToLower(x => x != "True");

            List<string> filters;
            if (!data.Filters.TryGetValue(player.UserIDString, out filters))
            {
                data.Filters.Add(player.UserIDString, filters = new List<string>());
            }

            if (args.Length == 0)
            {
                if (DestroyRadar(player))
                {
                    return;
                }
            }
            else
            {
                switch (args[0])
                {
                    case "move":
                        {
                            var offsets = GetOffsets(player);
                            offsets.Mover = !offsets.Mover;
                            offsets.changed = True;
                            args = Array.FindAll(args, x => x != args[0]);
                        }
                        break;
                    case "reset":
                        {
                            data.Offsets.Remove(player.userID);
                            args = Array.FindAll(args, x => x != args[0]);
                        }
                        break;

                    case "buildings":
                        {
                            if (config.Options.BuildingsDrawTime > 0)
                            {
                                DrawBuildings(player, args.Contains("raid"), args.Contains("twig"));
                            }
                        }
                        return;
                    case "drops":
                        {
                            if (config.Options.DropsDrawTime > 0)
                            {
                                _coroutines.Push(ServerMgr.Instance.StartCoroutine(DrawDropsRoutine(player)));
                            }
                        }
                        return;
                    case "findbyid":
                        {
                            if (config.Options.FindByIDDrawTime > 0)
                            {
                                if (args.Length != 2)
                                {
                                    Player.Message(player, $"{command} findbyid id");
                                    return;
                                }
                                ulong userid;
                                if (!ulong.TryParse(args[1], out userid))
                                {
                                    Player.Message(player, $"Invalid steam id: {userid}");
                                    return;
                                }
                                _coroutines.Push(ServerMgr.Instance.StartCoroutine(FindByIDRoutine(player, userid)));
                            }
                        }
                        return;
                    case "find":
                        if (args.Length > 1 && config.Options.FindDrawTime > 0)
                        {
                            _coroutines.Push(ServerMgr.Instance.StartCoroutine(DrawObjectsRoutine(player, args[1])));
                        }
                        return;
                    case "online":
                        {
                            if (!data.OnlineBoxes.Remove(player.UserIDString)) data.OnlineBoxes.Add(player.UserIDString);

                            Message(player, data.OnlineBoxes.Contains(player.UserIDString) ? "BoxesOnlineOnly" : "BoxesAll");
                        }
                        return;
                    case "vision":
                        {
                            if (!data.Visions.Remove(player.UserIDString)) data.Visions.Add(player.UserIDString);

                            Message(player, data.Visions.Contains(player.UserIDString) ? "VisionOn" : "VisionOff");
                        }
                        return;
                    case "ext":
                        {
                            if (!data.Extended.Remove(player.UserIDString)) data.Extended.Add(player.UserIDString);

                            Message(player, data.Extended.Contains(player.UserIDString) ? "ExtendedPlayersOn" : "ExtendedPlayersOff");
                        }
                        return;
                    case "help":
                        {
                            Message(player, "Help1", string.Join(", ", GetButtonNames().Keys) + ", HT");
                            Message(player, "Help2", command, "online");
                            Message(player, "Help3", command, "ui");
                            Message(player, "Help7", command, "vision");
                            Message(player, "Help8", command, "ext");
                            Message(player, "Help9", command, config.Distance.Loot);
                            Message(player, "Help5", command);
                            Message(player, "Help6", command);
                            Message(player, "PreviousFilter", command);
                        }
                        return;
                    case "ui":
                        {
                            if (!data.Hidden.Remove(player.UserIDString))
                            {
                                data.Hidden.Add(player.UserIDString);

                                DestroyUI(player);
                            }

                            Message(player, data.Hidden.Contains(player.UserIDString) ? "GUIHidden" : "GUIShown");

                            args = filters.Where(x => x != args[0]);
                        }
                        break;
                    case "f":
                        {
                            args = filters.ToArray();
                        }
                        break;
                }
            }

            if (command == "espgui")
            {
                foreach (var filter in args)
                {
                    if (!filters.Remove(filter))
                    {
                        filters.Add(filter);
                    }
                }
                args = filters.ToArray();
            }
            else
            {
                if (config.Settings.Cooldown > 0f && _cooldowns.ContainsKey(player.UserIDString))
                {
                    float cooldown = _cooldowns[player.UserIDString] - Time.realtimeSinceStartup;

                    if (cooldown > 0)
                    {
                        Message(player, "WaitCooldown", cooldown);
                        return;
                    }

                    _cooldowns.Remove(player.UserIDString);
                }

                if (args.Length == 0) data.Filters.Remove(player.UserIDString);
                else data.Filters[player.UserIDString] = args.ToList();
            }

            Radar radar = player.GetComponent<Radar>();

            if (radar == null)
            {
                radar = player.gameObject.AddComponent<Radar>();

                radar.Init(this);
            }

            float invokeTime, maxDistance, outTime, outDistance;

            if (args.Length > 0 && float.TryParse(args[0], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out outTime))
            {
                invokeTime = outTime < 0.1f ? 0.1f : outTime;
            }
            else invokeTime = config.Settings.DefaultInvokeTime;

            if (args.Length > 1 && float.TryParse(args[1], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out outDistance))
            {
                maxDistance = outDistance <= 0f ? config.Settings.DefaultMaxDistance : outDistance;
            }
            else maxDistance = config.Settings.DefaultMaxDistance;

            radar.showAll = config.GUI.All && isArg(args, "all");
            radar.showHT = isArg(args, "ht");
            radar.entityTypes.Clear();

            int limit = config.Settings.GetLimit(player);

            if (limit > 0)
            {
                if (radar.showAll)
                {
                    args = Array.FindAll(args, x => x != "all");

                    radar.showAll = false;
                }

                if (args.Length > limit)
                {
                    args = args.Take(limit);
                }

                data.Filters[player.UserIDString] = args.ToList();
            }

            foreach (EntityType type in _allEntityTypes)
            {
                if (_errorTypes.Contains(type))
                {
                    continue;
                }
                if (radar.showAll || isArg(args, type.ToString().ToLower()))
                {
                    radar.Add(type);
                }
                else if (config.Additional.Get(type) && !config.GUI.Get(type))
                {
                    radar.Add(type);
                }
            }

            if (config.Limit.Enabled)
            {
                radar.Add(EntityType.Limit);
            }

            if (config.Core.Active)
            {
                radar.Add(EntityType.Active);
            }

            if (config.Settings.UI && !data.Hidden.Contains(player.UserIDString))
            {
                ShowRadarUi(player, radar, False);
            }

            if (!data.Active.Contains(player.UserIDString))
            {
                data.Active.Add(player.UserIDString);
            }

            if (config.Settings.ShowToggle && command != "espgui")
            {
                Message(player, "Activated", invokeTime, maxDistance, command);
            }

            if (radar.maxDistance != maxDistance)
            {
                radar.maxDistance = maxDistance;
                radar.group = null;
            }

            radar.invokeTime = Mathf.Max(0.1f, invokeTime, config.Settings.MinInvokeTime);

            radar.SetupFilters(config.Settings.Barebones);
        }

        private void Init()
        {
            isUnloading = False;
            _isPopulatingCache = True;
            cache = new Cache(this);
            Unsubscribe(nameof(OnPlayerRespawned));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnPlayerVoice));
            Unsubscribe(nameof(OnPlayerSleepEnded));
            Unsubscribe(nameof(OnRadarActivated));
            Unsubscribe(nameof(OnRadarDeactivated));
            permission.RegisterPermission("adminradar.allowed", this);
            permission.RegisterPermission("adminradar.bypass", this);
            permission.RegisterPermission("adminradar.auto", this);
            permission.RegisterPermission("adminradar.bypass.override", this);
            permission.RegisterPermission("adminradar.list", this);
            LoadData();
            RegisterCommands();
        }

        private void Unload()
        {
            isUnloading = True;
            StopFillCache();
            foreach (var radar in _radars.ToList())
            {
                radar.StopAll();
                _radars.Remove(radar);
                UnityEngine.Object.Destroy(radar);
            }
            StringBuilderCache.Clear();
            SaveData();
        }

        private void OnServerInitialized()
        {
            RemoveNonAuthorizedOffsetData();

            if (!config.Methods.Box && !config.Methods.Text && !config.Methods.Arrow)
            {
                Puts("Configuration does not have a chosen drawing method. Setting drawing method to text.");
                config.Methods.Text = True;
            }

            if (config.Voice.Enabled)
            {
                Subscribe(nameof(OnPlayerVoice));
            }

            if (config.Settings.Barebones)
            {
                return;
            }

            _coroutines.Push(ServerMgr.Instance.StartCoroutine(FillCache()));

            if (data.Active.Count > 0)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (HasAccess(player) && data.Active.Contains(player.UserIDString))
                    {
                        DelayedInvoke(player);
                    }
                }
            }

            if (permission.GetPermissionUsers("adminradar.auto").Length > 0)
            {
                Subscribe(nameof(OnPlayerSleepEnded));
            }

            if (_sendDiscordMessages)
            {
                Subscribe(nameof(OnRadarActivated));
                Subscribe(nameof(OnRadarDeactivated));
            }

            Subscribe(nameof(OnEntitySpawned));
            SetupClanTeamColors();
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (config.Settings.Barebones)
            {
                _radars.ForEach(radar => radar.TryCacheOnlinePlayer(player));
            }
            else
            {
                _spawnedEntities.Add(player);
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (HasAccess(player) && permission.UserHasPermission(player.UserIDString, "adminradar.auto"))
            {
                DelayedInvoke(player);
            }
        }

        private void OnPlayerVoice(BasePlayer player, byte[] data)
        {
            Timer voice;
            if (!_voices.TryGetValue(player.userID, out voice))
            {
                float max = config.Settings.DefaultInvokeTime;

                foreach (var radar in _radars)
                {
                    max = Mathf.Max(radar.invokeTime, max);
                }

                ulong userid = player.userID;

                _voices.Add(userid, timer.Once(config.Voice.Interval + max, () => _voices.Remove(userid)));
            }
            else voice.Reset();
        }

        private void OnPlayerTrackStarted(BasePlayer player, ulong targetId)
        {
            if (player.userID != targetId)
            {
                _radars.Find(x => x.userid == player.UserIDString)?.exclude?.Add(targetId);
            }
        }

        private void OnPlayerTrackEnded(BasePlayer player, ulong targetId)
        {
            if (player.userID != targetId)
            {
                _radars.Find(x => x.userid == player.UserIDString)?.exclude?.Remove(targetId);
            }
        }

        private void OnRadarActivated(BasePlayer player, string playerName, string playerId, Vector3 lastPosition)
        {
            AdminRadarDiscordMessage(playerName, playerId, True, lastPosition);
        }

        private void OnRadarDeactivated(BasePlayer player, string playerName, string playerId, Vector3 lastPosition)
        {
            AdminRadarDiscordMessage(playerName, playerId, False, lastPosition);
        }

        private void OnEntitySpawned(BaseEntity entity)
        {
            _spawnedEntities.Add(entity);
        }

        private void OnEntityDeath(BaseEntity entity, HitInfo info)
        {
            if (entity.IsValid())
            {
                _despawnedEntities[entity.net.ID] = entity.transform.position;
            }
        }

        private void OnEntityKill(BaseEntity entity)
        {
            if (entity.IsValid())
            {
                _despawnedEntities[entity.net.ID] = entity.transform.position;
            }
        }

        private void OnTeamCreated(BasePlayer player, RelationshipManager.PlayerTeam team)
        {
            string hex = $"#{Core.Random.Range(0x1000000):X6}";
            _teamColors[team.teamID] = hex;
            Interface.CallHook("OnTeamCreatedColor", team.teamID, hex);
        }

        private void OnClanCreate(string tag)
        {
            string hex = $"#{Core.Random.Range(0x1000000):X6}";
            _clanColors[tag] = hex;
            Interface.CallHook("OnClanCreateColor", tag, hex);
        }

        private string GetClanColor(ulong targetId)
        {
            string clan = GetClanOf(targetId);

            if (string.IsNullOrEmpty(clan) || !_clanColors.ContainsKey(clan))
            {
                return null;
            }

            return _clanColors[clan];
        }

        private Dictionary<string, string> GetAllClanColors() => _clanColors;

        private Dictionary<ulong, string> GetAllTeamColors() => _teamColors;

        private string GetTeamColor(ulong id)
        {
            if (id.IsSteamId())
            {
                RelationshipManager.PlayerTeam team;
                if (!RelationshipManager.ServerInstance.playerToTeam.TryGetValue(id, out team))
                {
                    return null;
                }

                id = team.teamID;
            }

            if (!_teamColors.ContainsKey(id))
            {
                return null;
            }

            return _teamColors[id];
        }

        private string GetClanOf(ulong playerId)
        {
            string clan;
            if (!_clans.TryGetValue(playerId, out clan))
            {
                _clans[playerId] = clan = Clans?.Call("GetClanOf", playerId) as string ?? string.Empty;
                timer.Once(30f, () => _clans.Remove(playerId));
            }

            return clan;
        }

        private void SetupClanTeamColors()
        {
            foreach (var team in RelationshipManager.ServerInstance.teams)
            {
                _teamColors[team.Key] = $"#{Core.Random.Range(0x1000000):X6}";
            }

            Interface.CallHook("OnTeamColorsInitialized", _teamColors);

            var clans = Clans?.Call("GetAllClans");

            if (clans is JArray)
            {
                foreach (var token in (JArray)clans)
                {
                    _clanColors[token.ToString()] = $"#{Core.Random.Range(0x1000000):X6}";
                }
            }

            Interface.CallHook("OnClanColorsInitialized", _clanColors);
        }

        private bool DestroyRadar(BasePlayer player)
        {
            foreach (var x in _radars)
            {
                if (x.player == player)
                {
                    data.Active.Remove(player.UserIDString);
                    UnityEngine.Object.Destroy(x);
                    _radars.Remove(x);
                    return True;
                }
            }
            return False;
        }

        private bool IsRadar(string id)
        {
            return _radars.Exists(radar => radar.userid == id) ? True : False;
        }

        public void AdminCommand(BasePlayer player, Action action)
        {
            bool isAdmin = player.IsAdmin;
            if (!isAdmin && !player.IsDeveloper && player.IsFlying)
            {
                return;
            }
            if (!isAdmin)
            {
                player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, True);
                player.SendNetworkUpdateImmediate();
            }
            try
            {
                action();
            }
            finally
            {
                if (!isAdmin)
                {
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.IsAdmin, False);
                    player.SendNetworkUpdateImmediate();
                }
            }
        }

        private static Color __(string value)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(value.StartsWith("#") ? value : $"#{value}", out color) ? color : Color.white;
        }

        private string StripTags(string value)
        {
            var sb = StringBuilderCache.Acquire(value);

            foreach (string str in _tags)
            {
                sb.Replace(str, string.Empty);
            }

            return StringBuilderCache.GetStringAndRelease(sb);
        }

        private bool HasAccess(BasePlayer player)
        {
            if ((object)player == null)
                return False;

            if (DeveloperList.Contains(player.userID))
                return True;

            if (config.Settings.Authorized.Count > 0)
                return config.Settings.Authorized.Contains(player.UserIDString);

            if (permission.UserHasPermission(player.UserIDString, "adminradar.allowed"))
                return True;

            if (player.IsConnected && player.net.connection.authLevel >= config.Settings.authLevel)
                return True;

            return False;
        }

        private bool isArg(string[] args, string val, bool equalTo = True)
        {
            if (equalTo)
            {
                return Array.Exists(args, arg => arg.Equals(val));
            }
            return Array.Exists(args, arg => arg.Contains(val) || val.Contains(arg));
        }

        private void DrawBuildings(BasePlayer player, bool showNonPlayerBases, bool showTwigOnly)
        {
            var objects = Pool.GetList<object[]>();
            foreach (var building in BuildingManager.server.buildingDictionary.Values)
            {
                if (!building.HasBuildingBlocks()) continue;
                foreach (var block in building.buildingBlocks)
                {
                    if (block.IsKilled()) continue;
                    if (showNonPlayerBases && block.OwnerID.IsSteamId()) continue;
                    if (!showNonPlayerBases && !block.OwnerID.IsSteamId()) continue;
                    if (showTwigOnly && block.grade != BuildingGrade.Enum.Twigs) continue;
                    var targetName = covalence.Players.FindPlayerById(block.OwnerID.ToString())?.Name;
                    if (string.IsNullOrEmpty(targetName)) targetName = block.OwnerID.IsSteamId() ? block.OwnerID.ToString() : "No owner";
                    objects.Add(new object[2] { block.transform.position, $"<size={config.Settings.PlayerTextSize}>{targetName}</size>" });
                    break;
                }
            }
            AdminCommand(player, () =>
            {
                foreach (var obj in objects)
                {
                    player.SendConsoleCommand("ddraw.text", config.Options.BuildingsDrawTime, Color.red, obj[0], obj[1]);
                }
            });
            Pool.FreeList(ref objects);
        }

        private IEnumerator FindByIDRoutine(BasePlayer player, ulong userID)
        {
            Message(player, "ProcessRequest");
            int checks = 0;
            var objects = Pool.GetList<object[]>();
            foreach (BaseEntity entity in BaseNetworkable.serverEntities.OfType<BaseEntity>())
            {
                if (entity is BuildingPrivlidge && (entity as BuildingPrivlidge).IsAuthed(userID))
                {
                    objects.Add(new object[2] { Color.cyan, entity.transform.position });
                }
                else if (entity?.OwnerID == userID || entity is CodeLock && (entity as CodeLock).whitelistPlayers.Contains(userID))
                {
                    objects.Add(new object[2] { Color.red, entity.transform.position });
                }
                else if (entity is SleepingBag && (entity as SleepingBag).deployerUserID == userID)
                {
                    objects.Add(new object[2] { Color.green, entity.transform.position });
                }
                else if (entity is AutoTurret && (entity as AutoTurret).IsAuthed(userID))
                {
                    objects.Add(new object[2] { Color.blue, entity.transform.position });
                }
                if (++checks % 200 == 0)
                {
                    yield return CoroutineEx.waitForSeconds(0.0025f);
                }
            }
            AdminCommand(player, () =>
            {
                foreach (var obj in objects)
                {
                    player.SendConsoleCommand("ddraw.text", config.Options.FindByIDDrawTime, obj[0], obj[1], userID);
                }
            });
            Message(player, "ProcessRequestFinished", objects.Count);
            Pool.FreeList(ref objects);
        }

        private IEnumerator DrawObjectsRoutine(BasePlayer player, string value)
        {
            Message(player, "ProcessRequest");
            int checks = 0;
            var objects = Pool.GetList<object[]>();
            foreach (var e in BaseNetworkable.serverEntities)
            {
                if (e.ShortPrefabName.Contains(value, CompareOptions.OrdinalIgnoreCase) || value == "electrical" && e is IOEntity)
                {
                    objects.Add(new object[3] { e.transform.position, e.ShortPrefabName, Mathf.CeilToInt(Vector3.Distance(e.transform.position, player.transform.position)) });
                }
                if (++checks % 200 == 0)
                {
                    yield return CoroutineEx.waitForSeconds(0.0025f);
                }
            }
            AdminCommand(player, () =>
            {
                foreach (var obj in objects)
                {
                    player.SendConsoleCommand("ddraw.text", config.Options.FindDrawTime, Color.red, obj[0], $"<size={config.Settings.PlayerTextSize}>{obj[1]} {obj[2]}</size>");
                }
            });
            Message(player, "ProcessRequestFinished", objects.Count);
            Pool.FreeList(ref objects);
        }

        private IEnumerator DrawDropsRoutine(BasePlayer player)
        {
            Message(player, "ProcessRequest");
            int checks = 0;
            var objects = Pool.GetList<object[]>();
            foreach (var entity in BaseNetworkable.serverEntities)
            {
                if (entity is DroppedItem || entity is Landmine || entity is BearTrap || entity is DroppedItemContainer || entity is RFTimedExplosive)
                {
                    if (config.Settings.DropExceptions.Exists(entity.ShortPrefabName.Contains))
                    {
                        continue;
                    }
                    var currDistance = Mathf.CeilToInt(Vector3.Distance(entity.transform.position, player.transform.position));
                    if (currDistance <= config.Distance.Drops)
                    {
                        objects.Add(new object[2] { entity.transform.position, $"{entity.ShortPrefabName} <color=#FFFF00>{currDistance}</color>" });
                    }
                }
                if (++checks % 200 == 0)
                {
                    yield return CoroutineEx.waitForSeconds(0.0025f);
                }
            }
            AdminCommand(player, () =>
            {
                foreach (var obj in objects)
                {
                    if (config.Methods.Text) player.SendConsoleCommand("ddraw.text", config.Options.DropsDrawTime, Color.red, obj[0], obj[1]);
                    if (config.Methods.Box) player.SendConsoleCommand("ddraw.box", config.Options.DropsDrawTime, Color.red, obj[0], 0.25f);
                }
            });
            Message(player, "ProcessRequestFinished", objects.Count);
            Pool.FreeList(ref objects);
        }

        private void LoadData()
        {
            try
            {
                data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);
            }
            catch (Exception ex) { UnityEngine.Debug.LogException(ex); }

            if (data == null)
            {
                data = new StoredData();
            }
        }

        private void RemoveNonAuthorizedOffsetData()
        {
            foreach (ulong userid in data.Offsets.Keys.ToList())
            {
                if (permission.UserHasPermission(userid.ToString(), "adminradar.allowed"))
                {
                    continue;
                }
                if (DeveloperList.Contains(userid.ToString()))
                {
                    continue;
                }
                var user = ServerUsers.Get(userid);
                if (user?.group == ServerUsers.UserGroup.Owner)
                {
                    continue;
                }
                if (user?.group == ServerUsers.UserGroup.Moderator)
                {
                    continue;
                }
                data.Offsets.Remove(userid);
            }
        }

        private void SaveOffsetData()
        {
            bool changed = False;
            foreach (var obj in data.Offsets.ToList())
            {
                if (obj.Value.Equals(DefaultOffset) && obj.Value.Mover)
                {
                    data.Offsets.Remove(obj.Key);
                }
                if (obj.Value.changed)
                {
                    obj.Value.changed = False;
                    changed = True;
                }
            }
            if (changed && saveTimer == null)
            {
                saveTimer = timer.Once(300f, SaveData);
            }
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, data);
        }

        private void DelayedInvoke(BasePlayer player)
        {
            player.Invoke(() =>
            {
                if (!player.IsDestroyed && !IsRadar(player.UserIDString))
                {
                    RadarCommandX(player, "radar", data.Filters.ContainsKey(player.UserIDString) ? data.Filters[player.UserIDString].ToArray() : new string[0]);
                }
            }, UnityEngine.Random.Range(0.1f, 1f));
        }

        private Timer saveTimer;

        #region UI

        private List<ulong> isMovingUi = new List<ulong>();
        private List<string> radarUI = new List<string>();
        private const string RadarPanelName = "AdminRadar_UI";
        private const double S_X = 49.14;
        private const double S_Y = 22.03;
        private UiOffsets DefaultOffset;

        public void DestroyUI(BasePlayer player)
        {
            if (radarUI.Remove(player.UserIDString))
            {
                CuiHelper.DestroyUi(player, RadarPanelName);
            }
        }

        public static void AddCuiPanel(CuiElementContainer container, bool cursor, string color, string amin, string amax, string omin, string omax, string parent, string name)
        {
            container.Add(new CuiPanel
            {
                CursorEnabled = cursor,
                Image = { Color = color },
                RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
            }, parent, name, name);
        }

        public static void AddCuiButton(CuiElementContainer container, string buttonColor, string command, string text, string textColor, int fontSize, TextAnchor align, string amin, string amax, string omin, string omax, string parent, string name, string font = "robotocondensed-regular.ttf")
        {
            container.Add(new CuiButton
            {
                Button = { Color = buttonColor, Command = command },
                Text = { Text = text, Font = font, FontSize = fontSize, Align = align, Color = textColor },
                RectTransform = { AnchorMin = amin, AnchorMax = amax, OffsetMin = omin, OffsetMax = omax }
            }, parent, name, name);
        }

        private UiOffsets GetOffsets(BasePlayer player)
        {
            UiOffsets offsets;
            if (!data.Offsets.TryGetValue(player.userID, out offsets))
            {
                if (!string.IsNullOrEmpty(config.GUI.Arrow))
                {
                    Message(player, "Radar UI Help", $"{radarCommand} move|reset");
                }

                data.Offsets[player.userID] = offsets = new UiOffsets(DefaultOffset.Min, DefaultOffset.Max);
            }
            return offsets;
        }

        private void ShowRadarUi(BasePlayer player, Radar radar, bool showMoveUi)
        {
            var container = new CuiElementContainer();
            var buttons = GetButtonNames().ToList();
            var offsets = GetOffsets(player);
            var rowLeftToRight = 0;
            var colTopToBottom = 0;

            if (config.GUI.All)
            {
                buttons.Insert(0, new KeyValuePair<string, EntityType>(m("All", player.UserIDString), (EntityType)0));
            }

            if (offsets.Mover && !string.IsNullOrEmpty(config.GUI.Arrow))
            {
                buttons.Insert(buttons.Count, new KeyValuePair<string, EntityType>(config.GUI.Arrow, (EntityType)0));
            }

            AddCuiPanel(container, False, "0 0 0 0", "0.5 0", "0.5 0", offsets.Min, offsets.Max, "Overlay", RadarPanelName);

            foreach (var obj in buttons)
            {
                var color = obj.Key == config.GUI.Arrow ? config.GUI.Off : obj.Key == "All" ? (radar.showAll ? config.GUI.On : config.GUI.Off) : (radar.GetBool(obj.Value) ? config.GUI.On : config.GUI.Off);
                var offsetMin = $"{-22.956 + (rowLeftToRight * S_X)} {-9.571 - (colTopToBottom * S_Y)}";
                var offsetMax = $"{22.956 + (rowLeftToRight * S_X)} {9.571 - (colTopToBottom * S_Y)}";
                var text = m(obj.Key, player.UserIDString);
                var command = $"espgui {obj.Key}";

                AddCuiButton(container, color, command, text, "1 1 1 1", 8, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", offsetMin, offsetMax, RadarPanelName, $"BTN_{rowLeftToRight}_{colTopToBottom}");

                if (++rowLeftToRight >= 5)
                {
                    rowLeftToRight = 0;
                    colTopToBottom++;
                }
            }

            if (!radarUI.Contains(player.UserIDString))
            {
                radarUI.Add(player.UserIDString);
            }

            CuiHelper.AddUi(player, container);

            if (showMoveUi)
            {
                ShowMoveUi(player, False);
            }
        }

        public void ShowMoveUi(BasePlayer player, bool destroyUi)
        {
            string name = $"{RadarPanelName}_MOVE";

            if (destroyUi && isMovingUi.Remove(player.userID))
            {
                CuiHelper.DestroyUi(player, name);
                SaveOffsetData();
                return;
            }
            else if (!isMovingUi.Contains(player.userID))
            {
                isMovingUi.Add(player.userID);
            }

            ulong userid = player.userID;
            var container = new CuiElementContainer();

            AddCuiPanel(container, True, "0 0 1 0.6", "0.5 1", "0.5 1", "58.804 -16.298", "137.604 7.102", RadarPanelName, name);
            AddCuiButton(container, "0 0 0.75 0.6", $"espgui move left", "←", "1 1 1 1", 10, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "-35.484 -7.548", "-17.742 7.548", name, $"{name}_L");
            AddCuiButton(container, "0 0 0.75 0.6", $"espgui move up", "↑", "1 1 1 1", 10, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "-17.743 -7.548", "0 7.548", name, $"{name}_T");
            AddCuiButton(container, "0 0 0.75 0.6", $"espgui move down", "↓", "1 1 1 1", 10, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "-0.001 -7.548", "17.742 7.548", name, $"{name}_B");
            AddCuiButton(container, "0 0 0.75 0.6", $"espgui move right", "→", "1 1 1 1", 10, TextAnchor.MiddleCenter, "0.5 0.5", "0.5 0.5", "17.742 -7.548", "35.485 7.548", name, $"{name}_R");

            CuiHelper.AddUi(player, container);
        }

        public SortedDictionary<string, EntityType> GetButtonNames()
        {
            var buttons = new SortedDictionary<string, EntityType>();

            foreach (EntityType type in _allEntityTypes)
            {
                if (config.GUI.Get(type))
                {
                    buttons.Add(type.ToString(), type);
                }
            }

            return buttons;
        }

        public class UiOffsets
        {
            [JsonIgnore]
            public bool changed;
            public bool Mover = True;
            public string Min;
            public string Max;
            public UiOffsets() { }
            public UiOffsets(string min, string max)
            {
                Min = min;
                Max = max;
            }
            public bool Equals(UiOffsets other)
            {
                if (other != null && other.Min == Min)
                {
                    return other.Max == Max;
                }
                return False;
            }
        }

        #endregion

        #region Config

        public bool _sendDiscordMessages;

        private void AdminRadarDiscordMessage(string playerName, string playerId, bool state, Vector3 position)
        {
            if (isUnloading || DiscordMessages == null)
            {
                return;
            }

            var text = state ? config.Discord.On : config.Discord.Off;
            var grid = PhoneController.PositionToGridCoord(position);
            var message = $"[{DateTime.Now}] {playerName} ({playerId} @ {grid}): {text}";
            var chatEntry = new ConVar.Chat.ChatEntry { Message = message, UserId = playerId, Username = playerName, Time = Facepunch.Math.Epoch.Current };
            var steam = $"[{playerName}](https://steamcommunity.com/profiles/{playerId})";
            var server = $"steam://connect/{ConVar.Server.ip}:{ConVar.Server.port}";

            object fields = new[]
            {
                new { name = config.Discord.Player, value = steam, inline = True },
                new { name = config.Discord.Message, value = text, inline = False },
                new { name = config.Discord.Server, value = server, inline = False },
                new { name = config.Discord.Location, value = grid, inline = False }
            };

            LogToFile("toggles", message, this, False);
            RCon.Broadcast(RCon.LogType.Chat, chatEntry);
            Interface.CallHook("API_SendFancyMessage", config.Discord.Webhook, config.Discord.Title, config.Discord.Color, JsonConvert.SerializeObject(fields), null, this);
        }

        private static List<string> ItemExceptions
        {
            get
            {
                return new List<string> { "bottle", "planner", "rock", "torch", "can.", "arrow." };
            }
        }

        private Configuration config;

        private string GetGroupColor(int index)
        {
            if (config.Limit.ColorsEnabled)
            {
                string color;
                if (config.Limit.Colors.TryGetValue(index.ToString(), out color))
                {
                    return color;
                }
            }

            return config.Limit.Basic;
        }

        private static Dictionary<string, string> DefaultColors
        {
            get
            {
                return new Dictionary<string, string>
                {
                    ["0"] = "#FF00FF", // magenta
                    ["1"] = "#008000", // green
                    ["2"] = "#0000FF", // blue
                    ["3"] = "#FFA500", // orange
                    ["4"] = "#FFFF00" // yellow
                };
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NotAllowed"] = "You are not allowed to use this command.",
                ["PreviousFilter"] = "To use your previous filter type <color=#FFA500>/{0} f</color>",
                ["Activated"] = "ESP Activated - {0}s refresh - {1}m distance. Use <color=#FFA500>/{2} help</color> for help.",
                ["Deactivated"] = "ESP Deactivated.",
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
                ["Help9"] = "<color=#FFA500>/{0} drops</color> - Show all dropped items within {1}m.",
                ["NoActiveRadars"] = "No one is using Radar at the moment.",
                ["ActiveRadars"] = "Active radar users: {0}",
                ["All"] = "All",
                ["Bags"] = "Bags",
                ["Box"] = "Box",
                ["Collectibles"] = "Collectibles",
                ["Dead"] = "Dead",
                ["Loot"] = "Loot",
                ["Ore"] = "Ore",
                ["Sleepers"] = "Sleepers",
                ["Stash"] = "Stash",
                ["TC"] = "TC",
                ["Turrets"] = "Turrets",
                ["Bear"] = "Bear",
                ["Boar"] = "Boar",
                ["Chicken"] = "Chicken",
                ["Wolf"] = "Wolf",
                ["Stag"] = "Stag",
                ["Horse"] = "Horse",
                ["My Base"] = "My Base",
                ["scarecrow"] = "scarecrow",
                ["murderer"] = "murderer",
                ["WaitCooldown"] = "You must wait {0} seconds to use this command again.",
                ["missionprovider_stables_a"] = "missions",
                ["missionprovider_stables_b"] = "missions",
                ["missionprovider_outpost_a"] = "missions",
                ["missionprovider_outpost_b"] = "missions",
                ["missionprovider_fishing_a"] = "missions",
                ["missionprovider_fishing_b"] = "missions",
                ["missionprovider_bandit_a"] = "missions",
                ["missionprovider_bandit_b"] = "missions",
                ["simpleshark"] = "shark",
                ["stables_shopkeeper"] = "shopkeeper",
                ["npc_underwaterdweller"] = "dweller",
                ["boat_shopkeeper"] = "shopkeeper",
                ["bandit_shopkeeper"] = "shopkeeper",
                ["outpost_shopkeeper"] = "shopkeeper",
                ["npc_bandit_guard"] = "guard",
                ["bandit_conversationalist"] = "vendor",
                ["npc_tunneldweller"] = "dweller",
                ["ProcessRequest"] = "Processing request; this will take several seconds...",
                ["ProcessRequestFinished"] = "{0} entities were found.",
                ["Radar UI Help"] = "You can toggle the move button/reset the UI using: {0}",
                ["AT"] = "AT",
                ["bag"] = "bag",
                ["LR300AR"] = "LR300",
                ["AR"] = "AK47",
                ["ARICE"] = "AK47",
                ["M92P"] = "M92",
                ["M39P"] = "M39",
            }, this, "en");
        }

        public class ConfigurationSettings
        {
            public int GetLimit(BasePlayer player)
            {
                if (player.Connection.authLevel >= 2) return Owner;
                if (player.Connection.authLevel == 1) return Moderator;
                return Allowed;
            }

            [JsonProperty(PropertyName = "Barebones Performance Mode")]
            public bool Barebones;

            [JsonProperty(PropertyName = "Restrict Access To Steam64 IDs", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Authorized = new List<string>();

            [JsonProperty(PropertyName = "Restrict Access To Auth Level")]
            public int authLevel = 1;

            [JsonProperty(PropertyName = "Max Active Filters (OWNERID)")]
            public int Owner = 0;

            [JsonProperty(PropertyName = "Max Active Filters (MODERATORID)")]
            public int Moderator = 4;

            [JsonProperty(PropertyName = "Max Active Filters (ADMINRADAR.ALLOWED)")]
            public int Allowed = 2;

            [JsonProperty(PropertyName = "Default Distance")]
            public float DefaultMaxDistance = 500f;

            [JsonProperty(PropertyName = "Default Refresh Time")]
            public float DefaultInvokeTime = 5f;

            [JsonProperty(PropertyName = "Minimum Refresh Time")]
            public float MinInvokeTime = 1.0f;

            [JsonProperty(PropertyName = "Dropped Item Exceptions", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> DropExceptions;

            [JsonProperty(PropertyName = "Deactivate Radar After X Seconds Inactive")]
            public float InactiveSeconds = 300;

            [JsonProperty(PropertyName = "Deactivate Radar After X Seconds Activated")]
            public float DeactivateSeconds;

            [JsonProperty(PropertyName = "User Interface Enabled")]
            public bool UI = True;

            [JsonProperty(PropertyName = "Show Average Ping Every X Seconds [0 = disabled]")]
            public float AveragePingInterval;

            [JsonProperty(PropertyName = "Re-use Cooldown, Seconds")]
            public float Cooldown;

            [JsonProperty(PropertyName = "Show Radar Activated/Deactivated Messages")]
            public bool ShowToggle = True;

            [JsonProperty(PropertyName = "Player Name Text Size")]
            public int PlayerNameSize = 24;

            [JsonProperty(PropertyName = "Player Information Text Size")]
            public int PlayerTextSize = 24;

            [JsonProperty(PropertyName = "Entity Name Text Size")]
            public int EntityNameSize = 24;

            [JsonProperty(PropertyName = "Entity Information Text Size")]
            public int EntityTextSize = 24;

            [JsonProperty(PropertyName = "Unique Clan/Team Color Applies To Entire Player Text")]
            public bool ApplySameColor;

            [JsonProperty(PropertyName = "Track Group Name")]
            public string New = "";

            [JsonProperty(PropertyName = "Tracked Group Name Text")]
            public string NewText = "<color=#00FF00>*</color>";

            [JsonProperty(PropertyName = "Chat Command")]
            public string Primary = "radar";

            [JsonProperty(PropertyName = "Second Command")]
            public string Secondary = "radar";
        }

        public class ConfigurationOptions
        {
            [JsonProperty(PropertyName = "Additional Traps", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> AdditionalTraps = new List<string> { "barricade.metal", "barricade.stone", "barricade.wood", "barricade.woodwire", "spikes.floor", "guntrap", "sam_site_turret_deployed", "flameturret" };

            [JsonProperty(PropertyName = "Draw Distant Players With X")]
            public bool DrawX;

            [JsonProperty(PropertyName = "Draw Empty Containers")]
            public bool DrawEmptyContainers = True;

            [JsonProperty(PropertyName = "Abbreviate Item Names")]
            public bool Abbr = True;

            [JsonProperty(PropertyName = "Show Resource Amounts")]
            public bool ResourceAmounts = True;

            [JsonProperty(PropertyName = "Show X Items From Barrel And Crate")]
            public int LootContentAmount;

            [JsonProperty(PropertyName = "Show X Items From Airdrop")]
            public int AirdropContentAmount;

            [JsonProperty(PropertyName = "Show X Items From Stash")]
            public int StashContentAmount;

            [JsonProperty(PropertyName = "Show X Items From Backpacks")]
            public int BackpackContentAmount = 3;

            [JsonProperty(PropertyName = "Show X Items From Corpses")]
            public int CorpseContentAmount = 3;

            [JsonProperty(PropertyName = "Show NPC At World View")]
            public bool WorldView = True;

            [JsonProperty(PropertyName = "Show Authed Count On Cupboards")]
            public bool TCAuthed = True;

            [JsonProperty(PropertyName = "Show Bag Count On Cupboards")]
            public bool TCBags = True;

            [JsonProperty(PropertyName = "Show Npc Player Target")]
            public bool DrawTargetsVictim;

            [JsonProperty(PropertyName = "Radar Buildings Draw Time")]
            public float BuildingsDrawTime = 60f;

            [JsonProperty(PropertyName = "Radar Drops Draw Time")]
            public float DropsDrawTime = 60f;

            [JsonProperty(PropertyName = "Radar Find Draw Time")]
            public float FindDrawTime = 60f;

            [JsonProperty(PropertyName = "Radar FindByID Draw Time")]
            public float FindByIDDrawTime = 60f;

            public int Get(EntityType type)
            {
                switch (type)
                {
                    case EntityType.Backpack: return BackpackContentAmount;
                    case EntityType.Dead: return CorpseContentAmount;
                    case EntityType.Airdrop: return AirdropContentAmount;
                    case EntityType.Stash: return StashContentAmount;
                    case EntityType.Ore: return ResourceAmounts ? 1 : 0;
                    case EntityType.Col: return ResourceAmounts ? 1 : 0;
                    case EntityType.Loot: return LootContentAmount;
                    default: return 0;
                }
            }
        }

        public class ConfigurationDrawMethods
        {
            [JsonProperty(PropertyName = "Draw Arrows On Players")]
            public bool Arrow;

            [JsonProperty(PropertyName = "Draw Boxes")]
            public bool Box;

            [JsonProperty(PropertyName = "Draw Text")]
            public bool Text = True;
        }

        public class ConfigurationLimits
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled;

            [JsonProperty(PropertyName = "Limit")]
            public int Amount = 4;

            [JsonProperty(PropertyName = "Range")]
            public float Range = 50f;

            [JsonProperty(PropertyName = "Height Offset [0.0 = disabled]")]
            public float Height = 40f;

            [JsonProperty(PropertyName = "Use Group Colors Configuration")]
            public bool ColorsEnabled = True;

            [JsonProperty(PropertyName = "Dead Color")]
            public string Dead = "#ff0000";

            [JsonProperty(PropertyName = "Group Color Basic")]
            public string Basic = "#ffff00";

            [JsonProperty(PropertyName = "Group Limit Colors", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, string> Colors = DefaultColors;
        }

        public class ConfigurationDrawDistances
        {
            [JsonProperty(PropertyName = "Sleepers Min Y")]
            public float MinY = 0f;

            [JsonProperty(PropertyName = "Player Corpses")]
            public float Corpse = 200;

            [JsonProperty(PropertyName = "Players")]
            public float Players = 500;

            [JsonProperty(PropertyName = "Airdrop Crates")]
            public float Airdrop = 400f;

            [JsonProperty(PropertyName = "Animals")]
            public float Animal = 200;

            [JsonProperty(PropertyName = "Boats")]
            public float Boat = 150f;

            [JsonProperty(PropertyName = "Boxes")]
            public float Box = 100;

            [JsonProperty(PropertyName = "Cars")]
            public float Car = 500f;

            [JsonProperty(PropertyName = "CCTV")]
            public float CCTV = 500;

            [JsonProperty(PropertyName = "Collectibles")]
            public float Col = 100;

            [JsonProperty(PropertyName = "Loot Containers")]
            public float Loot = 150;

            [JsonProperty(PropertyName = "MiniCopter")]
            public float MC = 200f;

            [JsonProperty(PropertyName = "MLRS")]
            public float MLRS = 5000f;

            [JsonProperty(PropertyName = "NPC Players")]
            public float NPC = 300;

            [JsonProperty(PropertyName = "Resources (Ore)")]
            public float Ore = 200;

            [JsonProperty(PropertyName = "Ridable Horses")]
            public float RH = 250;

            [JsonProperty(PropertyName = "Sleeping Bags")]
            public float Bag = 250;

            [JsonProperty(PropertyName = "Stashes")]
            public float Stash = 250;

            [JsonProperty(PropertyName = "Tool Cupboards")]
            public float TC = 150;

            [JsonProperty(PropertyName = "Tool Cupboard Arrows")]
            public float TCArrows = 250;

            [JsonProperty(PropertyName = "Traps")]
            public float Traps = 100;

            [JsonProperty(PropertyName = "Turrets")]
            public float Turret = 100;

            [JsonProperty(PropertyName = "Vending Machines")]
            public float VendingMachine = 250;

            [JsonProperty(PropertyName = "Radar Drops Command")]
            public float Drops = 150;

            public float Get(EntityType type, BaseEntity entity)
            {
                switch (type)
                {
                    case EntityType.Active: return Players;
                    case EntityType.Sleeper: return Players;
                    case EntityType.Dead: return Corpse;
                    case EntityType.Airdrop: return Airdrop;
                    case EntityType.Backpack: return Loot;
                    case EntityType.Boat: return Boat;
                    case EntityType.Bag: return Bag;
                    case EntityType.Car: return Car;
                    case EntityType.CCTV: return CCTV;
                    case EntityType.Col: return Col;
                    case EntityType.Loot: return Loot;
                    case EntityType.Mini: return MC;
                    case EntityType.MLRS: return MLRS;
                    case EntityType.Ore: return Ore;
                    case EntityType.Horse: return RH;
                    case EntityType.RHIB: return Boat;
                    case EntityType.Stash: return Stash;
                    case EntityType.TC: return TC;
                    case EntityType.TCArrow: return TCArrows;
                    case EntityType.Trap: return Traps;
                    case EntityType.Turret: return Turret;
                    case EntityType.Bradley:
                    case EntityType.CargoPlane:
                    case EntityType.CargoShip:
                    case EntityType.CH47:
                    case EntityType.Limit:
                    default:
                        {
                            if (entity is VendingMachine) return VendingMachine;
                            if (type == EntityType.Box) return Box;
                            if (entity is BaseNpc) return Animal;
                            if (type == EntityType.Npc) return NPC;
                            return 9999f;
                        }
                }
            }
        }

        public class ConfigurationCoreTracking
        {
            [JsonProperty(PropertyName = "Players")]
            public bool Active = True;

            [JsonProperty(PropertyName = "Sleepers")]
            public bool Sleepers = True;

            [JsonProperty(PropertyName = "Animals")]
            public bool Animals = True;

            [JsonProperty(PropertyName = "Bags")]
            public bool Bags = True;

            [JsonProperty(PropertyName = "Box")]
            public bool Box = True;

            [JsonProperty(PropertyName = "Collectibles")]
            public bool Col = True;

            [JsonProperty(PropertyName = "Dead")]
            public bool Dead = True;

            [JsonProperty(PropertyName = "Loot")]
            public bool Loot = True;

            [JsonProperty(PropertyName = "NPC")]
            public bool NPCPlayer = True;

            [JsonProperty(PropertyName = "Ore")]
            public bool Ore = True;

            [JsonProperty(PropertyName = "Stash")]
            public bool Stash = True;

            [JsonProperty(PropertyName = "SupplyDrops")]
            public bool Airdrop = True;

            [JsonProperty(PropertyName = "TC")]
            public bool TC = True;

            [JsonProperty(PropertyName = "Turrets")]
            public bool Turrets = True;
        }

        public class ConfigurationAdditionalTracking
        {
            [JsonProperty(PropertyName = "Backpacks Plugin")]
            public bool BackpackPlugin { get; set; }

            [JsonProperty(PropertyName = "Boats")]
            public bool Boats;

            [JsonProperty(PropertyName = "Bradley APC")]
            public bool Bradley = True;

            [JsonProperty(PropertyName = "Cars")]
            public bool Cars;

            [JsonProperty(PropertyName = "CargoPlanes")]
            public bool CP;

            [JsonProperty(PropertyName = "CargoShips")]
            public bool CS;

            [JsonProperty(PropertyName = "CCTV")]
            public bool CCTV;

            [JsonProperty(PropertyName = "CH47")]
            public bool CH47;

            [JsonProperty(PropertyName = "Helicopters")]
            public bool Heli = True;

            [JsonProperty(PropertyName = "Helicopter Rotor Health")]
            public bool RotorHealth;

            [JsonProperty(PropertyName = "MiniCopter")]
            public bool MC;

            [JsonProperty(PropertyName = "MLRS")]
            public bool MLRS = True;

            [JsonProperty(PropertyName = "Ridable Horses")]
            public bool RH;

            [JsonProperty(PropertyName = "RHIB")]
            public bool RHIB;

            [JsonProperty(PropertyName = "Traps")]
            public bool Traps;

            public bool Get(EntityType type)
            {
                switch (type)
                {
                    case EntityType.Boat: return Boats;
                    case EntityType.Bradley: return Bradley;
                    case EntityType.CargoPlane: return CP;
                    case EntityType.CargoShip: return CS;
                    case EntityType.Car: return Cars;
                    case EntityType.CCTV: return CCTV;
                    case EntityType.CH47: return CH47;
                    case EntityType.Heli: return Heli;
                    case EntityType.Horse: return RH;
                    case EntityType.Mini: return MC;
                    case EntityType.MLRS: return MLRS;
                    case EntityType.RHIB: return RHIB;
                    case EntityType.Trap: return Traps;
                    default: return False;
                }
            }
        }

        public class ConfigurationHex
        {
            [JsonProperty(PropertyName = "Player Arrows")]
            public string Arrows = "#000000";

            [JsonProperty(PropertyName = "Distance")]
            public string Dist = "#ffa500";

            [JsonProperty(PropertyName = "Helicopters")]
            public string Heli = "#ff00ff";

            [JsonProperty(PropertyName = "Bradley")]
            public string Bradley = "#ff00ff";

            [JsonProperty(PropertyName = "MiniCopter")]
            public string MC = "#ff00ff";

            [JsonProperty(PropertyName = "MiniCopter (ScrapTransportHelicopter)")]
            public string STH = "#ff00ff";

            [JsonProperty(PropertyName = "Online Player")]
            public string Online = "#ffffff";

            [JsonProperty(PropertyName = "Online Player (Underground)")]
            public string Underground = "#ffffff";

            [JsonProperty(PropertyName = "Online Player (Flying)")]
            public string Flying = "#ffffff";

            [JsonProperty(PropertyName = "Online Dead Player")]
            public string OnlineDead = "#ff0000";

            [JsonProperty(PropertyName = "Dead Player")]
            public string Dead = "#ff0000";

            [JsonProperty(PropertyName = "Sleeping Player")]
            public string Sleeper = "#00ffff";

            [JsonProperty(PropertyName = "Sleeping Dead Player")]
            public string SleeperDead = "#ff0000";

            [JsonProperty(PropertyName = "Health")]
            public string Health = "#ff0000";

            [JsonProperty(PropertyName = "Backpacks")]
            public string Backpack = "#c0c0c0";

            [JsonProperty(PropertyName = "Scientists")]
            public string Scientist = "#ffff00";

            [JsonProperty(PropertyName = "Scientist Peacekeeper")]
            public string Peacekeeper = "#ffff00";

            [JsonProperty(PropertyName = "Murderers")]
            public string Murderer = "#000000";

            [JsonProperty(PropertyName = "Animals")]
            public string Animal = "#0000ff";

            [JsonProperty(PropertyName = "Resources")]
            public string Resource = "#ffff00";

            [JsonProperty(PropertyName = "Collectibles")]
            public string Col = "#ffff00";

            [JsonProperty(PropertyName = "Tool Cupboards")]
            public string TC = "#000000";

            [JsonProperty(PropertyName = "Sleeping Bags")]
            public string Bag = "#ff00ff";

            [JsonProperty(PropertyName = "Airdrops")]
            public string AD = "#ff00ff";

            [JsonProperty(PropertyName = "AutoTurrets")]
            public string AT = "#ffff00";

            [JsonProperty(PropertyName = "Corpses")]
            public string Corpse = "#ffff00";

            [JsonProperty(PropertyName = "Box")]
            public string Box = "#ff00ff";

            [JsonProperty(PropertyName = "Loot")]
            public string Loot = "#ffff00";

            [JsonProperty(PropertyName = "Stash")]
            public string Stash = "#ffffff";

            [JsonProperty(PropertyName = "Boat")]
            public string Boat = "#ff00ff";

            [JsonProperty(PropertyName = "CargoPlane")]
            public string CP = "#ff00ff";

            [JsonProperty(PropertyName = "CargoShip")]
            public string CS = "#ff00ff";

            [JsonProperty(PropertyName = "Car")]
            public string Cars = "#ff00ff";

            [JsonProperty(PropertyName = "CCTV")]
            public string CCTV = "#ff00ff";

            [JsonProperty(PropertyName = "CH47")]
            public string CH47 = "#ff00ff";

            [JsonProperty(PropertyName = "RidableHorse")]
            public string RH = "#ff00ff";

            [JsonProperty(PropertyName = "MLRS")]
            public string MLRS = "#ff00ff";

            [JsonProperty(PropertyName = "NPC")]
            public string NPC = "#ff00ff";

            [JsonProperty(PropertyName = "RHIB")]
            public string RHIB = "#ff00ff";

            [JsonProperty(PropertyName = "Traps")]
            public string Traps = "#ff00ff";

            public string Get(EntityType type)
            {
                switch (type)
                {
                    case EntityType.Active: return Online;
                    case EntityType.Airdrop: return Box;
                    case EntityType.Backpack: return Backpack;
                    case EntityType.Bag: return Bag;
                    case EntityType.Boat: return Boat;
                    case EntityType.Box: return Box;
                    case EntityType.Bradley: return Bradley;
                    case EntityType.CargoPlane: return CP;
                    case EntityType.CargoShip: return CS;
                    case EntityType.Car: return Cars;
                    case EntityType.CCTV: return CCTV;
                    case EntityType.CH47: return CH47;
                    case EntityType.Col: return Col;
                    case EntityType.Dead: return Dead;
                    case EntityType.Heli: return Heli;
                    case EntityType.Horse: return RH;
                    case EntityType.Loot: return Loot;
                    case EntityType.Mini: return MC;
                    case EntityType.MLRS: return MLRS;
                    case EntityType.Npc: return NPC;
                    case EntityType.Ore: return Resource;
                    case EntityType.RHIB: return RHIB;
                    case EntityType.Sleeper: return Sleeper;
                    case EntityType.Stash: return Stash;
                    case EntityType.TC: return TC;
                    case EntityType.Turret: return AT;
                    case EntityType.Trap: return Traps;
                    default: return "#ff00ff";
                }
            }
        }

        public class ConfigurationGUI
        {
            [JsonProperty(PropertyName = "Move Arrow Text")]
            public string Arrow = "↕";

            [JsonProperty(PropertyName = "Offset Min")]
            public string OffsetMin = "185.044 91.429";

            [JsonProperty(PropertyName = "Offset Max")]
            public string OffsetMax = "230.956 110.571";

            [JsonProperty(PropertyName = "Color On")]
            public string On = "0.69 0.49 0.29 0.5";

            [JsonProperty(PropertyName = "Color Off")]
            public string Off = "0.29 0.49 0.69 0.5";

            [JsonProperty(PropertyName = "Show Button - All")]
            public bool All = True;

            [JsonProperty(PropertyName = "Show Button - Bags")]
            public bool Bags = True;

            [JsonProperty(PropertyName = "Show Button - Boats")]
            public bool Boats;

            [JsonProperty(PropertyName = "Show Button - Bradley")]
            public bool Bradley;

            [JsonProperty(PropertyName = "Show Button - Box")]
            public bool Box = True;

            [JsonProperty(PropertyName = "Show Button - Cars")]
            public bool Cars;

            [JsonProperty(PropertyName = "Show Button - CCTV")]
            public bool CCTV = True;

            [JsonProperty(PropertyName = "Show Button - CargoPlanes")]
            public bool CP;

            [JsonProperty(PropertyName = "Show Button - CargoShips")]
            public bool CS;

            [JsonProperty(PropertyName = "Show Button - CH47")]
            public bool CH47;

            [JsonProperty(PropertyName = "Show Button - Collectibles")]
            public bool Col = True;

            [JsonProperty(PropertyName = "Show Button - Dead")]
            public bool Dead = True;

            [JsonProperty(PropertyName = "Show Button - Heli")]
            public bool Heli;

            [JsonProperty(PropertyName = "Show Button - Loot")]
            public bool Loot = True;

            [JsonProperty(PropertyName = "Show Button - MiniCopter")]
            public bool MC;

            [JsonProperty(PropertyName = "Show Button - MLRS")]
            public bool MLRS = True;

            [JsonProperty(PropertyName = "Show Button - NPC")]
            public bool NPC = True;

            [JsonProperty(PropertyName = "Show Button - Ore")]
            public bool Ore = True;

            [JsonProperty(PropertyName = "Show Button - Ridable Horses")]
            public bool Horse;

            [JsonProperty(PropertyName = "Show Button - RigidHullInflatableBoats")]
            public bool RHIB;

            [JsonProperty(PropertyName = "Show Button - Sleepers")]
            public bool Sleepers = True;

            [JsonProperty(PropertyName = "Show Button - Stash")]
            public bool Stash = True;

            [JsonProperty(PropertyName = "Show Button - TC")]
            public bool TC = True;

            [JsonProperty(PropertyName = "Show Button - TC Arrow")]
            public bool TCArrow = True;

            [JsonProperty(PropertyName = "Show Button - TC Turrets")]
            public bool Turrets = True;

            [JsonProperty(PropertyName = "Show Button - Traps")]
            public bool Traps = True;

            public bool Get(EntityType type)
            {
                switch (type)
                {
                    case EntityType.Airdrop: return Box;
                    case EntityType.Bag: return Bags;
                    case EntityType.Boat: return Boats;
                    case EntityType.Box: return Box;
                    case EntityType.Bradley: return Bradley;
                    case EntityType.CargoPlane: return CP;
                    case EntityType.CargoShip: return CS;
                    case EntityType.Car: return Cars;
                    case EntityType.CCTV: return CCTV;
                    case EntityType.CH47: return CH47;
                    case EntityType.Col: return Col;
                    case EntityType.Dead: return Dead;
                    case EntityType.Heli: return Heli;
                    case EntityType.Horse: return Horse;
                    case EntityType.Loot: return Loot;
                    case EntityType.Mini: return MC;
                    case EntityType.MLRS: return MLRS;
                    case EntityType.Npc: return NPC;
                    case EntityType.Ore: return Ore;
                    case EntityType.RHIB: return RHIB;
                    case EntityType.Sleeper: return Sleepers;
                    case EntityType.Stash: return Stash;
                    case EntityType.TC: return TC;
                    case EntityType.TCArrow: return TCArrow;
                    case EntityType.Turret: return Turrets;
                    case EntityType.Trap: return Traps;
                    default: return False;
                }
            }
        }

        public class ConfigurationVoiceDetection
        {
            [JsonProperty(PropertyName = "Enabled")]
            public bool Enabled = True;

            [JsonProperty(PropertyName = "Timeout After X Seconds")]
            public int Interval = 3;

            [JsonProperty(PropertyName = "Detection Radius")]
            public float Distance = 30f;
        }

        public class ConfigurationDiscord
        {
            [JsonProperty(PropertyName = "Message - Embed Color (DECIMAL)")]
            public int Color = 3329330;

            [JsonProperty(PropertyName = "Message - Webhook URL")]
            public string Webhook = "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

            [JsonProperty(PropertyName = "Embed_MessageServer")]
            public string Server = "Server";

            [JsonProperty(PropertyName = "Embed_MessageLocation")]
            public string Location = "Location";

            [JsonProperty(PropertyName = "Embed_MessageTitle")]
            public string Title = "Player Message";

            [JsonProperty(PropertyName = "Embed_MessagePlayer")]
            public string Player = "Player";

            [JsonProperty(PropertyName = "Embed_MessageMessage")]
            public string Message = "Message";

            [JsonProperty(PropertyName = "Off")]
            public string Off = "Radar turned off.";

            [JsonProperty(PropertyName = "On")]
            public string On = "Radar turned on.";
        }

        public class ConfigurationTrack
        {
            [JsonProperty(PropertyName = "Radar")]
            public bool Radar;

            [JsonProperty(PropertyName = "Radar Text")]
            public string RadarText = "<color=#00FF00>R</color>";

            [JsonProperty(PropertyName = "Console Godmode")]
            public bool God;

            [JsonProperty(PropertyName = "Console Godmode Text")]
            public string GodText = "<color=#89CFF0>G</color>";

            [JsonProperty(PropertyName = "Plugin Godmode")]
            public bool GodPlugin;

            [JsonProperty(PropertyName = "Plugin Godmode Text")]
            public string GodPluginText = "<color=#0000CD>G</color>";

            [JsonProperty(PropertyName = "Vanish")]
            public bool Vanish = True;

            [JsonProperty(PropertyName = "Vanish Text")]
            public string VanishText = "<color=#FF00FF>V</color>";

            [JsonProperty(PropertyName = "NOCLIP")]
            public bool NoClip;

            [JsonProperty(PropertyName = "NOCLIP Text")]
            public string NoClipText = "<color=#FFFF00>F</color>";
        }

        public class Configuration
        {
            [JsonProperty(PropertyName = "Core Tracking")]
            public ConfigurationCoreTracking Core { get; set; } = new ConfigurationCoreTracking();

            [JsonProperty(PropertyName = "Additional Tracking")]
            public ConfigurationAdditionalTracking Additional { get; set; } = new ConfigurationAdditionalTracking();

            [JsonProperty(PropertyName = "Color-Hex Codes")]
            public ConfigurationHex Hex { get; set; } = new ConfigurationHex();

            [JsonProperty(PropertyName = "DiscordMessages")]
            public ConfigurationDiscord Discord { get; set; } = new ConfigurationDiscord();

            [JsonProperty(PropertyName = "Drawing Distances")]
            public ConfigurationDrawDistances Distance { get; set; } = new ConfigurationDrawDistances();

            [JsonProperty(PropertyName = "Drawing Methods")]
            public ConfigurationDrawMethods Methods { get; set; } = new ConfigurationDrawMethods();

            [JsonProperty(PropertyName = "Group Limit")]
            public ConfigurationLimits Limit { get; set; } = new ConfigurationLimits();

            [JsonProperty(PropertyName = "GUI")]
            public ConfigurationGUI GUI { get; set; } = new ConfigurationGUI();

            [JsonProperty(PropertyName = "Options")]
            public ConfigurationOptions Options { get; set; } = new ConfigurationOptions();

            [JsonProperty(PropertyName = "Settings")]
            public ConfigurationSettings Settings { get; set; } = new ConfigurationSettings();

            [JsonProperty(PropertyName = "Track Admin Status")]
            public ConfigurationTrack Track { get; set; } = new ConfigurationTrack();

            [JsonProperty(PropertyName = "Voice Detection")]
            public ConfigurationVoiceDetection Voice { get; set; } = new ConfigurationVoiceDetection();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) LoadDefaultConfig();
                if (config.Settings.DropExceptions == null) config.Settings.DropExceptions = ItemExceptions;
                SaveConfig();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogException(ex);
                LoadDefaultConfig();
            }

            if (config.GUI.Boats) config.Additional.Boats = True;
            if (config.GUI.Bradley) config.Additional.Bradley = True;
            if (config.GUI.Cars) config.Additional.Cars = True;
            if (config.GUI.CCTV) config.Additional.CCTV = True;
            if (config.GUI.CP) config.Additional.CP = True;
            if (config.GUI.CS) config.Additional.CS = True;
            if (config.GUI.CH47) config.Additional.CH47 = True;
            if (config.GUI.Heli) config.Additional.Heli = True;
            if (config.GUI.MC) config.Additional.MC = True;
            if (config.GUI.MLRS) config.Additional.MLRS = True;
            if (config.GUI.Horse) config.Additional.RH = True;
            if (config.GUI.RHIB) config.Additional.RHIB = True;
            if (config.Voice.Interval < 3) config.Voice.Interval = 3;

            DefaultOffset = new UiOffsets(config.GUI.OffsetMin, config.GUI.OffsetMax);

            _sendDiscordMessages = !string.IsNullOrEmpty(config.Discord.Webhook) && config.Discord.Webhook != "https://support.discordapp.com/hc/en-us/articles/228383668-Intro-to-Webhooks";
        }

        private void RegisterCommands()
        {
            if (!string.IsNullOrEmpty(config.Settings.Primary))
            {
                radarCommand = config.Settings.Primary;
                AddCovalenceCommand(config.Settings.Primary, nameof(RadarCommand));
            }
            if (!string.IsNullOrEmpty(config.Settings.Secondary) && config.Settings.Primary != config.Settings.Secondary)
            {
                if (string.IsNullOrEmpty(radarCommand))
                {
                    radarCommand = config.Settings.Secondary;
                }
                AddCovalenceCommand(config.Settings.Secondary, nameof(RadarCommand));
            }
        }

        private string radarCommand;

        protected override void SaveConfig() => Config.WriteObject(config);

        protected override void LoadDefaultConfig() => config = new Configuration();

        private string m(string key, string id, params object[] args)
        {
            return args.Length > 0 ? string.Format(lang.GetMessage(key, this, id), args) : lang.GetMessage(key, this, id);
        }

        private static string r(string source)
        {
            return source.Contains(">") ? Regex.Replace(source, "<.*?>", string.Empty) : source;
        }

        private void Message(BasePlayer target, string key, params object[] args)
        {
            if (target.IsValid())
            {
                Player.Message(target, m(key, target.UserIDString, args), 0uL);
            }
        }

        #endregion
    }
}

namespace Oxide.Plugins.AdminRadarExtensionMethods
{
    public static class ExtensionMethods
    {
        public static T ElementAt<T>(this IEnumerable<T> a, int b) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == 0) { return c.Current; } b--; } } return default(T); }
        public static T FirstOrDefault<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == null || b(c.Current)) { return c.Current; } } } return default(T); }
        public static List<T> ToList<T>(this IEnumerable<T> a, Func<T, bool> b = null) { var c = new List<T>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { if (b == null || b(d.Current)) { c.Add(d.Current); } } } return c; }
        public static string[] ToLower(this IEnumerable<string> a, Func<string, bool> b = null) { var c = new List<string>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { if (b == null || b(d.Current)) { c.Add(d.Current.ToLower()); } } } return c.ToArray(); }
        public static T[] Take<T>(this IList<T> a, int b) { var c = new List<T>(); for (int i = 0; i < a.Count; i++) { if (c.Count == b) { break; } c.Add(a[i]); } return c.ToArray(); }
        public static IEnumerable<V> Select<T, V>(this IEnumerable<T> a, Func<T, V> b) { var c = new List<V>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { c.Add(b(d.Current)); } } return c; }
        public static T[] Where<T>(this IEnumerable<T> a, Func<T, bool> b) { var c = new List<T>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { if (b(d.Current)) { c.Add(d.Current); } } } return c.ToArray(); }
        public static IEnumerable<T> OfType<T>(this IEnumerable<object> a) { foreach (object b in a) { if (b is T) { yield return (T)b; } } }
        public static float Sum<T>(this IEnumerable<T> a, Func<T, float> b) { float c = 0; if (a == null) return c; foreach (T d in a) { if (d == null) continue; c = checked(c + b(d)); } return c; }
        public static int Sum<T>(this IEnumerable<T> a, Func<T, int> b) { int c = 0; if (a == null) return c; foreach (T d in a) { if (d == null) continue; c = checked(c + b(d)); } return c; }
        public static bool IsKilled(this BaseNetworkable a) { return (object)a == null || a.IsDestroyed; }
        public static void ResetToPool<K, V>(this Dictionary<K, V> collection) { collection.Clear(); Pool.Free(ref collection); }
        public static void ResetToPool<T>(this HashSet<T> collection) { collection.Clear(); Pool.Free(ref collection); }
        public static void ResetToPool<T>(this List<T> collection) { collection.Clear(); Pool.Free(ref collection); }
    }
}