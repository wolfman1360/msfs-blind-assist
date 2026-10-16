using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// The INS window's automatic waypoint loading button reads its new state back once the reader's scrape
/// reports it (a focused WinForms button's label change is not spoken by a screen reader). Pure: the window
/// arms it after a press that was sent and feeds it every scrape's state. It speaks the new state once when
/// the report reaches the one asked for, and says nothing if it does not within the bound.
/// </summary>
public class L1011AutoBatchReadBackTests
{
    private const long Start = 50_000;

    [Fact]
    public void The_new_state_is_spoken_once_when_the_report_flips_to_it()
    {
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: true, Start, keysBusyMs: 0);
        Assert.Null(readBack.Observe(false, Start + 200));   // the scrape before the aircraft applied it
        Assert.Equal("Automatic waypoint loading on", readBack.Observe(true, Start + 700));
        Assert.Null(readBack.Observe(true, Start + 1_200));  // once only
        Assert.Null(readBack.Observe(true, Start + 1_700));
    }

    [Fact]
    public void Turning_it_off_is_spoken_in_its_own_words()
    {
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: false, Start, keysBusyMs: 0);
        Assert.Null(readBack.Observe(true, Start + 300));
        Assert.Equal("Automatic waypoint loading off", readBack.Observe(false, Start + 800));
    }

    [Fact]
    public void A_report_that_stays_at_the_old_state_says_nothing_then_or_later()
    {
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: true, Start, keysBusyMs: 0);
        for (long t = Start + 500; t <= Start + IniL1011Definition.BatchReadoutTimeoutMs; t += 500)
            Assert.Null(readBack.Observe(false, t));
        Assert.Null(readBack.Observe(false, Start + IniL1011Definition.BatchReadoutTimeoutMs + 500));
        Assert.False(readBack.Pending);
    }

    [Fact]
    public void A_flip_reported_after_the_bound_says_nothing()
    {
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: true, Start, keysBusyMs: 0);
        Assert.Null(readBack.Observe(false, Start + 1_000));
        Assert.Null(readBack.Observe(true, Start + IniL1011Definition.BatchReadoutTimeoutMs + 1));
        Assert.False(readBack.Pending);
        Assert.Null(readBack.Observe(true, Start + IniL1011Definition.BatchReadoutTimeoutMs + 600));
    }

    [Fact]
    public void A_flip_reported_exactly_at_the_bound_is_still_spoken()
    {
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: false, Start, keysBusyMs: 0);
        Assert.Equal("Automatic waypoint loading off",
            readBack.Observe(false, Start + IniL1011Definition.BatchReadoutTimeoutMs));
    }

    [Fact]
    public void The_bound_starts_when_the_chained_keys_have_run()
    {
        // The press waits behind keys already chained, so the aircraft cannot have applied it sooner.
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: true, Start, keysBusyMs: 1_500);
        Assert.Equal("Automatic waypoint loading on",
            readBack.Observe(true, Start + 1_500 + IniL1011Definition.BatchReadoutTimeoutMs));

        readBack.Expect(on: true, Start, keysBusyMs: 1_500);
        Assert.Null(readBack.Observe(true, Start + 1_500 + IniL1011Definition.BatchReadoutTimeoutMs + 1));
    }

    [Fact]
    public void A_report_moving_away_from_the_state_asked_for_says_nothing()
    {
        // On was asked for; the report is off, or is lost, however often it changes: nothing is spoken
        // and the wait goes on until the report reaches on or the bound passes.
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: true, Start, keysBusyMs: 0);
        Assert.Null(readBack.Observe(false, Start + 200));
        Assert.Null(readBack.Observe(null, Start + 700));
        Assert.Null(readBack.Observe(false, Start + 1_200));
        Assert.True(readBack.Pending);
        Assert.Equal("Automatic waypoint loading on", readBack.Observe(true, Start + 1_700));
    }

    [Fact]
    public void A_second_press_replaces_the_first_and_only_its_state_is_spoken()
    {
        var readBack = new L1011AutoBatchReadBack();
        readBack.Expect(on: true, Start, keysBusyMs: 0);
        readBack.Expect(on: false, Start + 400, keysBusyMs: 0);
        Assert.Null(readBack.Observe(true, Start + 600));
        Assert.Equal("Automatic waypoint loading off", readBack.Observe(false, Start + 1_100));
        Assert.Null(readBack.Observe(false, Start + 1_600));
    }

    [Fact]
    public void Nothing_is_spoken_before_a_press_or_after_a_reset()
    {
        var readBack = new L1011AutoBatchReadBack();
        Assert.Null(readBack.Observe(true, Start));
        Assert.False(readBack.Pending);

        readBack.Expect(on: true, Start, keysBusyMs: 0);
        readBack.Reset();   // the unit changed, or the window was hidden
        Assert.False(readBack.Pending);
        Assert.Null(readBack.Observe(true, Start + 500));
    }

    [Fact]
    public void The_words_are_the_buttons_state_in_the_windows_phrasing()
    {
        Assert.Equal("Automatic waypoint loading on", L1011AutoBatchReadBack.Words(true));
        Assert.Equal("Automatic waypoint loading off", L1011AutoBatchReadBack.Words(false));
    }
}
