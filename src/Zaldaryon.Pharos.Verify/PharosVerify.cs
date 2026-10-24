using System.Text;
using VerifyTests;

namespace Zaldaryon.Pharos.Verify;

public static class PharosVerify
{
    public static void Initialize()
    {
        VerifierSettings.AddScrubber((StringBuilder sb) => { });
    }
}
