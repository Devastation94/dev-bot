
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
    private static WoWAuditClient WoWAuditClient = new();
    private static RaidBotsClient RaidBotsClient = new();
    private static RealmClient RealmClient = new();
    private static GoogleSheetsClient GoogleSheetsClient;
    private static ulong ChannelToJoinId = 1344347126330560625;
    private static Timer Timer;
    private static AiClient AiClient;
    private static BattleNetClient BattleNetClient = new();
    private static RefinedClient RefinedClient = new();

    public static async Task Main()
    {
        var discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };
        DiscordBotClient = new DiscordSocketClient(discordConfig);
        AppSettings.Initialize();
        GoogleSheetsClient = new GoogleSheetsClient();
        AiClient = new();
        DiscordBotClient.Log += Log;
        DiscordBotClient.MessageReceived += MonitorMessages;

        await DiscordBotClient.LoginAsync(TokenType.Bot, AppSettings.Discord.Token);
        await DiscordBotClient.StartAsync();
        DiscordBotClient.Ready += OnReady;

        await Task.Delay(-1);
    }

    private static async Task OnReady()
    {
        await ScheduleCheck();
    }

    private static Task OnUserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        _ = Task.Run(async () =>
        {
            await HandleUserVoiceStateUpdated(user, before, after);
        });

        return Task.CompletedTask;
    }

    private static async Task HandleUserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
    {
        if (user.Id != AppSettings.Discord.UserId || before.VoiceChannel != after.VoiceChannel) return;

        var guildUser = user as SocketGuildUser;
        if (guildUser == null)
        {
            Console.WriteLine("User is not a guild member.");
            return;
        }

        var channel = guildUser.VoiceChannel;
        if (channel == null)
        {
            Console.WriteLine("User left all voice channels.");
            return;
        }

        var botUser = channel.Guild.CurrentUser;
        var existingConnection = botUser.VoiceChannel;

        if (existingConnection != null)
        {
            Console.WriteLine($"Bot is already in {existingConnection.Name}, disconnecting first.");
            await existingConnection.DisconnectAsync();
            await Task.Delay(1000); // Ensure disconnection completes
        }

        try
        {
            var audioClient = await channel.ConnectAsync();
            Console.WriteLine($"Joined voice channel: {channel.Name}");

            await PlaySound(audioClient, $"{AppSettings.BasePath}/obama-tony.mp3");

            await audioClient.StopAsync();
            Console.WriteLine("Disconnected from voice channel.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error connecting to voice: {ex.Message}");
        }
    }


    //public static async Task JoinAndLeaveVoiceChannel(ulong channelId)
    //{
    //    var channel = DiscordBotClient.GetChannel(channelId) as SocketVoiceChannel;
    //    if (channel == null)
    //    {
    //        Console.WriteLine("Voice channel not found.");
    //        return;
    //    }

    //    var audioClient = await channel.ConnectAsync(); // Join the voice channel
    //    Console.WriteLine($"Joined voice channel: {channel.Name}");

    //    await PlaySound(audioClient);

    //    await audioClient.StopAsync(); // Proper way to leave
    //    Console.WriteLine("Disconnected from voice channel.");
    //}

    public static async Task MonitorMessages(SocketMessage message)
    {
        // Raidbots messages
        if (AppSettings.WowAudit.Any(wa => wa.ChannelId == message.Channel.Id))
        {
            await MonitorDroptimizers(message);
        }
        else if (message.MentionedUsers.Any(u => u.Username == "Refined Bot") && message.Author.Username != "Refined Bot")
        {
            var hasRole = ((SocketGuildUser)message.Author).Roles.Any(r => AppSettings.GptSettings.AllowedRoles.Contains(r.Name.ToUpper()));
            var mentioningUser = message.Author;

            if (!hasRole)
            {
                await message.Channel.SendMessageAsync($"You lack the power to control me {mentioningUser.Mention} :pig:");
                return;
            }
            else if (message.Channel.Name.ToUpper() != "BOT-SPAM")
            {
                await message.Channel.SendMessageAsync($"If you want me to reply using skynet then message me in #bot-spam :pig:");
                return;
            }

            var response = await AiClient.GetResponse($"{mentioningUser.Mention} said {message.Content}", 1);

            await message.Channel.SendMessageAsync($"{response}");
        }
        else if (message.Channel.Id == 1405315545179619428)
        {

        }
    }

    public static async Task MonitorApplications(SocketUserMessage message)
    {
        var guild = ((SocketGuildChannel)message.Channel).Guild;

        if (message.Author.IsBot)
        {
            var test = await guild.CreateTextChannelAsync("reserved/test");
        }
    }

    public static async Task MonitorDroptimizers(SocketMessage message)
    {
        var raidBotsUrls = Helpers.ExtractUrls(message.Content);
        var wowAudit = AppSettings.WowAudit.First(wa => wa.ChannelId == message.Channel.Id);

        if (!message.Author.IsBot)
        {
            var validWoWAuditReport = false;
            var validGoogleSheetsReport = false;
            var uploadedToGoogleSheets = false;
            var errors = string.Empty;

            if (raidBotsUrls.Count > 0)
            {
                Console.WriteLine($"Begin Processing reports");

                try
                {
                    var itemUpgrades = new List<ItemUpgrade>();

                    foreach (var raidBotsUrl in raidBotsUrls)
                    {
                        Console.WriteLine($"Processing {raidBotsUrl}");
                        var currentItemUpgrades = new List<ItemUpgrade>();

                        var response = await WoWAuditClient.UpdateWishlist(raidBotsUrl.Split('/').Last(), wowAudit.Guild);
                        validWoWAuditReport = bool.Parse(response.Created);

                        if (!validWoWAuditReport)
                        {
                            errors += response.Base[0];
                            await message.Author.SendMessageAsync($"You did not send a valid droptimizer {errors}");
                            await message.DeleteAsync();
                            return;
                        }

                        validGoogleSheetsReport = await RaidBotsClient.IsValidReport(raidBotsUrl);

                        if (wowAudit.Guild == "REFINED" && validGoogleSheetsReport)
                        {
                            itemUpgrades = await RaidBotsClient.GetItemUpgrades(itemUpgrades, raidBotsUrl.Split('/').Last());
                        }
                    }

                    if (itemUpgrades.Count > 0)
                    {
                        uploadedToGoogleSheets = await GoogleSheetsClient.UpdateSheet(itemUpgrades);
                    }


                    await message.AddReactionAsync(new Emoji("✅"));

                    //   await message.Author.SendMessageAsync($"You did not send a valid droptimizer {errors}");
                    // await message.DeleteAsync();


                    if (message.Author.Id == 285277811348996097)
                    {
                        await message.Author.SendMessageAsync("Pig");
                    }
                    else if (message.Author.Id == 221473784174084097)
                    {
                        await message.Author.SendMessageAsync("Oink for me Piggie");
                    }
                }
                catch (Exception ex)
                {
                    await message.AddReactionAsync(new Emoji("❌"));
                    await message.Author.SendMessageAsync("WoWAudit is currently down. Please try again later. Also compliment epic on his tuna can");
                    Console.WriteLine(ex.Message);
                    throw;
                }
            }
            else
            {
                if (!((SocketGuildUser)message.Author).GuildPermissions.Administrator)
                {
                    Console.WriteLine(message.Content);
                    await message.DeleteAsync();
                }
            }
        }
    }

    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }

    public static async Task ReplyToSpecificMessage(ulong channelId, ulong messageId, string replyContent)
    {
        var channel = await DiscordBotClient.GetChannelAsync(channelId) as SocketTextChannel;
        // Fetch the message by ID
        var message = await channel.GetMessageAsync(messageId) as IUserMessage;

        if (message != null)
        {
            // Create a message reference to reply to the specific message
            var reference = new MessageReference(message.Id);

            // Reply to the message with the specified content
            await channel.SendMessageAsync(text: replyContent, messageReference: reference);
        }
        else
        {
            Console.WriteLine("Message not found!");
        }
    }

    private static async Task PlaySound(IAudioClient client, string filePath)
    {
        using (var ffmpeg = CreateStream(filePath))
        using (var output = ffmpeg.StandardOutput.BaseStream)
        using (var discord = client.CreatePCMStream(AudioApplication.Voice))
        {
            try
            {
                await output.CopyToAsync(discord);
            }
            finally
            {
                await discord.FlushAsync();
            }
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
           await RealmClient.PostServerAvailability();
           // var realms = await BattleNetClient.GetRealms();
            //await BattleNetClient.GetAuctions(realms);
            var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, eastern);
            if (now.DayOfWeek == DayOfWeek.Tuesday && now.Hour == 17 && now.Minute == 00)
            {
                var channel = DiscordBotClient.GetChannel(1339754498549219329) as IMessageChannel;
                if (channel != null)
                {
                    await channel.SendMessageAsync("@here Make sure to post droptimizers or you're not getting loot");
                }

                await Task.Delay(TimeSpan.FromSeconds(61));
            }
            else if (AppSettings.KeyAudit && (now.DayOfWeek == DayOfWeek.Friday && now.Hour == 20 && now.Minute == 0 || now.DayOfWeek == DayOfWeek.Monday && now.Hour == 17 && now.Minute == 0))
            {
                await RefinedClient.PostBadPlayers();
                await Task.Delay(TimeSpan.FromMinutes(61));
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}