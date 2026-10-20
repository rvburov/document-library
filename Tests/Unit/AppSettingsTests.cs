using PdfLibrary.Utils;

namespace PdfLibrary.Tests.Unit;

/// <summary>
/// Настройки (<see cref="AppSettings"/>): стандарт — в коде, в базе только отличия; мусор в базе
/// читается как стандарт.
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    private sealed class Store : ISettingsStore
    {
        public readonly Dictionary<string, string> Values = [];
        public string? Get(string key) => Values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => Values[key] = value;
        public void Remove(string key) => Values.Remove(key);
    }

    private readonly Store _store = new();

    public AppSettingsTests() => AppSettings.AttachStore(_store);

    public void Dispose() => AppSettings.AttachStore(null);

    [Fact]
    public void Defaults_match_the_mockup()
    {
        Assert.False(AppSettings.Autostart);
        Assert.False(AppSettings.Background);
        Assert.False(AppSettings.NotifyNew);
        Assert.False(AppSettings.NotifyRead);
        Assert.True(AppSettings.Watch);
        Assert.True(AppSettings.ReadAuto);
        Assert.Equal(200, AppSettings.MaxFileMb);
        Assert.Equal(CardSize.Medium, AppSettings.CardSize);
        Assert.True(AppSettings.CardThumbnail);
        Assert.True(AppSettings.FeedMissing);
        Assert.True(AppSettings.FeedScans);
        Assert.Equal(AppTheme.Light, AppSettings.Theme);
        Assert.Equal(1.0, AppSettings.Scale);
        Assert.Equal(0, AppSettings.FontDelta);
        Assert.True(AppSettings.AutoUpdate);
        Assert.Equal("rel", AppSettings.Sort);
        Assert.Empty(_store.Values);
    }

    [Fact]
    public void Only_differences_are_stored()
    {
        AppSettings.CardSize = CardSize.Large;
        AppSettings.Theme = AppTheme.Dark;
        AppSettings.MaxFileMb = 50;
        Assert.Equal(3, _store.Values.Count);

        AppSettings.CardSize = CardSize.Medium;
        AppSettings.Theme = AppTheme.Light;
        AppSettings.MaxFileMb = 200;
        Assert.Empty(_store.Values);
    }

    [Fact]
    public void Garbage_reads_as_default()
    {
        _store.Values[AppSettings.KeyScale] = "7.5";
        _store.Values[AppSettings.KeyMaxFileMb] = "333";
        _store.Values[AppSettings.KeyFontDelta] = "abc";
        _store.Values[AppSettings.KeyWatch] = "maybe";

        Assert.Equal(1.0, AppSettings.Scale);
        Assert.Equal(200, AppSettings.MaxFileMb);
        Assert.Equal(0, AppSettings.FontDelta);
        Assert.True(AppSettings.Watch);
    }

    [Fact]
    public void Change_event_fires_once_per_real_change()
    {
        var changed = new List<string>();
        void Handler(string key) => changed.Add(key);
        AppSettings.Changed += Handler;
        try
        {
            AppSettings.Watch = false;
            AppSettings.Watch = false;
            AppSettings.Watch = true;
        }
        finally { AppSettings.Changed -= Handler; }

        Assert.Equal([AppSettings.KeyWatch, AppSettings.KeyWatch], changed);
    }

    [Fact]
    public void Last_document_round_trips()
    {
        AppSettings.LastDocument = ("/docs/a.pdf", 12);
        Assert.Equal(("/docs/a.pdf", 12), AppSettings.LastDocument);
        AppSettings.LastDocument = ("", 0);
        Assert.Empty(_store.Values);
    }
}
