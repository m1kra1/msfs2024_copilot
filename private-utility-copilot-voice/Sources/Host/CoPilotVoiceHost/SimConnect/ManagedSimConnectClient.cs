using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using CoPilotVoiceHost.Models;

namespace CoPilotVoiceHost.SimConnect;

/// <summary>
/// Attempts to use Managed SimConnect (Microsoft.FlightSimulator.SimConnect) via reflection
/// so the host builds without the MSFS SDK reference. Falls back is handled by factory.
/// Data requests use period ≈ 5 Hz intent (SIMCONNECT_PERIOD.SECOND).
/// OnRecvSimObjectData is hooked so DEF_STATUS payloads update <see cref="Snapshot"/>.
/// </summary>
public sealed class ManagedSimConnectClient : ISimConnectClient
{
    private object? _simConnect;
    private Type? _simConnectType;
    private readonly Dictionary<string, Enum> _mappedEvents = new(StringComparer.OrdinalIgnoreCase);
    private Delegate? _recvSimObjectDataHandler;
    private bool _disposed;

    public bool IsConnected { get; private set; }
    public string StatusMessage { get; private set; } = "Not connected";
    public SimVarSnapshot Snapshot { get; } = new();

    /// <summary>True after a successful OnRecvSimObjectData → Snapshot apply (or test inject).</summary>
    public bool HasReceivedStatusData { get; private set; }

    public static bool TryCreate(out ManagedSimConnectClient? client, out string reason)
    {
        try
        {
            var asm = TryLoadSimConnectAssembly();
            if (asm is null)
            {
                client = null;
                reason = "Microsoft.FlightSimulator.SimConnect assembly not found on this machine.";
                return false;
            }

            var t = asm.GetType("Microsoft.FlightSimulator.SimConnect.SimConnect", throwOnError: false);
            if (t is null)
            {
                client = null;
                reason = "SimConnect type missing in loaded assembly.";
                return false;
            }

            client = new ManagedSimConnectClient { _simConnectType = t };
            reason = "Managed SimConnect assembly loaded.";
            return true;
        }
        catch (Exception ex)
        {
            client = null;
            reason = ex.Message;
            return false;
        }
    }

    public bool Connect(string appName, int configIndex = 0)
    {
        if (_simConnectType is null)
        {
            StatusMessage = "SimConnect type not loaded.";
            return false;
        }

        try
        {
            var ctor = _simConnectType.GetConstructors()
                .FirstOrDefault(c => c.GetParameters().Length >= 3);

            if (ctor is null)
            {
                StatusMessage = "No suitable SimConnect constructor found.";
                return false;
            }

            var parameters = ctor.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = appName;
            args[1] = IntPtr.Zero;
            args[2] = (uint)0x0402;
            for (var i = 3; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType == typeof(uint) || parameters[i].ParameterType == typeof(int))
                    args[i] = configIndex;
                else
                    args[i] = null!;
            }

            _simConnect = ctor.Invoke(args);
            HookRecvSimObjectData();
            MapStandardEvents();
            RegisterStatusDataDefinition();
            RegisterDataDefineStruct();
            RequestStatusData();
            IsConnected = true;
            StatusMessage = $"SimConnect connected as '{appName}' (config_index={configIndex}); OnRecvSimObjectData hooked.";
            return true;
        }
        catch (TargetInvocationException tie)
        {
            IsConnected = false;
            StatusMessage = $"SimConnect open failed: {tie.InnerException?.Message ?? tie.Message}";
            _simConnect = null;
            return false;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusMessage = $"SimConnect open failed: {ex.Message}";
            _simConnect = null;
            return false;
        }
    }

    public void Disconnect()
    {
        try
        {
            UnhookRecvSimObjectData();
            if (_simConnect is IDisposable d)
                d.Dispose();
        }
        catch
        {
            // ignore dispose races with sim exit
        }

        _simConnect = null;
        IsConnected = false;
        StatusMessage = "Disconnected";
    }

    public void TransmitEvent(string eventName, uint data = 0)
    {
        if (_simConnect is null)
            throw new InvalidOperationException("Not connected");

        if (!_mappedEvents.TryGetValue(eventName, out var enumId))
        {
            MapClientEvent(eventName, PrivateCopilotEventId.PRIVATE_COPILOT_EVT_GENERIC);
            enumId = _mappedEvents[eventName];
        }

        var method = _simConnectType!.GetMethod("TransmitClientEvent",
            BindingFlags.Instance | BindingFlags.Public);
        if (method is null)
            throw new MissingMethodException("TransmitClientEvent");

        var groupType = _simConnectType.Assembly.GetType(
            "Microsoft.FlightSimulator.SimConnect.SIMCONNECT_NOTIFICATION_GROUP_ID")
            ?? typeof(PrivateCopilotEventId);

        object? groupId = Enum.ToObject(groupType, 0);
        var flagType = _simConnectType.Assembly.GetType(
            "Microsoft.FlightSimulator.SimConnect.SIMCONNECT_EVENT_FLAG");
        object flags = flagType is not null ? Enum.ToObject(flagType, 0x10) : 0;

        method.Invoke(_simConnect, new object[] { 0u, enumId, data, groupId!, flags });
    }

    public void SetSimVar(string name, double value, string units)
    {
        Snapshot.Set(name, value);
        StatusMessage = $"SetSimVar requested: {name}={value} {units} (event-preferred architecture)";
    }

    public void ReceiveMessage()
    {
        if (_simConnect is null) return;
        try
        {
            var method = _simConnectType!.GetMethod("ReceiveMessage",
                BindingFlags.Instance | BindingFlags.Public);
            method?.Invoke(_simConnect, null);
        }
        catch (COMException)
        {
            IsConnected = false;
            StatusMessage = "SimConnect receive COM exception — connection lost.";
        }
        catch (TargetInvocationException)
        {
            // Quiet during disconnect races
        }
    }

    /// <summary>
    /// Test / offline inject of a status payload into Snapshot (same path as live recv).
    /// </summary>
    public void ApplyStatusData(PrivateCopilotStatusData data)
    {
        StatusSnapshotMapper.Apply(Snapshot, data);
        HasReceivedStatusData = true;
    }

    /// <summary>Test inject of ordered doubles matching StatusSimVars.Definitions.</summary>
    public void ApplyStatusData(ReadOnlySpan<double> values)
    {
        StatusSnapshotMapper.Apply(Snapshot, values);
        HasReceivedStatusData = true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }

    private void HookRecvSimObjectData()
    {
        if (_simConnect is null || _simConnectType is null) return;

        var evt = _simConnectType.GetEvent("OnRecvSimObjectData");
        if (evt?.EventHandlerType is null)
        {
            StatusMessage += " [warn: OnRecvSimObjectData event missing]";
            return;
        }

        try
        {
            // Build (sender, e) => this.HandleRecvSimObjectData(e) matching SDK EventHandler<T>
            var handlerType = evt.EventHandlerType;
            var invoke = handlerType.GetMethod("Invoke")
                         ?? throw new MissingMethodException(handlerType.Name, "Invoke");
            var invokeParams = invoke.GetParameters();
            if (invokeParams.Length != 2)
                throw new InvalidOperationException("OnRecvSimObjectData handler arity != 2");

            var senderParam = Expression.Parameter(invokeParams[0].ParameterType, "sender");
            var argsParam = Expression.Parameter(invokeParams[1].ParameterType, "e");
            var target = Expression.Constant(this);
            var handleMethod = typeof(ManagedSimConnectClient).GetMethod(
                nameof(HandleRecvSimObjectData),
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new MissingMethodException(nameof(HandleRecvSimObjectData));

            var call = Expression.Call(
                target,
                handleMethod,
                Expression.Convert(argsParam, typeof(object)));
            var lambda = Expression.Lambda(handlerType, call, senderParam, argsParam);
            _recvSimObjectDataHandler = lambda.Compile();
            evt.AddEventHandler(_simConnect, _recvSimObjectDataHandler);
            StatusMessage += " [OnRecvSimObjectData hooked]";
        }
        catch (Exception ex)
        {
            StatusMessage += $" [warn: OnRecv hook failed: {ex.Message}]";
            _recvSimObjectDataHandler = null;
        }
    }

    private void UnhookRecvSimObjectData()
    {
        if (_simConnect is null || _simConnectType is null || _recvSimObjectDataHandler is null)
            return;

        try
        {
            var evt = _simConnectType.GetEvent("OnRecvSimObjectData");
            evt?.RemoveEventHandler(_simConnect, _recvSimObjectDataHandler);
        }
        catch
        {
            // ignore
        }

        _recvSimObjectDataHandler = null;
    }

    /// <summary>
    /// Parses SIMCONNECT_RECV_SIMOBJECT_DATA via reflection and updates Snapshot.
    /// Shipped live path for context-aware conditions (gear, VS, lights, etc.).
    /// </summary>
    public void HandleRecvSimObjectData(object data)
    {
        if (data is null) return;

        try
        {
            var dataType = data.GetType();
            var reqIdObj = dataType.GetField("dwRequestID")?.GetValue(data)
                           ?? dataType.GetProperty("dwRequestID")?.GetValue(data);
            if (reqIdObj is null) return;

            var reqId = Convert.ToUInt32(reqIdObj);
            if (reqId != (uint)PrivateCopilotRequestId.PRIVATE_COPILOT_REQ_STATUS)
                return;

            var dwDataField = dataType.GetField("dwData") ?? dataType.GetProperty("dwData") as MemberInfo;
            object? dwData = dwDataField switch
            {
                FieldInfo fi => fi.GetValue(data),
                PropertyInfo pi => pi.GetValue(data),
                _ => null
            };

            if (dwData is null) return;

            // Managed SimConnect typically puts the registered struct in dwData[0]
            if (dwData is Array arr && arr.Length > 0)
            {
                var item = arr.GetValue(0);
                if (item is PrivateCopilotStatusData status)
                {
                    StatusSnapshotMapper.Apply(Snapshot, status);
                    HasReceivedStatusData = true;
                    return;
                }

                if (item is not null)
                {
                    // Boxed struct from other assembly layout — map by field order if doubles
                    if (TryMapBoxedStatus(item))
                    {
                        HasReceivedStatusData = true;
                        return;
                    }
                }
            }

            if (dwData is PrivateCopilotStatusData direct)
            {
                StatusSnapshotMapper.Apply(Snapshot, direct);
                HasReceivedStatusData = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SimConnect] OnRecvSimObjectData parse failed: {ex.Message}");
        }
    }

    private bool TryMapBoxedStatus(object item)
    {
        var t = item.GetType();
        // Prefer field/property doubles in definition order if names match
        var values = new double[StatusSimVars.Definitions.Length];
        var filled = 0;
        for (var i = 0; i < StatusSimVars.Definitions.Length; i++)
        {
            // PrivateCopilotStatusData field order
            var fieldNames = new[]
            {
                "GearPosition", "VerticalSpeed", "FlapsHandleIndex", "AutopilotMaster",
                "LightLanding", "LightTaxi", "LightStrobe", "LightBeacon", "LightNav",
                "BrakeParkingPosition", "AntiSkidBrakesActive", "EngAntiIce",
                "AirspeedIndicated", "PlaneAltitude", "SimOnGround"
            };
            if (i >= fieldNames.Length) break;
            var f = t.GetField(fieldNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            var p = t.GetProperty(fieldNames[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            object? v = f?.GetValue(item) ?? p?.GetValue(item);
            if (v is null) continue;
            values[i] = Convert.ToDouble(v);
            filled++;
        }

        if (filled == 0)
            return false;

        StatusSnapshotMapper.Apply(Snapshot, values.AsSpan());
        return true;
    }

    private void MapStandardEvents()
    {
        foreach (var kv in StandardEventMap.All)
            MapClientEvent(kv.Key, kv.Value);
    }

    private void MapClientEvent(string simEventName, PrivateCopilotEventId id)
    {
        if (_simConnect is null || _simConnectType is null) return;

        var mapMethod = _simConnectType.GetMethod("MapClientEventToSimEvent",
            BindingFlags.Instance | BindingFlags.Public);
        if (mapMethod is null) return;

        mapMethod.Invoke(_simConnect, new object[] { id, simEventName });
        _mappedEvents[simEventName] = id;
    }

    private void RegisterStatusDataDefinition()
    {
        if (_simConnect is null || _simConnectType is null) return;

        var addToDataDef = _simConnectType.GetMethod("AddToDataDefinition",
            BindingFlags.Instance | BindingFlags.Public);
        if (addToDataDef is null) return;

        var datatype = _simConnectType.Assembly.GetType(
            "Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE");
        object float64 = datatype is not null ? Enum.Parse(datatype, "FLOAT64") : 0;

        foreach (var (name, units) in StatusSimVars.Definitions)
        {
            try
            {
                addToDataDef.Invoke(_simConnect, new object?[]
                {
                    PrivateCopilotDefineId.PRIVATE_COPILOT_DEF_STATUS,
                    name,
                    units,
                    float64,
                    0f,
                    0xffffffffu
                });
            }
            catch
            {
                // individual var may not exist on all aircraft
            }
        }
    }

    private void RegisterDataDefineStruct()
    {
        if (_simConnect is null || _simConnectType is null) return;

        var methods = _simConnectType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == "RegisterDataDefineStruct" && m.IsGenericMethodDefinition)
            .ToList();

        foreach (var method in methods)
        {
            try
            {
                var closed = method.MakeGenericMethod(typeof(PrivateCopilotStatusData));
                var ps = closed.GetParameters();
                if (ps.Length == 1)
                {
                    closed.Invoke(_simConnect, new object[] { PrivateCopilotDefineId.PRIVATE_COPILOT_DEF_STATUS });
                    return;
                }
            }
            catch
            {
                // try next overload
            }
        }
    }

    private void RequestStatusData()
    {
        if (_simConnect is null || _simConnectType is null) return;

        var requestMethod = _simConnectType.GetMethod("RequestDataOnSimObject",
            BindingFlags.Instance | BindingFlags.Public);
        if (requestMethod is null) return;

        var periodType = _simConnectType.Assembly.GetType(
            "Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD");
        object period = periodType is not null ? Enum.Parse(periodType, "SECOND") : 1;

        var flagType = _simConnectType.Assembly.GetType(
            "Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATA_REQUEST_FLAG");
        object flags = flagType is not null ? Enum.ToObject(flagType, 0) : 0;

        try
        {
            requestMethod.Invoke(_simConnect, new object[]
            {
                PrivateCopilotRequestId.PRIVATE_COPILOT_REQ_STATUS,
                PrivateCopilotDefineId.PRIVATE_COPILOT_DEF_STATUS,
                0u,
                period,
                flags,
                0u,
                0u,
                0u
            });
        }
        catch
        {
            // Overload differences across SDK versions
        }
    }

    private static Assembly? TryLoadSimConnectAssembly()
    {
        try
        {
            return Assembly.Load("Microsoft.FlightSimulator.SimConnect");
        }
        catch
        {
            // continue path search
        }

        var candidates = new List<string>();
        var env = Environment.GetEnvironmentVariable("MSFS_SDK");
        if (!string.IsNullOrEmpty(env))
        {
            candidates.Add(Path.Combine(env, "SimConnect SDK", "lib", "managed", "Microsoft.FlightSimulator.SimConnect.dll"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        candidates.Add(Path.Combine(programFiles, "Microsoft Flight Simulator 2024 SDK", "SimConnect SDK", "lib", "managed", "Microsoft.FlightSimulator.SimConnect.dll"));

        foreach (var path in candidates.Where(File.Exists))
        {
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch
            {
                // try next
            }
        }

        return null;
    }
}

public static class SimConnectClientFactory
{
    public static ISimConnectClient Create(bool preferOffline = false)
    {
        if (!preferOffline && ManagedSimConnectClient.TryCreate(out var managed, out var reason))
        {
            Console.WriteLine($"[SimConnect] {reason}");
            return managed!;
        }

        Console.WriteLine("[SimConnect] Using recording/offline client (Managed SimConnect unavailable or forced offline).");
        return new RecordingSimConnectClient();
    }
}
