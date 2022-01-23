namespace SelfDestructChannel;

public record Match
{
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public DateTimeOffset Created { get; set; }

    public Match(
        ulong guildId,
        ulong channelId,
        DateTimeOffset created)
    {
        GuildId = guildId;
        ChannelId = channelId;
        Created = created;
    }

    public Match() : this(default, default, default) { }
}

public record GuildConfig
{
    // setters required for source-gen deserialization
    public ulong GuildId { get; set; }
    public ulong AccessRoleId { get; set; }
    public ulong CategoryId { get; set; }
    public ulong LogChannelId { get; set; }
    public ulong GMRoleId { get; set; }
    public ulong? RtaRoleId { get; set; }
    public ulong? EnduranceRoleId { get; set; }

    public GuildConfig(
        ulong guildId,
        ulong accessRoleId,
        ulong categoryId,
        ulong logChannelId,
        ulong gMRoleId,
        ulong? rtaRoleId = null,
        ulong? enduranceRoleId = null)
    {
        GuildId = guildId;
        AccessRoleId = accessRoleId;
        CategoryId = categoryId;
        LogChannelId = logChannelId;
        GMRoleId = gMRoleId;
        RtaRoleId = rtaRoleId;
        EnduranceRoleId = enduranceRoleId;
    }

    public GuildConfig() : this(default, default, default, default, default) { }
};

public record Leaderboard
{

    public Score High { get; set; }
    public Score Low { get; set; }

    public Leaderboard(Score high, Score low)
    {
        High = high;
        Low = low;
    }

    public Leaderboard() : this(new(), new()) { }
}

public record Score
{
    public ulong GuildId { get; set; }
    public string ServerName { get; set; }
    public ulong UserId { get; set; }
    public string Username { get; set; }
    public double DurationSeconds { get; set; }
    public ulong Timestamp { get; set; }

    public Score(ulong guildId, string serverName, ulong userId, string username, double durationSeconds, ulong timestamp)
    {
        GuildId = guildId;
        ServerName = serverName;
        UserId = userId;
        Username = username;
        DurationSeconds = durationSeconds;
        Timestamp = timestamp;
    }

    public Score() : this(default, "", default, "", default, default) { }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GuildConfig))]
[JsonSerializable(typeof(Match))]
[JsonSerializable(typeof(Leaderboard))]
[JsonSerializable(typeof(Score))]
public partial class Models : JsonSerializerContext { }