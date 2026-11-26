using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Felix.Core.Accounts;

public sealed record Account(string Username, string Uuid, string AccessToken);

/// <summary>
/// Compte « pseudo seul » (serveur en online-mode=false). La connexion Microsoft viendra en dernier :
/// il suffira d'ajouter un autre fournisseur de comptes qui renvoie un Account.
/// </summary>
public static class OfflineAccount
{
    private static readonly Regex ValidName = new("^[A-Za-z0-9_]{3,16}$");

    public static bool IsValidUsername(string? username) => username is not null && ValidName.IsMatch(username.Trim());

    public static Account Create(string username)
    {
        username = username.Trim();
        if (!IsValidUsername(username))
            throw new ArgumentException("Pseudo invalide : 3 à 16 caractères (lettres, chiffres, _).");

        // Même UUID que celui calculé par Minecraft pour un joueur hors ligne.
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var uuid = $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
        return new Account(username, uuid, "0");
    }
}
