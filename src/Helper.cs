using System;
using System.IO;
using System.Linq;
using System.Reflection;

// SidetoneHelper (part of Sidetone Control for HyperX): runs on the .NET runtime that NGENUITY installs and talks to NGENUITY's
// background service (hxn-srv-communication-hub) through NGENUITY's own CommunicationHubManager.
// The UI process drives it with one command per line on stdin and reads one reply line per command:
//   status          -> OK hub=1 headset=1 level=-6 monitoring=1
//   level <dB>      -> OK level=<dB as reported back by the headset>
//   monitoring 0|1  -> OK monitoring=<state>
// Errors reply "ERR <message>". The helper exits when stdin closes.
static class Helper
{
    static int Main(string[] args)
    {
        var dir = args.Length > 0 ? args[0] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NGENUITY", "current");
        try { Hub.LoadNgenuity(dir); }
        catch (Exception ex) { Console.WriteLine("ERR NGENUITY not found: " + ex.Message); return 1; }

        Hub hub = null;
        string line;
        while ((line = Console.ReadLine()) != null)
        {
            var parts = line.Trim().TrimStart('﻿').Split(' ');
            try
            {
                if (hub == null)
                {
                    var h = new Hub();
                    if (h.Connect()) hub = h;
                    else
                    {
                        h.Dispose();
                        Console.WriteLine("OK hub=0 headset=0");
                        Console.Out.Flush();
                        continue;
                    }
                }

                var headset = hub.HeadsetConnected();
                if (headset == null)
                {
                    // The service went away (NGENUITY closed or restarted); reconnect on the next command.
                    hub.Dispose();
                    hub = null;
                    Console.WriteLine("OK hub=0 headset=0");
                }
                else if (!headset.Value)
                    Console.WriteLine("OK hub=1 headset=0");
                else if (parts[0] == "level")
                {
                    hub.SetLevelDb(int.Parse(parts[1]));
                    Console.WriteLine("OK hub=1 headset=1 level=" + Fmt(hub.LevelDb()));
                }
                else if (parts[0] == "monitoring")
                {
                    hub.SetMonitoringEnabled(parts[1] == "1");
                    Console.WriteLine("OK hub=1 headset=1 monitoring=" + Fmt(hub.MonitoringEnabled()));
                }
                else
                    Console.WriteLine("OK hub=1 headset=1 level=" + Fmt(hub.LevelDb()) + " monitoring=" + Fmt(hub.MonitoringEnabled()) + " battery=" + Fmt(hub.BatteryPercent()));
            }
            catch (Exception ex)
            {
                if (hub != null) { hub.Dispose(); hub = null; }
                Console.WriteLine("ERR " + ex.Message.Replace('\n', ' ').Replace('\r', ' '));
            }
            Console.Out.Flush();
        }
        if (hub != null) hub.Dispose();
        return 0;
    }

    static string Fmt(int? v) { return v.HasValue ? v.Value.ToString() : "?"; }
    static string Fmt(bool? v) { return v.HasValue ? (v.Value ? "1" : "0") : "?"; }
}

// Thin reflection wrapper around NGENUITY's CommunicationHubManager.
class Hub : IDisposable
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Assembly asm;
    object hub, client;
    Type hubType;
    ulong deviceId;

    public static void LoadNgenuity(string dir)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var p = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null;
        };
        asm = Assembly.LoadFrom(Path.Combine(dir, "NGENUITY.dll"));
    }

    // Same connection NGENUITY makes in CommunicationHubManager.StartAsync, minus its DI lookups.
    public bool Connect()
    {
        hubType = asm.GetType("HXN.IPC.Managers.CommunicationHubManager");
        hub = Activator.CreateInstance(hubType, true);
        var clientType = asm.GetType("HXN.IPC.IpcClient");
        client = Activator.CreateInstance(clientType, true);
        var ack = (byte[])asm.GetTypes().First(t => t.Name == "AppConstants").GetField("HX_ACK", All).GetValue(null);
        var connect = clientType.GetMethod("Connect", All);
        var ok = false;
        // NGENUITY waits only 10 ms for the handshake, which a cold process can miss; retry a few times.
        for (int i = 0; i < 5 && !ok; i++)
            ok = (bool)connect.Invoke(client, new object[] { 6890, 6899, ack, 5, 500 });
        if (!ok) return false;
        hubType.GetField("_ipcClient", All).SetValue(hub, client);
        hubType.GetField("_isConnected", All).SetValue(hub, true);

        var ids = Call("GetDeviceList") as ulong[];
        if (ids == null) return false;
        foreach (var id in ids)
        {
            var info = Call("GetDeviceInformation", id);
            var product = info == null ? null : info.GetType().GetProperty("Product");
            if (product != null && Convert.ToString(product.GetValue(info)).StartsWith("CloudIIIS", StringComparison.OrdinalIgnoreCase))
            {
                deviceId = id;
                return true;
            }
        }
        return false;
    }

    public bool? HeadsetConnected() { return Call("GetWirelessRFConnectionStatus", deviceId) as bool?; }
    public bool? MonitoringEnabled() { return Call("GetAudioInputMonitoringStatus", deviceId) as bool?; }
    public void SetMonitoringEnabled(bool on) { Call("SetAudioInputMonitoringStatus", deviceId, on); }

    // The level byte is a signed dB value: 250 = -6 dB, 0 = 0 dB, 6 = +6 dB (the headset clamps to that range).
    public int? LevelDb()
    {
        var b = Call("GetAudioInputMonitoringLevel", deviceId) as byte?;
        return b.HasValue ? (int?)unchecked((sbyte)b.Value) : null;
    }

    public void SetLevelDb(int db)
    {
        db = Math.Max(-6, Math.Min(6, db)); // never send anything outside the range the headset accepts
        Call("SetAudioInputMonitoringLevel", deviceId, unchecked((byte)(sbyte)db));
    }

    // Battery changes slowly; ask the headset at most every 30 seconds.
    int? battery;
    DateTime batteryAt = DateTime.MinValue;

    public int? BatteryPercent()
    {
        if (DateTime.UtcNow - batteryAt < TimeSpan.FromSeconds(30)) return battery;
        batteryAt = DateTime.UtcNow;
        var info = Call("GetWirelessRFBatteryInformation", deviceId, 1);
        var level = info == null ? null : info.GetType().GetProperty("BatteryLevel");
        if (level != null) battery = Convert.ToInt32(level.GetValue(info));
        return battery;
    }

    object Call(string name, params object[] a)
    {
        var m = hubType.GetMethods(All).First(x => x.Name == name && x.GetParameters().Length == a.Length);
        try { return m.Invoke(hub, a); }
        catch (TargetInvocationException) { return null; }
    }

    public void Dispose()
    {
        if (client == null) return;
        try { client.GetType().GetMethod("Dispose", All).Invoke(client, null); } catch { }
        client = null;
    }
}
