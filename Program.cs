
using dev_library.Clients;
using dev_library.Data;
using dev_library.Data.WoW.Raidbots;
using dev_refined;
using dev_refined.Clients;
using Discord;
using Discord.Audio;
using Discord.WebSocket;
using System.Diagnostics;
using TimeZoneConverter;

public class Program
{
    private static DiscordSocketClient DiscordBotClient;
    private static readonly WoWAuditClient WoWAuditClient = new();
    private static readonly RaidBotsClient RaidBotsClient = new();
    private static readonly RealmClient RealmClient = new();
    private static readonly RefinedClient RefinedClient = new();
    private static GoogleSheetsClient GoogleSheetsClient;
    private static AiClient AiClient;

    public static async Task Main()
    {
        var discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent | GatewayIntents.Guilds | GatewayIntents.GuildMembers,
            AlwaysDownloadUsers = true
        };

        AppSettings.Initialize();
        GoogleSheetsClient = new GoogleSheetsClient();
        AiClient = new();

        DiscordBotClient = new DiscordSocketClient(discordConfig);
        DiscordBotClient.Log += Log;
        DiscordBotClient.Ready += OnReady;
        DiscordBotClient.MessageReceived += MonitorMessages;
        DiscordBotClient.GuildMemberUpdated += OnGuildMemberUpdatedAsync;

        await DiscordBotClient.LoginAsync(TokenType.Bot, AppSettings.Discord.Token);
        await DiscordBotClient.StartAsync();

        await Task.Delay(-1);
    }

    private static async Task OnReady()
    {
        await ScheduleCheck();
    }

    private static async Task OnGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        // if (before.Id != 496045399321083915) return;

        // var beforeUser = await before.GetOrDownloadAsync();
        // if (beforeUser.AvatarId == after.AvatarId) return;

        // var channel = DiscordBotClient.GetChannel(840082901890629644) as IMessageChannel;
        // Console.WriteLine("She did it!");
        // await SendMessageAsync(channel, "<@496045399321083915> :eyes:");
    }

    public static async Task MonitorMessages(SocketMessage message)
    {
        if (message.Author.IsBot) return;

        var matchedWowAudit = AppSettings.WowAudit.FirstOrDefault(wa => wa.ChannelIds.Contains(message.Channel.Id));
        if (matchedWowAudit != null && !matchedWowAudit.ReminderOnly)
        {
            await MonitorDroptimizers(message);
        }
        else if (message.MentionedUsers.Any(u => u.Username == "Refined Bot") && message.Author.Username != "Refined Bot")
        {
            var mentioningUser = message.Author;
            var hasRole = ((SocketGuildUser)mentioningUser).Roles.Any(r => AppSettings.GptSettings.AllowedRoles.Contains(r.Name.ToUpper()));

            if (!hasRole)
            {
                await SendMessageAsync(message.Channel, $"You lack the power to control me {mentioningUser.Mention} :pig:");
                return;
            }

            if (message.Channel.Name.ToUpper() != "BOT-SPAM")
            {
                await SendMessageAsync(message.Channel, $"If you want me to reply using skynet then message me in #bot-spam :pig:");
                return;
            }

            var response = await AiClient.GetResponse($"{mentioningUser.Mention} said {message.Content}", 1);
            await SendMessageAsync(message.Channel, response);
        }
    }

    public static async Task MonitorDroptimizers(SocketMessage message)
    {
        var raidBotsUrls = Helpers.ExtractUrls(message.Content);
        var wowAudit = AppSettings.WowAudit.First(wa => wa.ChannelIds.Contains(message.Channel.Id));

        if (raidBotsUrls.Count == 0)
        {
            if (!((SocketGuildUser)message.Author).GuildPermissions.Administrator)
            {
                Console.WriteLine(message.Content);
                await DeleteAsync(message);
            }
            return;
        }

        Console.WriteLine("Begin Processing reports");

        try
        {
            var itemUpgrades = new List<ItemUpgrade>();

            foreach (var raidBotsUrl in raidBotsUrls)
            {
                var reportId = raidBotsUrl.Split('/').Last();
                Console.WriteLine($"Processing {raidBotsUrl}");

                var response = await WoWAuditClient.UpdateWishlist(reportId, wowAudit.Guild);

                if (!bool.Parse(response.Created))
                {
                    await SendDmAsync(message.Author, $"You did not send a valid droptimizer {response.Base[0]}");
                    await DeleteAsync(message);
                    return;
                }

                var validGoogleSheetsReport = await RaidBotsClient.IsValidReport(raidBotsUrl);
                if (wowAudit.Guild == "REFINED" && validGoogleSheetsReport)
                {
                    itemUpgrades = await RaidBotsClient.GetItemUpgrades(itemUpgrades, reportId);
                }
            }

            if (itemUpgrades.Count > 0)
                await GoogleSheetsClient.UpdateSheet(itemUpgrades);

            await ReactAsync(message, new Emoji("✅"));

            if (message.Author.Id == 341726443295866893)
            {
                var textChannel = message.Channel as ITextChannel;
                if (textChannel != null)
                    await textChannel.SendMessageAsync("https://tenor.com/view/bosnov-67-bosnov-67-67-meme-gif-16727368109953357722", messageReference: new MessageReference(message.Id));
            }
        }
        catch (Exception ex)
        {
            await ReactAsync(message, new Emoji("❌"));
            await SendDmAsync(message.Author, "WoWAudit is currently down. Please try again later. Also compliment epic on his tuna can");
            Console.WriteLine(ex.Message);
            throw;
        }
    }

    // Dry-run aware Discord helpers
    private static async Task SendMessageAsync(IMessageChannel channel, string content)
    {
        if (AppSettings.DryRun) Console.WriteLine($"[DRY RUN] Send to #{channel.Name}: {content}");
        var allowedMentions = AppSettings.DryRun ? AllowedMentions.None : AllowedMentions.All;
        await channel.SendMessageAsync(content, allowedMentions: allowedMentions);
    }

    private static async Task SendDmAsync(IUser user, string content)
    {
        if (AppSettings.DryRun) Console.WriteLine($"[DRY RUN] DM to {user.Username}: {content}");
        else await user.SendMessageAsync(content);
    }

    private static async Task ReactAsync(IMessage message, IEmote emote)
    {
        if (AppSettings.DryRun) Console.WriteLine($"[DRY RUN] React {emote.Name} on message {message.Id}");
        else
        {
            try
            {
                await message.AddReactionAsync(emote);
            }
            catch (Discord.Net.HttpException ex) when ((int)ex.HttpCode == 403)
            {
                Console.WriteLine($"[WARN] Missing permissions to react in #{message.Channel.Name}");
            }
        }
    }

    private static async Task DeleteAsync(IMessage message)
    {
        if (AppSettings.DryRun) Console.WriteLine($"[DRY RUN] Delete message {message.Id} from {message.Author.Username}");
        else await message.DeleteAsync();
    }

    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    public static async Task ReplyToSpecificMessage(ulong channelId, ulong messageId, string replyContent)
    {
        var channel = await DiscordBotClient.GetChannelAsync(channelId) as SocketTextChannel;
        var message = await channel.GetMessageAsync(messageId) as IUserMessage;

        if (message == null)
        {
            Console.WriteLine("Message not found!");
            return;
        }

        await channel.SendMessageAsync(text: replyContent, messageReference: new MessageReference(message.Id));
    }

    private static async Task PlaySound(IAudioClient client, string filePath)
    {
        using var ffmpeg = CreateStream(filePath);
        using var output = ffmpeg.StandardOutput.BaseStream;
        using var discord = client.CreatePCMStream(AudioApplication.Voice);

        try
        {
            await output.CopyToAsync(discord);
        }
        finally
        {
            await discord.FlushAsync();
        }
    }

    private static Process CreateStream(string filePath)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{filePath}\" -filter:a \"volume=1\" -ac 2 -f s16le -ar 48000 pipe:1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.ErrorDataReceived += (sender, e) => Console.WriteLine($"FFmpeg Error: {e.Data}");
        process.Start();
        process.BeginErrorReadLine();

        return process;
    }

    private static async Task ScheduleCheck()
    {
        var eastern = TZConvert.GetTimeZoneInfo("Eastern Standard Time");

        while (true)
        {
            try
            {
                await RealmClient.PostServerAvailability();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PostServerAvailability failed: {ex.Message}");
            }

            var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, eastern);

            if (now.DayOfWeek == DayOfWeek.Tuesday && now.Hour == 17 && now.Minute == 0)
            {
                await SendDroptimizerReminders();
            }
            else if (AppSettings.KeyAudit && IsKeyAuditTime(now) && AppSettings.WowAudit.Any(wa => IsWowAuditActive(wa, now)))
            {
                if (AppSettings.DryRun) Console.WriteLine("[DRY RUN] PostBadPlayers");
                else await RefinedClient.PostBadPlayers();
            }

            // Sleep until the start of the next minute
            var delayUntilNextMinute = TimeSpan.FromSeconds(60 - now.Second);
            await Task.Delay(delayUntilNextMinute);
        }
    }

    private static bool IsKeyAuditTime(DateTime now) =>
        (now.DayOfWeek == DayOfWeek.Friday && now.Hour == 20 && now.Minute == 0) ||
        (now.DayOfWeek == DayOfWeek.Monday && now.Hour == 17 && now.Minute == 0);

    private static bool IsWowAuditActive(WowAuditSettings wowAudit, DateTime now) =>
        (!wowAudit.StartDate.HasValue || now >= wowAudit.StartDate.Value) &&
        (!wowAudit.EndDate.HasValue || now <= wowAudit.EndDate.Value);

    private static async Task SendDroptimizerReminders()
    {
        var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TZConvert.GetTimeZoneInfo("Eastern Standard Time"));

        foreach (var wowAudit in AppSettings.WowAudit.Where(wa => IsWowAuditActive(wa, now)))
        {
            foreach (var channelId in wowAudit.ChannelIds)
            {
                var channel = DiscordBotClient.GetChannel(channelId) as IMessageChannel;
                if (channel != null)
                    await SendMessageAsync(channel, "@here Make sure to post droptimizers or you're not getting loot");
            }
        }
    }
}