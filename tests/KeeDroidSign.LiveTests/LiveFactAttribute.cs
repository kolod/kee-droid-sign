using Xunit;

namespace KeeDroidSign.LiveTests
{
    /// <summary>A fact that talks to real GitHub; skipped unless a token is configured.</summary>
    public sealed class LiveFactAttribute : FactAttribute
    {
        public LiveFactAttribute()
        {
            if (!LiveSettings.IsEnabled)
                Skip = "Live test disabled: set " + LiveSettings.TokenVariable + " to run it";
        }
    }
}
