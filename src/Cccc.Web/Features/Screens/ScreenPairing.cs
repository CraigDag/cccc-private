using System.Security.Cryptography;
using System.Text;
using Cccc.Store;

namespace Cccc.Web.Features.Screens;

/// <summary>
/// Pairing a screen: the screen shows a short code, an administrator types it in Administration ▸ Screens,
/// and the screen collects its token. Nobody types a passphrase on a TV, and nothing secret is shown on
/// it: the code only says which waiting screen the administrator means, and only the browser that asked
/// for the code holds the claim that collects the token. Waiting screens live in memory for ten minutes;
/// a restart simply shows a new code.
/// </summary>
public sealed class ScreenPairing(ScreenDirectory screens)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>How long a paired screen has to collect its token: it asks every few seconds.</summary>
    public static readonly TimeSpan CollectWithin = TimeSpan.FromMinutes(2);
    public const int MaxWaiting = 64;
    public const int CodeLength = 6;

    /// <summary>Letters and digits nobody misreads across a corridor: no 0/O, 1/I/L, 2/Z, 5/S, 8/B, 6/G.</summary>
    public const string Alphabet = "ACDEFHJKMNPRTUVWXY34679";

    private sealed class Waiting
    {
        public required string Code { get; init; }
        public required string ClaimHash { get; init; }
        public required DateTimeOffset Until { get; set; }
        public string? Token { get; set; }
    }

    public enum Outcome { Pending, Paired, Unknown }

    private readonly Lock _gate = new();
    private readonly List<Waiting> _waiting = [];

    /// <summary>A new code for a screen to show, and the claim it keeps to itself. Null when too many are waiting.</summary>
    public (string Code, string Claim)? Start(DateTimeOffset now)
    {
        lock (_gate)
        {
            _waiting.RemoveAll(w => w.Until <= now);
            if (_waiting.Count >= MaxWaiting) return null;
            string code;
            do code = new string(Enumerable.Range(0, CodeLength).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
            while (_waiting.Any(w => w.Code == code));
            var claim = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _waiting.Add(new Waiting { Code = code, ClaimHash = Hash(claim), Until = now + Lifetime });
            return (code, claim);
        }
    }

    /// <summary>
    /// Pairs the screen showing this code, as typed (any case, spaces and dashes ignored). Refuses a code
    /// that is not waiting or has run out, or one already used.
    /// </summary>
    public Screen Approve(string typed, string name, Guid? roomId, DateTimeOffset now)
    {
        var code = Normalise(typed);
        lock (_gate)
        {
            _waiting.RemoveAll(w => w.Until <= now);
            var w = _waiting.FirstOrDefault(x => x.Code == code && x.Token is null)
                ?? throw new ScreenRefused("No screen is showing that code. Codes last ten minutes: look at the screen for the one it shows now.");
            var (screen, token) = screens.Pair(name, roomId, now);
            w.Token = token;
            w.Until = now + CollectWithin;
            return screen;
        }
    }

    /// <summary>What a waiting screen finds when it asks: still waiting, paired (with its token, once), or unknown.</summary>
    public (Outcome Outcome, string? Token) Collect(string? claim, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(claim) || claim.Length > 200) return (Outcome.Unknown, null);
        var hash = Hash(claim.Trim());
        lock (_gate)
        {
            _waiting.RemoveAll(w => w.Until <= now);
            var w = _waiting.FirstOrDefault(x => x.ClaimHash == hash);
            if (w is null) return (Outcome.Unknown, null);
            if (w.Token is not { } token) return (Outcome.Pending, null);
            _waiting.Remove(w);
            return (Outcome.Paired, token);
        }
    }

    public static string Normalise(string? typed) =>
        new string((typed ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>"K7Q 4MX": two halves, easier to read from across a room and to type.</summary>
    public static string Show(string code) => code.Length == CodeLength ? code[..3] + " " + code[3..] : code;

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
