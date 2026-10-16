using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The INS waypoint list, read from the gauge's own waypoint slots (<c>gpsPoints[1..9]</c>). The first
/// case is the route the EFB's "Send to INS" put into INS 1 live at a gate (2026-10-03): two en-route fixes, SID and
/// STAR left out by the aircraft, slots 3 to 9 empty, batch 1 of 1. The INS showed 37179N / 117186W for slot 1.</summary>
public class L1011InsWaypointsTests
{
    private static L1011InsWaypoint?[] Slots(params L1011InsWaypoint?[] filled)
    {
        var slots = new L1011InsWaypoint?[9];
        for (int i = 0; i < filled.Length; i++)
            slots[i] = filled[i];
        for (int i = filled.Length; i < 9; i++)
            slots[i] = new L1011InsWaypoint("", 0, 0);
        return slots;
    }

    [Fact]
    public void The_live_simbrief_route_in_ins_1()
    {
        var lines = L1011InsWaypoints.Lines(Slots(
            new L1011InsWaypoint("KENNO", 37.2982, -117.3102),
            new L1011InsWaypoint("RUSME", 37.4942, -117.5200)), activeBatch: 1, batchCount: 1);
        Assert.Equal(new[]
        {
            "Waypoint 1, KENNO: N 37 17.9, W 117 18.6",
            "Waypoint 2, RUSME: N 37 29.7, W 117 31.2",
            "Waypoint 3: not set",
            "Waypoint 4: not set",
            "Waypoint 5: not set",
            "Waypoint 6: not set",
            "Waypoint 7: not set",
            "Waypoint 8: not set",
            "Waypoint 9: not set",
            "Route: batch 1 of 1",
        }, lines);
    }

    [Fact]
    public void A_typed_waypoint_has_no_ident_worth_reading()
    {
        // The gauge names a keyed-in waypoint USER<k> (INS.js); the slot number already says that.
        var lines = L1011InsWaypoints.Lines(Slots(new L1011InsWaypoint("USER1", 43.791529, -79.773882)), 0, 0);
        Assert.Equal("Waypoint 1: N 43 47.5, W 079 46.4", lines[0]);
        Assert.Equal(9, lines.Count);   // no route loaded: no batch line
    }

    [Fact]
    public void A_route_lost_with_the_power_is_not_mentioned()
    {
        // Live, 2026-10-03: after INS 1 lost power its slots read USER1..USER9 at 0, 0 while the gauge
        // still held "batch 1 of 1" from the route it had before.
        var slots = Enumerable.Range(1, 9).Select(k => (L1011InsWaypoint?)new L1011InsWaypoint("USER" + k, 0, 0)).ToArray();
        var lines = L1011InsWaypoints.Lines(slots, activeBatch: 1, batchCount: 1);
        Assert.Equal(9, lines.Count);
        Assert.All(lines, l => Assert.EndsWith(": not set", l));
    }

    [Fact]
    public void A_missing_slot_reads_as_not_set()
    {
        var slots = Slots();
        slots[4] = null;
        Assert.Equal("Waypoint 5: not set", L1011InsWaypoints.Lines(slots, 0, 0)[4]);
    }

    [Fact]
    public void Minutes_that_round_to_sixty_carry_into_the_degree()
    {
        Assert.Equal("N 37 00.0, E 151 10.6", L1011InsWaypoints.Position(36.99999, 151.17722));
        Assert.Equal("S 33 56.8, W 180 00.0", L1011InsWaypoints.Position(-33.94611, -179.99999));
    }

    [Fact]
    public void A_comma_decimal_culture_changes_nothing()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("N 37 17.9, W 117 18.6", L1011InsWaypoints.Position(37.2982, -117.3102));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void A_comma_decimal_culture_changes_nothing_in_the_list()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            var lines = L1011InsWaypoints.Lines(Slots(
                new L1011InsWaypoint("KENNO", 37.2982, -117.3102)), activeBatch: 1, batchCount: 1);
            Assert.Equal("Waypoint 1, KENNO: N 37 17.9, W 117 18.6", lines[0]);
            Assert.Equal("Waypoint 2: not set", lines[1]);
            Assert.Equal("Route: batch 1 of 1", lines[9]);
            Assert.Equal(10, lines.Count);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
