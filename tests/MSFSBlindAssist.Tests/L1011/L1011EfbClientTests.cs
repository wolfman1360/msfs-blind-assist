using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The TriStar's Paper Flight Bag client: one Coherent view per seat (live 2026-10-03:
/// "VCockpit10 - iniEFBL1011_1" for the captain, "VCockpit17 - iniEFBL1011_2" for the first officer), and
/// every TriStar reader script copied beside the app, which the build only does for a script that has its
/// own entry in MSFSBlindAssist.csproj. The test output receives the same copies through the project
/// reference, so a missing entry fails here.</summary>
public class L1011EfbClientTests
{
    [Fact]
    public void Each_seat_reads_its_own_tablet()
    {
        Assert.Equal("iniEFBL1011_1", CoherentPmdgEfbClient.L1011ViewTitle(firstOfficer: false));
        Assert.Equal("iniEFBL1011_2", CoherentPmdgEfbClient.L1011ViewTitle(firstOfficer: true));
        Assert.Equal("coherent-l1011-efb-agent.js", CoherentPmdgEfbClient.L1011AgentFile);
    }

    [Theory]
    [InlineData("coherent-l1011-afcs-agent.js")]
    [InlineData("coherent-l1011-ins-agent.js")]
    [InlineData("coherent-l1011-efb-agent.js")]
    public void Every_tristar_reader_is_copied_beside_the_app(string file)
    {
        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", file)),
            $"{file} is not copied: give it its own <None Update> entry in MSFSBlindAssist.csproj");
    }
}
