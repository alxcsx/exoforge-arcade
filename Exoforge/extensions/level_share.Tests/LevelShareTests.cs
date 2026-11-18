using System.Security.Cryptography;
using System.Text.Json;
using Exoforge.Extensions;
using Exoforge.Plugin.SDK;
using Xunit;

public class LevelShareTests
{
    private sealed class Backend
    {
        public List<JsonElement> Uploads { get; } = new();
        public List<JsonElement> SignedUrls { get; } = new();
    }

    private static Backend Fake(ExoTestContext ctx)
    {
        ExoServices.Reset();
        var backend = new Backend();
        ctx.Actions.Register("file_bucket", "upload_file", payload =>
        {
            var request = JsonSerializer.SerializeToElement(payload);
            backend.Uploads.Add(request);
            var sha = Convert.ToHexString(SHA256.HashData(Convert.FromBase64String(request.GetProperty("content").GetString()!))).ToLowerInvariant();
            return JsonSerializer.SerializeToElement(new { file = new { id = $"fl_{backend.Uploads.Count}", sha256 = sha } });
        });
        ctx.Actions.Register("file_bucket", "signed_url", payload =>
        {
            backend.SignedUrls.Add(JsonSerializer.SerializeToElement(payload));
            return JsonSerializer.SerializeToElement(new { url = "/api/files/signed/x?sig=abc", expires_at = 1234L });
        });
        ctx.Actions.Register("player_data", "get_player", _ => JsonSerializer.SerializeToElement(new { player = new { name = "Ana" } }));
        return backend;
    }

    private static byte[] Png(int width = 32, int height = 18, byte variant = 0)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        foreach (var n in new[] { width, height }) bytes.AddRange(new byte[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n });
        bytes.AddRange(new byte[] { 8, 2, 0, 0, 0, variant });
        return bytes.ToArray();
    }

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes);

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static LevelShareExtension As(ExoTestContext ctx, string playerId)
    {
        ctx.CallerPlayerId = playerId;
        return ctx.Create<LevelShareExtension>();
    }

    private static string PublishLevel(ExoTestContext ctx, string playerId, byte variant = 0, string title = "Spike Alley", string difficulty = "hard")
    {
        var ext = As(ctx, playerId);
        var upload = ext.UploadLevel(B64(Png(variant: variant)));
        ext.RecordClear(upload.Sha256);
        return ext.Publish(title, difficulty, upload.FileId).LevelId;
    }

    private static string Code(Action action) => Assert.Throws<ExoActionException>(action).Code;

    [Fact]
    public void Upload_stores_the_png_through_the_file_bucket_and_returns_its_hash()
    {
        using var ctx = ExoTestHost.Start("level_share");
        var backend = Fake(ctx);
        var png = Png();

        var result = As(ctx, "p1").UploadLevel(B64(png));

        Assert.Equal("fl_1", result.FileId);
        Assert.Equal(Sha(png), result.Sha256);
        var request = Assert.Single(backend.Uploads);
        Assert.Equal("doodle_levels", request.GetProperty("bucket").GetString());
        Assert.Equal("image/png", request.GetProperty("content_type").GetString());
        Assert.Equal("p1", request.GetProperty("metadata").GetProperty("player_id").GetString());
        Assert.Equal("Ana", request.GetProperty("metadata").GetProperty("author").GetString());
        Assert.Equal("p1", ctx.Database.Get<LevelUpload>("level_uploads", "fl_1")!.PlayerId);
    }

    [Theory]
    [InlineData("not base64 !!", "invalid_png")]
    [InlineData("aGVsbG8gd29ybGQgaGVsbG8gd29ybGQgaGVsbG8gd29ybGQ=", "invalid_png")]
    public void Upload_refuses_what_is_not_a_png(string base64, string code)
    {
        using var ctx = ExoTestHost.Start("level_share");
        var backend = Fake(ctx);

        Assert.Equal(code, Code(() => As(ctx, "p1").UploadLevel(base64)));
        Assert.Empty(backend.Uploads);
    }

    [Theory]
    [InlineData(31, 18)]
    [InlineData(32, 19)]
    [InlineData(64, 36)]
    public void Upload_refuses_any_size_but_32_by_18(int width, int height)
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);

        Assert.Equal("invalid_size", Code(() => As(ctx, "p1").UploadLevel(B64(Png(width, height)))));
    }

    [Fact]
    public void Upload_refuses_big_files()
    {
        using var ctx = ExoTestHost.Start("level_share");
        var backend = Fake(ctx);
        var big = Png().Concat(new byte[4096]).ToArray();

        Assert.Equal("png_too_large", Code(() => As(ctx, "p1").UploadLevel(B64(big))));
        Assert.Empty(backend.Uploads);
    }

    [Fact]
    public void Upload_is_capped_per_player()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var ext = As(ctx, "p1");
        for (byte i = 0; i < 25; i++) ext.UploadLevel(B64(Png(variant: i)));

        Assert.Equal("upload_limit", Code(() => ext.UploadLevel(B64(Png(variant: 99)))));
        As(ctx, "p2").UploadLevel(B64(Png(variant: 99)));
    }

    [Fact]
    public void Publish_requires_a_recorded_clear()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var ext = As(ctx, "p1");
        var upload = ext.UploadLevel(B64(Png()));

        Assert.Equal("clear_required", Code(() => ext.Publish("Spike Alley", "hard", upload.FileId)));
        Assert.Empty(ctx.Database.All<Level>("levels"));
    }

    [Fact]
    public void Clear_of_another_image_does_not_unlock_publish()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var ext = As(ctx, "p1");
        var upload = ext.UploadLevel(B64(Png()));
        ext.RecordClear(Sha(Png(variant: 1)));

        Assert.Equal("clear_required", Code(() => ext.Publish("t", "hard", upload.FileId)));
    }

    [Fact]
    public void Another_players_upload_cannot_be_published()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var upload = As(ctx, "p1").UploadLevel(B64(Png()));
        var thief = As(ctx, "thief");
        thief.RecordClear(upload.Sha256);

        Assert.Equal("invalid_file", Code(() => thief.Publish("Mine now", "hard", upload.FileId)));
        Assert.Equal("invalid_file", Code(() => thief.Publish("Nothing", "hard", "fl_missing")));
    }

    [Fact]
    public void The_same_image_is_published_once()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        PublishLevel(ctx, "p1");
        var copycat = As(ctx, "p2");
        var upload = copycat.UploadLevel(B64(Png()));
        copycat.RecordClear(upload.Sha256);

        Assert.Equal("already_published", Code(() => copycat.Publish("Copy", "hard", upload.FileId)));
    }

    [Fact]
    public void Clear_keys_must_be_an_image_hash_or_a_level()
    {
        using var ctx = ExoTestHost.Start("level_share");

        Assert.Equal("invalid_key", Code(() => As(ctx, "p1").RecordClear("anything")));
    }

    [Fact]
    public void Publish_stores_the_level_with_the_author_from_player_data_and_emits_the_event()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);

        var id = PublishLevel(ctx, "p1");

        var level = ctx.Database.Get<Level>("levels", id)!;
        Assert.Equal("difficulty:hard", level.Tag);
        Assert.Equal("p1", level.AuthorId);
        Assert.Equal("Ana", level.Author);
        Assert.Equal("fl_1", level.FileId);
        Assert.True(ctx.Events.HasEmitted("level_published", "level_share:feed"));
        var published = ctx.Events.GetPayload<LevelPublished>("level_published")!;
        Assert.Equal("p1", published.PlayerId);
        Assert.Equal(id, published.LevelId);
    }

    [Fact]
    public void Level_url_is_a_short_lived_signed_url_for_the_levels_file()
    {
        using var ctx = ExoTestHost.Start("level_share");
        var backend = Fake(ctx);
        var id = PublishLevel(ctx, "p1");

        var url = As(ctx, "p2").GetLevelUrl(id);

        Assert.Equal("/api/files/signed/x?sig=abc", url.Url);
        var request = Assert.Single(backend.SignedUrls);
        Assert.Equal("fl_1", request.GetProperty("id").GetString());
        Assert.Equal("doodle_levels", request.GetProperty("bucket").GetString());
        Assert.Equal(300, request.GetProperty("ttl_seconds").GetInt32());
        Assert.Equal("level_not_found", Code(() => As(ctx, "p2").GetLevelUrl("nope")));
    }

    [Fact]
    public void Browse_does_not_expose_file_locations()
    {
        Assert.DoesNotContain(typeof(LevelSummary).GetProperties(), p => p.Name.StartsWith("File"));
    }

    [Fact]
    public void Rating_averages_over_players()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var id = PublishLevel(ctx, "p1");

        As(ctx, "p2").Rate(id, 5);
        var result = As(ctx, "p3").Rate(id, 2);

        Assert.Equal(3.5, result.Rating);
        Assert.Equal(2, result.RatingCount);
    }

    [Fact]
    public void A_player_rates_a_level_only_once()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var id = PublishLevel(ctx, "p1");
        var ext = As(ctx, "p2");
        ext.Rate(id, 4);

        Assert.Equal("already_rated", Code(() => ext.Rate(id, 1)));
        Assert.Equal(4, ext.Browse("", "top", 10)[0].Rating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Rating_must_be_one_to_five(int stars)
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var id = PublishLevel(ctx, "p1");

        Assert.Equal("invalid_rating", Code(() => As(ctx, "p2").Rate(id, stars)));
    }

    [Fact]
    public void Browse_sorts_by_top_rating_and_newest()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var low = PublishLevel(ctx, "p1", 1, "Low");
        var high = PublishLevel(ctx, "p1", 2, "High");
        As(ctx, "p2").Rate(low, 2);
        As(ctx, "p2").Rate(high, 5);
        var older = ctx.Database.Get<Level>("levels", low)!;
        ctx.Database.Put("levels", low, older with { PublishedAt = older.PublishedAt - 100 });

        var browser = As(ctx, "p3");

        Assert.Equal(new[] { "High", "Low" }, browser.Browse("", "top", 10).Select(l => l.Title));
        Assert.Equal(new[] { "High", "Low" }, browser.Browse("", "newest", 10).Select(l => l.Title));
    }

    [Fact]
    public void Browse_filters_by_tag()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        PublishLevel(ctx, "p1", 1, "Hard one", "hard");
        PublishLevel(ctx, "p1", 2, "Easy one", "easy");

        var hard = As(ctx, "p2").Browse("difficulty:hard", "newest", 10);

        Assert.Equal("Hard one", Assert.Single(hard).Title);
    }

    [Fact]
    public void Clearing_a_published_level_counts_one_play_per_player()
    {
        using var ctx = ExoTestHost.Start("level_share");
        Fake(ctx);
        var id = PublishLevel(ctx, "p1");
        var ext = As(ctx, "p2");

        Assert.True(ext.RecordClear(id).Counted);
        Assert.False(ext.RecordClear(id).Counted);

        Assert.Equal(1, ext.Browse("", "popular", 10)[0].Plays);
    }

    [Fact]
    public void Calls_without_a_player_are_refused()
    {
        using var ctx = ExoTestHost.Start("level_share");
        var ext = As(ctx, "");

        Assert.Equal("unauthorized", Code(() => ext.Browse("", "top", 5)));
        Assert.Equal("unauthorized", Code(() => ext.UploadLevel("")));
    }
}
