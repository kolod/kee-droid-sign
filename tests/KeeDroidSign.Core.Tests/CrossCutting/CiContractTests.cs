using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KeeDroidSign.Core.Keystore;
using KeeDroidSign.Core.Passwords;
using KeeDroidSign.Core.Tests.Keystore;
using Xunit;

namespace KeeDroidSign.Core.Tests.CrossCutting
{
    /// <summary>
    /// Checks the values against the reference GitHub Actions workflow from the spec:
    /// <c>echo "$B64" | base64 -d</c> and an unquoted heredoc writing keystore.properties.
    /// </summary>
    public class CiContractTests
    {
        [Fact]
        public void Passwords_SurviveUnquotedHeredocAndPropertiesParsing()
        {
            var generator = new PasswordGenerator();

            for (int i = 0; i < 2000; i++)
            {
                string password = generator.Generate(new PasswordPolicy());

                string heredocLine = ExpandUnquotedHeredoc("storePassword=" + password);
                Assert.Equal("storePassword=" + password, heredocLine);

                Dictionary<string, string> props = ParseProperties(heredocLine);
                Assert.Equal(password, props["storePassword"]);
            }
        }

        [Fact]
        public async Task KeystoreBase64_IsSingleLineStandardBase64()
        {
            SigningKeystore keystore = await new KeystoreGenerator()
                .GenerateAsync(KeystoreGeneratorTests.FastRequest(), CancellationToken.None);

            string base64 = keystore.ToBase64();

            Assert.Matches(new Regex("^[A-Za-z0-9+/]+=*$"), base64);
            Assert.Equal(keystore.Content, Convert.FromBase64String(base64));
        }

        /// <summary>
        /// Bash unquoted heredoc semantics relevant here: '$' starts expansion, '`' starts command
        /// substitution and '\' escapes the next character. Any of them changes the text.
        /// </summary>
        private static string ExpandUnquotedHeredoc(string line)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '$' || c == '`')
                    return "<expanded>";
                if (c == '\\' && i + 1 < line.Length)
                {
                    i++;
                    sb.Append(line[i]);
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>Subset of java.util.Properties line parsing: key=value, '\' escapes, trimmed leading value space.</summary>
        private static Dictionary<string, string> ParseProperties(string line)
        {
            var result = new Dictionary<string, string>();
            int separator = line.IndexOfAny(new[] { '=', ':' });
            string key = line.Substring(0, separator).Trim();
            string raw = line.Substring(separator + 1).TrimStart(' ', '\t', '\f');

            var value = new StringBuilder();
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length)
                {
                    i++;
                    value.Append(raw[i]);
                    continue;
                }
                value.Append(raw[i]);
            }
            result[key] = value.ToString();
            return result;
        }
    }
}
