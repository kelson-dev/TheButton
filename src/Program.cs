WriteLine("Gate Open: START");
const ulong BOT_AUTHOR_ID = 188136808658239488;
string key = GetEnvironmentVariable("credentials_token") ?? File.ReadAllText("./Configurations/bot.credentials");
string config_dir = GetEnvironmentVariable("config_path") ?? "./Configurations";

ConcurrentDictionary<ulong, GuildConfig> configs = new();
ConcurrentDictionary<ulong, Match> matches = new();

var dir = new DirectoryInfo(config_dir);
foreach (var file in dir.EnumerateFiles())
{
    if (file.Name.EndsWith(".match.json") && ulong.TryParse(file.Name.Split('.')[0], out ulong match_guild_id))
    {
        WriteLine($"Loading match file {match_guild_id}");
        using var stream = File.OpenRead(file.FullName);
        var match = await JsonSerializer.DeserializeAsync<Match>(stream!, Models.Default.Match);
        matches.TryAdd(match_guild_id, match!);
    }
    else if (file.Name.EndsWith(".json") && ulong.TryParse(file.Name.Split('.')[0], out ulong config_guild_id))
    {
        WriteLine($"Loading guild file {config_guild_id}");
        using var stream = File.OpenRead(file.FullName);
        var config = await JsonSerializer.DeserializeAsync<GuildConfig>(stream!, Models.Default.GuildConfig);
        configs.TryAdd(config_guild_id, config!);
    }
}
WriteLine("Finished loading");

DiscordSocketClient client = new(new()
{
    AlwaysDownloadUsers = false,
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages,
    MessageCacheSize = 0,
});


await client.LoginAsync(TokenType.Bot, key);
WriteLine("Logged in");

DateTimeOffset created = DateTimeOffset.MinValue;

client.MessageReceived += async (message) =>
{
    if (message.Author.Id == client.CurrentUser.Id || message.Author.IsBot)
        return;

    if (message is SocketUserMessage user_message
     && user_message.Channel is SocketTextChannel text_channel
     && user_message.Author is SocketGuildUser author)
    {
        var context = new SocketCommandContext(client, user_message);
        var guild_id = context.Guild.Id;
        try
        {
            if (!configs.TryGetValue(guild_id, out GuildConfig? guild_config))
            {
                guild_config = new(guild_id, 0, 0, 0, 0);
                configs.TryAdd(guild_id, guild_config);
                await PersistConfig(guild_id, null);
            }

            // handle channel delete
            if (matches.TryGetValue(guild_id, out Match? match)
                && text_channel.Id == match.ChannelId)
            {
                var time = DateTimeOffset.Now;
                TimeSpan duration = time - match.Created;
                string log_message = $"Match started <t:{match.Created.ToUnixTimeSeconds()}:R>, ended by {author.Username} {author.Id} at <t:{DateTimeOffset.Now.ToUnixTimeSeconds()}:F>, lasted {duration.TotalSeconds} seconds";
                await message.DeleteAsync(); // specifically delete message so logging bots see the deletion
                var update_task = HandleRecordUpdate(guild_config, context.Guild, author, match, time);
                WriteLine($"{context.Guild.Name} {guild_id} {log_message}");
                await text_channel.DeleteAsync(options: new() { AuditLogReason = $"Message sent by {author.Username}" });
                matches.TryRemove(guild_id, out var _);
                _ = PersistMatch(guild_id);
                await update_task;
                return;
            }

            if (user_message.MentionedUsers.Any(u => u.Id == client.CurrentUser.Id)
            && (author.Id == BOT_AUTHOR_ID
             || author.Id == context.Guild.OwnerId
             || author.Roles.Any(r => r.Id == guild_config.GMRoleId || r.Permissions.Administrator)))
            {
                if (user_message.Content.Contains("configure CategoryId") && ulong.TryParse(message.Content.Split(' ')[^1], out ulong category_id))
                {
                    configs.AddOrUpdate(guild_id, id => guild_config with { CategoryId = category_id }, (id, config) => config with { CategoryId = category_id });
                    await PersistConfig(guild_id, user_message);
                    return;
                }
                else if (user_message.Content.Contains("configure AccessRoleId") && ulong.TryParse(message.Content.Split(' ')[^1], out ulong access_role_id))
                {
                    configs.AddOrUpdate(guild_id, id => guild_config with { AccessRoleId = access_role_id }, (id, config) => config with { AccessRoleId = access_role_id });
                    await PersistConfig(guild_id, user_message);
                    return;
                }
                else if (user_message.Content.Contains("configure LogChannelId") && ulong.TryParse(message.Content.Split(' ')[^1], out ulong log_channel_id))
                {
                    configs.AddOrUpdate(guild_id, id => guild_config with { LogChannelId = log_channel_id }, (id, config) => config with { LogChannelId = log_channel_id });
                    await PersistConfig(guild_id, user_message);
                    return;
                }
                else if (user_message.Content.Contains("configure GMRoleId") && ulong.TryParse(message.Content.Split(' ')[^1], out ulong mod_role_id))
                {
                    configs.AddOrUpdate(guild_id, id => guild_config with { GMRoleId = mod_role_id }, (id, config) => config with { GMRoleId = mod_role_id });
                    await PersistConfig(guild_id, user_message);
                    return;
                }
                else if (user_message.Content.Contains("start"))
                {
                    if (matches.TryGetValue(guild_id, out Match? running_match))
                    {
                        var channel = context.Guild.GetTextChannel(running_match.ChannelId);
                        if (channel != null)
                        {
                            var time = running_match.Created.ToUnixTimeSeconds();
                            await user_message.ReplyAsync($"A match is already running in <#{running_match.ChannelId}> since <t:{time}:f>, <t:{time}:R>", allowedMentions: AllowedMentions.None);
                            return;
                        }
                        else
                        {
                            matches.TryRemove(guild_id, out var _);
                            await PersistMatch(guild_id);
                        }
                    }
                    List<string> invalid_configs = new();
                    if (guild_config.CategoryId == 0 || context.Guild.GetCategoryChannel(guild_config.CategoryId) == null)
                        invalid_configs.Add($"CategoryId: {guild_config.CategoryId}");
                    if (guild_config.AccessRoleId == 0 || context.Guild.GetRole(guild_config.AccessRoleId) == null)
                        invalid_configs.Add($"AccessRoleId: {guild_config.AccessRoleId}");
                    if (guild_config.GMRoleId == 0 || context.Guild.GetRole(guild_config.GMRoleId) == null)
                        invalid_configs.Add($"GMRoleId: {guild_config.GMRoleId}");
                    if (guild_config.LogChannelId == 0 || context.Guild.GetTextChannel(guild_config.LogChannelId) == null)
                        invalid_configs.Add($"LogChannelId: {guild_config.LogChannelId}");
                    if (invalid_configs.Count > 0)
                    {
                        await user_message.ReplyAsync(
                            $"The following configs are unset or point to objects that could not be found: \n{string.Join(Environment.NewLine, invalid_configs)}\n"
                            + "README: https://github.com/kelson-dev/TheButton/blob/deploy/README.md",
                            allowedMentions: AllowedMentions.None);
                        return;
                    }

                    if (configs.TryGetValue(guild_id, out var start_config))
                    {
                        var channel = await context.Guild.CreateTextChannelAsync("self-destructs", func: Configure(context.Guild, start_config));
                        var match_state = new Match(guild_id, channel.Id, DateTimeOffset.Now);
                        await channel.SendMessageAsync("If a message is sent here the channel will be deleted");
                        matches.AddOrUpdate(guild_id, match_state, (id, match) => match_state);
                        _ = PersistMatch(guild_id);
                        await user_message.ReplyAsync($"<#{channel.Id}>", allowedMentions: AllowedMentions.None);
                        return;
                    }
                }
            }
        }
        catch (Exception e)
        {
            WriteLine(e);
        }
    }
};  

await client.StartAsync();
WriteLine("Started");
while (true)
    await Task.Delay(TimeSpan.FromMilliseconds(100));

Action<TextChannelProperties> Configure(IGuild guild, GuildConfig config) => props =>
{
    props.CategoryId = config.CategoryId;
    props.PermissionOverwrites = new Optional<IEnumerable<Overwrite>>(new Overwrite[] {
        new Overwrite(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Deny, sendMessages: PermValue.Deny)),
        new Overwrite(config.AccessRoleId, PermissionTarget.Role, new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: PermValue.Allow)),
    });
    props.Topic = "If a message is sent here the channel will be deleted";
};

async Task PersistConfig(ulong guildId, SocketUserMessage? message)
{
    if (configs.TryGetValue(guildId, out var config))
    {
        var name = $"{guildId}.json";
        try
        {
            var json = JsonSerializer.Serialize(config, Models.Default.GuildConfig);
            await File.WriteAllTextAsync(Path.Combine(config_dir, name), json);
            await (message?.ReplyAsync($"```json\n{json}\n```", allowedMentions: AllowedMentions.None) ?? Task.CompletedTask);
            WriteLine("Persisted guild configuration");
        }
        catch (IOException)
        {
            WriteLine("Could not persist config update");
        }
    }
}



async Task PersistMatch(ulong guildId)
{
    var name = $"{guildId}.match.json";
    var name_back = name + ".bak";
    if (matches.TryGetValue(guildId, out var config))
    {
        try
        {
            var json = JsonSerializer.Serialize(config, Models.Default.Match);
            await File.WriteAllTextAsync(Path.Combine(config_dir, name), json);
            WriteLine("Persisted match state");
        }
        catch (IOException e)
        {
            WriteLine("Could not persist match update");
            WriteLine(e);
        }
    }
    else
    {
        try
        {
            File.Delete(Path.Combine(config_dir, name));
            WriteLine("Removed match state");
        }
        catch (IOException e)
        {
            WriteLine("Could not delete match file");
            WriteLine(e);
        }
    }
}

async Task HandleRecordUpdate(GuildConfig config, SocketGuild guild, SocketGuildUser user, Match match, DateTimeOffset ended)
{
    var duration = ended - match.Created;
    string guild_leaderboard_file = $"{guild.Id}.record";
    const string global_leaderboard_file = "global.record";
    var guild_current = await GetLeaderboard(guild_leaderboard_file);
    var new_score = new Score(
        guild.Id,
        guild.Name,
        user.Id,
        user.Nickname ?? user.Username,
        duration.TotalSeconds,
        (ulong)ended.ToUnixTimeSeconds());
    var guild_updated = Update(guild_current, new_score);
    if (guild_current != guild_updated)
        await SaveLeaderboard(guild_leaderboard_file, guild_updated);
    var global_current = await GetLeaderboard(global_leaderboard_file);
    var global_updated = Update(global_current, new_score);
    if (global_updated != global_current)
        await SaveLeaderboard(global_leaderboard_file, global_updated);
    await SendMatchSummary(config, guild, user, match, ended, guild_updated, global_updated);
}

async Task SendMatchSummary(
    GuildConfig config, 
    SocketGuild guild, 
    SocketGuildUser user, 
    Match match, 
    DateTimeOffset ended,
    Leaderboard localScore,
    Leaderboard globalScore)
{
    var duration = ended - match.Created;
    var channel = guild.GetTextChannel(config.LogChannelId);
    if (channel == null)
        return;
    await channel.SendMessageAsync($"Channel deleted by {user.Mention} at <t:{DateTimeOffset.Now.ToUnixTimeSeconds()}:F>",
        embed: new EmbedBuilder()
            .WithFooter(new EmbedFooterBuilder().WithIconUrl(user.GetAvatarUrl()).WithText(user.Username))
            .WithTimestamp(ended)
            .WithTitle("Button pressed")
            .WithThumbnailUrl(client.CurrentUser.GetAvatarUrl())
            .WithFields(
                new EmbedFieldBuilder()
                    .WithIsInline(false)
                    .WithName("Duration (seconds)")
                    .WithValue($"{duration.TotalSeconds} seconds"),
                new EmbedFieldBuilder()
                    .WithIsInline(false)
                    .WithName("Local RTA Time")
                    .WithValue($"{TimeSpan.FromSeconds(localScore.Low.DurationSeconds).TotalSeconds} seconds" +
                        $"\nby {localScore.Low.Username} ({localScore.Low.UserId})," +
                        $"\n<t:{localScore.Low.Timestamp}:R>"),
                new EmbedFieldBuilder()
                    .WithIsInline(false)
                    .WithName("Global RTA Time")
                    .WithValue($"{TimeSpan.FromSeconds(globalScore.Low.DurationSeconds).TotalSeconds} seconds" +
                        $"\nin {globalScore.Low.ServerName}" +
                        $"\n<t:{globalScore.Low.Timestamp}:R>"),
                new EmbedFieldBuilder()
                    .WithIsInline(false)
                    .WithName("Local Endurance Time")
                    .WithValue($"{TimeSpan.FromSeconds(localScore.High.DurationSeconds).Humanize(precision: 3)} " +
                        $"\nby {localScore.High.Username} ({localScore.High.UserId})," +
                        $"\n<t:{localScore.High.Timestamp}:R>"),
                new EmbedFieldBuilder()
                    .WithIsInline(false)
                    .WithName("Global Endurance Time")
                    .WithValue($"{TimeSpan.FromSeconds(globalScore.High.DurationSeconds).Humanize(precision: 3)} " +
                        $"\nin {globalScore.High.ServerName}" +
                        $"\n<t:{globalScore.High.Timestamp}:R>"))
            .Build());
}

static Leaderboard Update(Leaderboard? board, Score score)
{
    if (board is null)
        return new Leaderboard(score, score);
    else if (score.DurationSeconds > board.High.DurationSeconds)
        return board with { High = score };
    else if (score.DurationSeconds < board.Low.DurationSeconds)
        return board with { Low = score };
    else 
        return board;
}

async Task<Leaderboard?> GetLeaderboard(string filename)
{
    if (!File.Exists(filename))
        return null;
    try
    {
        await using var stream = File.OpenRead(Path.Combine(config_dir, filename));
        return await JsonSerializer.DeserializeAsync<Leaderboard>(stream, Models.Default.Leaderboard);
    }
    catch (Exception e) 
    {
        WriteLine(e.Message);
        WriteLine(e.StackTrace);
        return null; 
    }
}

async Task SaveLeaderboard(string filename, Leaderboard board)
{
    await using var stream = File.OpenWrite(Path.Combine(config_dir, filename));
    await JsonSerializer.SerializeAsync<Leaderboard>(stream, board, Models.Default.Leaderboard);
}

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

    public GuildConfig(
        ulong guildId,
        ulong accessRoleId,
        ulong categoryId,
        ulong logChannelId,
        ulong gMRoleId)
    {
        GuildId = guildId;
        AccessRoleId = accessRoleId;
        CategoryId = categoryId;
        LogChannelId = logChannelId;
        GMRoleId = gMRoleId;
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