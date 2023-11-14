using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("Admin Radar", "nivex", "4.5.0")]
    [Description("Radar tool for Admins and Developers.")]
    public class AdminRadar : RustPlugin
    {
        public enum CupboardAction
        {
            Authorize,
            Clear,
            Deauthorize
        }

        private class Cache
        {
            public readonly List<SupplyDrop> Airdrops = new List<SupplyDrop>();
            public readonly List<BaseNpc> Animals = new List<BaseNpc>();
            public readonly List<BradleyAPC> APCs = new List<BradleyAPC>();
            public readonly Dictionary<Vector3, CachedInfo> Backpacks = new Dictionary<Vector3, CachedInfo>();
            public readonly Dictionary<Vector3, CachedInfo> Bags = new Dictionary<Vector3, CachedInfo>();
            public readonly List<BaseCar> Cars = new List<BaseCar>();
            public readonly List<CargoShip> CargoShips = new List<CargoShip>();
            public readonly Dictionary<Vector3, CachedInfo> Collectibles = new Dictionary<Vector3, CachedInfo>();
            public readonly Dictionary<Vector3, CachedInfo> Containers = new Dictionary<Vector3, CachedInfo>();
            public readonly Dictionary<PlayerCorpse, CachedInfo> Corpses = new Dictionary<PlayerCorpse, CachedInfo>();
            public readonly List<BuildingPrivlidge> Cupboards = new List<BuildingPrivlidge>();
            public readonly List<BaseHelicopter> Helis = new List<BaseHelicopter>();
            public readonly List<BasePlayer> NPCPlayers = new List<BasePlayer>();
            public readonly Dictionary<Vector3, CachedInfo> Ores = new Dictionary<Vector3, CachedInfo>();
            public readonly Dictionary<Vector3, CachedInfo> Turrets = new Dictionary<Vector3, CachedInfo>();
            public readonly List<Zombie> Zombies = new List<Zombie>();
        }

        public class Marker : FacepunchBehaviour
        {
            public BaseEntity entity;
            public BasePlayer player;
            public BuildingPrivlidge privilege;
            public MapMarkerGenericRadius generic;
            public VendingMachineMapMarker vending, personal;
            public Vector3 lastPosition;
            public string markerName, uid;

            public bool IsPrivilegeMarker
            {
                get
                {
                    return privilege != null;
                }
            }

            public bool IsScientist(BaseNetworkable entity)
            {
                var player = entity as BasePlayer;
                return entity is Scientist || entity is HTNPlayer || (player != null && player.displayName == player.userID.ToString());
            }

            private void Awake()
            {
                privilege = GetComponent<BuildingPrivlidge>();
                player = GetComponent<BasePlayer>();
                entity = gameObject.ToBaseEntity();
                uid = player?.UserIDString ?? Guid.NewGuid().ToString();
                markerName = IsScientist(entity) ? ins.msg("scientist") : player?.displayName ?? ins.msg(entity.ShortPrefabName);
                lastPosition = transform.position;
                markers[uid] = this;

                if (IsPrivilegeMarker)
                {
                    UpdatePrivilegeMarker();
                }
                else
                {
                    UpdateMarker();
                }
            }

            private void FixedUpdate()
            {
                if (IsPrivilegeMarker || (!useNpcUpdateTracking && entity != null && entity.IsNpc))
                {
                    return;
                }

                if (entity == null || entity.transform == null || entity.IsDestroyed)
                {
                    Destroy(this);
                    return;
                }

                if (Vector3.Distance(entity.transform.position, lastPosition) <= markerOverlapDistance)
                {
                    return;
                }

                lastPosition = entity.transform.position;
                UpdateMarker();
            }

            private void OnDestroy()
            {
                RemoveMarker();
                markers.Remove(uid);
                Destroy(this);
            }

            public void UpdateMarker()
            {
                RemoveMarker();

                vending = GameManager.server.CreateEntity(vendingPrefab, lastPosition) as VendingMachineMapMarker;

                if (vending != null)
                {
                    var sb = new StringBuilder();

                    sb.AppendLine(markerName);

                    foreach (var marker in markers.Values.Where(t => t != this && !string.IsNullOrEmpty(t.markerName) && Vector3.Distance(t.lastPosition, lastPosition) <= markerOverlapDistance).ToList())
                    {
                        if (marker.IsPrivilegeMarker) sb.AppendLine("TC");
                        sb.AppendLine(marker.markerName);
                    }

                    sb.Length--;
                    vending.OwnerID = markerId;
                    vending.markerShopName = sb.ToString();
                    vending.enabled = false;
                    vending.Spawn();
                }

                generic = GameManager.server.CreateEntity(genericPrefab, lastPosition) as MapMarkerGenericRadius;

                if (generic != null)
                {
                    var color1 = entity.IsNpc ? GetNpcColor() : player != null && player.IsAdmin ? adminColor : player != null && !player.IsConnected ? sleeperColor : onlineColor;

                    generic.OwnerID = markerId;
                    generic.alpha = 1f;
                    generic.color1 = color1;
                    generic.color2 = entity.IsNpc ? defaultNpcColor : privilegeColor2;
                    generic.radius = 2f;
                    generic.enabled = true;
                    generic.Spawn();
                    generic.SendUpdate();
                }
            }

            private Color GetNpcColor()
            {
                switch (entity.ShortPrefabName)
                {
                    case "bear":
                        return bearColor;
                    case "boar":
                        return boarColor;
                    case "chicken":
                        return chickenColor;
                    case "wolf":
                        return wolfColor;
                    case "stag":
                        return stagColor;
                    case "horse":
                        return horseColor;
                    case "murderer":
                        return __(murdererCC);
                    case "bandit_guard":
                    case "bandit_shopkeeper":
                    case "scientist":                    
                    case "scientistjunkpile":
                    case "scientiststationary":
                        return __(scientistCC);
                    case "scientistpeacekeeper":
                        return __(peacekeeperCC);
                    case "scientist_astar_full_any":
                    case "scientist_full_any":
                    case "scientist_full_lr300":
                    case "scientist_full_mp5":
                    case "scientist_full_pistol":
                    case "scientist_full_shotgun":
                    case "scientist_turret_any":
                    case "scientist_turret_lr300":
                        return __(htnscientistCC);
                    default:
                        return defaultNpcColor;
                }
            }

            public void UpdatePrivilegeMarker()
            {
                UpdatePrivilegeMarker(markerName);
            }

            public void UpdatePrivilegeMarker(string newMarkerName)
            {
                if (privilege == null || privilege.transform == null || privilege.IsDestroyed)
                {
                    Destroy(this);
                    return;
                }

                RemoveMarker();

                markerName = newMarkerName;
                vending = GameManager.server.CreateEntity(vendingPrefab, privilege.transform.position) as VendingMachineMapMarker;

                if (vending != null)
                {
                    vending.OwnerID = markerId;
                    vending.markerShopName = string.Format("{0}\n{1}", ins.msg("TC"), markerName);
                    vending.enabled = false;
                    vending.Spawn();
                }

                if (usePersonalMarkers)
                {
                    personal = GameManager.server.CreateEntity(vendingPrefab, privilege.transform.position) as VendingMachineMapMarker;

                    if (personal != null)
                    {
                        personal.OwnerID = markerId;
                        personal.markerShopName = "My Base";
                        personal.enabled = false;
                        personal.Spawn();
                    }
                }

                generic = GameManager.server.CreateEntity(genericPrefab, privilege.transform.position) as MapMarkerGenericRadius;

                if (generic != null)
                {
                    generic.OwnerID = markerId;
                    generic.alpha = 1f;
                    generic.color1 = privilegeColor1;
                    generic.color2 = privilegeColor2;
                    generic.radius = 2f;
                    generic.enabled = true;
                    generic.Spawn();
                    generic.SendUpdate();
                }
            }

            public void RemoveMarker()
            {
                if (generic != null && !generic.IsDestroyed)
                {
                    generic.Kill();
                }

                if (personal != null && !personal.IsDestroyed)
                {
                    personal.Kill();
                }

                if (vending != null && !vending.IsDestroyed)
                {
                    vending.Kill();
                }
            }
        }

        private class PlayerTracker : FacepunchBehaviour
        {
            private BasePlayer player;
            private ulong uid;

            private void Awake()
            {
                player = GetComponent<BasePlayer>();
                uid = player.userID;
                InvokeRepeating(UpdateMovement, 0f, trackerUpdateInterval);
                UpdateMovement();
            }

            private void UpdateMovement()
            {
                if (!player || !player.IsConnected)
                {
                    Destroy(this);
                    return;
                }

                if (!trackers.ContainsKey(uid))
                    trackers.Add(uid, new SortedDictionary<float, Vector3>());

                float time = Time.realtimeSinceStartup;

                foreach (float stamp in trackers[uid].Keys.ToList()) // keep the dictionary from becoming enormous by removing entries which are too old
                    if (time - stamp > trackerAge)
                        trackers[uid].Remove(stamp);

                if (trackers[uid].Count > 1)
                {
                    var lastPos = trackers[uid].Values.ElementAt(trackers[uid].Count - 1); // get the last position the player was at

                    if (Vector3.Distance(lastPos, transform.position) <= 1f) // check the distance against the minimum requirement. without this the dictionary will accumulate thousands of entries
                        return;
                }

                trackers[uid][time] = transform.position;
                UpdateTimer();
            }

            private void UpdateTimer()
            {
                if (trackerTimers.ContainsKey(uid))
                {
                    if (trackerTimers[uid] != null)
                    {
                        trackerTimers[uid].Reset();
                        return;
                    }

                    trackerTimers.Remove(uid);
                }

                trackerTimers.Add(uid, ins.timer.Once(trackerAge, () =>
                {
                    trackers.Remove(uid);
                    trackerTimers.Remove(uid);
                }));
            }

            private void OnDestroy()
            {
                CancelInvoke(UpdateMovement);
                UpdateTimer();
                Destroy(this);
            }
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

        private class Radar : FacepunchBehaviour
        {
            private readonly List<BasePlayer> distantPlayers = new List<BasePlayer>();
            private int drawnObjects;
            private ErrorType error;
            private int _inactiveSeconds;
            private int activeSeconds;
            public float invokeTime;
            public float maxDistance;
            public BasePlayer player;
            private Vector3 position;

            private bool setSource = true;
            private bool showActive = true;
            public bool showBags;
            public bool showBox;
            public bool showCollectible;
            public bool showDead;
            public bool showHT;
            private bool showLimits = true;
            public bool showLoot;
            public bool showNPC;
            public bool showOre;
            public bool showSleepers;
            public bool showStash;
            public bool showTC;
            public bool showTurrets;
            private BaseEntity source;

            private enum ErrorType
            {
                Active,
                Airdrops,
                Animals,
                Bags,
                Backpacks,
                Bradley,
                Cars,
                CargoShips,
                Containers,
                Collectibles,
                Cupboards,
                Dead,
                GroupLimitHightlighting,
                Heli,
                Npc,
                Ore,
                Sleepers,
                Source,
                Turrets,
                Zombies
            }

            private void Awake()
            {
                activeRadars.Add(this);
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

                CancelInvoke(DoRadar);
                activeRadars.Remove(this);
                player.ChatMessage(ins.msg("Deactivated", player.UserIDString));
                Destroy(this);
            }

            private bool LatencyAccepted(DateTime tick)
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
                var tick = DateTime.Now;

                try
                {
                    if (!player.IsConnected)
                    {
                        Destroy(this);
                        return;
                    }

                    drawnObjects = 0;

                    if (!SetSource())
                        return;

                    if (!ShowActive(tick))
                        return;

                    if (!ShowSleepers(tick))
                        return;

                    if (barebonesMode)
                        return;

                    if (!ShowCars(tick))
                        return;

                    if (!ShowCargoShips(tick))
                        return;

                    if (!ShowHeli(tick))
                        return;

                    if (!ShowBradley(tick))
                        return;

                    if (!ShowLimits(tick))
                        return;

                    if (!ShowTC(tick))
                        return;

                    if (!ShowContainers(tick))
                        return;

                    if (!ShowBags(tick))
                        return;

                    if (!ShowTurrets(tick))
                        return;

                    if (!ShowDead(tick))
                        return;

                    if (!ShowNPC(tick))
                        return;

                    if (!ShowOre(tick))
                        return;

                    ShowCollectables(tick);
                }
                catch (Exception ex)
                {
                    ins.Puts("Error @{0}: {1} --- {2}", Enum.GetName(typeof(ErrorType), error), ex.Message, ex.StackTrace);
                    player.ChatMessage(ins.msg("Exception", player.UserIDString));

                    switch (error)
                    {
                        case ErrorType.Active:
                            {
                                showActive = false;
                            }
                            break;
                        case ErrorType.Airdrops:
                            {
                                showBox = false;
                            }
                            break;
                        case ErrorType.Animals:
                        case ErrorType.Npc:
                        case ErrorType.Zombies:
                            {
                                showNPC = false;
                            }
                            break;
                        case ErrorType.Bags:
                            {
                                showBags = false;
                            }
                            break;
                        case ErrorType.Backpacks:
                            {
                                showLoot = false;
                            }
                            break;
                        case ErrorType.Bradley:
                            {
                                trackBradley = false;
                            }
                            break;
                        case ErrorType.Cars:
                            {
                                trackCars = false;
                            }
                            break;
                        case ErrorType.CargoShips:
                            {
                                trackCargoShips = false;
                            }
                            break;
                        case ErrorType.Containers:
                            {
                                showBox = false;
                                showLoot = false;
                                showStash = false;
                            }
                            break;
                        case ErrorType.Collectibles:
                            {
                                showCollectible = false;
                            }
                            break;
                        case ErrorType.Cupboards:
                            {
                                showTC = false;
                            }
                            break;
                        case ErrorType.Dead:
                            {
                                showDead = false;
                            }
                            break;
                        case ErrorType.GroupLimitHightlighting:
                            {
                                showLimits = false;
                            }
                            break;
                        case ErrorType.Heli:
                            {
                                trackHeli = false;
                            }
                            break;
                        case ErrorType.Ore:
                            {
                                showOre = false;
                            }
                            break;
                        case ErrorType.Sleepers:
                            {
                                showSleepers = false;
                            }
                            break;
                        case ErrorType.Source:
                            {
                                setSource = false;
                            }
                            break;
                        case ErrorType.Turrets:
                            {
                                showTurrets = false;
                            }
                            break;
                    }
                }
                finally
                {
                    if (!LatencyAccepted(tick))
                    {
                        double ms = (DateTime.Now - tick).TotalMilliseconds;
                        string message = ins.msg("DoESP", player.UserIDString, ms, latencyMs);
                        ins.Puts("{0} for {1} ({2})", message, player.displayName, player.UserIDString);
                        Destroy(this);
                    }
                }
            }

            private bool SetSource()
            {
                if (!setSource)
                {
                    source = player;
                    return true;
                }

                error = ErrorType.Source;
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

            private bool ShowActive(DateTime tick)
            {
                if (!showActive)
                    return true;

                error = ErrorType.Active;

                foreach (var target in BasePlayer.activePlayerList.Where(target => target != null && target.transform != null && target.IsConnected))
                {
                    double currDistance = Math.Floor(Vector3.Distance(target.transform.position, source.transform.position));

                    if (player == target || currDistance > maxDistance || useBypass && ins.permission.UserHasPermission(target.UserIDString, permBypass))
                        continue;

                    var color = __(target.IsAlive() ? activeCC : activeDeadCC);

                    if (currDistance < playerDistance)
                    {
                        string extText = string.Empty;

                        if (storedData.Extended.Contains(player.UserIDString))
                        {
                            extText = target.GetActiveItem()?.info.displayName.translated ?? string.Empty;

                            if (!string.IsNullOrEmpty(extText))
                            {
                                var itemList = target.GetHeldEntity()?.GetComponent<BaseProjectile>()?.GetItem()?.contents?.itemList;

                                if (itemList?.Count > 0)
                                {
                                    string contents = string.Join("|", itemList.Select(item => item.info.displayName.translated.Replace("Weapon ", "").Replace("Simple Handmade ", "").Replace("Muzzle ", "").Replace("4x Zoom Scope", "4x")).ToArray());

                                    if (!string.IsNullOrEmpty(contents))
                                    {
                                        extText = string.Format("{0} ({1})", extText, contents);
                                    }
                                }
                            }
                        }

                        string vanished = ins.Vanish != null && target.IPlayer.HasPermission("vanish.use") && (bool)ins.Vanish.Call("IsInvisible", target) ? "<color=magenta>V</color>" : string.Empty;
                        string health = showHT && target.metabolism != null ? string.Format("{0} <color=orange>{1}</color>:<color=lightblue>{2}</color>", Math.Floor(target.health), target.metabolism.calories.value.ToString("#0"), target.metabolism.hydration.value.ToString("#0")) : Math.Floor(target.health).ToString("#0");

                        if (storedData.Visions.Contains(player.UserIDString)) DrawVision(player, target, invokeTime);
                        if (drawArrows) player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, color, target.transform.position + new Vector3(0f, target.transform.position.y + 10), target.transform.position, 1);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, color, target.transform.position + new Vector3(0f, 2f, 0f), string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>{5} {6}", target.displayName ?? target.userID.ToString(), healthCC, health, distCC, currDistance, vanished, extText));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, color, target.transform.position + new Vector3(0f, 1f, 0f), target.GetHeight(target.modelState.ducked));
                        if (voices.ContainsKey(target.userID) && Vector3.Distance(target.transform.position, player.transform.position) <= voiceDistance)
                        {
                            player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, Color.yellow, target.transform.position + new Vector3(0f, 2.5f, 0f), target.transform.position, 1);
                        }
                    }
                    else if (drawX)
                        distantPlayers.Add(target);
                    else
                        player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, color, target.transform.position + new Vector3(0f, 1f, 0f), 5f);

                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit)
                        return false;
                }

                return LatencyAccepted(tick);
            }

            private bool ShowSleepers(DateTime tick)
            {
                if (!showSleepers)
                    return true;

                error = ErrorType.Sleepers;

                foreach (var sleeper in BasePlayer.sleepingPlayerList.Where(target => target != null && target.transform != null))
                {
                    double currDistance = Math.Floor(Vector3.Distance(sleeper.transform.position, source.transform.position));

                    if (currDistance > maxDistance)
                        continue;

                    if (currDistance < playerDistance)
                    {
                        string health = showHT && sleeper.metabolism != null ? string.Format("{0} <color=orange>{1}</color>:<color=lightblue>{2}</color>", Math.Floor(sleeper.health), sleeper.metabolism.calories.value.ToString("#0"), sleeper.metabolism.hydration.value.ToString("#0")) : Math.Floor(sleeper.health).ToString("#0");
                        var color = __(sleeper.IsAlive() ? sleeperCC : sleeperDeadCC);

                        if (drawArrows) player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, color, sleeper.transform.position + new Vector3(0f, sleeper.transform.position.y + 10), sleeper.transform.position, 1);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, color, sleeper.transform.position, string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>", sleeper.displayName ?? sleeper.userID.ToString(), healthCC, health, distCC, currDistance));
                        if (drawX) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, color, sleeper.transform.position + new Vector3(0f, 1f, 0f), "X");
                        else if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, color, sleeper.transform.position, GetScale(currDistance));
                    }
                    else player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, Color.cyan, sleeper.transform.position + new Vector3(0f, 1f, 0f), 5f);

                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                }

                return LatencyAccepted(tick);
            }

            private bool ShowCars(DateTime tick)
            {
                if (!trackCars)
                    return true;

                error = ErrorType.Cars;

                if (cache.Cars.Count > 0)
                {
                    foreach (var car in cache.Cars.Where(target => target != null && target.transform != null))
                    {
                        double currDistance = Math.Floor(Vector3.Distance(car.transform.position, source.transform.position));

                        if (currDistance > maxDistance)
                            continue;

                        string carHealth = car.health > 1000 ? Math.Floor(car.health).ToString("#,##0,K", CultureInfo.InvariantCulture) : Math.Floor(car.health).ToString("#0");
                        string info = string.Format("<color={0}>{1}</color>", healthCC, carHealth);

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(heliCC), car.transform.position + new Vector3(0f, 2f, 0f), string.Format("Car {0} <color={1}>{2}</color>", info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(heliCC), car.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowCargoShips(DateTime tick)
            {
                if (!trackCargoShips)
                    return true;

                error = ErrorType.CargoShips;

                if (cache.CargoShips.Count > 0)
                {
                    foreach (var cs in cache.CargoShips.Where(target => target != null && target.transform != null))
                    {
                        double currDistance = Math.Floor(Vector3.Distance(cs.transform.position, source.transform.position));
                        string cargoHealth = cs.Health() > 1000 ? Math.Floor(cs.Health()).ToString("#,##0,K", CultureInfo.InvariantCulture) : Math.Floor(cs.Health()).ToString("#0");
                        string info = string.Format("<color={0}>{1}</color>", healthCC, cargoHealth);

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(heliCC), cs.transform.position + new Vector3(0f, 2f, 0f), string.Format("CS {0} <color={1}>{2}</color>", info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(heliCC), cs.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowHeli(DateTime tick)
            {
                if (!trackHeli)
                    return true;

                error = ErrorType.Heli;

                if (cache.Helis.Count > 0)
                {
                    foreach (var heli in cache.Helis.Where(target => target != null && target.transform != null))
                    {
                        double currDistance = Math.Floor(Vector3.Distance(heli.transform.position, source.transform.position));
                        string heliHealth = heli.health > 1000 ? Math.Floor(heli.health).ToString("#,##0,K", CultureInfo.InvariantCulture) : Math.Floor(heli.health).ToString("#0");
                        string info = showHeliRotorHealth ? string.Format("<color={0}>{1}</color> (<color=yellow>{2}</color>/<color=yellow>{3}</color>)", healthCC, heliHealth, Math.Floor(heli.weakspots[0].health), Math.Floor(heli.weakspots[1].health)) : string.Format("<color={0}>{1}</color>", healthCC, heliHealth);

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(heliCC), heli.transform.position + new Vector3(0f, 2f, 0f), string.Format("H {0} <color={1}>{2}</color>", info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(heliCC), heli.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowBradley(DateTime tick)
            {
                if (!trackBradley)
                    return true;

                error = ErrorType.Bradley;

                if (cache.APCs.Count > 0)
                {
                    foreach (var bradley in cache.APCs.Where(target => target != null && target.transform != null))
                    {
                        double currDistance = Math.Floor(Vector3.Distance(bradley.transform.position, source.transform.position));
                        string info = string.Format("<color={0}>{1}</color>", healthCC, bradley.health > 1000 ? Math.Floor(bradley.health).ToString("#,##0,K", CultureInfo.InvariantCulture) : Math.Floor(bradley.health).ToString());

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(bradleyCC), bradley.transform.position + new Vector3(0f, 2f, 0f), string.Format("B {0} <color={1}>{2}</color>", info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(bradleyCC), bradley.transform.position + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowLimits(DateTime tick)
            {
                if (!showLimits)
                    return true;

                error = ErrorType.GroupLimitHightlighting;

                if (distantPlayers.Count > 0)
                {
                    var dict = new Dictionary<int, List<BasePlayer>>();

                    foreach (var target in distantPlayers.ToList())
                    {
                        var list = distantPlayers.Where(x => Vector3.Distance(x.transform.position, target.transform.position) < groupRange && !dict.Any(y => y.Value.Contains(x))).ToList();

                        if (list.Count() >= groupLimit)
                        {
                            int index = 0;

                            while (dict.ContainsKey(index))
                                index++;

                            dict.Add(index, list);
                            distantPlayers.RemoveAll(x => list.Contains(x));
                        }
                    }

                    foreach (var target in distantPlayers)
                        player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, target.IsAlive() ? Color.green : Color.red, target.transform.position + new Vector3(0f, 1f, 0f), "X");

                    foreach (var entry in dict)
                    {
                        foreach (var target in entry.Value)
                        {
                            player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(target.IsAlive() ? GetGroupColor(entry.Key) : groupColorDead), target.transform.position + new Vector3(0f, 1f, 0f), "X");
                        }

                        if (groupCountHeight > 0f)
                        {
                            player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, Color.magenta, entry.Value.First().transform.position + new Vector3(0f, groupCountHeight, 0f), entry.Value.Count.ToString());
                        }
                    }

                    distantPlayers.Clear();
                    dict.Clear();
                }

                return LatencyAccepted(tick);
            }

            private bool ShowTC(DateTime tick)
            {
                if (!showTC)
                    return true;

                error = ErrorType.Cupboards;

                foreach (var tc in cache.Cupboards.Where(target => target != null && target.transform != null))
                {
                    double currDistance = Math.Floor(Vector3.Distance(tc.transform.position, source.transform.position));

                    if (currDistance < tcDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(tcCC), tc.transform.position + new Vector3(0f, 0.5f, 0f), string.Format("TC <color={0}>{1}</color>", distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(tcCC), tc.transform.position + new Vector3(0f, 0.5f, 0f), 3f);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowContainers(DateTime tick)
            {
                if (!showBox && !showLoot && !showStash)
                {
                    return true;
                }

                if (showLoot)
                {
                    error = ErrorType.Backpacks;

                    foreach (var entry in cache.Backpacks)
                    {
                        double currDistance = Math.Floor(Vector3.Distance(entry.Key, source.transform.position));

                        if (currDistance > maxDistance || currDistance > lootDistance)
                            continue;

                        string contents = string.Empty;
                        uint uid;

                        if (entry.Value.Info != null && uint.TryParse(entry.Value.Info.ToString(), out uid))
                        {
                            var backpack = BaseNetworkable.serverEntities.Find(uid) as DroppedItemContainer;

                            if (backpack == null || backpack.transform == null || backpack.IsDestroyed)
                                continue;

                            if (backpack.inventory?.itemList != null) contents = string.Format("({0}) ", backpackContentAmount > 0 && backpack.inventory.itemList.Count > 0 ? string.Join(", ", backpack.inventory.itemList.Take(backpackContentAmount).Select(item => string.Format("{0} ({1})", item.info.displayName.translated.ToLower(), item.amount)).ToArray()) : backpack.inventory.itemList.Count().ToString());
                            if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(backpackCC), entry.Key + new Vector3(0f, 0.5f, 0f), string.Format("{0} <color={1}>{2}</color><color={3}>{4}</color>", string.IsNullOrEmpty(backpack._playerName) ? ins.msg("backpack", player.UserIDString) : backpack._playerName, backpackCC, contents, distCC, currDistance));
                            if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(backpackCC), entry.Key + new Vector3(0f, 0.5f, 0f), GetScale(currDistance));
                            if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                        }
                    }
                }

                if (showBox)
                {
                    error = ErrorType.Airdrops;
                    foreach (var drop in cache.Airdrops.Where(target => target != null && target.transform != null))
                    {
                        double currDistance = Math.Floor(Vector3.Distance(drop.transform.position, source.transform.position));

                        if (currDistance > maxDistance || currDistance > adDistance)
                            continue;

                        string contents = showAirdropContents && drop.inventory.itemList.Count > 0 ? string.Format("({0}) ", string.Join(", ", drop.inventory.itemList.Select(item => string.Format("{0} ({1})", item.info.displayName.translated.ToLower(), item.amount)).ToArray())) : string.Format("({0}) ", drop.inventory.itemList.Count());

                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(airdropCC), drop.transform.position + new Vector3(0f, 0.5f, 0f), string.Format("{0} {1}<color={2}>{3}</color>", _(drop.ShortPrefabName), contents, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(airdropCC), drop.transform.position + new Vector3(0f, 0.5f, 0f), GetScale(currDistance));
                    }
                }

                error = ErrorType.Containers;
                foreach (var box in cache.Containers)
                {
                    double currDistance = Math.Floor(Vector3.Distance(box.Key, source.transform.position));

                    if (currDistance > maxDistance)
                        continue;

                    bool isBox = box.Value.Name.Contains("box") || box.Value.Name.Equals("heli_crate");
                    bool isLoot = box.Value.Name.Contains("loot") || box.Value.Name.Contains("crate_") || box.Value.Name.Contains("trash") || box.Value.Name.Contains("hackable");

                    if (isBox)
                    {
                        if (!showBox || currDistance > boxDistance)
                            continue;
                    }

                    if (isLoot)
                    {
                        if (!showLoot || currDistance > lootDistance)
                            continue;
                    }

                    if (box.Value.Name.Contains("stash"))
                    {
                        if (!showStash || currDistance > stashDistance)
                            continue;
                    }

                    string colorHex = isBox ? boxCC : isLoot ? lootCC : stashCC;
                    string contents = string.Empty;
                    uint uid;

                    if (box.Value.Info != null && uint.TryParse(box.Value.Info.ToString(), out uid))
                    {
                        var container = BaseNetworkable.serverEntities.Find(uid) as StorageContainer;

                        if (container == null)
                            continue;

                        if (storedData.OnlineBoxes.Contains(player.UserIDString) && container.name.Contains("box"))
                        {
                            var owner = BasePlayer.activePlayerList.Find(x => x.userID == container.OwnerID);

                            if (owner == null || !owner.IsConnected)
                            {
                                continue;
                            }
                        }

                        if (container.inventory?.itemList != null)
                        {
                            if (container.inventory.itemList.Count > 0)
                            {
                                if (isLoot && showLootContents || container.ShortPrefabName.Contains("stash") && showStashContents)
                                    contents = string.Format("({0}) ", string.Join(", ", container.inventory.itemList.Select(item => string.Format("{0} ({1})", item.info.displayName.translated.ToLower(), item.amount)).ToArray()));
                                else
                                    contents = string.Format("({0}) ", container.inventory.itemList.Count());
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(contents) && !drawEmptyContainers) continue;
                    if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(colorHex), box.Key + new Vector3(0f, 0.5f, 0f), string.Format("{0} {1}<color={2}>{3}</color>", _(box.Value.Name), contents, distCC, currDistance));
                    if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(colorHex), box.Key + new Vector3(0f, 0.5f, 0f), GetScale(currDistance));
                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                }

                return LatencyAccepted(tick);
            }

            private bool ShowBags(DateTime tick)
            {
                if (!showBags)
                    return true;

                error = ErrorType.Bags;

                foreach (var bag in cache.Bags)
                {
                    var currDistance = Math.Floor(Vector3.Distance(bag.Key, source.transform.position));

                    if (currDistance < bagDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(bagCC), bag.Key, string.Format("bag <color={0}>{1}</color>", distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(bagCC), bag.Key, bag.Value.Size);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowTurrets(DateTime tick)
            {
                if (!showTurrets)
                    return true;

                error = ErrorType.Turrets;

                foreach (var turret in cache.Turrets)
                {
                    var currDistance = Math.Floor(Vector3.Distance(turret.Key, source.transform.position));

                    if (currDistance < turretDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(atCC), turret.Key + new Vector3(0f, 0.5f, 0f), string.Format("AT ({0}) <color={1}>{2}</color>", turret.Value.Info, distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(atCC), turret.Key + new Vector3(0f, 0.5f, 0f), turret.Value.Size);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowDead(DateTime tick)
            {
                if (!showDead)
                    return true;

                error = ErrorType.Dead;

                foreach (var corpse in cache.Corpses.Where(kvp => kvp.Key != null && kvp.Key.transform != null))
                {
                    double currDistance = Math.Floor(Vector3.Distance(source.transform.position, corpse.Key.transform.position));

                    if (currDistance < corpseDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(corpseCC), corpse.Key.transform.position + new Vector3(0f, 0.25f, 0f), string.Format("{0} ({1})", corpse.Value.Name, corpse.Value.Info));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(corpseCC), corpse.Key, GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowNPC(DateTime tick)
            {
                if (!showNPC)
                    return true;

                error = ErrorType.Zombies;
                foreach (var zombie in cache.Zombies.Where(target => target != null && target.transform != null))
                {
                    double currDistance = Math.Floor(Vector3.Distance(zombie.transform.position, source.transform.position));

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

                error = ErrorType.Npc;
                foreach (var target in cache.NPCPlayers.Where(target => target != null && target.transform != null))
                {
                    double currDistance = Math.Floor(Vector3.Distance(target.transform.position, source.transform.position));

                    if (player == target || currDistance > maxDistance)
                        continue;

                    bool digits = target.displayName != null && target.displayName.All(char.IsDigit);
                    string npcColor = target is HTNPlayer ? htnscientistCC : target.ShortPrefabName.Contains("peacekeeper") ? peacekeeperCC : target.ShortPrefabName.Contains("scientist") ? scientistCC : target.ShortPrefabName == "murderer" ? murdererCC : npcCC;

                    if (currDistance < playerDistance)
                    {
                        string displayName = digits ? ins.msg("scientist", player.UserIDString) : target.displayName ?? (target.ShortPrefabName == "scientist" ? ins.msg("scientist", player.UserIDString) : ins.msg("npc", player.UserIDString));

                        if (drawArrows) player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, target.transform.position.y + 10), target.transform.position, 1);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, 2f, 0f), string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>", displayName, healthCC, Math.Floor(target.health), distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, 1f, 0f), target.GetHeight(target.modelState.ducked));
                    }
                    else player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(npcColor), target.transform.position + new Vector3(0f, 1f, 0f), 5f);

                    if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                }

                error = ErrorType.Animals;
                foreach (var npc in cache.Animals.Where(target => target != null && target.transform != null))
                {
                    double currDistance = Math.Floor(Vector3.Distance(npc.transform.position, source.transform.position));

                    if (currDistance < npcDistance && currDistance < maxDistance)
                    {
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(npcCC), npc.transform.position + new Vector3(0f, 1f, 0f), string.Format("{0} <color={1}>{2}</color> <color={3}>{4}</color>", npc.ShortPrefabName, healthCC, Math.Floor(npc.health), distCC, currDistance));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(npcCC), npc.transform.position + new Vector3(0f, 1f, 0f), npc.bounds.size.y);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowOre(DateTime tick)
            {
                if (!showOre)
                    return true;

                error = ErrorType.Ore;

                foreach (var ore in cache.Ores)
                {
                    double currDistance = Math.Floor(Vector3.Distance(source.transform.position, ore.Key));

                    if (currDistance < oreDistance && currDistance < maxDistance)
                    {
                        object value = showResourceAmounts ? string.Format("({0})", ore.Value.Info) : string.Format("<color={0}>{1}</color>", distCC, currDistance);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(resourceCC), ore.Key + new Vector3(0f, 1f, 0f), string.Format("{0} {1}", ore.Value.Name, value));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(resourceCC), ore.Key + new Vector3(0f, 1f, 0f), GetScale(currDistance));
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private bool ShowCollectables(DateTime tick)
            {
                if (!showCollectible)
                    return true;

                error = ErrorType.Collectibles;

                foreach (var col in cache.Collectibles)
                {
                    var currDistance = Math.Floor(Vector3.Distance(col.Key, source.transform.position));

                    if (currDistance < colDistance && currDistance < maxDistance)
                    {
                        object value = showResourceAmounts ? string.Format("({0})", col.Value.Info) : string.Format("<color={0}>{1}</color>", distCC, currDistance);
                        if (drawText) player.SendConsoleCommand("ddraw.text", invokeTime + flickerDelay, __(colCC), col.Key + new Vector3(0f, 1f, 0f), string.Format("{0} {1}", _(col.Value.Name), value));
                        if (drawBox) player.SendConsoleCommand("ddraw.box", invokeTime + flickerDelay, __(colCC), col.Key + new Vector3(0f, 1f, 0f), col.Value.Size);
                        if (objectsLimit > 0 && ++drawnObjects > objectsLimit) return false;
                    }
                }

                return LatencyAccepted(tick);
            }

            private static double GetScale(double value)
            {
                return value * 0.02;
            }
        }

        private static bool isLoading = true;
        private const ulong markerId = 51239124;
        private const string permName = "adminradar.allowed";
        private const string permBypass = "adminradar.bypass";
        private const string genericPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";
        private const string vendingPrefab = "assets/prefabs/deployable/vendingmachine/vending_mapmarker.prefab";
        private const float flickerDelay = 0.05f;
        private static AdminRadar ins;
        private static StoredData storedData = new StoredData();
        private static bool init; // don't use cache while false

        private static readonly List<string> tags = new List<string>
            {"ore", "cluster", "1", "2", "3", "4", "5", "6", "_", ".", "-", "deployed", "wooden", "large", "pile", "prefab", "collectable", "loot", "small"}; // strip these from names to reduce the size of the text and make it more readable

        private static readonly Dictionary<ulong, SortedDictionary<float, Vector3>> trackers = new Dictionary<ulong, SortedDictionary<float, Vector3>>(); // player id, timestamp and player's position
        private static readonly Dictionary<ulong, Timer> voices = new Dictionary<ulong, Timer>();
        private static readonly Dictionary<ulong, Timer> trackerTimers = new Dictionary<ulong, Timer>();
        private static readonly Dictionary<ulong, Color> playersColor = new Dictionary<ulong, Color>();
        private static readonly Dictionary<string, Marker> markers = new Dictionary<string, Marker>();
        private static readonly List<Radar> activeRadars = new List<Radar>();
        private static Cache cache = new Cache();
        [PluginReference] private Plugin Vanish;

        private bool IsRadar(string id)
        {
            return activeRadars.Any(x => x.player.UserIDString == id);
        }

        private void Init()
        {
            ins = this;
            isLoading = true;
            Unsubscribe(nameof(OnEntityDeath));
            Unsubscribe(nameof(OnEntityKill));
            Unsubscribe(nameof(OnEntitySpawned));
            Unsubscribe(nameof(OnPlayerDisconnected));
            Unsubscribe(nameof(OnPlayerSleepEnded));
            Unsubscribe(nameof(OnPlayerVoice));
            Unsubscribe(nameof(OnPlayerInit));
            Unsubscribe(nameof(CanNetworkTo));
            Unsubscribe(nameof(OnCupboardAuthorize));
            Unsubscribe(nameof(OnCupboardClearList));
            Unsubscribe(nameof(OnCupboardDeauthorize));
        }

        private void Loaded()
        {
            cache = new Cache();
            permission.RegisterPermission(permName, this);
            permission.RegisterPermission(permBypass, this);
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
                isLoading = false;
                return;
            }

            if (usePlayerTracker)
            {
                Subscribe(nameof(OnPlayerSleepEnded));
            }

            if (usePlayerMarkers || useSleeperMarkers || usePrivilegeMarkers)
            {
                Subscribe(nameof(OnPlayerInit));
                Subscribe(nameof(CanNetworkTo));

                if (!useVoiceDetection)
                {
                    Subscribe(nameof(OnPlayerDisconnected));
                }
            }

            if (usePlayerTracker || usePlayerMarkers)
            {
                foreach (var player in BasePlayer.activePlayerList)
                {
                    if (usePlayerTracker)
                    {
                        Track(player);
                    }

                    if (usePlayerMarkers)
                    {
                        coList.Add(player);
                    }
                }
            }

            if (useSleeperMarkers)
            {
                foreach (var player in BasePlayer.sleepingPlayerList)
                {
                    coList.Add(player);
                }
            }

            if (usePrivilegeMarkers)
            {
                Subscribe(nameof(OnCupboardAuthorize));
                Subscribe(nameof(OnCupboardClearList));
                Subscribe(nameof(OnCupboardDeauthorize));
            }

            var tick = DateTime.Now;

            Subscribe(nameof(OnEntityDeath));
            Subscribe(nameof(OnEntityKill));
            Subscribe(nameof(OnEntitySpawned));

            int cached = 0, total = 0;
            foreach (var e in BaseNetworkable.serverEntities.Cast<BaseEntity>())
            {                
                if (AddToCache(e))
                    cached++;

                total++;
            }

            isLoading = false;
            Puts("Took {0}ms to cache {1}/{2} entities", (DateTime.Now - tick).TotalMilliseconds, cached, total);
            StartCreateMapMarkersCoroutine();
        }

        private void OnCupboardAuthorize(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (privilege != null && player != null)
            {
                SetupPrivilegeMarker(privilege, CupboardAction.Authorize, player);
            }
        }

        private void OnCupboardClearList(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (privilege != null && player != null)
            {
                SetupPrivilegeMarker(privilege, CupboardAction.Clear, null);
            }
        }

        private void OnCupboardDeauthorize(BuildingPrivlidge privilege, BasePlayer player)
        {
            if (privilege != null && player != null)
            {
                SetupPrivilegeMarker(privilege, CupboardAction.Deauthorize, player);
            }
        }

        private object CanNetworkTo(BaseNetworkable entity, BasePlayer target)
        {
            if (entity == null || target == null || !target.IsConnected || !(entity is MapMarker) || entity.GetComponent<BaseEntity>().OwnerID != markerId)
            {
                return null;
            }

            if (hideSelfMarker && markers.Values.Any(m => m.entity == target && (m.generic == entity || m.vending == entity)))
            {
                return false;
            }

            if (markers.Values.Any(m => m.IsPrivilegeMarker && (entity == m.personal || entity == m.generic || entity == m.vending)))
            {
                var marker = markers.Values.First(m => m.IsPrivilegeMarker && (entity == m.personal || entity == m.generic || entity == m.vending));

                if (entity == marker.personal) // if usePersonalMarkers then show if the target is authed
                {
                    return marker.privilege.IsAuthed(target);
                }

                if (entity == marker.generic) // only show generic markers to authed players and admins
                {
                    if (usePersonalMarkers && marker.privilege.IsAuthed(target))
                    {
                        return true;
                    }

                    if (HasAccess(target))
                    {
                        return true;
                    }
                }

                if (entity == marker.vending) // this marker contains all authed users
                {
                    if (target.net.connection.authLevel == 0)
                    {
                        return false; // do not show who is authed to players
                    }

                    if (marker.personal != null && marker.privilege.IsAuthed(target))
                    {
                        return false; // if usePersonalMarkers then don't show if this is a cupboard they're authed on
                    }
                }
            }

            return HasAccess(target);
        }

        private void OnPlayerInit(BasePlayer player)
        {
            if (player == null)
                return;

            StartUpdateMapMarkersCoroutine(player);

            if (player.gameObject.GetComponent<Marker>() == null)
            {
                player.gameObject.AddComponent<Marker>();
            }
        }

        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (!trackAdmins && (player.IsAdmin || DeveloperList.Contains(player.userID)))
                return;

            Track(player);
        }

        private void OnPlayerVoice(BasePlayer player, Byte[] data)
        {
            ulong userId = player.userID;

            if (voices.ContainsKey(userId))
            {
                voices[userId].Reset();
                return;
            }

            voices.Add(userId, timer.Once(voiceInterval, () => voices.Remove(userId)));
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (useVoiceDetection)
            {
                voices.Remove(player.userID);
            }

            if (useSleeperMarkers)
            {
                var marker = player.gameObject.GetComponent<Marker>();

                if (marker != null)
                    marker.UpdateMarker();
                else player.gameObject.AddComponent<Marker>();
            }
            else if (usePlayerMarkers)
            {
                var marker = player.gameObject.GetComponent<Marker>();

                if (marker != null)
                {
                    UnityEngine.Object.Destroy(marker);
                }
            }
        }

        private void Unload()
        {
            Interface.Oxide.DataFileSystem.WriteObject(Name, storedData);

            var radarObjects = UnityEngine.Object.FindObjectsOfType(typeof(Radar));

            if (radarObjects != null)
            {
                foreach (var gameObj in radarObjects)
                {
                    UnityEngine.Object.Destroy(gameObj);
                }
            }

            var playerTrackerObjects = UnityEngine.Object.FindObjectsOfType(typeof(PlayerTracker));

            if (playerTrackerObjects != null)
            {
                foreach (var gameObj in playerTrackerObjects)
                {
                    UnityEngine.Object.Destroy(gameObj);
                }
            }

            var markerObjects = UnityEngine.Object.FindObjectsOfType(typeof(Marker));

            if (markerObjects != null)
            {
                foreach (var gameObj in markerObjects)
                {
                    UnityEngine.Object.Destroy(gameObj);
                }
            }

            foreach (var value in trackerTimers.Values.ToList())
            {
                if (value != null && !value.Destroyed)
                {
                    value.Destroy();
                }
            }

            markers.Clear();
            trackerTimers.Clear();
            playersColor.Clear();
            trackers.Clear();
            voices.Clear();
            tags.Clear();
            cache = null;
        }

        private void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            RemoveFromCache(entity);
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            RemoveFromCache(entity);
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            AddToCache(entity as BaseEntity);
        }

        public void SetupPrivilegeMarker(BuildingPrivlidge privilege, CupboardAction action, BasePlayer player)
        {
            var sb = new StringBuilder();

            if (action == CupboardAction.Authorize)
            {
                foreach (var pnid in privilege.authorizedPlayers)
                {
                    sb.AppendLine(pnid.username);
                }

                if (player != null)
                {
                    sb.AppendLine(player.displayName);
                }
            }
            else if (action == CupboardAction.Deauthorize)
            {
                foreach (var pnid in privilege.authorizedPlayers)
                {
                    if (player != null && pnid.userid == player.userID)
                    {
                        continue;
                    }

                    sb.AppendLine(pnid.username);
                }
            }

            var pt = privilege.gameObject.GetComponent<Marker>();

            if (sb.Length == 0 || action == CupboardAction.Clear)
            {
                if (pt != null)
                {
                    UnityEngine.Object.Destroy(pt);
                }

                return;
            }

            sb.Length--;

            if (pt == null)
            {
                pt = privilege.gameObject.AddComponent<Marker>();
            }

            pt.UpdatePrivilegeMarker(sb.ToString());
        }

        private static void DrawVision(BasePlayer player, BasePlayer target, float invokeTime)
        {
            RaycastHit hit;
            if (!Physics.Raycast(target.eyes.HeadRay(), out hit, Mathf.Infinity))
                return;

            player.SendConsoleCommand("ddraw.arrow", invokeTime + flickerDelay, Color.red, target.eyes.position + new Vector3(0f, 0.115f, 0f), hit.point, 0.15f);
        }

        private static Color __(string value)
        {
            if (value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'))
            {
                value = "#" + value;
            }

            Color color;
            if (!ColorUtility.TryParseHtmlString(value, out color))
                color = Color.white;

            return color;
        }

        private static string _(string value)
        {
            foreach (string str in tags)
                value = value.Replace(str, string.Empty);

            return value;
        }

        private void Track(BasePlayer player)
        {
            if (!player.gameObject.GetComponent<PlayerTracker>())
                player.gameObject.AddComponent<PlayerTracker>();

            if (trackerTimers.ContainsKey(player.userID))
            {
                trackerTimers[player.userID]?.Destroy();
                trackerTimers.Remove(player.userID);
            }
        }

        private static void RemoveFromCache(BaseNetworkable entity)
        {
            if (!init || entity == null)
                return;

            if (entity.transform != null)
            {
                if (cache.Backpacks.ContainsKey(entity.transform.position))
                    cache.Backpacks.Remove(entity.transform.position);
                else if (cache.Ores.ContainsKey(entity.transform.position))
                    cache.Ores.Remove(entity.transform.position);
                else if (cache.Containers.ContainsKey(entity.transform.position))
                    cache.Containers.Remove(entity.transform.position);
                else if (cache.Bags.ContainsKey(entity.transform.position))
                    cache.Bags.Remove(entity.transform.position);
                else if (cache.Turrets.ContainsKey(entity.transform.position))
                    cache.Turrets.Remove(entity.transform.position);
                else if (cache.Collectibles.ContainsKey(entity.transform.position))
                    cache.Collectibles.Remove(entity.transform.position);
            }

            if (entity is BasePlayer && cache.NPCPlayers.Contains(entity as BasePlayer))
                cache.NPCPlayers.Remove(entity as BasePlayer);
            else if (entity is BaseNpc)
                cache.Animals.Remove(entity as BaseNpc);
            else if (entity is BradleyAPC)
                cache.APCs.Remove(entity as BradleyAPC);
            else if (entity is BaseHelicopter)
                cache.Helis.Remove(entity as BaseHelicopter);
            else if (entity is PlayerCorpse)
                cache.Corpses.Remove(entity as PlayerCorpse);
            else if (entity is SupplyDrop)
                cache.Airdrops.Remove(entity as SupplyDrop);
            else if (entity is BaseCar)
                cache.Cars.Remove(entity as BaseCar);
            else if (entity is CargoShip)
                cache.CargoShips.Remove(entity as CargoShip);
            else if (entity is Zombie)
                cache.Zombies.Remove(entity as Zombie);
        }

        private readonly List<BaseEntity> coList = new List<BaseEntity>();

        private void StartCreateMapMarkersCoroutine()
        {
            ServerMgr.Instance.StartCoroutine(CreateMapMarkers());
        }

        private System.Collections.IEnumerator CreateMapMarkers()
        {
            for (var i = 0; i < coList.Count; i++)
            {
                var e = coList[i];

                if (e != null && !e.IsDestroyed)
                {
                    if (e is BuildingPrivlidge)
                        SetupPrivilegeMarker(e as BuildingPrivlidge, CupboardAction.Authorize, null);
                    else e.gameObject.AddComponent<Marker>();
                }

                yield return new WaitForSeconds(0.1f);
            }

            coList.Clear();
            ServerMgr.Instance.StopCoroutine(CreateMapMarkers());
        }

        private void StartUpdateMapMarkersCoroutine(BasePlayer player)
        {
            ServerMgr.Instance.StartCoroutine(UpdateMapMarkers(player));
        }

        private System.Collections.IEnumerator UpdateMapMarkers(BasePlayer player)
        {
            foreach (var marker in markers.Values.ToList())
            {
                if (marker != null && marker.isActiveAndEnabled)
                {
                    bool allowed = HasAccess(player);

                    if (marker.IsPrivilegeMarker)
                    {
                        if (allowed || (usePrivilegeMarkers && marker.privilege.IsAuthed(player)))
                        {
                            marker.UpdatePrivilegeMarker();
                        }
                    }
                    else if (allowed)
                    {
                        marker.UpdateMarker();
                    }

                    yield return new WaitForSeconds(0.1f);
                }
            }

            ServerMgr.Instance.StopCoroutine("UpdateMapMarkers");
        }

        private bool AddToCache(BaseEntity entity)
        {
            if (!init || entity?.transform == null || entity.IsDestroyed)
                return false;

            if (entity.IsNpc)
            {
                if (useHumanoidTracker && !(entity is BaseNpc) || useAnimalTracker && entity is BaseNpc)
                {
                    if (isLoading)
                    {
                        if (!coList.Contains(entity))
                        {
                            coList.Add(entity);
                        }
                    }
                    else entity.gameObject.AddComponent<Marker>();
                }
            }
            else if (usePrivilegeMarkers && entity is BuildingPrivlidge)
            {
                var priv = entity as BuildingPrivlidge;

                if (priv.AnyAuthed())
                {
                    if (isLoading)
                    {
                        if (!coList.Contains(entity))
                        {
                            coList.Add(entity);
                        }
                    }
                    else SetupPrivilegeMarker(priv, CupboardAction.Authorize, null);
                }
            }

            if (entity as BasePlayer != null)
            {
                var player = entity as BasePlayer;

                if (!player.userID.IsSteamId() && !cache.NPCPlayers.Contains(player))
                {
                    cache.NPCPlayers.Add(player);
                    return true;
                }
            }
            else if (entity is BaseNpc)
            {
                var npc = entity as BaseNpc;

                if (!cache.Animals.Contains(npc))
                {
                    cache.Animals.Add(npc);
                    return true;
                }
            }
            else if (trackCargoShips && entity is CargoShip)
            {
                var cargoship = entity as CargoShip;

                if (!cache.CargoShips.Contains(cargoship))
                {
                    cache.CargoShips.Add(cargoship);
                    return true;
                }
            }
            else if (trackCars && entity is BaseCar)
            {
                var car = entity as BaseCar;

                if (!cache.Cars.Contains(car))
                {
                    cache.Cars.Add(car);
                    return true;
                }
            }
            else if (entity is Zombie)
            {
                var zombie = entity as Zombie;

                if (!cache.Zombies.Contains(zombie))
                {
                    cache.Zombies.Add(zombie);
                    return true;
                }
            }
            else if (entity is BaseHelicopter && trackHeli)
            {
                var heli = entity as BaseHelicopter;

                if (!cache.Helis.Contains(heli))
                {
                    cache.Helis.Add(heli);
                    return true;
                }
            }
            else if (entity is BradleyAPC && trackBradley)
            {
                var apc = entity as BradleyAPC;

                if (!cache.APCs.Contains(apc))
                {
                    cache.APCs.Add(apc);
                    return true;
                }
            }
            else if (entity is BuildingPrivlidge && entity.transform != null)
            {
                var priv = entity as BuildingPrivlidge;

                if (!cache.Cupboards.Contains(priv))
                {
                    cache.Cupboards.Add(priv);
                    return true;
                }
            }
            else if (entity is SupplyDrop)
            {
                var drop = entity as SupplyDrop;

                if (!cache.Airdrops.Contains(drop))
                {
                    cache.Airdrops.Add(drop);
                    return true;
                }
            }
            else if (entity is StorageContainer)
            {
                if (entity.name.Contains("turret"))
                {
                    if (!cache.Turrets.ContainsKey(entity.transform.position))
                    {
                        cache.Turrets.Add(entity.transform.position, new CachedInfo { Size = 1f, Info = entity.GetComponent<StorageContainer>()?.inventory?.itemList?.Select(item => item.amount).Sum() ?? 0 });
                        return true;
                    }
                }
                else if (entity.name.Contains("box") || entity.ShortPrefabName.Equals("heli_crate") || entity.name.Contains("loot") || entity.name.Contains("crate_") || entity.name.Contains("stash") || entity.name.Contains("oil") || entity.name.Contains("hackable"))
                {
                    if (!cache.Containers.ContainsKey(entity.transform.position))
                    {
                        cache.Containers.Add(entity.transform.position, new CachedInfo { Name = entity.ShortPrefabName, Info = entity.net.ID });
                        return true;
                    }
                }
            }
            else if (entity is DroppedItemContainer)
            {
                var position = entity.transform.position;

                while (cache.Backpacks.ContainsKey(position))
                {
                    position.y += 1f;
                }

                cache.Backpacks.Add(position, new CachedInfo { Name = entity.ShortPrefabName, Info = entity.net.ID });
                return true;
            }
            else if (entity is SleepingBag)
            {
                if (!cache.Bags.ContainsKey(entity.transform.position))
                {
                    cache.Bags.Add(entity.transform.position, new CachedInfo { Size = 0.5f });
                    return true;
                }
            }
            else if (entity is PlayerCorpse)
            {
                var corpse = entity as PlayerCorpse;

                if (!cache.Corpses.ContainsKey(corpse))
                {
                    int amount = 0;

                    if (corpse.containers != null)
                        foreach (var container in corpse.containers)
                            amount += container.itemList.Count;

                    cache.Corpses.Add(corpse, new CachedInfo { Name = corpse.parentEnt?.ToString() ?? corpse.playerSteamID.ToString(), Info = amount });
                    return true;
                }
            }
            else if (entity is CollectibleEntity)
            {
                if (!cache.Collectibles.ContainsKey(entity.transform.position))
                {
                    cache.Collectibles.Add(entity.transform.position, new CachedInfo { Name = _(entity.ShortPrefabName), Size = 0.5f, Info = Math.Ceiling(entity.GetComponent<CollectibleEntity>()?.itemList?.Select(item => item.amount).Sum() ?? 0) });
                    return true;
                }
            }
            else if (entity.name.Contains("-ore"))
            {
                if (!cache.Ores.ContainsKey(entity.transform.position))
                {
                    cache.Ores.Add(entity.transform.position, new CachedInfo { Name = _(entity.ShortPrefabName), Info = Math.Ceiling(entity.GetComponentInParent<ResourceDispenser>()?.containedItems?.Select(item => item.amount).Sum() ?? 0) });
                    return true;
                }
            }

            return false;
        }

        [ConsoleCommand("espgui")]
        private void ccmdESPGUI(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();

            if (!player || !arg.HasArgs())
                return;

            cmdESP(player, "espgui", arg.Args);
        }

        private bool HasAccess(BasePlayer player)
        {
            if (DeveloperList.Contains(player.userID))
                return true;

            if (authorized.Count > 0)
                return authorized.Contains(player.UserIDString);

            if (player.net.connection.authLevel >= authLevel)
                return true;

            if (permission.UserHasPermission(player.UserIDString, "fauxadmin.allowed") && permission.UserHasPermission(player.UserIDString, permName) && player.IsDeveloper)
                return true;

            return false;
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
                            int drops = 0;

                            foreach (var entity in BaseNetworkable.serverEntities.Where(e => e is DroppedItem || e is Landmine || e is BearTrap || e.ShortPrefabName.Contains("item_drop")))
                            {
                                var drop = entity as DroppedItem;
                                string shortname = drop?.item?.info.shortname ?? entity.ShortPrefabName;
                                double currDistance = Math.Floor(Vector3.Distance(entity.transform.position, player.transform.position));

                                if (currDistance < lootDistance)
                                {
                                    if (drawText) player.SendConsoleCommand("ddraw.text", 30f, Color.red, entity.transform.position, string.Format("{0} <color=yellow>{1}</color>", shortname, currDistance));
                                    if (drawBox) player.SendConsoleCommand("ddraw.box", 30f, Color.red, entity.transform.position, 0.25f);
                                    drops++;
                                }
                            }

                            if (drops == 0)
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

            if (!storedData.Filters.ContainsKey(player.UserIDString))
                storedData.Filters.Add(player.UserIDString, args.ToList());

            if (args.Length == 0 && player.GetComponent<Radar>())
            {
                UnityEngine.Object.Destroy(player.GetComponent<Radar>());
                return;
            }

            args = args.Select(arg => arg.ToLower()).ToArray();

            if (args.Length == 1)
            {
                if (args[0] == "tracker")
                {
                    if (!usePlayerTracker)
                    {
                        player.ChatMessage(msg("TrackerDisabled", player.UserIDString));
                        return;
                    }

                    if (trackers.Count == 0)
                    {
                        player.ChatMessage(msg("NoTrackers", player.UserIDString));
                        return;
                    }

                    var lastPos = Vector3.zero;
                    bool inRange = false;
                    var colors = new List<Color>();

                    foreach (var kvp in trackers)
                    {
                        lastPos = Vector3.zero;

                        if (trackers[kvp.Key].Count > 0)
                        {
                            if (colors.Count == 0)
                                colors = new List<Color>
                                    {Color.blue, Color.cyan, Color.gray, Color.green, Color.magenta, Color.red, Color.yellow};

                            var color = playersColor.ContainsKey(kvp.Key) ? playersColor[kvp.Key] : colors[Random.Range(0, colors.Count - 1)];

                            playersColor[kvp.Key] = color;

                            colors.Remove(color);

                            foreach (var entry in trackers[kvp.Key])
                            {
                                if (Vector3.Distance(entry.Value, player.transform.position) < maxTrackReportDistance)
                                {
                                    if (lastPos == Vector3.zero)
                                    {
                                        lastPos = entry.Value;
                                        continue;
                                    }

                                    if (Vector3.Distance(lastPos, entry.Value) < playerOverlapDistance) // this prevents overlapping of most arrows
                                        continue;

                                    player.SendConsoleCommand("ddraw.arrow", trackDrawTime, color, lastPos, entry.Value, 0.1f);
                                    lastPos = entry.Value;
                                    inRange = true;
                                }
                            }

                            if (lastPos != Vector3.zero)
                            {
                                string name = covalence.Players.FindPlayerById(kvp.Key.ToString()).Name;
                                player.SendConsoleCommand("ddraw.text", trackDrawTime, color, lastPos, string.Format("{0} ({1})", name, trackers[kvp.Key].Count));
                            }
                        }
                    }

                    if (!inRange)
                        player.ChatMessage(msg("NoTrackersInRange", player.UserIDString, maxTrackReportDistance));

                    return;
                }

                if (args[0] == "help")
                {
                    player.ChatMessage(msg("Help1", player.UserIDString, "all, bag, box, col, dead, loot, npc, ore, stash, tc, turret, ht"));
                    player.ChatMessage(msg("Help2", player.UserIDString, szChatCommand, "online"));
                    player.ChatMessage(msg("Help3", player.UserIDString, szChatCommand, "ui"));
                    player.ChatMessage(msg("Help4", player.UserIDString, szChatCommand, "tracker"));
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
                    player.ChatMessage(activeRadars.Count == 0 ? msg("NoActiveRadars", player.UserIDString) : msg("ActiveRadars", player.UserIDString, string.Join(", ", activeRadars.Select(radar => radar.player.displayName).ToArray())));
                    return;
                }
                else if (args[0] == "f")
                    args = storedData.Filters[player.UserIDString].ToArray();
            }

            if (command == "espgui")
            {
                string filter = storedData.Filters[player.UserIDString].Find(f => f.Contains(args[0]) || args[0].Contains(f)) ?? args[0];

                if (storedData.Filters[player.UserIDString].Contains(filter))
                    storedData.Filters[player.UserIDString].Remove(filter);
                else
                    storedData.Filters[player.UserIDString].Add(filter);

                args = storedData.Filters[player.UserIDString].ToArray();
            }
            else
                storedData.Filters[player.UserIDString] = args.ToList();

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

            bool showAll = args.Any(arg => arg.Contains("all"));
            esp.showBags = args.Any(arg => arg.Contains("bag")) || showAll;
            esp.showBox = args.Any(arg => arg.Contains("box")) || showAll;
            esp.showCollectible = args.Any(arg => arg.Contains("col")) || showAll;
            esp.showDead = args.Any(arg => arg.Contains("dead")) || showAll;
            esp.showLoot = args.Any(arg => arg.Contains("loot")) || showAll;
            esp.showNPC = args.Any(arg => arg.Contains("npc")) || showAll;
            esp.showOre = args.Any(arg => arg.Contains("ore")) || showAll;
            esp.showSleepers = args.Any(arg => arg.Contains("sleep")) || showAll;
            esp.showStash = args.Any(arg => arg.Contains("stash")) || showAll;
            esp.showTC = args.Any(arg => arg.Contains("tc")) || showAll;
            esp.showTurrets = args.Any(arg => arg.Contains("turret")) || showAll;
            esp.showHT = args.Any(arg => arg.Contains("ht"));

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

        #region UI

        private static readonly List<string> radarUI = new List<string>();
        private readonly string UI_PanelName = "AdminRadar_UI";

        public void DestroyUI(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UI_PanelName);
            radarUI.Remove(player.UserIDString);
        }

        private void CreateUI(BasePlayer player, Radar esp, bool showAll)
        {
            var element = UI.CreateElementContainer(UI_PanelName, "0 0 0 0.5", anchorMin, anchorMax, false);

            UI.CreateButton(ref element, UI_PanelName, showAll ? uiColorOn : uiColorOff, msg("All", player.UserIDString), 10, "0.017 0.739", "0.331 0.957", "espgui all");
            UI.CreateButton(ref element, UI_PanelName, esp.showBags ? uiColorOn : uiColorOff, msg("Bags", player.UserIDString), 10, "0.017 0.5", "0.331 0.717", "espgui bags");
            UI.CreateButton(ref element, UI_PanelName, esp.showBox ? uiColorOn : uiColorOff, msg("Box", player.UserIDString), 10, "0.017 0.261", "0.331 0.478", "espgui box");
            UI.CreateButton(ref element, UI_PanelName, esp.showCollectible ? uiColorOn : uiColorOff, msg("Collectibles", player.UserIDString), 10, "0.017 0.022", "0.331 0.239", "espgui col");
            UI.CreateButton(ref element, UI_PanelName, esp.showDead ? uiColorOn : uiColorOff, msg("Dead", player.UserIDString), 10, "0.343 0.739", "0.657 0.957", "espgui dead");
            UI.CreateButton(ref element, UI_PanelName, esp.showLoot ? uiColorOn : uiColorOff, msg("Loot", player.UserIDString), 10, "0.343 0.5", "0.657 0.717", "espgui loot");
            UI.CreateButton(ref element, UI_PanelName, esp.showNPC ? uiColorOn : uiColorOff, msg("NPC", player.UserIDString), 10, "0.343 0.261", "0.657 0.478", "espgui npc");
            UI.CreateButton(ref element, UI_PanelName, esp.showOre ? uiColorOn : uiColorOff, msg("Ore", player.UserIDString), 10, "0.343 0.022", "0.657 0.239", "espgui ore");
            UI.CreateButton(ref element, UI_PanelName, esp.showSleepers ? uiColorOn : uiColorOff, msg("Sleepers", player.UserIDString), 10, "0.669 0.739", "0.984 0.957", "espgui sleepers");
            UI.CreateButton(ref element, UI_PanelName, esp.showStash ? uiColorOn : uiColorOff, msg("Stash", player.UserIDString), 10, "0.669 0.5", "0.984 0.717", "espgui stash");
            UI.CreateButton(ref element, UI_PanelName, esp.showTC ? uiColorOn : uiColorOff, msg("TC", player.UserIDString), 10, "0.669 0.261", "0.984 0.478", "espgui tc");
            UI.CreateButton(ref element, UI_PanelName, esp.showTurrets ? uiColorOn : uiColorOff, msg("Turrets", player.UserIDString), 10, "0.669 0.022", "0.984 0.239", "espgui turrets");

            if (!radarUI.Contains(player.UserIDString))
                radarUI.Add(player.UserIDString);

            CuiHelper.AddUi(player, element);
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
        private static bool drawText = true;
        private static bool drawBox;
        private static bool drawArrows;
        private static bool drawX;
        private static int authLevel;
        private static float defaultInvokeTime;
        private static float defaultMaxDistance;

        private static float adDistance;
        private static float boxDistance;
        private static float playerDistance;
        private static float tcDistance;
        private static float stashDistance;
        private static float corpseDistance;
        private static float oreDistance;
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
        private static bool trackHeli;
        private static bool trackBradley;
        private static bool trackCars;
        private static bool trackCargoShips;
        private static bool showHeliRotorHealth;
        private static bool usePlayerTracker;
        private static bool useAnimalTracker;
        private static bool useHumanoidTracker;
        private static bool trackAdmins;
        private static float trackerUpdateInterval;
        private static float trackerAge;
        private static float maxTrackReportDistance;
        private static float trackDrawTime;
        private static float playerOverlapDistance;
        private static int backpackContentAmount;
        private static int groupLimit;
        private static float groupRange;
        private static float groupCountHeight;
        private static int inactiveSeconds;
        private static int inactiveMinutes;
        private static bool showUI;
        private static bool useBypass;

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
        private static List<object> authorized;
        private static List<string> itemExceptions = new List<string>();
        private string anchorMin;

        private string anchorMax;

        //static string voiceSymbol;
        private static bool useVoiceDetection;
        private static int voiceInterval;
        private static float voiceDistance;
        private static bool usePrivilegeMarkers;
        private bool useSleeperMarkers;
        private bool usePlayerMarkers;
        private bool hideSelfMarker;
        private static bool usePersonalMarkers;
        private static float markerOverlapDistance;

        private static Color privilegeColor1 = Color.yellow;
        private static Color privilegeColor2 = Color.black;
        private static Color adminColor = Color.magenta;
        private static Color sleeperColor = Color.cyan;
        private static Color onlineColor = Color.green;
        private static Color bearColor;
        private static Color boarColor;
        private static Color chickenColor;
        private static Color horseColor;
        private static Color stagColor;
        private static Color wolfColor;
        private static Color defaultNpcColor;
        private static bool useNpcUpdateTracking;

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
            groupColors.Clear();

            if (list != null && list.Count > 0)
            {
                foreach (var entry in list)
                {
                    if (entry is Dictionary<string, object>)
                    {
                        var dict = (Dictionary<string, object>)entry;

                        foreach (var kvp in dict)
                        {
                            int key = 0;
                            if (int.TryParse(kvp.Key, out key))
                            {
                                string value = kvp.Value.ToString();

                                if (__(value) == Color.red)
                                {
                                    if (__(activeDeadCC) == Color.red || __(sleeperDeadCC) == Color.red)
                                    {
                                        groupColors[key] = "magenta";
                                        continue;
                                    }
                                }

                                if (value.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F'))
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
                        ["0"] = "magenta",
                        ["1"] = "green",
                        ["2"] = "blue",
                        ["3"] = "orange",
                        ["4"] = "yellow"
                    }
                };
            }
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NotAllowed"] = "You are not allowed to use this command.",
                ["PreviousFilter"] = "To use your previous filter type <color=orange>/{0} f</color>",
                ["Activated"] = "ESP Activated - {0}s refresh - {1}m distance. Use <color=orange>/{2} help</color> for help.",
                ["Deactivated"] = "ESP Deactivated.",
                ["DoESP"] = "DoESP() took {0}ms (max: {1}ms) to execute!",
                ["TrackerDisabled"] = "Player Tracker is disabled.",
                ["NoTrackers"] = "No players have been tracked yet.",
                ["NoTrackersInRange"] = "No trackers in range ({0}m)",
                ["Exception"] = "ESP Tool: An error occured. Please check the server console.",
                ["GUIShown"] = "GUI will be shown",
                ["GUIHidden"] = "GUI will now be hidden",
                ["InvalidID"] = "{0} is not a valid steam id. Entry removed.",
                ["BoxesAll"] = "Now showing all boxes.",
                ["BoxesOnlineOnly"] = "Now showing online player boxes only.",
                ["Help1"] = "<color=orange>Available Filters</color>: {0}",
                ["Help2"] = "<color=orange>/{0} {1}</color> - Toggles showing online players boxes only when using the <color=red>box</color> filter.",
                ["Help3"] = "<color=orange>/{0} {1}</color> - Toggles quick toggle UI on/off",
                ["Help4"] = "<color=orange>/{0} {1}</color> - Draw on your screen the movement of nearby players. Must be enabled.",
                ["Help5"] = "e.g: <color=orange>/{0} 1 1000 box loot stash</color>",
                ["Help6"] = "e.g: <color=orange>/{0} 0.5 400 all</color>",
                ["VisionOn"] = "You will now see where players are looking.",
                ["VisionOff"] = "You will no longer see where players are looking.",
                ["ExtendedPlayersOn"] = "Extended information for players is now on.",
                ["ExtendedPlayersOff"] = "Extended information for players is now off.",
                ["Help7"] = "<color=orange>/{0} {1}</color> - Toggles showing where players are looking.",
                ["Help8"] = "<color=orange>/{0} {1}</color> - Toggles extended information for players.",
                ["backpack"] = "backpack",
                ["scientist"] = "scientist",
                ["npc"] = "npc",
                ["NoDrops"] = "No item drops found within {0}m",
                ["Help9"] = "<color=orange>/{0} drops</color> - Show all dropped items within {1}m.",
                ["Zombie"] = "<color=red>Zombie</color>",
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
            }, this);
        }

        private void LoadVariables()
        {
            barebonesMode = Convert.ToBoolean(GetConfig("Settings", "Barebones Performance Mode", false));

            authorized = GetConfig("Settings", "Restrict Access To Steam64 IDs", new List<object>()) as List<object>;

            foreach (var auth in authorized.ToList())
            {
                if (auth == null || !auth.ToString().IsSteamId())
                {
                    PrintWarning(msg("InvalidID", null, auth == null ? "null" : auth.ToString()));
                    authorized.Remove(auth);
                }
            }

            authLevel = authorized.Count == 0 ? Convert.ToInt32(GetConfig("Settings", "Restrict Access To Auth Level", 1)) : int.MaxValue;
            defaultMaxDistance = Convert.ToSingle(GetConfig("Settings", "Default Distance", 500.0));
            defaultInvokeTime = Convert.ToSingle(GetConfig("Settings", "Default Refresh Time", 5.0));
            latencyMs = Convert.ToInt32(GetConfig("Settings", "Latency Cap In Milliseconds (0 = no cap)", 1000.0));
            objectsLimit = Convert.ToInt32(GetConfig("Settings", "Objects Drawn Limit (0 = unlimited)", 250));
            itemExceptions = (GetConfig("Settings", "Dropped Item Exceptions", ItemExceptions) as List<object>).Cast<string>().ToList();
            inactiveSeconds = Convert.ToInt32(GetConfig("Settings", "Deactivate Radar After X Seconds Inactive", 300));
            inactiveMinutes = Convert.ToInt32(GetConfig("Settings", "Deactivate Radar After X Minutes", 0));
            showUI = Convert.ToBoolean(GetConfig("Settings", "User Interface Enabled", true));
            useBypass = Convert.ToBoolean(GetConfig("Settings", "Use Bypass Permission", false));

            showLootContents = Convert.ToBoolean(GetConfig("Options", "Show Barrel And Crate Contents", false));
            showAirdropContents = Convert.ToBoolean(GetConfig("Options", "Show Airdrop Contents", false));
            showStashContents = Convert.ToBoolean(GetConfig("Options", "Show Stash Contents", false));
            drawEmptyContainers = Convert.ToBoolean(GetConfig("Options", "Draw Empty Containers", true));
            showResourceAmounts = Convert.ToBoolean(GetConfig("Options", "Show Resource Amounts", true));
            backpackContentAmount = Convert.ToInt32(GetConfig("Options", "Show X Items In Backpacks [0 = amount only]", 3));

            drawArrows = Convert.ToBoolean(GetConfig("Drawing Methods", "Draw Arrows On Players", false));
            drawBox = Convert.ToBoolean(GetConfig("Drawing Methods", "Draw Boxes", false));
            drawText = Convert.ToBoolean(GetConfig("Drawing Methods", "Draw Text", true));

            drawX = Convert.ToBoolean(GetConfig("Group Limit", "Draw Distant Players With X", true));
            groupLimit = Convert.ToInt32(GetConfig("Group Limit", "Limit", 4));
            groupRange = Convert.ToSingle(GetConfig("Group Limit", "Range", 50f));
            groupCountHeight = Convert.ToSingle(GetConfig("Group Limit", "Height Offset [0.0 = disabled]", 0f));

            adDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Airdrop Crates", 400f));
            npcDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Animals", 200));
            bagDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Sleeping Bags", 250));
            boxDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Boxes", 100));
            colDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Collectibles", 100));
            corpseDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Player Corpses", 200));
            playerDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Players", 500));
            lootDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Loot Containers", 150));
            oreDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Resources (Ore)", 200));
            stashDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Stashes", 250));
            tcDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Tool Cupboards", 100));
            turretDistance = Convert.ToSingle(GetConfig("Drawing Distances", "Turrets", 100));

            trackBradley = Convert.ToBoolean(GetConfig("Bradleys", "Track Bradley APC", true));
            trackCars = Convert.ToBoolean(GetConfig("Cars", "Track Cars", false));
            trackCargoShips = Convert.ToBoolean(GetConfig("CargoShips", "Track CargoShips", false));
            trackHeli = Convert.ToBoolean(GetConfig("Helicopters", "Track Helicopters", true));
            showHeliRotorHealth = Convert.ToBoolean(GetConfig("Helicopters", "Show Rotors Health", false));

            usePlayerTracker = Convert.ToBoolean(GetConfig("Player Movement Tracker", "Enabled", false));
            trackAdmins = Convert.ToBoolean(GetConfig("Player Movement Tracker", "Track Admins", false));
            trackerUpdateInterval = Convert.ToSingle(GetConfig("Player Movement Tracker", "Update Tracker Every X Seconds", 1f));
            trackerAge = Convert.ToInt32(GetConfig("Player Movement Tracker", "Positions Expire After X Seconds", 600));
            maxTrackReportDistance = Convert.ToSingle(GetConfig("Player Movement Tracker", "Max Reporting Distance", 200f));
            trackDrawTime = Convert.ToSingle(GetConfig("Player Movement Tracker", "Draw Time", 60f));
            playerOverlapDistance = Convert.ToSingle(GetConfig("Player Movement Tracker", "Overlap Reduction Distance", 5f));
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

            useGroupColors = Convert.ToBoolean(GetConfig("Group Limit", "Use Group Colors Configuration", true));
            groupColorDead = Convert.ToString(GetConfig("Group Limit", "Dead Color", "#ff0000"));
            groupColorBasic = Convert.ToString(GetConfig("Group Limit", "Group Color Basic", "#ffff00"));

            var list = GetConfig("Group Limit", "Group Colors", DefaultGroupColors) as List<object>;

            if (list != null && list.Count > 0)
            {
                SetupGroupColors(list);
            }

            szChatCommand = Convert.ToString(GetConfig("Settings", "Chat Command", "radar"));

            if (!string.IsNullOrEmpty(szChatCommand))
                cmd.AddChatCommand(szChatCommand, this, cmdESP);

            if (szChatCommand != "radar")
                cmd.AddChatCommand("radar", this, cmdESP);

            //voiceSymbol = Convert.ToString(GetConfig("Voice Detection", "Voice Symbol", "🔊"));
            useVoiceDetection = Convert.ToBoolean(GetConfig("Voice Detection", "Enabled", true));
            voiceInterval = Convert.ToInt32(GetConfig("Voice Detection", "Timeout After X Seconds", 3));
            voiceDistance = Convert.ToSingle(GetConfig("Voice Detection", "Detection Radius", 30f));

            if (voiceInterval < 1)
                useVoiceDetection = false;

            useHumanoidTracker = Convert.ToBoolean(GetConfig("Map Markers", "Humanoids", false));
            useAnimalTracker = Convert.ToBoolean(GetConfig("Map Markers", "Animals", false));
            usePlayerMarkers = Convert.ToBoolean(GetConfig("Map Markers", "Players", false));
            useSleeperMarkers = Convert.ToBoolean(GetConfig("Map Markers", "Sleepers", false));
            usePrivilegeMarkers = Convert.ToBoolean(GetConfig("Map Markers", "Bases", false));
            hideSelfMarker = Convert.ToBoolean(GetConfig("Map Markers", "Hide Self Marker", true));
            usePersonalMarkers = Convert.ToBoolean(GetConfig("Map Markers", "Allow Players To See Their Base", false));
            markerOverlapDistance = Convert.ToSingle(GetConfig("Map Markers", "Overlap Reduction Distance", 15f));
            privilegeColor1 = __(Convert.ToString(GetConfig("Map Markers", "Color - Privilege Inner", "FFEB04")));
            privilegeColor2 = __(Convert.ToString(GetConfig("Map Markers", "Color - Privilege Outer", "000000")));
            adminColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Admin", "FF00FF")));
            onlineColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Online", "00FF00")));
            sleeperColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Sleeper", "00FFFF")));
            bearColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Bear", "000000")));
            boarColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Boar", "808080")));
            chickenColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Chicken", "9A9A00")));
            horseColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Horse", "8B4513")));
            stagColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Stag", "D2B48C")));
            wolfColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Wolf", "FF0000")));
            defaultNpcColor = __(Convert.ToString(GetConfig("Map Markers", "Color - Default NPC", "0000FF")));
            useNpcUpdateTracking = Convert.ToBoolean(GetConfig("Map Markers", "Update NPC Marker Position", true));

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
            string message = id == null ? RemoveFormatting(lang.GetMessage(key, this, id)) : lang.GetMessage(key, this, id);

            return args.Length > 0 ? string.Format(message, args) : message;
        }

        private string RemoveFormatting(string source)
        {
            return source.Contains(">") ? Regex.Replace(source, "<.*?>", string.Empty) : source;
        }

        #endregion
    }
}