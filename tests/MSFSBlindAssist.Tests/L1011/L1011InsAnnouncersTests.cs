using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>What the INS and the PMS say on their own. Alignment status values come from L1011_INS.js
/// (90 not aligned, falling while aligning, 2 aligned with the MSU in ALIGN, 1 navigating); the PMS phase from
/// L:PMS_FLIGHT_PHASE (1 ground, 2-3 climb, 4 cruise, 5 descent, 7 ground after flight, 9 go-around).</summary>
public class L1011InsAnnouncersTests
{
    private const double Align = 2, Nav = 3;

    [Fact]
    public void Navigation_starting_by_itself_is_spoken_once()
    {
        var a = new L1011InsAlignmentAnnouncer();
        Assert.Null(a.Observe(1, 90, Nav));            // baseline
        Assert.Null(a.Observe(1, 47.3, Nav));          // aligning: the INS window shows it
        Assert.Equal("INS 1 navigating", a.Observe(1, 1, Nav));
        Assert.Null(a.Observe(1, 1, Nav));
    }

    [Fact]
    public void A_status_of_two_with_the_msu_in_nav_is_still_aligning()
    {
        var a = new L1011InsAlignmentAnnouncer();
        a.Observe(2, 30, Nav);
        Assert.Null(a.Observe(2, 2, Nav));
        Assert.Equal("INS 2 navigating", a.Observe(2, 1, Nav));
    }

    [Fact]
    public void Turning_the_knob_from_align_to_nav_is_the_pilots_own_action()
    {
        // READY NAV lit at status 2 in ALIGN (its own lamp announcement); the pilot then turns the MSU to NAV.
        var a = new L1011InsAlignmentAnnouncer();
        a.Observe(3, 40, Align);
        Assert.Null(a.Observe(3, 2, Align));
        Assert.Null(a.Observe(3, 1, Nav));
    }

    [Fact]
    public void Turning_the_knob_to_nav_while_the_status_still_rests_at_two_stays_silent()
    {
        // Live (2026-10-16): INS 1 aligned with its MSU at ALIGN (status 2, READY NAV lit); the pilot turned the
        // MSU to NAV and the status stayed at 2 for one more update. The announcer re-read that 2 with the MSU at
        // NAV as "aligning", so the 1 that followed spoke "INS 1 navigating" over the pilot's own turn.
        var a = new L1011InsAlignmentAnnouncer();
        a.Observe(1, 40, Align);
        Assert.Null(a.Observe(1, 2, Align));
        Assert.Null(a.Observe(1, 2, Nav));   // the knob is at NAV, the status still 2: aligned, not aligning
        Assert.Null(a.Observe(1, 1, Nav));
        Assert.Null(a.Observe(1, 1, Nav));
    }

    [Fact]
    public void A_seeded_aligned_unit_stays_aligned_when_the_msu_goes_to_nav()
    {
        var a = new L1011InsAlignmentAnnouncer();
        Assert.True(a.Seed(1, 2, Align));
        Assert.Null(a.Observe(1, 2, Nav));
        Assert.Null(a.Observe(1, 1, Nav));
    }

    [Fact]
    public void Only_a_new_alignment_makes_an_aligned_unit_able_to_navigate_by_itself_again()
    {
        // A status of 3 or more is a re-alignment: the unit is aligning again, so with the MSU left at NAV the
        // status resting at 2 and then 1 is a unit starting to navigate by itself.
        var a = new L1011InsAlignmentAnnouncer();
        a.Observe(1, 40, Align);
        Assert.Null(a.Observe(1, 2, Align));   // aligned, MSU at ALIGN
        Assert.Null(a.Observe(1, 60, Nav));    // aligning again, MSU at NAV
        Assert.Null(a.Observe(1, 2, Nav));
        Assert.Equal("INS 1 navigating", a.Observe(1, 1, Nav));
    }

    [Fact]
    public void An_aligned_unit_that_goes_to_nav_and_back_to_two_does_not_speak()
    {
        var a = new L1011InsAlignmentAnnouncer();
        a.Observe(2, 40, Align);
        Assert.Null(a.Observe(2, 2, Align));
        Assert.Null(a.Observe(2, 2, Nav));
        Assert.Null(a.Observe(2, 2, Align));   // knob back to ALIGN
        Assert.Null(a.Observe(2, 2, Nav));
        Assert.Null(a.Observe(2, 1, Nav));
    }

    [Fact]
    public void Units_are_independent_and_reset_restores_the_baseline()
    {
        var a = new L1011InsAlignmentAnnouncer();
        a.Observe(1, 90, Nav);
        Assert.Null(a.Observe(2, 1, Nav));             // unit 2's first value is its baseline
        a.Reset();
        Assert.Null(a.Observe(1, 1, Nav));
    }

    [Fact]
    public void An_unknown_status_keeps_the_last_state()
    {
        var a = new L1011InsAlignmentAnnouncer();
        a.Observe(1, 50, Nav);
        Assert.Null(a.Observe(1, 0, Nav));
        Assert.Equal("INS 1 navigating", a.Observe(1, 1, Nav));
    }

    [Fact]
    public void A_seed_is_a_baseline_and_never_overwrites_one()
    {
        var a = new L1011InsAlignmentAnnouncer();
        Assert.True(a.Seed(1, 50, Nav));
        Assert.False(a.Seed(1, 1, Nav));
        Assert.Equal("INS 1 navigating", a.Observe(1, 1, Nav));
    }

    [Fact]
    public void Status_keys_name_their_unit()
    {
        Assert.Equal("INS_2_ALIGNMENT_STATUS", L1011InsAlignmentAnnouncer.StatusKey(2));
        Assert.Equal("ROTARY_INS_MODE_3", L1011InsAlignmentAnnouncer.MsuKey(3));
        Assert.Equal(2, L1011InsAlignmentAnnouncer.UnitOf("INS_2_ALIGNMENT_STATUS"));
        Assert.Null(L1011InsAlignmentAnnouncer.UnitOf("INS_4_ALIGNMENT_STATUS"));
        Assert.Null(L1011InsAlignmentAnnouncer.UnitOf("INS_1_READY"));
    }

    [Fact]
    public void The_pms_phase_follows_its_mode_lights()
    {
        var p = new L1011PmsPhaseAnnouncer();
        Assert.Null(p.Observe(1));                     // baseline: on the ground
        Assert.Equal("PMS climb", p.Observe(2));
        Assert.Null(p.Observe(3));                     // still the climb light
        Assert.Equal("PMS cruise", p.Observe(4));
        Assert.Equal("PMS descent", p.Observe(5));
        Assert.Null(p.Observe(7));                     // landed: the lights go out, nothing to say
        Assert.Null(p.Observe(9));                     // a go-around lights no PMS mode
    }

    [Fact]
    public void The_pms_phase_baseline_and_seed()
    {
        var p = new L1011PmsPhaseAnnouncer();
        Assert.True(p.Seed(4));
        Assert.False(p.Seed(5));
        Assert.Null(p.Observe(4));
        Assert.Equal("PMS descent", p.Observe(5));
        p.Reset();
        Assert.Null(p.Observe(2));
    }
}
