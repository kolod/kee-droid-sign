using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>Certificate owner details (subject of the self-signed certificate).</summary>
    public sealed class DistinguishedName
    {
        public string CommonName { get; set; }
        public string OrganizationalUnit { get; set; }
        public string Organization { get; set; }
        public string Locality { get; set; }
        public string State { get; set; }

        /// <summary>Two-letter ISO 3166 country code; stored upper-case.</summary>
        public string Country { get; set; }

        /// <summary>Throws <see cref="ArgumentException"/> when a field is missing or invalid.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(CommonName))
                throw new ArgumentException("The common name (CN) is required.");

            CheckLength(CommonName, 64, "common name (CN)");
            CheckLength(OrganizationalUnit, 64, "organizational unit (OU)");
            CheckLength(Organization, 64, "organization (O)");
            CheckLength(Locality, 128, "locality (L)");
            CheckLength(State, 128, "state (ST)");

            if (!string.IsNullOrEmpty(Country))
            {
                if (Country.Length != 2 || !Country.All(IsAsciiLetter))
                    throw new ArgumentException("The country (C) must be a two-letter ISO country code.");
            }
        }

        /// <summary>
        /// Builds the subject from ordered OID/value pairs, so user input is never parsed and needs
        /// no escaping. Encoded most general first (C ... CN), as keytool does, so tools display
        /// it as "CN=..., O=..., C=...".
        /// </summary>
        internal X509Name ToX509Name()
        {
            var oids = new List<DerObjectIdentifier>();
            var values = new List<string>();
            foreach (var (oid, _, value) in Attributes().Reverse())
            {
                oids.Add(oid);
                values.Add(value);
            }
            return new X509Name(oids, values);
        }

        /// <summary>RFC 4514 display string, e.g. <c>CN=Jane Doe, O=Acme\, Inc., C=UA</c>.</summary>
        public string ToRfc4514()
        {
            return string.Join(", ", Attributes().Select(a => a.Label + "=" + Escape(a.Value)));
        }

        public override string ToString() => ToRfc4514();

        private IEnumerable<(DerObjectIdentifier Oid, string Label, string Value)> Attributes()
        {
            if (!string.IsNullOrEmpty(CommonName)) yield return (X509Name.CN, "CN", CommonName);
            if (!string.IsNullOrEmpty(OrganizationalUnit)) yield return (X509Name.OU, "OU", OrganizationalUnit);
            if (!string.IsNullOrEmpty(Organization)) yield return (X509Name.O, "O", Organization);
            if (!string.IsNullOrEmpty(Locality)) yield return (X509Name.L, "L", Locality);
            if (!string.IsNullOrEmpty(State)) yield return (X509Name.ST, "ST", State);
            if (!string.IsNullOrEmpty(Country)) yield return (X509Name.C, "C", Country.ToUpperInvariant());
        }

        private static string Escape(string value)
        {
            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool special = c == ',' || c == '+' || c == '"' || c == '\\' || c == '<' || c == '>' || c == ';';
                bool leading = i == 0 && (c == '#' || c == ' ');
                bool trailing = i == value.Length - 1 && c == ' ';
                if (special || leading || trailing)
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static void CheckLength(string value, int max, string field)
        {
            if (value != null && value.Length > max)
                throw new ArgumentException($"The {field} must not be longer than {max} characters.");
        }

        private static bool IsAsciiLetter(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}
