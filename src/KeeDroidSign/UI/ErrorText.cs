using System;
using KeeDroidSign.Core;
using KeeDroidSign.Properties;

namespace KeeDroidSign.UI
{
    /// <summary>Turns exceptions into text that is safe to show (no secret values).</summary>
    internal static class ErrorText
    {
        /// <summary>
        /// Core and plugin exceptions carry fixed English messages without secrets and are shown as
        /// is; anything else shows only its type name.
        /// </summary>
        public static string For(Exception ex)
        {
            if (ex is KeeDroidSignException || ex is ArgumentException || ex is InvalidOperationException)
                return ex.Message;
            return string.Format(Strings.ErrorUnexpected, ex.GetType().Name);
        }
    }
}
