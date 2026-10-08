using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KeeDroidSign.Core.Tests.Support
{
    /// <summary>Asserts that secret values never appear in messages, results or request bodies.</summary>
    internal static class SecretLeakScanner
    {
        public static void AssertNoLeak(IEnumerable<string> secrets, params string[] texts)
        {
            foreach (string secret in secrets.Where(s => s != null && s.Length >= 4))
            {
                foreach (string text in texts.Where(t => t != null))
                {
                    Assert.False(text.Contains(secret),
                        $"A secret value of length {secret.Length} leaked into a text of length {text.Length}.");
                }
            }
        }

        public static string[] Collect(Exception ex)
        {
            var texts = new List<string>();
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                texts.Add(e.Message);
                texts.Add(e.ToString());
            }
            return texts.ToArray();
        }
    }
}
