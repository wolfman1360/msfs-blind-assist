using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Models;
using Xunit;

namespace MSFSBlindAssist.Tests.FirstOfficer;

/// <summary>
/// BEHAVIOUR tests of FlowManager's handling of <see cref="FlowStep{TState}.ReminderTextProvider"/>, on a real
/// FlowManager and a real ChecklistManager: a Captain step whose provider has a text at dispatch speaks that text
/// in place of its <see cref="FlowStep{TState}.ReminderText"/> (the TriStar's stabilizer trim reminder names the
/// tablet's value); a provider with no text (null, blank, or one that throws) speaks the static text. A Captain
/// step with no provider, every aircraft's until now, speaks exactly as before: its ReminderText, else its Label.
/// Harness as FlowManagerTargetProviderTests (one step per flow, so no inter-step pause).
/// </summary>
public class FlowManagerReminderProviderTests
{
    private const string Static = "Set the stabilizer trim for take-off";
    private const string Dynamic = "Set stabilizer trim for take-off: 2.38 units";

    private sealed class FakeState : IFoStateEvaluator
    {
        public volatile string? Text;
        public bool IsAvailable => true;
        public double GetValue(string field) => double.NaN;
        public bool IsOn(string field) => false;
        public bool IsPosition(string field, int position) => false;
        public void SetTakeoffFlaps(int flaps) { }
        public void SetEngineN2(double eng1N2, double eng2N2) { }
        public void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt) { }
    }

    private sealed class FakeExecutor : IFoActionExecutor
    {
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;
        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            Sent.Enqueue(step.EventName ?? "");
            return Task.FromResult(true);
        }
        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    private sealed class Harness
    {
        public FakeState State { get; } = new();
        public FakeExecutor Executor { get; } = new();
        public GatedSpeechCapture Speech { get; } = new();
        public ChecklistManager<FakeExecutor, FakeState> Checklist { get; }
        public FlowManager<FakeExecutor, FakeState> Flows { get; }
        public List<string> Reminders { get; } = new();

        public Harness()
        {
            var group = new ChecklistGroup<FakeExecutor, FakeState> { Id = "G", Name = "G", Items = new() };
            Checklist = new ChecklistManager<FakeExecutor, FakeState>(State, Executor, new() { group });
            Flows = new FlowManager<FakeExecutor, FakeState>(State, Executor, Checklist, Speech);
            Flows.CaptainReminderRequired += text => { lock (Reminders) Reminders.Add(text); };
        }

        public async Task RunAsync(params FlowStep<FakeState>[] steps)
        {
            Flows.StartFlow(new FlowDefinition<FakeState> { Id = "TEST", Name = "Test", Steps = steps.ToList() });
            var sw = Stopwatch.StartNew();
            while (Flows.IsRunning)
            {
                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), "flow did not finish");
                await Task.Delay(20);
            }
        }

        /// <summary>The flow's speech between its start and its end.</summary>
        public string[] Spoken => Speech.All.ToArray();
    }

    private static FlowStep<FakeState> Captain(string? reminderText, Func<FakeState, string?>? provider = null) => new()
    {
        Id = "CPT",
        Label = Static,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText,
        ReminderTextProvider = provider,
    };

    private static string[] Expected(string text) =>
        new[] { "Test flow started", $"Captain action required: {text}", "Test flow complete" };

    [Fact]
    public async Task A_provider_with_a_text_is_spoken_in_place_of_the_static_text()
    {
        var h = new Harness();
        h.State.Text = Dynamic;

        await h.RunAsync(Captain(Static, s => s.Text));

        Assert.Equal(Expected(Dynamic), h.Spoken);
        Assert.Equal(new[] { Dynamic }, h.Reminders);
        Assert.Empty(h.Executor.Sent);
    }

    [Fact]
    public async Task A_provider_returning_null_falls_back_to_the_reminder_text()
    {
        var h = new Harness();

        await h.RunAsync(Captain(Static, s => s.Text));

        Assert.Equal(Expected(Static), h.Spoken);
        Assert.Equal(new[] { Static }, h.Reminders);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_provider_returning_blank_falls_back_to_the_reminder_text(string blank)
    {
        var h = new Harness();
        h.State.Text = blank;

        await h.RunAsync(Captain(Static, s => s.Text));

        Assert.Equal(Expected(Static), h.Spoken);
    }

    /// <summary>A reminder is the pilot's only word of the action: a provider that throws must never cost it.</summary>
    [Fact]
    public async Task A_provider_that_throws_falls_back_to_the_reminder_text()
    {
        var h = new Harness();

        await h.RunAsync(Captain(Static, _ => throw new InvalidOperationException("unread")));

        Assert.Equal(Expected(Static), h.Spoken);
        Assert.Equal(new[] { Static }, h.Reminders);
    }

    /// <summary>Every aircraft's Captain steps until now: no provider, the ReminderText, else the Label.</summary>
    [Fact]
    public async Task With_no_provider_the_reminder_speaks_exactly_as_before()
    {
        var withText = new Harness();
        withText.State.Text = Dynamic;   // read by nothing: no provider
        await withText.RunAsync(Captain("Check the flight controls"));
        Assert.Equal(Expected("Check the flight controls"), withText.Spoken);
        Assert.Equal(new[] { "Check the flight controls" }, withText.Reminders);

        var labelOnly = new Harness();
        await labelOnly.RunAsync(Captain(reminderText: null));
        Assert.Equal(Expected(Static), labelOnly.Spoken);
    }

    /// <summary>The provider is read at dispatch, on every run: a value exported between two runs is spoken by the
    /// second.</summary>
    [Fact]
    public async Task The_provider_is_read_on_every_run()
    {
        var h = new Harness();
        var step = Captain(Static, s => s.Text);

        await h.RunAsync(step);
        h.State.Text = Dynamic;
        await h.RunAsync(step);

        Assert.Equal(Expected(Static).Concat(Expected(Dynamic)), h.Spoken);
        Assert.Equal(Static, step.ReminderText);
    }
}
