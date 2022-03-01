const string GLOBAL_LEADERBOARD_FILE = "global.record";
const ulong BOT_AUTHOR_ID = 188136808658239488;

WriteLine("Gate Open: START");

string key = GetEnvironmentVariable("credentials_token") ?? File.ReadAllText("./Configurations/bot.credentials");
string config_dir = GetEnvironmentVariable("config_path") ?? "./Configurations";

ConcurrentDictionary<ulong, GuildConfig> configs = new();
ConcurrentDictionary<ulong, Match> matches = new();

var dir = new DirectoryInfo(config_dir);
foreach (var file in dir.EnumerateFiles())
{
    WriteLine(file.FullName);
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
                try
                {
                    await message.DeleteAsync(); // specifically delete message so logging bots see the deletion
                }
                catch (HttpException e)
                {
                    WriteLine($"Did not have permission to delete message:\n{message.Author.Id} {author.Username}\n{message.Content}");
                }
                var update_task = HandleRecordUpdate(guild_config, context.Guild, author, match, time);
                WriteLine($"{context.Guild.Name} {guild_id} {log_message}");
                await text_channel.DeleteAsync(options: new() { AuditLogReason = $"Message sent by {author.Username}" });
                matches.TryRemove(guild_id, out var _);
                _ = PersistMatch(guild_id);
                await update_task;
                return;
            }

            
            var reader = new ContentReader(message.Content);
            if (reader.TryReadMentionId(Mention.USER, Mention.NICKNAME, out ulong first_mentioned_role_id) && first_mentioned_role_id == client.CurrentUser.Id)
            {
                reader.TrimStart();
                // global commands
                if (reader.TryReadText("leaderboard") || reader.TryReadText("stats"))
                {
                    await ReplyWithLeaderboard(guild_config, context.Guild, author, user_message);
                }
                if (reader.TryReadText("time since") || reader.TryReadText("how long since") || reader.TryReadText("when was"))
                {
                    if (reader.TrimStart().TryReadU64(out ulong id))
                    {
                        Snowflake snowflake = id;
                        var epoch = snowflake.ToTimestamp().ToUnixTimeSeconds().ToString();
                        await user_message.ReplyAsync($"<t:{epoch}:R>, <t:{epoch}:F>", allowedMentions: AllowedMentions.None);
                    }
                }
                if (author.Id == BOT_AUTHOR_ID
                 || author.Id == context.Guild.OwnerId
                 || author.Roles.Any(r => r.Id == guild_config.GMRoleId || r.Permissions.Administrator))
                {
                    if (reader.TryReadText("configure"))
                    {
                        reader.TrimStart();
                        if (reader.TryReadText(nameof(GuildConfig.CategoryId)) && reader.TrimStart().TryReadU64(out ulong category_id))
                        {
                            configs.AddOrUpdate(guild_id, id => guild_config with { CategoryId = category_id }, (id, config) => config with { CategoryId = category_id });
                            await PersistConfig(guild_id, user_message);
                        }
                        else if (reader.TryReadText(nameof(GuildConfig.AccessRoleId)) && reader.TrimStart().TryReadU64(out ulong access_role_id))
                        {
                            configs.AddOrUpdate(guild_id, id => guild_config with { AccessRoleId = access_role_id }, (id, config) => config with { AccessRoleId = access_role_id });
                            await PersistConfig(guild_id, user_message);
                        }
                        else if (reader.TryReadText(nameof(GuildConfig.LogChannelId)) && reader.TrimStart().TryReadU64(out ulong log_channel_id))
                        {
                            configs.AddOrUpdate(guild_id, id => guild_config with { LogChannelId = log_channel_id }, (id, config) => config with { LogChannelId = log_channel_id });
                            await PersistConfig(guild_id, user_message);
                        }
                        else if (reader.TryReadText(nameof(GuildConfig.GMRoleId)) && reader.TrimStart().TryReadU64(out ulong mod_role_id))
                        {
                            configs.AddOrUpdate(guild_id, id => guild_config with { GMRoleId = mod_role_id }, (id, config) => config with { GMRoleId = mod_role_id });
                            await PersistConfig(guild_id, user_message);
                        }
                        else if (reader.TryReadText(nameof(GuildConfig.RtaRoleId)))
                        {
                            ulong? rta_id = reader.TrimStart().TryReadU64(out ulong rta_role_id) ? rta_role_id : null;
                            configs.AddOrUpdate(guild_id, id => guild_config with { RtaRoleId = rta_id }, (id, config) => config with { RtaRoleId = rta_id });
                            if (rta_id is not null && !(context.Guild.GetUser(client.CurrentUser.Id)?.GuildPermissions.ManageRoles ?? false))
                                await user_message.ReplyAsync("To use role features make sure I have 'ManageRoles' permission. I don't request this permission by default with the standard invite link.");
                            await PersistConfig(guild_id, user_message);
                        }
                        else if (reader.TryReadText(nameof(GuildConfig.EnduranceRoleId)))
                        {
                            ulong? end_id = reader.TrimStart().TryReadU64(out ulong endurance_role_id) ? endurance_role_id : null;
                            configs.AddOrUpdate(guild_id, id => guild_config with { EnduranceRoleId = end_id }, (id, config) => config with { EnduranceRoleId = end_id });
                            if (end_id is not null && !(context.Guild.GetUser(client.CurrentUser.Id)?.GuildPermissions.ManageRoles ?? false))
                                await user_message.ReplyAsync("To use role features make sure I have 'ManageRoles' permission. I don't request this permission by default with the standard invite link.");
                            await PersistConfig(guild_id, user_message);
                        }
                        return;
                    }
                    else if (reader.TryReadText("start"))
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
                                return;
                            }
                        }
                        List<string> invalid_configs = new();
                        if (guild_config.CategoryId == 0 || context.Guild.GetCategoryChannel(guild_config.CategoryId) == null)
                            invalid_configs.Add($"CategoryId: {guild_config.CategoryId} could not be found");
                        if (guild_config.AccessRoleId == 0 || context.Guild.GetRole(guild_config.AccessRoleId) == null)
                            invalid_configs.Add($"AccessRoleId: {guild_config.AccessRoleId} could not be found");
                        if (guild_config.GMRoleId == 0 || context.Guild.GetRole(guild_config.GMRoleId) == null)
                            invalid_configs.Add($"GMRoleId: {guild_config.GMRoleId}  could not be found");
                        if (guild_config.LogChannelId == 0 || context.Guild.GetTextChannel(guild_config.LogChannelId) == null)
                            invalid_configs.Add($"LogChannelId: {guild_config.LogChannelId} could not be found");
                        if (guild_config.RtaRoleId is not null && context.Guild.GetRole(guild_config.RtaRoleId.Value) is null)
                            invalid_configs.Add($"RtaRoleId: {guild_config.RtaRoleId} could not be found");
                        if (guild_config.EnduranceRoleId is not null && context.Guild.GetRole(guild_config.EnduranceRoleId.Value) is null)
                            invalid_configs.Add($"EnduranceRoleId: {guild_config.EnduranceRoleId} could not be found");
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

async Task ReplyWithLeaderboard(GuildConfig config, SocketGuild guild, SocketGuildUser user, SocketUserMessage message)
{
    string guild_leaderboard_file = $"{guild.Id}.record";
    var guild_current = await GetLeaderboard(guild_leaderboard_file);
    var global_current = await GetLeaderboard(GLOBAL_LEADERBOARD_FILE);
    await message.ReplyAsync("",
        embed: BuildLeaderboardEmbed(DateTimeOffset.Now, guild_current, global_current!)
            .WithTitle("Current Button Leaderboard")
            .Build(),
        allowedMentions: AllowedMentions.None);
}

async Task HandleRecordUpdate(GuildConfig config, SocketGuild guild, SocketGuildUser user, Match match, DateTimeOffset ended)
{
    var duration = ended - match.Created;
    string guild_leaderboard_file = $"{guild.Id}.record";
    var guild_current = await GetLeaderboard(guild_leaderboard_file);
    var new_score = new Score(
        guild.Id,
        guild.Name,
        user.Id,
        user.Nickname ?? user.Username,
        duration.TotalSeconds,
        (ulong)ended.ToUnixTimeSeconds());
    var guild_updated = Update(guild_current, new_score);
    var guild_save_task = Task.CompletedTask;
    if (guild_current != guild_updated)
    {
        guild_save_task = SaveLeaderboard(guild_leaderboard_file, guild_updated);
        if (guild.GetUser(client.CurrentUser.Id)?.GuildPermissions.ManageRoles ?? false)
        {
            if (guild_updated.High != guild_current?.High && config.EnduranceRoleId is ulong endurance_role_id && duration > TimeSpan.FromHours(8))
            {
                var endurance_role = guild.GetRole(endurance_role_id);
                if (endurance_role is not null)
                {
                    foreach (var previous in endurance_role.Members)
                        await previous.RemoveRoleAsync(endurance_role_id);
                    await user.AddRoleAsync(endurance_role_id);
                }
            }
            if (guild_updated.Low != guild_current?.Low && config.RtaRoleId is ulong rta_role_id && duration < TimeSpan.FromSeconds(1.5))
            {
                var rta_role = guild.GetRole(rta_role_id);
                if (rta_role is not null)
                {
                    foreach (var previous in rta_role.Members)
                        await previous.RemoveRoleAsync(rta_role_id);
                    await user.AddRoleAsync(rta_role_id);
                }
            }
        }
    }
    var global_current = await GetLeaderboard(GLOBAL_LEADERBOARD_FILE);
    var global_updated = Update(global_current, new_score);
    if (global_updated != global_current)
        await SaveLeaderboard(GLOBAL_LEADERBOARD_FILE, global_updated);
    await guild_save_task;
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
        embed: BuildLeaderboardEmbed(ended, localScore, globalScore,
            new EmbedFieldBuilder()
                    .WithIsInline(false)
                    .WithName("Duration")
                    .WithValue($"{duration.TotalSeconds} seconds\n{duration.Humanize(precision: 3)}"))
            .WithFooter(new EmbedFooterBuilder().WithIconUrl(user.GetAvatarUrl()).WithText(user.Username))
            .WithTitle("Button pressed")
            .WithThumbnailUrl(client.CurrentUser.GetAvatarUrl())
            .Build());
}

static EmbedBuilder BuildLeaderboardEmbed(DateTimeOffset time, Leaderboard? localScore, Leaderboard globalScore, params EmbedFieldBuilder[] preceedingFields)
{
    var local_rta_field = localScore is null
        ? new EmbedFieldBuilder().WithName("Local RTA Time").WithValue("None")
        : new EmbedFieldBuilder()
                .WithIsInline(false)
                .WithName("Local RTA Time")
                .WithValue($"{TimeSpan.FromSeconds(localScore.Low.DurationSeconds).TotalSeconds} seconds" +
                    $"\nby {localScore.Low.Username} ({localScore.Low.UserId})," +
                    $"\n<t:{localScore.Low.Timestamp}:R>");
    var local_endurance_field = localScore is null
        ? new EmbedFieldBuilder().WithName("Local Endurance Time").WithValue("None")
        : new EmbedFieldBuilder()
                .WithIsInline(false)
                .WithName("Local Endurance Time")
                .WithValue($"{TimeSpan.FromSeconds(localScore.High.DurationSeconds).Humanize(precision: 3)} " +
                    $"\nby {localScore.High.Username} ({localScore.High.UserId})," +
                    $"\n<t:{localScore.High.Timestamp}:R>");
    var builder = new EmbedBuilder().WithTimestamp(time);
    if (preceedingFields is { Length: > 0 })
        builder = builder.WithFields(preceedingFields);
    builder = builder
        .WithFields(
            local_rta_field,
            new EmbedFieldBuilder()
                .WithIsInline(false)
                .WithName("Global RTA Time")
                .WithValue($"{TimeSpan.FromSeconds(globalScore.Low.DurationSeconds).TotalSeconds} seconds" +
                    $"\nin {globalScore.Low.ServerName}" +
                    $"\n<t:{globalScore.Low.Timestamp}:R>"),
            local_endurance_field,
            new EmbedFieldBuilder()
                .WithIsInline(false)
                .WithName("Global Endurance Time")
                .WithValue($"{TimeSpan.FromSeconds(globalScore.High.DurationSeconds).Humanize(precision: 3)} " +
                    $"\nin {globalScore.High.ServerName}" +
                    $"\n<t:{globalScore.High.Timestamp}:R>"));
    return builder;
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
    try
    {
        return JsonSerializer.Deserialize<Leaderboard>(
            await File.ReadAllBytesAsync(
                Path.Combine(config_dir, filename)), 
                Models.Default.Leaderboard);
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
    var path = Path.Combine(config_dir, filename);
    var json = JsonSerializer.SerializeToUtf8Bytes<Leaderboard>(board, Models.Default.Leaderboard);
    File.Delete(path);
    await File.WriteAllBytesAsync(path, json);
}
