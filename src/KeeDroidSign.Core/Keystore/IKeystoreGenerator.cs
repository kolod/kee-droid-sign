using System.Threading;
using System.Threading.Tasks;

namespace KeeDroidSign.Core.Keystore
{
    /// <summary>Creates Android signing keystores.</summary>
    public interface IKeystoreGenerator
    {
        /// <summary>Generates a keystore in memory; nothing is written to disk.</summary>
        Task<SigningKeystore> GenerateAsync(KeyRequest request, CancellationToken ct);
    }
}
