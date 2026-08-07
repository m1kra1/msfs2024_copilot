using System.Runtime.InteropServices;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.SimConnect;

/// <summary>
/// Native P/Invoke SimConnect client (no managed Microsoft.FlightSimulator.SimConnect.dll required).
/// Loads SimConnect.dll from app dir or known MSFS/Addon paths, maps events, transmits to user aircraft.
/// Pattern proven in FRFE NativeSimConnect for this machine.
/// </summary>
public sealed class NativeSimConnectClient : ISimConnectClient
{
    private IntPtr _h = IntPtr.Zero;
    private Thread? _dispatchThread;
    private volatile bool _run;
    private bool _disposed;
    private readonly Dictionary<string, uint> _eventMap = new(StringComparer.OrdinalIgnoreCase);
    private uint _nextEventId = 0xC0030001; // PRIVATE_COPILOT event base
    private bool _defsRegistered;

    private const uint DEFINITION_STATUS = 0xC0010001;
    private const uint REQUEST_STATUS = 0xC0020001;
    private const uint OBJECT_USER = 0;
    private const int DATATYPE_FLOAT64 = 4;
    private const int PERIOD_SECOND = 3;
    private const uint RECV_EXCEPTION = 2;
    private const uint RECV_OPEN = 1;
    private const uint RECV_QUIT = 3;
    private const uint RECV_SIMOBJECT_DATA = 8;
    private const uint GROUP_PRIORITY_HIGHEST = 1;
    private const uint EVENT_FLAG_DEFAULT = 0;

    public bool IsConnected => _h != IntPtr.Zero;
    public bool IsLive => IsConnected;
    public string StatusMessage { get; private set; } = "Native SimConnect not open";
    public SimVarSnapshot Snapshot { get; } = new();

    public bool Connect(string appName, int configIndex = 0)
    {
        if (IsConnected) return true;
        try
        {
            if (!TryLoadNativeDll(out var loadedFrom))
            {
                StatusMessage =
                    "SimConnect.dll not found. Place it next to CoPilotVoiceHost.exe " +
                    "(from MSFS SDK redistributable or a known client folder).";
                return false;
            }

            var hr = SimConnect_Open(out _h, appName, IntPtr.Zero, 0, IntPtr.Zero, (uint)configIndex);
            if (hr < 0 || _h == IntPtr.Zero)
            {
                StatusMessage = $"SimConnect_Open failed HRESULT=0x{hr:X8} (is MSFS Free Flight running?)";
                _h = IntPtr.Zero;
                return false;
            }

            MapAllStandardEvents();
            RegisterStatusDefinitions();
            RequestStatusData();

            _run = true;
            _dispatchThread = new Thread(DispatchLoop)
            {
                IsBackground = true,
                Name = "PrivateCoPilot-SimConnect"
            };
            _dispatchThread.Start();

            StatusMessage = $"Native SimConnect LIVE (dll={loadedFrom}, app={appName}). Events will reach the sim.";
            return true;
        }
        catch (DllNotFoundException)
        {
            StatusMessage = "SimConnect.dll not found (DllNotFoundException).";
            _h = IntPtr.Zero;
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = "Native SimConnect open error: " + ex.Message;
            _h = IntPtr.Zero;
            return false;
        }
    }

    public void Disconnect()
    {
        _run = false;
        try { _dispatchThread?.Join(1500); } catch { /* ignore */ }
        _dispatchThread = null;
        if (_h != IntPtr.Zero)
        {
            try { SimConnect_Close(_h); } catch { /* ignore */ }
            _h = IntPtr.Zero;
        }
        StatusMessage = "Native SimConnect closed";
    }

    public void TransmitEvent(string eventName, uint data = 0)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Native SimConnect not connected");

        if (!_eventMap.TryGetValue(eventName, out var id))
        {
            id = _nextEventId++;
            var hrMap = SimConnect_MapClientEventToSimEvent(_h, id, eventName);
            if (hrMap < 0)
                throw new InvalidOperationException($"MapClientEventToSimEvent({eventName}) failed 0x{hrMap:X8}");
            _eventMap[eventName] = id;
        }

        var hr = SimConnect_TransmitClientEvent(
            _h, OBJECT_USER, id, data, GROUP_PRIORITY_HIGHEST, EVENT_FLAG_DEFAULT);
        if (hr < 0)
            throw new InvalidOperationException($"TransmitClientEvent({eventName}) failed 0x{hr:X8}");

        Console.WriteLine($"[SimConnect] LIVE event sent: {eventName} data={data}");
        StatusMessage = $"Event sent: {eventName}";
    }

    public void SetSimVar(string name, double value, string units)
    {
        Snapshot.Set(name, value);
        StatusMessage = $"SetSimVar local-only: {name}={value} {units}";
    }

    public void ReceiveMessage()
    {
        // Dispatch thread owns GetNextDispatch; nothing to do on UI timer.
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }

    private void MapAllStandardEvents()
    {
        foreach (var name in StandardEventMap.All.Keys)
        {
            var id = _nextEventId++;
            var hr = SimConnect_MapClientEventToSimEvent(_h, id, name);
            if (hr >= 0)
                _eventMap[name] = id;
            else
                Console.WriteLine($"[SimConnect] Map failed for {name}: 0x{hr:X8}");
        }
    }

    private void RegisterStatusDefinitions()
    {
        if (_defsRegistered || !IsConnected) return;
        foreach (var (name, units) in StatusSimVars.Definitions)
        {
            try
            {
                SimConnect_AddToDataDefinition(
                    _h, DEFINITION_STATUS, name, units, DATATYPE_FLOAT64, 0f, uint.MaxValue);
            }
            catch
            {
                // optional vars
            }
        }
        _defsRegistered = true;
    }

    private void RequestStatusData()
    {
        if (!IsConnected) return;
        SimConnect_RequestDataOnSimObject(
            _h, REQUEST_STATUS, DEFINITION_STATUS, OBJECT_USER,
            PERIOD_SECOND, 0, 0, 0, 0);
    }

    private void DispatchLoop()
    {
        while (_run && _h != IntPtr.Zero)
        {
            try
            {
                var hr = SimConnect_GetNextDispatch(_h, out var pData, out var cb);
                if (hr == 0 && pData != IntPtr.Zero && cb > 0)
                    ProcessDispatch(pData, cb);
                else
                    Thread.Sleep(40);
            }
            catch
            {
                Thread.Sleep(100);
            }
        }
    }

    private void ProcessDispatch(IntPtr pData, uint cb)
    {
        var dwId = (uint)Marshal.ReadInt32(pData, 8);
        if (dwId == RECV_OPEN)
        {
            StatusMessage = "Native SimConnect RECV_OPEN";
            return;
        }

        if (dwId == RECV_QUIT)
        {
            StatusMessage = "SimConnect quit from sim";
            _run = false;
            return;
        }

        if (dwId == RECV_EXCEPTION)
        {
            // dwException at offset 12 typically
            var ex = Marshal.ReadInt32(pData, 12);
            Console.WriteLine($"[SimConnect] RECV_EXCEPTION code={ex}");
            return;
        }

        if (dwId != RECV_SIMOBJECT_DATA)
            return;

        const int headerSize = 40;
        var defs = StatusSimVars.Definitions;
        var need = headerSize + defs.Length * sizeof(double);
        if (cb < need) return;

        var values = new double[defs.Length];
        for (var i = 0; i < defs.Length; i++)
        {
            values[i] = Marshal.PtrToStructure<double>(IntPtr.Add(pData, headerSize + i * sizeof(double)));
        }

        StatusSnapshotMapper.Apply(Snapshot, values);
    }

    /// <summary>Locate and load native SimConnect.dll. Returns path used.</summary>
    public static bool TryLoadNativeDll(out string loadedFrom)
    {
        loadedFrom = "";
        foreach (var dir in EnumerateSearchDirs())
        {
            var candidate = Path.Combine(dir, "SimConnect.dll");
            if (!File.Exists(candidate))
                continue;
            try
            {
                SetDllDirectory(dir);
                NativeLibrary.Load(candidate);
                loadedFrom = candidate;
                return true;
            }
            catch
            {
                // try next
            }
        }

        // Also try default search (PATH / already loaded)
        try
        {
            NativeLibrary.Load("SimConnect.dll");
            loadedFrom = "SimConnect.dll (system path)";
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> EnumerateSearchDirs()
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p) && !list.Contains(p, StringComparer.OrdinalIgnoreCase))
                list.Add(p);
        }

        Add(AppContext.BaseDirectory);
        Add(Directory.GetCurrentDirectory());

        var env = Environment.GetEnvironmentVariable("MSFS_SDK");
        if (!string.IsNullOrEmpty(env))
        {
            Add(Path.Combine(env, "SimConnect SDK", "lib", "static"));
            Add(Path.Combine(env, "SimConnect SDK", "lib"));
            Add(Path.Combine(env, "SimConnect SDK", "lib", "x64"));
        }

        Add(@"F:\Addon Manager\couatl");
        Add(@"C:\Addon Manager\couatl");
        Add(@"F:\SteamLibrary\steamapps\common\MSFS2024");
        Add(@"C:\Program Files (x86)\Steam\steamapps\common\MSFS2024");
        Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft Flight Simulator 2024 SDK", "SimConnect SDK", "lib", "static"));
        Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft Flight Simulator 2024 SDK", "SimConnect SDK", "lib"));

        // Steam libraryfolders — common extra roots
        foreach (var steamRoot in new[]
                 {
                     @"F:\SteamLibrary\steamapps\common",
                     @"C:\Program Files (x86)\Steam\steamapps\common",
                     @"D:\SteamLibrary\steamapps\common"
                 })
        {
            if (!Directory.Exists(steamRoot)) continue;
            try
            {
                foreach (var d in Directory.GetDirectories(steamRoot, "*MSFS*"))
                    Add(d);
            }
            catch { /* ignore */ }
        }

        return list;
    }

    #region P/Invoke

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string lpPathName);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int SimConnect_Open(
        out IntPtr phSimConnect,
        [MarshalAs(UnmanagedType.LPStr)] string szName,
        IntPtr hWnd,
        uint UserEventWin32,
        IntPtr hEventHandle,
        uint ConfigIndex);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int SimConnect_Close(IntPtr hSimConnect);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int SimConnect_GetNextDispatch(
        IntPtr hSimConnect,
        out IntPtr ppData,
        out uint pcbData);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int SimConnect_AddToDataDefinition(
        IntPtr hSimConnect,
        uint DefineID,
        [MarshalAs(UnmanagedType.LPStr)] string DatumName,
        [MarshalAs(UnmanagedType.LPStr)] string? UnitsName,
        int DatumType,
        float fEpsilon,
        uint DatumID);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int SimConnect_RequestDataOnSimObject(
        IntPtr hSimConnect,
        uint RequestID,
        uint DefineID,
        uint ObjectID,
        int Period,
        uint Flags,
        uint origin,
        uint interval,
        uint limit);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int SimConnect_MapClientEventToSimEvent(
        IntPtr hSimConnect,
        uint EventID,
        [MarshalAs(UnmanagedType.LPStr)] string EventName);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int SimConnect_TransmitClientEvent(
        IntPtr hSimConnect,
        uint ObjectID,
        uint EventID,
        uint dwData,
        uint GroupID,
        uint Flags);

    #endregion
}
