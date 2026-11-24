using System.ComponentModel;
using EnvDTE;
using ModelContextProtocol.Server;

namespace mydebug_mcp;

[McpServerToolType]
public sealed class VisualStudioTools(VisualStudioService vs)
{
    [McpServerTool, Description("List running Visual Studio instances (PID, version, open solution, debugger mode).")]
    public Task<VsInstance[]> ListInstances() =>
        vs.RunAsync(() => vs.EnumerateInstances().Select(vs.Describe).ToArray());

    [McpServerTool, Description("Open a solution file in the Visual Studio instance.")]
    public Task<string> OpenSolution(string path, int? processId = null) => vs.RunAsync(() =>
    {
        vs.Get(processId).Solution.Open(path);
        return $"Opened {path}";
    });

   

    [McpServerTool, Description("Start debugging (F5) and return immediately.")]
    public Task<string> StartDebugging(int? processId = null) => vs.RunAsync(() =>
    {
        var dte = vs.Get(processId);
        dte.Debugger.Go(false);
        return dte.Debugger.CurrentMode.ToString();
    });

    [McpServerTool, Description("Break all (pause) the debugged program.")]
    public Task<string> Break(int? processId = null) => vs.RunAsync(() =>
    {
        var dte = vs.Get(processId);
        dte.Debugger.Break(false);
        return dte.Debugger.CurrentMode.ToString();
    });

    [McpServerTool, Description("Continue execution.")]
    public Task<string> Continue(int? processId = null) => vs.RunAsync(() =>
    {
        var dte = vs.Get(processId);
        dte.Debugger.Go(false);
        return dte.Debugger.CurrentMode.ToString();
    });

    [McpServerTool, Description("Step: 'over', 'into' or 'out'.")]
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

    [McpServerTool, Description("Stop debugging.")]
    public Task<string> StopDebugging(int? processId = null) => vs.RunAsync(() =>
    {
        var d = vs.Get(processId).Debugger;
        d.Stop(false);
        return d.CurrentMode.ToString();
    });

    [McpServerTool, Description("Toggle a breakpoint by adding one at file:line.")]
    public Task<string> AddBreakpoint(string file, int line, int? processId = null) => vs.RunAsync(() =>
    {
        vs.Get(processId).Debugger.Breakpoints.Add("", file, line);
        return $"Breakpoint set at {file}:{line}";
    });

    [McpServerTool, Description("Add a tracepoint (a breakpoint that logs a message and does not break). " +
        "Message may contain expressions in braces, e.g. \"x = {x}\". Output goes to the 'Debug' Output pane.")]
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

    [McpServerTool, Description("List the panes of the Output window (e.g. Build, Debug).")]
    public Task<string[]> ListOutputPanes(int? processId = null) => vs.RunAsync(() =>
        vs.Get(processId).ToolWindows.OutputWindow.OutputWindowPanes
            .Cast<OutputWindowPane>().Select(p => p.Name).ToArray());

    [McpServerTool, Description("Read text from an Output window pane (default 'Debug'). " +
        "Returns the last maxChars characters.")]
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

    [McpServerTool, Description("Evaluate an expression in the current debug context (must be in break mode).")]
    public Task<string> Evaluate(string expression, int? processId = null) => vs.RunAsync(() =>
    {
        var d = vs.Get(processId).Debugger;
        if (d.CurrentMode != dbgDebugMode.dbgBreakMode)
            throw new InvalidOperationException("Debugger is not in break mode.");
        var e = d.GetExpression(expression);
        return e.IsValidValue ? e.Value : "<invalid expression>";
    });

    [McpServerTool, Description("Return the current call stack (must be in break mode).")]
    public Task<string[]> CallStack(int? processId = null) => vs.RunAsync(() =>
    {
        var d = vs.Get(processId).Debugger;
        if (d.CurrentMode != dbgDebugMode.dbgBreakMode)
            throw new InvalidOperationException("Debugger is not in break mode.");
        return d.CurrentThread.StackFrames.Cast<StackFrame>()
            .Select(f => $"{f.FunctionName} ({f.Module})").ToArray();
    });

    [McpServerTool, Description("Execute any Visual Studio command by name, e.g. 'File.SaveAll'.")]
    public Task<string> ExecuteCommand(string command, string args = "", int? processId = null) => vs.RunAsync(() =>
    {
        vs.Get(processId).ExecuteCommand(command, args);
        return $"Executed {command}";
    });
}
