using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using EnvDTE;
using EnvDTE80;

namespace mydebug_mcp;

public sealed record VsInstance(int ProcessId, string Version, string Solution, string Mode);

/// <summary>
/// Finds running Visual Studio instances through the Running Object Table
/// and exposes their EnvDTE automation object.
/// </summary>
public sealed class VisualStudioService
{
    [DllImport("ole32.dll")]
    private static extern int GetRunningObjectTable(uint reserved, out IRunningObjectTable rot);

    [DllImport("ole32.dll")]
    private static extern int CreateBindCtx(uint reserved, out IBindCtx ctx);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    // All COM calls are funnelled through a single STA thread, which avoids
    // apartment problems with Kestrel's MTA thread-pool threads.
    private readonly StaWorker _sta = new();

    public Task<T> RunAsync<T>(Func<T> work) => _sta.RunAsync(work);

    public IEnumerable<DTE2> EnumerateInstances()
    {
        GetRunningObjectTable(0, out var rot);
        rot.EnumRunning(out var enumMoniker);
        CreateBindCtx(0, out var ctx);
        var monikers = new IMoniker[1];
        while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
        {
            monikers[0].GetDisplayName(ctx, null, out var name);
            if (!name.StartsWith("!VisualStudio.DTE.", StringComparison.OrdinalIgnoreCase))
                continue;
            rot.GetObject(monikers[0], out var obj);
            if (obj is DTE2 dte)
                yield return dte;
        }
    }

    public static int GetPid(DTE2 dte)
    {
        GetWindowThreadProcessId((IntPtr)dte.MainWindow.HWnd, out var pid);
        return (int)pid;
    }

    public DTE2 Get(int? processId)
    {
        var all = EnumerateInstances().ToList();
        if (all.Count == 0)
            throw new InvalidOperationException("No running Visual Studio instance found.");
        if (processId is int pid)
            return all.FirstOrDefault(d => GetPid(d) == pid)
                ?? throw new InvalidOperationException($"No Visual Studio instance with PID {pid}.");
        if (all.Count > 1)
            throw new InvalidOperationException(
                "Multiple Visual Studio instances are running; specify processId (see ListInstances).");
        return all[0];
    }

    public VsInstance Describe(DTE2 dte) =>
        new(GetPid(dte), dte.Version, dte.Solution?.FullName ?? "", dte.Debugger.CurrentMode.ToString());

    private sealed class StaWorker
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _queue = new();

        public StaWorker()
        {
            var t = new System.Threading.Thread(() =>
            {
                foreach (var a in _queue.GetConsumingEnumerable()) a();
            }) { IsBackground = true, Name = "VS-STA" };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public Task<T> RunAsync<T>(Func<T> work)
        {
            var tcs = new TaskCompletionSource<T>();
            _queue.Add(() =>
            {
                try { tcs.SetResult(work()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}
