using System.Text;

namespace Cccc.Store.Tests;

/// <summary>
/// The screens paired to show the building. A screen proves itself with a token from its cookie, and the
/// store keeps only the token's hash, so a copy of the store (a backup, say) lets nobody pose as one.
/// Unpairing stops a screen at once.
/// </summary>
public class ScreenDirectoryTests
{
    [Fact]
    public void APairedScreenIsFoundByItsTokenAndTheStoreKeepsOnlyTheTokensHash()
    {
        using var s = new Scratch();
        var store = s.Open();
        var screens = new ScreenDirectory(store);
        var (screen, token) = screens.Pair("Corridor", null, s.Now);
        Assert.True(token.Length >= 40, $"a token of {token.Length} characters");
        var found = screens.FindByToken(token);
        Assert.Equal((screen.Id, "Corridor", (Guid?)null), (found?.Id, found?.Name, found?.RoomId));

        // Nowhere in the store's files is the token itself.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var files = Directory.GetFiles(s.Folder);
        Assert.NotEmpty(files);
        foreach (var f in files)
            Assert.DoesNotContain(token, File.ReadAllText(f, Encoding.Latin1));

        // What a copy of the store does hold is not itself a way in, and nor is anything near the token.
        Assert.Null(screens.FindByToken(ScreenDirectory.Hash(token)));
        foreach (var wrong in new[] { null, "", "   ", token + "x", token[..^1] })
            Assert.Null(screens.FindByToken(wrong));
    }

    [Fact]
    public void ChangingAScreenKeepsItsTokenAndUnpairingStopsItAtOnce()
    {
        using var s = new Scratch();
        var screens = new ScreenDirectory(s.Open());
        var (corridor, token) = screens.Pair("Corridor", null, s.Now);
        var (canteen, other) = screens.Pair("Canteen", null, s.Now);
        var room = Guid.NewGuid();

        screens.Change(corridor.Id, "  Studio A door ", room);
        var changed = screens.FindByToken(token);
        Assert.Equal(("Studio A door", (Guid?)room), (changed?.Name, changed?.RoomId));
        screens.Change(corridor.Id, "Corridor", null);
        Assert.Null(screens.FindByToken(token)!.RoomId);

        Assert.True(screens.Unpair(corridor.Id));
        Assert.Null(screens.FindByToken(token));
        Assert.False(screens.Unpair(corridor.Id));
        Assert.Throws<ScreenRefused>(() => screens.Change(corridor.Id, "Corridor", null));
        // The other screen carries on.
        Assert.Equal(canteen.Id, screens.FindByToken(other)?.Id);
        Assert.Equal(["Canteen"], screens.All().Select(x => x.Name));
    }

    [Fact]
    public void AScreenNeedsAShortNameAndScreensAreListedInDanishOrder()
    {
        using var s = new Scratch();
        var screens = new ScreenDirectory(s.Open());
        Assert.Throws<ScreenRefused>(() => screens.Pair(" \t ", null, s.Now));
        Assert.Throws<ScreenRefused>(() => screens.Pair(new string('x', ScreenDirectory.MaxName + 1), null, s.Now));
        Assert.Empty(screens.All());
        Assert.Equal("Corridor", screens.Pair("Cor\u0007ridor\n", null, s.Now).Screen.Name);
        screens.Pair("Østre gang", null, s.Now);
        screens.Pair("Aarhus-rummet", null, s.Now);
        screens.Pair("Zinkgangen", null, s.Now);
        // Æ Ø Å after Z, and Aa read as Å.
        Assert.Equal(["Corridor", "Zinkgangen", "Østre gang", "Aarhus-rummet"], screens.All().Select(x => x.Name));
    }

    [Fact]
    public void ALookIsWrittenDownAtMostEveryFiveMinutes()
    {
        using var s = new Scratch();
        var screens = new ScreenDirectory(s.Open());
        var id = screens.Pair("Corridor", null, s.Now).Screen.Id;
        Assert.Null(screens.Find(id)!.Seen);
        screens.Saw(screens.Find(id)!, s.Now);
        Assert.Equal(s.Now, screens.Find(id)!.Seen);
        screens.Saw(screens.Find(id)!, s.Now.AddMinutes(4));
        Assert.Equal(s.Now, screens.Find(id)!.Seen);
        screens.Saw(screens.Find(id)!, s.Now + ScreenDirectory.SeenEvery);
        Assert.Equal(s.Now + ScreenDirectory.SeenEvery, screens.Find(id)!.Seen);
    }

    [Fact]
    public void NoMoreThanFiftyScreensArePairedAtOnce()
    {
        using var s = new Scratch();
        var screens = new ScreenDirectory(s.Open());
        var first = screens.Pair("Screen 0", null, s.Now).Screen;
        for (int i = 1; i < ScreenDirectory.MaxScreens; i++) screens.Pair($"Screen {i}", null, s.Now);
        Assert.Throws<ScreenRefused>(() => screens.Pair("One more", null, s.Now));
        screens.Unpair(first.Id);
        screens.Pair("One more", null, s.Now);
        Assert.Equal(ScreenDirectory.MaxScreens, screens.All().Count);
    }
}
