using Discord.Commands;

WriteLine("Gate Open: START");

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
        matches.TryAdd(match_guild_id, match);
    }
    else if (file.Name.EndsWith(".json") && ulong.TryParse(file.Name.Split('.')[0], out ulong config_guild_id))
    {
        WriteLine($"Loading guild file {config_guild_id}");
        using var stream = File.OpenRead(file.FullName);
        var config = await JsonSerializer.DeserializeAsync<GuildConfig>(stream!, Models.Default.GuildConfig);
        configs.TryAdd(config_guild_id, config!);
    }
}

DiscordSocketClient client = new (new()
{
    AlwaysDownloadUsers = false,
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildPresences | GatewayIntents.GuildMessages,
    MessageCacheSize = 0,
});

await client.LoginAsync(TokenType.Bot, key);

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
        if (configs.TryGetValue(guild_id, out GuildConfig guild_config))
        {
            // handle channel delete
            if (matches.TryGetValue(guild_id, out Match match)
             && text_channel.Id == match.ChannelId)
            {
                string log_message = $"Match started <t:{match.Created.ToUnixTimeSeconds()}:R>, ended by {author.Username} {author.Id} at <t:{DateTimeOffset.Now.ToUnixTimeSeconds()}:F>, lasted {(DateTimeOffset.Now - match.Created).TotalSeconds} seconds";
                WriteLine($"{context.Guild.Name} {guild_id} {log_message}");
                var log_channel = context.Guild.GetTextChannel(guild_config.LogChannelId);
                if (log_channel is not null)
                    _ = log_channel.SendMessageAsync(log_message);
                await text_channel.DeleteAsync(options: new() { AuditLogReason = $"Message sent by {author.Username}" });
                matches.TryRemove(guild_id, out var _);
                _=PersistMatch(guild_id);
                return;
            }

            if (user_message.MentionedUsers.Any(u => u.Id == client.CurrentUser.Id) 
            && (author.Id == context.Guild.OwnerId || author.Roles.Any(r => r.Id == guild_config.GMRoleId)))
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
                    if (matches.TryGetValue(guild_id, out Match running_match))
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
                        _=PersistMatch(guild_id);
                        await user_message.ReplyAsync($"<#{channel.Id}>", allowedMentions: AllowedMentions.None);
                        return;
                    }
                }
            }
        }
    }
};

await client.StartAsync();
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

async Task PersistConfig(ulong guildId, SocketUserMessage message)
{
    if (configs.TryGetValue(guildId, out var config))
    {
        var name = $"{guildId}.json";
        var name_back = name + ".bak";
        try
        {
            var json = JsonSerializer.Serialize(config, Models.Default.GuildConfig);
            await File.WriteAllTextAsync(Path.Combine(config_dir, name_back), json);
            File.Replace(Path.Combine(config_dir, name_back), Path.Combine(config_dir, name), null);
            await message.ReplyAsync($"```json\n{json}\n```", allowedMentions: AllowedMentions.None);
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
        }
        catch (IOException)
        {
            WriteLine("Could not persist match update");
        }
    }
    else
    {
        try
        {
            File.Delete(Path.Combine(config_dir, name));
        }
        catch (IOException)
        {
            WriteLine("Could not delete match file");
        }
    }
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

[JsonSourceGenerationOptions(WriteIndented =true)]
[JsonSerializable(typeof(GuildConfig))]
[JsonSerializable(typeof(Match))]
public partial class Models : JsonSerializerContext { }