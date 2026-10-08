using System;
using System.Linq;
using KeeDroidSign.Core.Keystore;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Xunit;

namespace KeeDroidSign.Core.Tests.Keystore
{
    public class DistinguishedNameTests
    {
        [Fact]
        public void Validate_RequiresCommonName()
        {
            Assert.Throws<ArgumentException>(() => new DistinguishedName { CommonName = " " }.Validate());
            Assert.Throws<ArgumentException>(() => new DistinguishedName().Validate());
        }

        [Theory]
        [InlineData("U")]
        [InlineData("UKR")]
        [InlineData("U1")]
        [InlineData("УК")]
        public void Validate_RejectsInvalidCountry(string country)
        {
            var dn = new DistinguishedName { CommonName = "Jane", Country = country };

            Assert.Throws<ArgumentException>(() => dn.Validate());
        }

        [Fact]
        public void Country_IsUpperCased()
        {
            var dn = new DistinguishedName { CommonName = "Jane", Country = "ua" };
            dn.Validate();

            Assert.Equal("UA", ReadValues(dn, X509Name.C).Single());
        }

        [Theory]
        [InlineData(65, 0)]
        [InlineData(0, 65)]
        public void Validate_RejectsTooLongFields(int cnLength, int orgLength)
        {
            var dn = new DistinguishedName
            {
                CommonName = cnLength > 0 ? new string('a', cnLength) : "Jane",
                Organization = orgLength > 0 ? new string('o', orgLength) : null,
            };

            Assert.Throws<ArgumentException>(() => dn.Validate());
        }

        [Fact]
        public void Validate_RejectsTooLongLocality()
        {
            var dn = new DistinguishedName { CommonName = "Jane", Locality = new string('l', 129) };

            Assert.Throws<ArgumentException>(() => dn.Validate());
        }

        [Fact]
        public void SpecialCharactersAndCyrillic_RoundTripExactly()
        {
            var dn = new DistinguishedName
            {
                CommonName = "Олександр \"Sasha\" Doe, Jr.",
                OrganizationalUnit = "R+D; Mobile",
                Organization = "Acme, Inc.",
                Locality = "Київ",
                State = "a=b",
                Country = "UA",
            };
            dn.Validate();

            Assert.Equal(dn.CommonName, ReadValues(dn, X509Name.CN).Single());
            Assert.Equal(dn.OrganizationalUnit, ReadValues(dn, X509Name.OU).Single());
            Assert.Equal(dn.Organization, ReadValues(dn, X509Name.O).Single());
            Assert.Equal(dn.Locality, ReadValues(dn, X509Name.L).Single());
            Assert.Equal(dn.State, ReadValues(dn, X509Name.ST).Single());
            Assert.Equal("UA", ReadValues(dn, X509Name.C).Single());
        }

        [Fact]
        public void EmptyOptionalFields_AreOmitted()
        {
            var dn = new DistinguishedName { CommonName = "Jane", Organization = "" };

            X509Name name = dn.ToX509Name();

            Assert.Single(name.GetOidList());
        }

        [Fact]
        public void ToRfc4514_EscapesSpecialCharacters()
        {
            var dn = new DistinguishedName
            {
                CommonName = "#Jane, \"J\" + <x>; y\\z ",
                Organization = "Acme",
                Country = "UA",
            };

            Assert.Equal("CN=\\#Jane\\, \\\"J\\\" \\+ \\<x\\>\\; y\\\\z\\ , O=Acme, C=UA", dn.ToRfc4514());
        }

        [Fact]
        public void ToRfc4514_EscapesLeadingSpace()
        {
            var dn = new DistinguishedName { CommonName = " Jane" };

            Assert.Equal("CN=\\ Jane", dn.ToRfc4514());
        }

        private static string[] ReadValues(DistinguishedName dn, DerObjectIdentifier oid)
        {
            return dn.ToX509Name().GetValueList(oid).ToArray();
        }
    }
}
