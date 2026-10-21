using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// FO-13 on the TriStar: the gear read-back lines and the read-only gear checks read the lever AND
/// the three stock legs (<see cref="L1011GearConfirmation"/>), the MD-11's shape, never the lever
/// alone. UP is the lever above 75 with every leg below 5 percent, DOWN the lever below 25 with every
/// leg above 95 percent. NaN while any input is unread.
/// </summary>
public class L1011GearConfirmationTests
{
    private const string Left = "L1011_RO_GEAR_LEFT";
    private const string Nose = "L1011_RO_GEAR_NOSE";
    private const string Right = "L1011_RO_GEAR_RIGHT";

    private static Func<string, double> Read(double lever, double left, double nose, double right) => key => key switch
    {
        L1011Levers.GearLeverKey => lever,
        Left => left,
        Nose => nose,
        Right => right,
        _ => double.NaN,
    };

    [Fact]
    public void Gear_confirmation_needs_the_lever_and_every_leg()
    {
        // Up: the lever up and every leg in.
        Assert.Equal(1.0, L1011GearConfirmation.UpValue(Read(100, 0, 0, 0)));
        Assert.Equal(0.0, L1011GearConfirmation.UpValue(Read(100, 50, 0, 0)));     // a leg in transit
        Assert.Equal(0.0, L1011GearConfirmation.UpValue(Read(100, 0, 50, 0)));
        Assert.Equal(0.0, L1011GearConfirmation.UpValue(Read(100, 0, 0, 50)));
        Assert.Equal(0.0, L1011GearConfirmation.UpValue(Read(50, 0, 0, 0)));      // the lever at off
        Assert.True(double.IsNaN(L1011GearConfirmation.UpValue(Read(100, 0, double.NaN, 0))));

        // Down: the lever down and every leg out.
        Assert.Equal(1.0, L1011GearConfirmation.DownValue(Read(0, 100, 100, 100)));
        Assert.Equal(1.0, L1011GearConfirmation.DownValue(Read(0, 96, 100, 100)));
        Assert.Equal(0.0, L1011GearConfirmation.DownValue(Read(0, 94, 100, 100)));
        Assert.Equal(0.0, L1011GearConfirmation.DownValue(Read(50, 100, 100, 100))); // the lever at off
    }

    [Theory]
    [InlineData(75.0, 0)]     // the lever must be ABOVE 75 for up
    [InlineData(75.1, 1)]
    public void Up_needs_the_lever_above_seventy_five(double lever, double want)
        => Assert.Equal(want, L1011GearConfirmation.UpValue(Read(lever, 0, 0, 0)));

    [Theory]
    [InlineData(25.0, 0)]     // the lever must be BELOW 25 for down
    [InlineData(24.9, 1)]
    public void Down_needs_the_lever_below_twenty_five(double lever, double want)
        => Assert.Equal(want, L1011GearConfirmation.DownValue(Read(lever, 100, 100, 100)));

    [Theory]
    [InlineData(4.9, 1)]
    [InlineData(5.0, 0)]
    public void Up_needs_every_leg_below_five_percent(double leg, double want)
        => Assert.Equal(want, L1011GearConfirmation.UpValue(Read(100, leg, leg, leg)));

    [Theory]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(100, double.NaN, 0, 0)]
    [InlineData(100, 0, double.NaN, 0)]
    [InlineData(100, 0, 0, double.NaN)]
    public void Any_unread_input_is_unknown(double lever, double left, double nose, double right)
    {
        Assert.True(double.IsNaN(L1011GearConfirmation.UpValue(Read(lever, left, nose, right))));
        Assert.True(double.IsNaN(L1011GearConfirmation.DownValue(Read(lever, left, nose, right))));
    }

    [Fact]
    public void The_fields_and_inputs_are_pinned()
    {
        Assert.Equal("FO_GEAR_UP_CONFIRMED", L1011GearConfirmation.UpField);
        Assert.Equal("FO_GEAR_DOWN_CONFIRMED", L1011GearConfirmation.DownField);
        Assert.Equal(new[] { L1011Levers.GearLeverKey, Left, Nose, Right }, L1011GearConfirmation.Inputs);
    }

    [Fact]
    public void The_evaluator_publishes_both_fields()
    {
        Assert.Equal(1.0, L1011FoStateEvaluator.Compute(L1011GearConfirmation.UpField, Read(100, 0, 0, 0)));
        Assert.Equal(1.0, L1011FoStateEvaluator.Compute(L1011GearConfirmation.DownField, Read(0, 100, 100, 100)));
        Assert.True(double.IsNaN(L1011FoStateEvaluator.Compute(L1011GearConfirmation.DownField, Read(0, 100, double.NaN, 100))));
        Assert.Contains(L1011GearConfirmation.UpField, L1011FoStateEvaluator.SyntheticKeys);
        Assert.Contains(L1011GearConfirmation.DownField, L1011FoStateEvaluator.SyntheticKeys);
    }

    /// <summary>The legs are the stock simvars the gear readouts read, on request, and the window polls them.</summary>
    [Fact]
    public void The_legs_are_stock_readouts_polled_by_the_window()
    {
        var vars = new IniL1011Definition().GetVariables();
        string[] names = { "GEAR LEFT POSITION", "GEAR CENTER POSITION", "GEAR RIGHT POSITION" };
        string[] keys = { Left, Nose, Right };
        for (int i = 0; i < 3; i++)
        {
            var def = vars[keys[i]];
            Assert.Equal(names[i], def.Name);
            Assert.Equal(SimVarType.SimVar, def.Type);
            Assert.Equal("Percent", def.Units);
            Assert.Equal(UpdateFrequency.OnRequest, def.UpdateFrequency);
        }
        var polled = new L1011FoStateEvaluator().OnRequestPollFields;
        Assert.All(keys, k => Assert.Contains(k, polled));
    }
}
