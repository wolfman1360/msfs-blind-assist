using System.Collections.Concurrent;
using System.Diagnostics;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// Runs part of a real TriStar flow on a real <see cref="FlowManager{TExec,TState}"/> over the real evaluator, with an
/// executor that accepts every write except the keys it is told to fail (the real one needs the sim): what was spoken,
/// started, completed, skipped and sent. A run must finish by itself within 15 s, unless it is cancelled 300 ms after a
/// named step starts (a long wait that would otherwise run out). <c>onStarted</c> is told each step as it starts, so a
/// test can script what the aircraft does during a wait (the evaluator reads a seeded cache the test can change).
/// </summary>
internal static class L1011FoFlowRun
{
    internal sealed record Result(List<string> Speech, List<string> Started, List<string> Completed, List<string> Skipped,
        string[] Sent, bool Finished);

    private sealed class Executor : IFoActionExecutor
    {
        private readonly HashSet<string> _fails;
        public Executor(IEnumerable<string> fails) => _fails = new HashSet<string>(fails, StringComparer.Ordinal);
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;

        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            var keys = step.ActionType == FlowStepActionType.SetSwitchMultiple
                ? step.MultiActions.Select(a => a.EventName).ToArray()
                : new[] { step.EventName ?? "" };
            bool ok = true;
            foreach (var key in keys)
            {
                if (_fails.Contains(key)) { ok = false; continue; }
                Sent.Enqueue(key);
            }
            return Task.FromResult(ok);
        }

        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    internal static async Task<Result> RunAsync(string flowId, string[] stepIds, L1011FoStateEvaluator state,
        IEnumerable<string>? fail = null, string? cancelWhenStarted = null, Action<string>? onStarted = null)
    {
        var source = L1011FoFlowDefinitions.Build().Single(f => f.Id == flowId);
        var flow = new FlowDefinition<L1011FoStateEvaluator>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());

        var executor = new Executor(fail ?? Array.Empty<string>());
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<Executor, L1011FoStateEvaluator>(state, executor,
            new List<ChecklistGroup<Executor, L1011FoStateEvaluator>>());
        var flows = new FlowManager<Executor, L1011FoStateEvaluator>(state, executor, checklist, speech);
        var started = new List<string>();
        var completed = new List<string>();
        var skipped = new List<string>();
        flows.StepStarted += (_, step, _) =>
        {
            lock (started) started.Add(step.Id);
            onStarted?.Invoke(step.Id);
            if (step.Id == cancelWhenStarted) _ = Task.Delay(300).ContinueWith(_ => flows.Cancel());
        };
        flows.StepCompleted += (_, step, _) => { lock (completed) completed.Add(step.Id); };
        flows.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        flows.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(20);
        bool finished = !flows.IsRunning;
        flows.Cancel();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(20))
            await Task.Delay(20);

        return new Result(speech.All.ToList(), started, completed, skipped, executor.Sent.ToArray(), finished);
    }
}
