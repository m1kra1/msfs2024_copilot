using System.Runtime.InteropServices;
using CoPilotVoiceHost.Host;
using CoPilotVoiceHost.Models;
using CoPilotVoiceHost.Ui;

namespace CoPilotVoiceHost;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = HostOptions.Parse(args);

        // One-shot CLI and explicit headless keep console pipeline (tests + automation).
        var forceHeadless = options.Headless
                            || options.Once
                            || !string.IsNullOrWhiteSpace(options.InjectPhrase);

        if (forceHeadless)
        {
            EnsureConsole();
            var logPath = Path.Combine(AppContext.BaseDirectory, HostConstants.HeadlessLogFileName);
            try
            {
                using var file = new StreamWriter(logPath, append: false) { AutoFlush = true };
                var multiOut = new MultiTextWriter(Console.Out, file);
                var multiErr = new MultiTextWriter(Console.Error, file);
                Console.SetOut(multiOut);
                Console.SetError(multiErr);
                var code = Run(options);
                Console.WriteLine($"[Host] headless log file: {logPath}");
                return code;
            }
            catch (Exception ex)
            {
                try { Console.Error.WriteLine($"[FATAL] {ex}"); } catch { /* ignore */ }
                try { File.AppendAllText(logPath, ex.ToString()); } catch { /* ignore */ }
                return 2;
            }
        }

        // GUI mode — HostSession owns SimConnect/speech lifetime; MainWindow also disposes on tray Exit.
        try
        {
            var app = new App();
            app.InitializeComponent();
            using var session = new HostSession(options);
            session.Start();
            session.StartSpeechListening();
            var window = new MainWindow(session, options);
            app.Run(window);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                EnsureConsole();
                Console.Error.WriteLine($"[FATAL GUI] {ex}");
            }
            catch { /* ignore */ }
            return 2;
        }
    }

    /// <summary>Console / test entry — full headless pipeline (no WPF).</summary>
    public static int Run(HostOptions options)
    {
        using var session = new HostSession(options);
        try
        {
            Console.WriteLine("Private Voice Co-Pilot Host (CoPilotVoiceHost)");
            Console.WriteLine("==============================================");
            Console.WriteLine($"Package: {HostConstants.PackageName} | Creator: Private");
            Console.WriteLine();
            return session.RunHeadlessToCompletion();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] {ex}");
            return 2;
        }
    }

    private static void EnsureConsole()
    {
        try
        {
            // Prefer attaching to parent console (dotnet test / shells); else allocate one.
            if (GetConsoleWindow() == IntPtr.Zero)
            {
                if (!AttachConsole(ATTACH_PARENT_PROCESS))
                    AllocConsole();
            }

            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
        catch
        {
            // ignore
        }
    }

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}

/// <summary>Duplicates writes to console + file for WinExe headless capture.</summary>
internal sealed class MultiTextWriter : TextWriter
{
    private readonly TextWriter _a;
    private readonly TextWriter _b;

    public MultiTextWriter(TextWriter a, TextWriter b)
    {
        _a = a;
        _b = b;
    }

    public override System.Text.Encoding Encoding => _a.Encoding;

    public override void Write(char value)
    {
        _a.Write(value);
        _b.Write(value);
    }

    public override void Write(string? value)
    {
        _a.Write(value);
        _b.Write(value);
    }

    public override void WriteLine(string? value)
    {
        _a.WriteLine(value);
        _b.WriteLine(value);
    }

    public override void Flush()
    {
        _a.Flush();
        _b.Flush();
    }
}

public sealed class HostOptions
{
    public string? ConfigRoot { get; init; }
    public string? Profile { get; init; }
    public string? InjectPhrase { get; init; }
    public bool ForceOffline { get; init; }
    public bool NoTts { get; init; }
    public bool NoSpeech { get; init; }
    public bool Once { get; init; }
    public double? FixtureVerticalSpeed { get; init; }
    public bool SimulatePtt { get; init; }
    public bool BypassSpeechGate { get; init; }
    public bool AllowOfflineFallback { get; init; }
    public bool Headless { get; init; }

    public static HostOptions Parse(string[] args)
    {
        string? config = null;
        string? profile = null;
        string? inject = null;
        var offline = false;
        var noTts = false;
        var noSpeech = false;
        var once = false;
        double? vs = null;
        var simPtt = false;
        var bypassGate = false;
        var allowOffline = false;
        var headless = false;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a)
            {
                case "--config":
                    config = args[++i];
                    break;
                case "--profile":
                    profile = args[++i];
                    break;
                case "--inject":
                    inject = args[++i];
                    break;
                case "--offline":
                    offline = true;
                    break;
                case "--no-tts":
                    noTts = true;
                    break;
                case "--no-speech":
                    noSpeech = true;
                    break;
                case "--once":
                    once = true;
                    break;
                case "--vs":
                    vs = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--ptt":
                    simPtt = true;
                    break;
                case "--bypass-gate":
                    bypassGate = true;
                    break;
                case "--allow-offline-fallback":
                    allowOffline = true;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return new HostOptions
        {
            ConfigRoot = config,
            Profile = profile,
            InjectPhrase = inject,
            ForceOffline = offline,
            NoTts = noTts,
            NoSpeech = noSpeech,
            Once = once,
            FixtureVerticalSpeed = vs,
            SimulatePtt = simPtt,
            BypassSpeechGate = bypassGate,
            AllowOfflineFallback = allowOffline || offline,
            Headless = headless
        };
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            CoPilotVoiceHost — Private Voice Co-Pilot (WPF + headless)
            Usage:
              CoPilotVoiceHost [--config <dir>] [--profile generic|a320|b737|fenix_a320]
                               [--inject "Co Pilot gear up"] [--offline] [--no-tts] [--no-speech]
                               [--once] [--vs 500] [--ptt] [--bypass-gate] [--allow-offline-fallback]
                               [--headless]
            Default: GUI window. Use --headless (or --once / --inject) for console-only mode.
            --profile locks the aircraft profile for the session (disables auto-detect switching).
            Auto-detect: settings auto_detect_aircraft + config/aircraft_detection.json (when Live).
            """);
    }
}
