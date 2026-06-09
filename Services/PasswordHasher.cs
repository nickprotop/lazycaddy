// -----------------------------------------------------------------------
// LazyCaddy - in-process bcrypt hashing for Caddy basic-auth accounts. Caddy
// stores only the hash (never plaintext); we compute the $2a$14$... string here
// (cost 14 = Caddy's default), so no `caddy hash-password` shell-out and it works
// for any --url. No I/O.
// -----------------------------------------------------------------------

namespace LazyCaddy.Services;

public static class PasswordHasher
{
    private const int Cost = 14; // Caddy's default bcrypt cost.

    /// <summary>Bcrypt-hash a plaintext password into a Modular-Crypt-Format string.</summary>
    public static string Hash(string plaintext)
        => BCrypt.Net.BCrypt.HashPassword(plaintext, BCrypt.Net.BCrypt.GenerateSalt(Cost));
}
