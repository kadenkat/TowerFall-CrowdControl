using ConnectorLib.JSON;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Sockets;
using System.Text;
using TowerFall;

namespace CrowdControl;

public class SimpleTCPClient : IDisposable
{
    private TcpClient client;
    private readonly SemaphoreSlim client_lock = new(1);
    private readonly ManualResetEventSlim ready = new(false);
    private readonly ManualResetEventSlim error = new(false);

    private ILogger Logger => CrowdControlModule.Logger;

    private static readonly JsonSerializerSettings JSON_SETTINGS = new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore
    };

    public bool Connected { get; private set; }

    private readonly CancellationTokenSource quitting = new();

    public SimpleTCPClient()
    {
        Task.Factory.StartNew(ConnectLoop, TaskCreationOptions.LongRunning);
        Task.Factory.StartNew(Listen, TaskCreationOptions.LongRunning);
        Task.Factory.StartNew(KeepAlive, TaskCreationOptions.LongRunning);
    }

    ~SimpleTCPClient() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        quitting.Cancel();
        if (disposing) { client.Close(); }
    }

    private async Task ConnectLoop()
    {
        try
        {
            while (!quitting.IsCancellationRequested)
            {
                try
                {
                    client = new() { ExclusiveAddressUse = false, LingerState = new(true, 0) };
                    await client.ConnectAsync("127.0.0.1", 5144);
                    if (!client.Connected) { continue; }
                    Connected = true;
                    try { OnConnected?.Invoke(); }
                    catch (Exception e) { Logger.LogError(e, "Error occurred while invoking OnConnected event."); }
                    ready.Set();
                    await error.WaitHandle.WaitOneAsync(quitting.Token);
                }
                catch (Exception e) { Logger.LogError(e, "Error occurred in ConnectLoop."); }
                finally
                {
                    Connected = false;
                    error.Reset();
                    ready.Reset();
                    try { client.Close(); }
                    catch { /**/ }
                    if (!quitting.IsCancellationRequested) { await Task.Delay(TimeSpan.FromSeconds(1)); }
                }
            }
            Connected = false;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error occurred in ConnectLoop.");
        }
    }

    private async Task Listen()
    {
        try
        {
            List<byte> mBytes = new();
            byte[] buf = new byte[4096];
            while (!quitting.IsCancellationRequested)
            {
                try
                {
                    if (!(await ready.WaitHandle.WaitOneAsync(quitting.Token))) continue;
                    Socket socket = client.Client;

                    int bytesRead = socket.Receive(buf);
                    //Log.Debug($"Got {bytesRead} bytes from socket.");

                    //this is "slow" but the messages are tiny so we don't really care
                    foreach (byte b in buf.Take(bytesRead))
                    {
                        if (b != 0) mBytes.Add(b);
                        else
                        {
                            //Log.Debug($"Got a complete message: {mBytes.ToArray().ToHexadecimalString()}");
                            string json;
                            try { json = Encoding.UTF8.GetString(mBytes.ToArray()); }
                            finally { mBytes.Clear(); }
                            if (!SimpleJSONRequest.TryParse(json, out SimpleJSONRequest? req))
                            {
                                Logger.LogError("Failed to parse JSON payload.");
                                continue;
                            }
                            //Log.Debug($"Got a request with ID {req.id}.");
                            try { OnRequestReceived?.Invoke(req); }
                            catch (Exception e) { Logger.LogError(e, "Error occurred while invoking OnRequestReceived event."); }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Error occurred in Listen loop.");
                    mBytes.Clear(); // likely disconnect so clear pending message
                    error.Set();
                }
                finally { if (!quitting.IsCancellationRequested) await Task.Delay(TimeSpan.FromSeconds(1)); }
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error occurred in Listen loop.");
        }
    }

    private async Task KeepAlive()
    {
        try
        {

            while (!quitting.IsCancellationRequested)
            {
                try
                {
                    if (Connected) { await Respond(new() { id = 0, type = ResponseType.KeepAlive }); }
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                catch (Exception e)
                {
                    Logger.LogError(e, "Error occurred in KeepAlive loop.");
                    error.Set();
                }
                finally { if (!quitting.IsCancellationRequested) { await Task.Delay(TimeSpan.FromSeconds(1)); } }
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error occurred in KeepAlive loop.");
        }
    }

    public event Action<SimpleJSONRequest> OnRequestReceived;
    public event Action OnConnected;

    public async Task<bool> Respond(SimpleJSONResponse response)
    {
        string json = JsonConvert.SerializeObject(response, JSON_SETTINGS);
        byte[] buffer = Encoding.UTF8.GetBytes(json + '\0');
        Socket socket = client.Client;
        await client_lock.WaitAsync();
        try
        {
            int bytesSent = socket.Send(buffer);
            return bytesSent > 0;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Error occurred while sending response.");
            return false;
        }
        finally { client_lock.Release(); }
    }
}
