using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Exoforge.Plugin.SDK;
using Exoforge.Plugins.Generated;

namespace Exoforge.Extensions;

[ExoExtension("level_share", Version = "0.1.0", Category = "Game", Title = "Level Share", Icon = "🎨")]
[ExoDependency("file_bucket")]
[ExoDependency("player_data")]
public class LevelShareExtension
{
    private const string Bucket = "doodle_levels";
    private const int Width = 32;
    private const int Height = 18;
    private const int MaxPngBytes = 4096;
    private const int MaxUploadsPerPlayer = 25;
    private const int UrlTtlSeconds = 300;

    [ExoAction(Scope = "player")]
    public LevelUploadResult UploadLevel(string pngBase64)
    {
        var playerId = RequirePlayer();
        var png = DecodePng(pngBase64);
        if (Exo.Database.All<LevelUpload>().Count(u => u.PlayerId == playerId) >= MaxUploadsPerPlayer)
            throw new ExoActionException("upload_limit", $"At most {MaxUploadsPerPlayer} level images per player.");

        var metadata = new { player_id = playerId, author = AuthorOf(playerId), width = Width, height = Height };
        var stored = ExoServices.FileBucket.UploadFileAsync(new FileBucketUploadFileRequest
        {
            Filename = $"{Guid.NewGuid():N}.png",
            Bucket = Json(Bucket),
            Content = Json(Convert.ToBase64String(png)),
            ContentType = Json("image/png"),
            Metadata = Json(metadata),
        }).GetAwaiter().GetResult()!.File;

        var upload = new LevelUpload(Text(stored, "id"), playerId, Text(stored, "sha256"), png.Length, Now());
        Exo.Database.Put(upload);
        return new LevelUploadResult(upload.Id, upload.Sha256);
    }

    [ExoAction(Scope = "player")]
    public LevelPublishResult Publish(string title, string difficulty, string fileId)
    {
        var playerId = RequirePlayer();
        if (string.IsNullOrWhiteSpace(title))
            throw new ExoActionException("invalid_level", "A title is required.");

        var upload = Exo.Database.Get<LevelUpload>(fileId);
        if (upload == null || upload.PlayerId != playerId)
            throw new ExoActionException("invalid_file", "Upload the level image first; it must be your own upload.");

        if (Exo.Database.Get<LevelClear>(ClearId(playerId, upload.Sha256)) == null)
            throw new ExoActionException("clear_required", "Clear this exact level yourself before publishing it.");

        if (Exo.Database.All<Level>().Any(l => l.FileSha == upload.Sha256))
            throw new ExoActionException("already_published", "This level image is already published.");

        var tag = $"difficulty:{(string.IsNullOrWhiteSpace(difficulty) ? "normal" : difficulty.Trim().ToLowerInvariant())}";
        var level = new Level(Guid.NewGuid().ToString("N"), playerId, AuthorOf(playerId), title.Trim(), tag, fileId, upload.Sha256, 0, 0, 0, Now());
        Exo.Database.Put(level);
        Exo.Events.EmitAsync(new LevelPublished(playerId, level.Id, level.Title, tag, level.PublishedAt)).GetAwaiter().GetResult();
        return new LevelPublishResult(level.Id);
    }

    [ExoAction(Scope = "player")]
    public LevelUrlResult GetLevelUrl(string levelId)
    {
        RequirePlayer();
        var level = Exo.Database.Get<Level>(levelId)
            ?? throw new ExoActionException("level_not_found", $"No level {levelId}.");

        var signed = ExoServices.FileBucket.SignedUrlAsync(new FileBucketSignedUrlRequest
        {
            Id = level.FileId,
            Bucket = Json(Bucket),
            TtlSeconds = Json(UrlTtlSeconds),
        }).GetAwaiter().GetResult()!;
        return new LevelUrlResult(signed.Url, signed.ExpiresAt);
    }

    [ExoAction(Scope = "player")]
    public List<LevelSummary> Browse(string tag, string sort, int limit)
    {
        RequirePlayer();
        var levels = Exo.Database.All<Level>().AsEnumerable();
        if (!string.IsNullOrWhiteSpace(tag))
            levels = levels.Where(l => l.Tag == tag);

        levels = sort switch
        {
            "top" => levels.OrderByDescending(Average).ThenByDescending(l => l.RatingCount),
            "popular" => levels.OrderByDescending(l => l.Plays),
            _ => levels.OrderByDescending(l => l.PublishedAt),
        };

        return levels.Take(Math.Clamp(limit, 1, 50)).Select(Summarize).ToList();
    }

    [ExoAction(Scope = "player")]
    public LevelRateResult Rate(string levelId, int stars)
    {
        var playerId = RequirePlayer();
        if (stars is < 1 or > 5)
            throw new ExoActionException("invalid_rating", "Stars must be between 1 and 5.");

        var level = Exo.Database.Get<Level>(levelId)
            ?? throw new ExoActionException("level_not_found", $"No level {levelId}.");

        var ratingId = $"{levelId}:{playerId}";
        if (Exo.Database.Get<Rating>(ratingId) != null)
            throw new ExoActionException("already_rated", "You already rated this level.");

        Exo.Database.Put(new Rating(ratingId, levelId, playerId, stars));
        level = level with { RatingSum = level.RatingSum + stars, RatingCount = level.RatingCount + 1 };
        Exo.Database.Put(level);
        return new LevelRateResult(Average(level), level.RatingCount);
    }

    [ExoAction(Scope = "player")]
    public LevelClearResult RecordClear(string key)
    {
        var playerId = RequirePlayer();
        if (key is not { Length: 64 } && Exo.Database.Get<Level>(key) == null)
            throw new ExoActionException("invalid_key", "Use a level id or the sha256 of the level image.");

        var clearId = ClearId(playerId, key);
        if (Exo.Database.Get<LevelClear>(clearId) != null)
            return new LevelClearResult(false);

        Exo.Database.Put(new LevelClear(clearId, playerId, key, Now()));
        var level = Exo.Database.Get<Level>(key);
        if (level != null)
            Exo.Database.Put(level with { Plays = level.Plays + 1 });

        return new LevelClearResult(true);
    }

    private static string RequirePlayer()
    {
        var playerId = Exo.Context.CallerPlayerId;
        if (string.IsNullOrEmpty(playerId))
            throw new ExoActionException("unauthorized", "Player session is required.");

        return playerId;
    }

    private static byte[] DecodePng(string base64)
    {
        byte[] png;
        try
        {
            png = Convert.FromBase64String(base64 ?? "");
        }
        catch (FormatException)
        {
            throw new ExoActionException("invalid_png", "The image is not valid base64.");
        }

        if (png.Length > MaxPngBytes)
            throw new ExoActionException("png_too_large", $"A level image is at most {MaxPngBytes} bytes.");

        byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(signature) || !png.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new ExoActionException("invalid_png", "Not a PNG image.");

        if (ReadInt(png, 16) != Width || ReadInt(png, 20) != Height)
            throw new ExoActionException("invalid_size", $"A level image is exactly {Width}x{Height} pixels.");

        return png;
    }

    private static int ReadInt(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];

    private static string AuthorOf(string playerId) =>
        ExoServices.PlayerData.GetPlayerAsync(new PlayerDataGetPlayerRequest { PlayerId = playerId }).GetAwaiter().GetResult()?.Player?.Name ?? "";

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";

    private static string ClearId(string playerId, string key) => $"{playerId}:{key}";

    private static double Average(Level level) =>
        level.RatingCount == 0 ? 0 : Math.Round((double)level.RatingSum / level.RatingCount, 2);

    private static LevelSummary Summarize(Level l) =>
        new(l.Id, l.AuthorId, l.Author, l.Title, l.Tag, l.Plays, Average(l), l.RatingCount, l.PublishedAt);

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

[ExoResource("levels")]
public record Level(string Id, string AuthorId, string Author, string Title, string Tag, string FileId, string FileSha, int Plays, int RatingSum, int RatingCount, long PublishedAt);

[ExoResource("ratings")]
public record Rating(string Id, string LevelId, string PlayerId, int Stars);

[ExoResource("level_uploads")]
public record LevelUpload(string Id, string PlayerId, string Sha256, int Size, long UploadedAt);

[ExoResource("level_clears")]
public record LevelClear(string Id, string PlayerId, string Key, long ClearedAt);

public record LevelSummary(string Id, string AuthorId, string Author, string Title, string Tag, int Plays, double Rating, int RatingCount, long PublishedAt);

public record LevelUploadResult(string FileId, string Sha256);
public record LevelPublishResult(string LevelId);
public record LevelUrlResult(string Url, long ExpiresAt);
public record LevelRateResult(double Rating, int RatingCount);
public record LevelClearResult(bool Counted);

[ExoEvent("level_published", Topic = "level_share:feed")]
public record LevelPublished(string PlayerId, string LevelId, string Title, string Tag, long Timestamp);
