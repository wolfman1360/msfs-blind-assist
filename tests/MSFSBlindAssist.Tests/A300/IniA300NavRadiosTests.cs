using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

public class IniA300NavRadiosTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly List<string> _sent = new();

    public IniA300NavRadiosTests()
    {
        _def = new IniA300Definition { CanLand = _ => true, Send = (_, rpn) => _sent.Add(rpn) };
        _def.Attach(_sim);
    }

    [Fact]
    public void Both_vors_and_the_ils_are_set_in_one_string_and_confirmed()
    {
        _def.SetNavRadios(new NavRadioSettings(113.9, 90, 112.05, 270, 110.3, 135), _sim, _speech);
        Assert.Equal(new[]
        {
            "113 (>L:INI_VOR1_FREQUENCY_MHZ) 90 (>L:INI_VOR1_FREQUENCY_KHZ) 90 (>K:VOR1_SET) "
            + "112 (>L:INI_VOR2_FREQUENCY_MHZ) 5 (>L:INI_VOR2_FREQUENCY_KHZ) 270 (>K:VOR2_SET) "
            + "110 (>L:INI_ILS_FREQUENCY_MHZ) 30 (>L:INI_ILS_FREQUENCY_KHZ) 135 (>L:INI_ils_course)",
        }, _sent);
        Assert.Equal(new[] { "VOR 1 113.90; VOR 1 course 090; VOR 2 112.05; VOR 2 course 270; ILS 110.30; ILS course 135" },
            _speech.All);
    }

    [Fact]
    public void Without_an_ils_only_the_vors_are_set()
    {
        _def.SetNavRadios(new NavRadioSettings(113.9, 90, 112.05, 270), _sim, _speech);
        Assert.DoesNotContain("ILS", _sent.Single());
    }
}
