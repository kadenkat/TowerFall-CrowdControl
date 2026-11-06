using ConnectorLib.JSON;
using CrowdControl.Actions;
using FortRise;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Monocle;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using TowerFall;

namespace CrowdControl;

public class CrowdControlHelper
{
    private static readonly TimeSpan START_WAIT_INTERVAL = TimeSpan.FromMilliseconds(20);
    private static readonly TimeSpan MAX_START_WAIT = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MAX_EFFECT_LIFETIME = TimeSpan.FromMinutes(30);
    private const float GAME_STATUS_UPDATE_INTERVAL = 1f;

    private readonly ConcurrentDictionary<uint, DateTimeOffset> _allow_timed_responses = new();
    private float gameStatusUpdateTimer = 0f;

    private GameState? lastGameState;
    private bool effectsLoaded;

    private readonly SimpleTCPClient client;

    public Level? Level;
    public Scene? Scene;

    public List<Entity>? Players => Level?.Players;

    public bool InLevel { get => Level != null; }

    public static ILogger Logger => CrowdControlModule.Logger;

    public VersionNumber Version;

    public readonly Dictionary<string, Effect> Effects = new();
    public readonly Dictionary<string, Metadata.Metadata> Metadata = new();

    public IEnumerable<Effect> Active => Effects.Select(e => e.Value).Where(e => e.Active);
    public IEnumerable<Effect> ActiveGroup(string group) => Active.Where(e => string.Equals(e.Group, group));

    public static CrowdControlHelper Instance { get; private set; } = null!;

    private CrowdControlHelper(IModuleContext context, IModContent modContent)
    {
        client = new();
        client.OnConnected += ClientConnected;
        client.OnRequestReceived += ClientRequestReceived;

        context.Events.Level.LevelEntered += Level_LevelEntered;
        context.Events.Level.LevelExited += Level_LevelExited;

        Version = new VersionNumber(modContent.Metadata.Version.ToString());
    }

    public static void Create(IModContent modContent, IModuleContext context)
    {
        if (Instance != null)
        {
            return;
        }

        Instance = new CrowdControlHelper(context, modContent);
        context.Harmony.Patch(
            AccessTools.DeclaredMethod(typeof(TowerFall.TFGame), "Update"),
            postfix: new HarmonyMethod(TFGame_Update_Postfix)
        );
    }

    private void Update(GameTime gameTime)
    {
        gameStatusUpdateTimer += Engine.DeltaTime;
        if (gameStatusUpdateTimer >= GAME_STATUS_UPDATE_INTERVAL)
        {
            UpdateGameState(0);
            gameStatusUpdateTimer = 0f;
        }
        if (!InLevel) return;
        Logger.LogDebug($"In level.");

        if (Level?.Players != null)
        {
            for (int i = Level.Players.Count - 1; i >= 0; i--)
            {
                if (Level.Players[i] is Player player)
                {
                    player.HasShield = true;
                }
            }
        }
    }

    private static void TFGame_Update_Postfix(GameTime gameTime)
    {
        Instance.Update(gameTime);
        //Log.Message(_game_status_update_timer);



        /*        TryLoadEffects();
                if (m_effectsLoaded) foreach (Effect action in Active)
                {
                    if (!action.Active) continue;
                    try
                    {
                        switch (action.Type)
                        {
                            case Effect.EffectType.Timed:
                                TimeSpan timeRemaining = action.Duration - action.Elapsed;
                                if (timeRemaining > TimeSpan.Zero)
                                {
                                    action.Update(gameTime);
                                    if ((Level == null) || Level.InCutscene || action.Paused)
                                    {
                                        if (action.IsTimerTicking)
                                        {
                                            action.IsTimerTicking = false;
                                            Respond(action.CurrentRequest, EffectStatus.Paused, timeRemaining).Forget();
                                        }
                                        action.Elapsed -= gameTime.ElapsedGameTime;
                                    }
                                    else if (!action.IsTimerTicking)
                                    {
                                        action.IsTimerTicking = true;
                                        Respond(action.CurrentRequest, EffectStatus.Resumed, timeRemaining).Forget();
                                    }
                                }
                                else action.TryStop();
                                break;
                            case Effect.EffectType.BidWar:
                                action.Update(gameTime);
                                break;
                            default:
                                action.Update(gameTime);
                                action.TryStop();
                                break;
                        }
                    }
                    catch (Exception e) { Log.Error(e); }
                }

                while (_gui_messages.Count > MAX_GUI_MESSAGES) { _gui_messages.TryDequeue(out _); }
                foreach (var gm in _gui_messages) { gm.elapsed += gameTime.ElapsedGameTime; }
                while (_gui_messages.TryPeek(out var g) && (g.elapsed > MAX_GUI_MESSAGE_TIME)) { _gui_messages.TryDequeue(out _); }*/
    }

    private static void Level_LevelExited(object? sender, TowerFall.Level e)
    {
        Instance.Level = null;
        Instance.Scene = null;
    }

    private static void Level_LevelEntered(object? sender, TowerFall.Level e)
    {
        Instance.Level = e;
        Instance.Scene = e;
    }

    private void ClientRequestReceived(SimpleJSONRequest request)
    {
        if (request is EffectRequest effectRequest)
        {
            Logger.LogDebug($"EffectRequest received. Type: {effectRequest.type}, Code: {effectRequest.code}");

            switch (effectRequest.type)
            {
                case RequestType.EffectTest:
                    HandleEffectTest(effectRequest);
                    return;
                case RequestType.EffectStart:
                    HandleEffectStart(effectRequest);
                    return;
                case RequestType.EffectStop:
                    HandleEffectStop(effectRequest);
                    return;
                default:
                    return;
            }
        }
        else if (request.type == RequestType.GameUpdate)
        {
            UpdateGameState(request.id, GameState.InLevel, force: true);
        }
        else if (request.type == RequestType.Version)
        {
            Logger.LogDebug($"Got a version request. Our version: {Version.ToString()}");
            client.Respond(new VersionResponse(Version) { id = request.id }).Forget();
        }
    }

    private void ClientConnected()
    {
    }

    private void TryLoadEffects()
    {
        if (effectsLoaded) return;
        //this may be GetEntryAssembly or GetExecutingAssembly depending on how the modloader works
        foreach (Type type in Assembly.GetEntryAssembly().GetTypes())
        {
            //Log.Debug($"Loaded type: {type.Name}");
            if (type.IsAbstract) { continue; }

            if (typeof(Effect).IsAssignableFrom(type))
            {
                Effect action = default;// (Effect)type.GetConstructor(Everest._EmptyTypeArray).Invoke(Everest._EmptyObjectArray);
                action.Load();
                //Log.Debug($"Imported effect type: {type.Name}");
                Effects.Add(action.Code, action);
            }

            if (typeof(Metadata.Metadata).IsAssignableFrom(type))
            {
                Metadata.Metadata metadata = (Metadata.Metadata)type.GetConstructor(Type.EmptyTypes).Invoke([]);
                //Log.Debug($"Imported metadata type: {type.Name}");
                Metadata.Add(metadata.Key, metadata);
            }
        }
        effectsLoaded = true;
    }

    // Not-found should send a response, not just log. Otherwise the viewer waits out the timeout.
    // For a code the mod doesn't implement, use EffectResponse.Unavailable(id, message).
    // Unavailable is same as Failure, but the effect is no longer available for the rest
    // of the game, so the viewer gets refunded and the effect is disabled.
    // For "not right now" situations (in a menu, archer dead), use Retry. That's the "normal" failure response.
    private void HandleEffectStart(EffectRequest request)
    {
        Logger.LogDebug($"Got an effect start request [{request.id}:{request.code}].");

        if (!Effects.TryGetValue(request.code, out Effect effect))
        {
            Logger.LogError($"Effect {request.code} not found. Available effects: {string.Join(", ", Effects.Keys)}");
            //could not find the effect
            Respond(request, EffectStatus.Unavailable).Forget();
            return;
        }


        if (((request.parameters as JArray)?.Count ?? 0) < effect.ParameterTypes.Length)
        {
            Respond(request, EffectStatus.Failure).Forget();
            return;
        }

        if (effect.Type == Effect.EffectType.BidWar)
        {
            foreach (Effect e in ActiveGroup(effect.Group)) e.TryStop();
        }
        if (!effect.TryStart(request))
        {
            //Logger.LogDebug($"Effect {request.code} could not start.");
            Respond(request, EffectStatus.Retry).Forget();
            return;
        }

        Logger.LogDebug($"Effect {request.code} started.");
        Respond(request, EffectStatus.Success, ((effect.Type == Effect.EffectType.Timed) ? effect.Duration : null)).Forget();
    }

    //in some other situation we might do a more advanced strategy where certain effects might
    //want to include additional fields, but we're going to send these for all requests
    private Dictionary<string, DataResponse> GetMetadata()
        => Metadata.Select(m => new KeyValuePair<string, DataResponse>(m.Key, m.Value.TryGetValue())).ToDictionary();

    private void HandleEffectStop(EffectRequest request)
    {
        Logger.LogDebug($"Got an effect stop request [{request.id}:{request.code}].");
        if (!Effects.TryGetValue(request.code, out Effect effect))
        {
            Logger.LogError($"Effect {request.code} not found. Available effects: {string.Join(", ", Effects.Keys)}");
            //could not find the effect
            return;
        }
        /*
                if (!effect.TryStop())
                {
                    Logger.LogDebug($"Effect {request.code} failed to stop.");
                    return;
                }*/

        Logger.LogDebug($"Effect {request.code} stopped.");
        Respond(request, EffectStatus.Success).Forget();
    }
    private void HandleEffectTest(EffectRequest request)
    {
        Logger.LogDebug($"Got an effect test request [{request.id}:{request.code}].");
        if (!Effects.TryGetValue(request.code, out Effect effect))
        {
            Logger.LogError($"Effect {request.code} not found. Available effects: {string.Join(", ", Effects.Keys)}");
            //could not find the effect
            Respond(request, EffectStatus.Unavailable).Forget();
            return;
        }

        Logger.LogDebug($"Effect {request.code} is ready.");

        /*        if (((request.parameters as JArray)?.Count ?? 0) < effect.ParameterTypes.Length)
                {
                    Respond(request, EffectStatus.Failure).Forget();
                    return;
                }*/

        /*        if (!effect.IsReady())
                {
                    //Logger.LogDebug($"Effect {request.code} was not ready.");
                    Respond(request, EffectStatus.Retry).Forget();
                    return;
                }

                Logger.LogDebug($"Effect {request.code} is ready.");
                Respond(request, EffectStatus.Success, ((effect.Type == Effect.EffectType.Timed) ? effect.Duration : null)).Forget();*/
    }


    private void UpdateGameState(uint id, bool force = false)
    {
        if (Level == null) UpdateGameState(id, GameState.WrongMode, force).Forget();
        else if (Level.Paused) UpdateGameState(id, GameState.Paused, force).Forget();
        else UpdateGameState(id, GameState.InLevel, force).Forget();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Task<bool> UpdateGameState(uint id, GameState newState, bool force) => UpdateGameState(id, newState, null, force);
    private async Task<bool> UpdateGameState(uint id, GameState newState, string? message = null, bool force = false)
    {
        if (force || (lastGameState != newState))
        {
            lastGameState = newState;
            return await client.Respond(new GameUpdate(newState, message) { id = id });
        }
        return true;
    }

    private async Task<bool> Respond(EffectRequest request, EffectStatus result, TimeSpan? timeRemaining = null, string message = "")
    {
        try
        {
            TimeSpan timeWaited = TimeSpan.Zero;
            while (timeWaited < MAX_START_WAIT)
            {
                if (client.Connected && !IsPrematureTimedResponse(request, result)) break;
                await Task.Delay(START_WAIT_INTERVAL);
                timeWaited += START_WAIT_INTERVAL;
            }

            return await client.Respond(new EffectResponse
            {
                id = request.id,
                status = result,
                timeRemaining = ((long?)timeRemaining?.TotalMilliseconds) ?? 0L,
                message = message,
                type = ResponseType.EffectRequest,
                metadata = GetMetadata()
            });
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error occurred while responding to effect request.");
            return false;
        }
    }

    private bool IsPrematureTimedResponse(EffectRequest request, EffectStatus result)
    {
        if (result == EffectStatus.Success)
        {
            _allow_timed_responses[request.id] = DateTimeOffset.UtcNow;
            return false;
        }
        if (result is not (EffectStatus.Paused or EffectStatus.Resumed or EffectStatus.Finished)) return false;
        if (!_allow_timed_responses.TryGetValue(request.id, out DateTimeOffset setOn)) return true;
        if ((DateTimeOffset.UtcNow - setOn) > MAX_EFFECT_LIFETIME)
        {
            _allow_timed_responses.TryRemove(request.id, out _);
            return true;
        }
        _allow_timed_responses[request.id] = DateTimeOffset.UtcNow;
        return false;
    }
}