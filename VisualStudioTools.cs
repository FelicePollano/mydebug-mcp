using System.ComponentModel;
using EnvDTE;
using ModelContextProtocol.Server;

namespace mydebug_mcp;

public sealed record BreakpointInfo(string File, int Line, bool Enabled, bool IsTracepoint, string Message);

/// <summary>
/// Tools that drive a RUNNING Visual Studio instance (through EnvDTE automation) to debug a .NET application.
/// </summary>
[McpServerToolType]
public sealed class VisualStudioTools(VisualStudioService vs)
{
    public const string ServerInstructions =
        "Debug applications by remotely driving a Visual Studio instance that is already running on this " +
        "machine (via EnvDTE automation). Visual Studio must be open, in the same user session and with the " +
        "same elevation as this server. Typical workflow: ListInstances to find the Visual Studio process " +
        "(pass its PID as processId if several are open) -> OpenSolution if needed -> AddBreakpoint or " +
        "AddTracepoint -> StartDebugging -> when the debugger is in break mode use Evaluate and CallStack, " +
        "then Step/Continue -> ReadOutput to see Debug/Build output -> StopDebugging. " +
        "Every tool takes an optional processId; it can be omitted when only one Visual Studio is running.";

    [McpServerTool, Description("Visual Studio debugging: list the running Visual Studio instances that can be used " +
        "to debug (PID, version, open solution, debugger mode: design/run/break). Call this first to discover the processId.")]
    public Task<VsInstance[]> ListInstances() =>
        vs.RunAsync(() => vs.EnumerateInstances().Select(vs.Describe).ToArray());

    [McpServerTool, Description("Visual Studio debugging: open a solution (.sln/.slnx) file in the running Visual Studio instance.")]
    public Task<string> OpenSolution(string path, int? processId = null) => vs.RunAsync(() =>
    {
        vs.Get(processId).Solution.Open(path);
        return $"Opened {path}";
    });

    [McpServerTool, Description("Visual Studio debugging: start debugging the solution's startup project in the running " +
        "Visual Studio (like F5) and return immediately. Returns the debugger mode.")]
    public Task<string> StartDebugging(int? processId = null) => vs.RunAsync(() =>
    {
        var dte = vs.Get(processId);
        dte.Debugger.Go(false);
        return dte.Debugger.CurrentMode.ToString();
    });

    [McpServerTool, Description("Visual Studio debugging: pause (break all) the program being debugged in the running Visual Studio.")]
    public Task<string> Break(int? processId = null) => vs.RunAsync(() =>
    {
        var dte = vs.Get(processId);
        dte.Debugger.Break(false);
        return dte.Debugger.CurrentMode.ToString();
    });

    [McpServerTool, Description("Visual Studio debugging: resume execution of the program stopped at a breakpoint " +
        "in the running Visual Studio.")]
    public Task<string> Continue(int? processId = null) => vs.RunAsync(() =>
    {
        var dte = vs.Get(processId);
        dte.Debugger.Go(false);
        return dte.Debugger.CurrentMode.ToString();
    });

    [McpServerTool, Description("Visual Studio debugging: step the debugger, kind = 'over', 'into' or 'out'. " +
        "Debugger must be in break mode.")]
    public Task<string> Step(string kind = "over", int? processId = null) => vs.RunAsync(() =>
    {
        var d = vs.Get(processId).Debugger;
        switch (kind.ToLowerInvariant())
        {
            case "over": d.StepOver(true); break;
            case "into": d.StepInto(true); break;
            case "out": d.StepOut(true); break;
            default: throw new ArgumentException("kind must be over, into or out");
        }
        return d.CurrentMode.ToString();
    });

    [McpServerTool, Description("Visual Studio debugging: stop the debugging session in the running Visual Studio.")]
    public Task<string> StopDebugging(int? processId = null) => vs.RunAsync(() =>
    {
        var d = vs.Get(processId).Debugger;
        d.Stop(false);
        return d.CurrentMode.ToString();
    });

    [McpServerTool, Description("Visual Studio debugging: add a breakpoint at file:line (file is a full path) " +
        "in the running Visual Studio.")]
    public Task<string> AddBreakpoint(string file, int line, int? processId = null) => vs.RunAsync(() =>
    {
        vs.Get(processId).Debugger.Breakpoints.Add("", file, line);
        return $"Breakpoint set at {file}:{line}";
    });

    [McpServerTool, Description("Visual Studio debugging: add a tracepoint at file:line in the running Visual Studio " +
        "(a breakpoint that logs a message and does not stop execution). " +
        "Message may contain expressions in braces, e.g. \"x = {x}\". Output goes to the 'Debug' Output pane; read it with ReadOutput.")]
    public Task<string> AddTracepoint(string file, int line, string message, string condition = "",
        int? processId = null) => vs.RunAsync(() =>
    {
        var bps = vs.Get(processId).Debugger.Breakpoints;
        var added = bps.Add("", file, line, 1, condition);
        var bp = (EnvDTE80.Breakpoint2)added.Item(1);
        bp.Message = message;
        bp.BreakWhenHit = false;
        return $"Tracepoint set at {file}:{line}";
    });

    [McpServerTool, Description("Visual Studio debugging: list the breakpoints and tracepoints set in the running " +
        "Visual Studio (file, line, enabled, isTracepoint, tracepoint message).")]
    public Task<BreakpointInfo[]> ListBreakpoints(int? processId = null) => vs.RunAsync(() =>
        vs.Get(processId).Debugger.Breakpoints.Cast<Breakpoint>().Select(ToInfo).ToArray());

    [McpServerTool, Description("Visual Studio debugging: remove the breakpoint(s) (not tracepoints) at file:line " +
        "from the running Visual Studio. Returns how many were removed.")]
    public Task<string> RemoveBreakpoint(string file, int line, int? processId = null) =>
        vs.RunAsync(() => RemoveAt(processId, file, line, tracepoints: false));

    [McpServerTool, Description("Visual Studio debugging: remove the tracepoint(s) (not plain breakpoints) at " +
        "file:line from the running Visual Studio. Returns how many were removed.")]
    public Task<string> RemoveTracepoint(string file, int line, int? processId = null) =>
        vs.RunAsync(() => RemoveAt(processId, file, line, tracepoints: true));

    private static BreakpointInfo ToInfo(Breakpoint b)
    {
        var b2 = (EnvDTE80.Breakpoint2)b;
        var isTrace = !b2.BreakWhenHit;
        return new(b.File, b.FileLine, b.Enabled, isTrace, isTrace ? b2.Message : "");
    }

    private string RemoveAt(int? processId, string file, int line, bool tracepoints)
    {
        var target = Path.GetFullPath(file);
        var matches = vs.Get(processId).Debugger.Breakpoints.Cast<Breakpoint>()
            .Where(b => b.FileLine == line
                && string.Equals(Path.GetFullPath(b.File), target, StringComparison.OrdinalIgnoreCase)
                && !((EnvDTE80.Breakpoint2)b).BreakWhenHit == tracepoints)
            .ToList(); // materialize before deleting
        foreach (var b in matches) b.Delete();
        return $"Removed {matches.Count} {(tracepoints ? "tracepoint" : "breakpoint")}(s) at {file}:{line}";
    }

    [McpServerTool, Description("Visual Studio debugging: list the panes of the Output window of the running " +
        "Visual Studio (e.g. Build, Debug).")]
    public Task<string[]> ListOutputPanes(int? processId = null) => vs.RunAsync(() =>
        vs.Get(processId).ToolWindows.OutputWindow.OutputWindowPanes
            .Cast<OutputWindowPane>().Select(p => p.Name).ToArray());

    [McpServerTool, Description("Visual Studio debugging: read the text of an Output window pane (default 'Debug', " +
        "use 'Build' for build output) of the running Visual Studio. Returns the last maxChars characters.")]
    public Task<string> ReadOutput(string pane = "Debug", int maxChars = 20000, int? processId = null) =>
        vs.RunAsync(() =>
    {
        var panes = vs.Get(processId).ToolWindows.OutputWindow.OutputWindowPanes;
        var p = panes.Cast<OutputWindowPane>()
            .FirstOrDefault(x => x.Name.Equals(pane, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No output pane named '{pane}'. Use ListOutputPanes.");
        var doc = p.TextDocument;
        var text = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
        return text.Length > maxChars ? text[^maxChars..] : text;
    });

    [McpServerTool, Description("Visual Studio debugging: evaluate an expression or variable in the current debug " +
        "context of the running Visual Studio (debugger must be in break mode).")]
    public Task<string> Evaluate(string expression, int? processId = null) => vs.RunAsync(() =>
    {
        var d = vs.Get(processId).Debugger;
        if (d.CurrentMode != dbgDebugMode.dbgBreakMode)
            throw new InvalidOperationException("Debugger is not in break mode.");
        var e = d.GetExpression(expression);
        return e.IsValidValue ? e.Value : "<invalid expression>";
    });

    [McpServerTool, Description("Visual Studio debugging: return the call stack of the current thread in the running " +
        "Visual Studio (debugger must be in break mode).")]
    public Task<string[]> CallStack(int? processId = null) => vs.RunAsync(() =>
    {
        var d = vs.Get(processId).Debugger;
        if (d.CurrentMode != dbgDebugMode.dbgBreakMode)
            throw new InvalidOperationException("Debugger is not in break mode.");
        return d.CurrentThread.StackFrames.Cast<StackFrame>()
            .Select(f => $"{f.FunctionName} ({f.Module})").ToArray();
    });

    [McpServerTool, Description("Visual Studio debugging: execute any Visual Studio command by name in the running " +
        "instance, e.g. 'File.SaveAll' or 'Build.BuildSolution'.")]
    public Task<string> ExecuteCommand(string command, string args = "", int? processId = null) => vs.RunAsync(() =>
    {
        vs.Get(processId).ExecuteCommand(command, args);
        return $"Executed {command}";
    });
}
