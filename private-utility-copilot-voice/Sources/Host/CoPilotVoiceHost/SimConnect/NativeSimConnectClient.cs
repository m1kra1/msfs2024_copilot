using System.Runtime.InteropServices;
using System.Text;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.SimConnect;

/// <summary>
/// Native P/Invoke SimConnect client for MSFS 2020/2024.
/// Requires a real Microsoft SimConnect.dll (not FSX/FSW third-party copies) next to the EXE
/// plus SimConnect.cfg matching the sim's local server (typically IPv4 127.0.0.1:500).
/// </summary>
public sealed class NativeSimConnectClient : ISimConnectClient
{
    private IntPtr _h = IntPtr.Zero;
    private Thread? _dispatchThread;
    private volatile bool _run;
    private bool _disposed;
    private readonly Dictionary<string, uint> _eventMap = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Write definitions for SetSimVar (A:/L: vars), keyed by name|units.</summary>
    private readonly Dictionary<string, uint> _setVarDefs = new(StringComparer.OrdinalIgnoreCase);
    private uint _nextEventId = 0xC0030001;
    private uint _nextSetDefId = 0xC0010100;
    private bool _defsRegistered;

    private const uint DEFINITION_STATUS = 0xC0010001;
    private const uint DEFINITION_AIRCRAFT = 0xC0010002;
    private const uint REQUEST_STATUS = 0xC0020001;
    private const uint REQUEST_AIRCRAFT = 0xC0020002;
    private const uint OBJECT_USER = 0;
    private const int DATATYPE_FLOAT64 = 4;
    private const int DATATYPE_STRING256 = 9;
    private const int DATATYPE_STRING32 = 6;
    private const int PERIOD_SECOND = 3;
    private const uint RECV_EXCEPTION = 2;
    private const uint RECV_OPEN = 1;
    private const uint RECV_QUIT = 3;
    private const uint RECV_SIMOBJECT_DATA = 8;
    private const uint GROUP_PRIORITY_HIGHEST = 1;
    private const uint EVENT_FLAG_DEFAULT = 0;
    /// <summary>SIMCONNECT_DATA_SET_FLAG_DEFAULT</summary>
    private const uint DATA_SET_FLAG_DEFAULT = 0;
    /// <summary>SIMCONNECT_OPEN_CONFIGINDEX_LOCAL — use local sim without remote cfg.</summary>
    private const uint CONFIGINDEX_LOCAL = 0xFFFFFFFFu;

    public bool IsConnected => _h != IntPtr.Zero;
    public bool IsLive => IsConnected;
    public string StatusMessage { get; private set; } = "Native SimConnect not open";
    public SimVarSnapshot Snapshot { get; } = new();
    public string AircraftTitle { get; private set; } = "";
    public string AtcModel { get; private set; } = "";

    public bool Connect(string appName, int configIndex = 0)
    {
        if (IsConnected) return true;
        try
        {
            EnsureClientConfigFiles();
            // SimConnect resolves SimConnect.cfg relative to CWD and EXE dir — pin CWD to EXE.
            try
            {
                Directory.SetCurrentDirectory(AppContext.BaseDirectory);
                Console.WriteLine($"[SimConnect] CWD={Directory.GetCurrentDirectory()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SimConnect] Could not set CWD: {ex.Message}");
            }

            if (!TryLoadNativeDll(out var loadedFrom, out var loadDetail))
            {
                StatusMessage = loadDetail;
                return false;
            }

            Console.WriteLine($"[SimConnect] Loaded native DLL: {loadedFrom}");

            // Try several config indices — wrong index or wrong/missing cfg causes E_FAIL (0x80004005).
            var attempts = new (string Label, uint Index)[]
            {
                ("settings.config_index", unchecked((uint)configIndex)),
                ("cfg section [SimConnect] (IPv4:500)", 0u),
                ("SIMCONNECT_OPEN_CONFIGINDEX_LOCAL", CONFIGINDEX_LOCAL),
                ("cfg [SimConnect.1] Pipe", 1u),
                ("cfg [SimConnect.2] IPv6:501", 2u),
                ("cfg [SimConnect.3] Auto", 3u)
            };

            var errors = new List<string>();
            foreach (var (label, index) in attempts.DistinctBy(a => a.Index))
            {
                var hr = SimConnect_Open(out _h, appName, IntPtr.Zero, 0, IntPtr.Zero, index);
                if (hr >= 0 && _h != IntPtr.Zero)
                {
                    Console.WriteLine($"[SimConnect] Open OK via {label} (index=0x{index:X8})");
                    MapAllStandardEvents();
                    RegisterStatusDefinitions();
                    RegisterAircraftDefinitions();
                    RequestStatusData();
                    RequestAircraftData();

                    _run = true;
                    _dispatchThread = new Thread(DispatchLoop)
                    {
                        IsBackground = true,
                        Name = "PrivateCoPilot-SimConnect"
                    };
                    _dispatchThread.Start();

                    StatusMessage =
                        $"Native SimConnect LIVE (dll={Path.GetFileName(loadedFrom)}, open={label}, app={appName}).";
                    return true;
                }

                var msg = $"{label} → HRESULT=0x{unchecked((uint)hr):X8}";
                errors.Add(msg);
                Console.WriteLine($"[SimConnect] Open failed: {msg}");
                _h = IntPtr.Zero;
            }

            StatusMessage =
                "SimConnect_Open failed all strategies. " +
                "Need MSFS Free Flight running + Microsoft SimConnect.dll (not FSW/FSX) + SimConnect.cfg. " +
                "Details: " + string.Join("; ", errors);
            return false;
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
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("SimVar name required", nameof(name));

        // Always mirror into local snapshot (offline conditions / diagnostics).
        Snapshot.Set(name, value);

        if (!IsConnected)
        {
            StatusMessage = $"SetSimVar offline snapshot only: {name}={value} {units}";
            return;
        }

        var datumName = name.Trim();
        var unitName = string.IsNullOrWhiteSpace(units) ? "number" : units.Trim();
        var key = datumName + "|" + unitName;

        if (!_setVarDefs.TryGetValue(key, out var defId))
        {
            defId = _nextSetDefId++;
            // Clear leftover definition id then register single FLOAT64 field.
            try { SimConnect_ClearDataDefinition(_h, defId); } catch { /* first use */ }

            var hrDef = SimConnect_AddToDataDefinition(
                _h, defId, datumName, unitName, DATATYPE_FLOAT64, 0f, uint.MaxValue);
            if (hrDef < 0)
                throw new InvalidOperationException(
                    $"AddToDataDefinition({datumName}) failed 0x{unchecked((uint)hrDef):X8}");

            _setVarDefs[key] = defId;
        }

        var ptr = Marshal.AllocHGlobal(sizeof(double));
        try
        {
            Marshal.StructureToPtr(value, ptr, false);
            var hr = SimConnect_SetDataOnSimObject(
                _h, defId, OBJECT_USER, DATA_SET_FLAG_DEFAULT, 0, sizeof(double), ptr);
            if (hr < 0)
                throw new InvalidOperationException(
                    $"SetDataOnSimObject({datumName}={value}) failed 0x{unchecked((uint)hr):X8}");

            Console.WriteLine($"[SimConnect] LIVE SetSimVar: {datumName}={value} {unitName}");
            StatusMessage = $"SetSimVar: {datumName}={value} {unitName}";
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    public void ReceiveMessage()
    {
        // Dispatch thread owns GetNextDispatch.
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
                // optional
            }
        }
        _defsRegistered = true;
    }

    private void RegisterAircraftDefinitions()
    {
        if (!IsConnected) return;
        try
        {
            // TITLE STRING256 + ATC MODEL STRING32 (null units for strings)
            SimConnect_AddToDataDefinition(
                _h, DEFINITION_AIRCRAFT, "TITLE", null, DATATYPE_STRING256, 0f, uint.MaxValue);
            SimConnect_AddToDataDefinition(
                _h, DEFINITION_AIRCRAFT, "ATC MODEL", null, DATATYPE_STRING32, 0f, uint.MaxValue);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimConnect] Aircraft string defs failed: {ex.Message}");
        }
    }

    private void RequestStatusData()
    {
        if (!IsConnected) return;
        SimConnect_RequestDataOnSimObject(
            _h, REQUEST_STATUS, DEFINITION_STATUS, OBJECT_USER,
            PERIOD_SECOND, 0, 0, 0, 0);
    }

    private void RequestAircraftData()
    {
        if (!IsConnected) return;
        SimConnect_RequestDataOnSimObject(
            _h, REQUEST_AIRCRAFT, DEFINITION_AIRCRAFT, OBJECT_USER,
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
            var ex = Marshal.ReadInt32(pData, 12);
            Console.WriteLine($"[SimConnect] RECV_EXCEPTION code={ex}");
            return;
        }

        if (dwId != RECV_SIMOBJECT_DATA)
            return;

        // SIMCONNECT_RECV_SIMOBJECT_DATA: dwRequestID at offset 12
        var requestId = unchecked((uint)Marshal.ReadInt32(pData, 12));
        const int headerSize = 40;

        if (requestId == REQUEST_AIRCRAFT)
        {
            // TITLE STRING256 (256 bytes) + ATC MODEL STRING32 (32 bytes)
            if (cb < headerSize + 256 + 32) return;
            AircraftTitle = Marshal.PtrToStringAnsi(IntPtr.Add(pData, headerSize), 256)?.TrimEnd('\0').Trim() ?? "";
            AtcModel = Marshal.PtrToStringAnsi(IntPtr.Add(pData, headerSize + 256), 32)?.TrimEnd('\0').Trim() ?? "";
            return;
        }

        if (requestId != REQUEST_STATUS)
            return;

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

    /// <summary>
    /// Write SimConnect.cfg next to the EXE if missing (IPv4 127.0.0.1:500 matches default MSFS 2024 SimConnect.xml).
    /// </summary>
    public static void EnsureClientConfigFiles()
    {
        var dir = AppContext.BaseDirectory;
        var cfgPath = Path.Combine(dir, "SimConnect.cfg");
        if (!File.Exists(cfgPath))
        {
            File.WriteAllText(cfgPath, DefaultSimConnectCfg, Encoding.ASCII);
            Console.WriteLine($"[SimConnect] Wrote default {cfgPath}");
        }
    }

    public static bool TryLoadNativeDll(out string loadedFrom)
        => TryLoadNativeDll(out loadedFrom, out _);

    public static bool TryLoadNativeDll(out string loadedFrom, out string detail)
    {
        loadedFrom = "";
        detail = "";
        var rejected = new List<string>();

        foreach (var dir in EnumerateSearchDirs())
        {
            var candidate = Path.Combine(dir, "SimConnect.dll");
            if (!File.Exists(candidate))
                continue;

            if (!IsCompatibleMsfsClientDll(candidate, out var whyNot))
            {
                rejected.Add($"{candidate}: {whyNot}");
                Console.WriteLine($"[SimConnect] Skipping incompatible DLL: {candidate} ({whyNot})");
                continue;
            }

            try
            {
                SetDllDirectory(dir);
                // Also put cfg next to the DLL directory if host runs from elsewhere
                var cfgBesideDll = Path.Combine(dir, "SimConnect.cfg");
                if (!File.Exists(cfgBesideDll) && File.Exists(Path.Combine(AppContext.BaseDirectory, "SimConnect.cfg")))
                {
                    try { File.Copy(Path.Combine(AppContext.BaseDirectory, "SimConnect.cfg"), cfgBesideDll, overwrite: false); }
                    catch { /* ignore */ }
                }

                NativeLibrary.Load(candidate);
                loadedFrom = candidate;
                detail = "OK";
                return true;
            }
            catch (Exception ex)
            {
                rejected.Add($"{candidate}: load error {ex.Message}");
            }
        }

        // Default loader path last (only if not already rejected as FSW in app dir)
        try
        {
            var appDll = Path.Combine(AppContext.BaseDirectory, "SimConnect.dll");
            if (File.Exists(appDll) && IsCompatibleMsfsClientDll(appDll, out _))
            {
                NativeLibrary.Load("SimConnect.dll");
                loadedFrom = "SimConnect.dll (system/app path)";
                detail = "OK";
                return true;
            }
        }
        catch (Exception ex)
        {
            rejected.Add($"default load: {ex.Message}");
        }

        detail =
            "No compatible Microsoft SimConnect.dll found. " +
            "Rejected/missing: " + (rejected.Count > 0 ? string.Join(" | ", rejected) : "none") +
            ". Place the MSFS SDK redistributable SimConnect.dll next to CoPilotVoiceHost.exe " +
            "(must NOT be Flight Sim World / Dovetail).";
        return false;
    }

    /// <summary>
    /// Reject known-wrong third-party clients (e.g. Dovetail Flight Sim World) that return E_FAIL on Open.
    /// </summary>
    public static bool IsCompatibleMsfsClientDll(string path, out string reason)
    {
        reason = "";
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
            {
                reason = "missing";
                return false;
            }

            // Version resource
            try
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                var blob = $"{vi.ProductName}|{vi.CompanyName}|{vi.FileDescription}|{vi.InternalName}";
                if (blob.Contains("Dovetail", StringComparison.OrdinalIgnoreCase)
                    || blob.Contains("Flight Sim World", StringComparison.OrdinalIgnoreCase)
                    || blob.Contains("RailSimulator", StringComparison.OrdinalIgnoreCase))
                {
                    reason = $"third-party client ({vi.ProductName} / {vi.CompanyName}) — not MSFS";
                    return false;
                }
            }
            catch
            {
                // continue with content scan
            }

            // Content markers
            var bytes = File.ReadAllBytes(path);
            var ascii = Encoding.ASCII.GetString(bytes);
            if (ascii.Contains("Dovetail", StringComparison.Ordinal)
                || ascii.Contains("Flight Sim World", StringComparison.Ordinal))
            {
                reason = "binary contains Flight Sim World / Dovetail markers";
                return false;
            }

            // Prefer MSFS markers when present
            if (ascii.Contains("KittyHawk", StringComparison.Ordinal)
                || ascii.Contains("Microsoft Flight Simulator", StringComparison.Ordinal)
                || ascii.Contains("SimConnect_Port_IPv4", StringComparison.Ordinal))
            {
                reason = "ok";
                return true;
            }

            // Unknown but not FSW — allow attempt
            reason = "ok (unmarked)";
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            return false;
        }
    }

    public static IEnumerable<string> EnumerateSearchDirs()
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)
                && !list.Contains(p, StringComparer.OrdinalIgnoreCase))
                list.Add(p);
        }

        Add(AppContext.BaseDirectory);
        Add(Directory.GetCurrentDirectory());

        var env = Environment.GetEnvironmentVariable("MSFS_SDK");
        if (!string.IsNullOrEmpty(env))
        {
            Add(Path.Combine(env, "SimConnect SDK", "lib"));
            Add(Path.Combine(env, "SimConnect SDK", "lib", "static"));
            Add(Path.Combine(env, "SimConnect SDK", "lib", "x64"));
        }

        Add(@"C:\MSFS 2024 SDK\SimConnect SDK\lib");
        Add(@"C:\MSFS SDK\SimConnect SDK\lib");
        Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft Flight Simulator 2024 SDK", "SimConnect SDK", "lib"));

        // Do NOT prefer Addon Manager\couatl (often FSW-era DLL).

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

    private const string DefaultSimConnectCfg =
        """
        [SimConnect]
        Protocol=IPv4
        Address=127.0.0.1
        Port=500
        MaxReceiveSize=41088
        DisableNagle=0

        [SimConnect.1]
        Protocol=Pipe
        Address=localhost
        Port=Custom\SimConnect
        MaxReceiveSize=41088
        DisableNagle=0

        [SimConnect.2]
        Protocol=IPv6
        Address=::1
        Port=501
        MaxReceiveSize=41088
        DisableNagle=0

        [SimConnect.3]
        Protocol=Auto
        MaxReceiveSize=41088
        DisableNagle=0
        """;

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

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int SimConnect_ClearDataDefinition(
        IntPtr hSimConnect,
        uint DefineID);

    [DllImport("SimConnect.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern int SimConnect_SetDataOnSimObject(
        IntPtr hSimConnect,
        uint DefineID,
        uint ObjectID,
        uint Flags,
        uint ArrayCount,
        uint cbUnitSize,
        IntPtr pDataSet);

    #endregion
}
