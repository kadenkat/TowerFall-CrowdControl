using ConnectorLib.JSON;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Monocle;
using System.Collections.Concurrent;
using TowerFall;

namespace CrowdControl.Actions;

public abstract class Effect
{
    private static int _next_id;
    private uint LocalID { get; } = unchecked((uint)Interlocked.Increment(ref _next_id));

    private static readonly ILogger Logger = CrowdControlModule.Logger;

    public abstract string Code { get; }

    private readonly object activityLock = new();

    public TimeSpan Elapsed { get; set; }

    public bool Paused { get; set; }

    public bool IsTimerTicking { get; set; }

    protected Camera? Camera => Level?.Camera;
    protected Level? Level => CrowdControlHelper.Instance.Level;
    protected Scene? Scene => CrowdControlHelper.Instance.Scene;

    protected List<Player>? Players => CrowdControlHelper.Instance.Players;

    public virtual EffectType Type => EffectType.Instant;

    public EffectRequest? CurrentRequest { get; private set; }

    public TimeSpan Duration { get; private set; } = TimeSpan.Zero;

    public virtual TimeSpan DefaultDuration => TimeSpan.Zero;

    public virtual Type[] ParameterTypes => System.Type.EmptyTypes;

    protected object[] Parameters { get; private set; } = [];

    public virtual string Group { get; }

    public virtual string[] Mutex { get; } = [];

    private static readonly ConcurrentDictionary<string, bool> mutexes = new();

    private static bool TryGetMutexes(IEnumerable<string> mutexes)
    {
        List<string> captured = [];
        bool result = true;
        foreach (string mutex in mutexes)
        {
            if (Effect.mutexes.TryAdd(mutex, true)) captured.Add(mutex);
            else
            {
                result = false;
                break;
            }
        }
        if (!result) FreeMutexes(captured);
        return result;
    }

    public static void FreeMutexes(IEnumerable<string> mutexes)
    {
        foreach (string mutex in mutexes)
            Effect.mutexes.TryRemove(mutex, out _);
    }

    public enum EffectType : byte
    {
        Instant = 0,
        Timed = 1,
        BidWar = 2
    }

    public bool Active
    {
        get;
        private set
        {
            if (field == value) return;
            field = value;
            if (value)
            {
                Elapsed = TimeSpan.Zero;
                Start();
            }
            else End();
        }
    }

    public virtual void Load() => Logger.LogDebug($"{GetType().Name} was loaded. [{LocalID}]");

    public virtual void Unload() => Logger.LogDebug($"{GetType().Name} was unloaded. [{LocalID}]");

    public virtual void Start() => Logger.LogDebug($"{GetType().Name} was started. [{LocalID}]");

    public virtual void End() => Logger.LogDebug($"{GetType().Name} was stopped. [{LocalID}]");

    public virtual void Update(GameTime gameTime)
    {
        if (!Paused)
            Elapsed += gameTime.ElapsedGameTime;
    }

    public virtual void Draw(GameTime gameTime) { }

    public virtual bool IsReady() => Level != null;

    //public bool TryStart() => TryStart(new object[0]);

    public bool TryStart(EffectRequest request)
    {
        lock (activityLock)
        {
            if (Active || (!IsReady())) return false;
            if (!TryGetMutexes(Mutex)) return false;

            CurrentRequest = request;

            int len = ParameterTypes.Length;
            object[] p = new object[len];
            for (int i = 0; i < len; i++)
                p[i] = Convert.ChangeType(request.parameters[i], ParameterTypes[i]);
            Parameters = p;

            Duration = request.duration.HasValue ? TimeSpan.FromMilliseconds(request.duration.Value) : DefaultDuration;

            Paused = false;
            Active = true;
            return true;
        }
    }

    public bool TryStop()
    {
        lock (activityLock)
        {
            if (!Active) return false;
            FreeMutexes(Mutex);
            Active = false;
            CurrentRequest = null;
            return true;
        }
    }
}
