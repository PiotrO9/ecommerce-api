using System.Security.Cryptography;
using System.Text;

namespace E_commerce_backend.Services.Development;

public static class DemoSeedAccess
{
    public static bool IsAuthorized(string? suppliedKey, string configuredKey)
    {
        if (string.IsNullOrEmpty(suppliedKey) || string.IsNullOrEmpty(configuredKey))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configuredKey));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash);
    }
}
