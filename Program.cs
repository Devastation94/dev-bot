﻿
using dev_library.Clients;
using dev_library.Data;
using dev_library.Data.WoW.Raidbots;
using dev_refined.Clients;
using dev_refined.Data;
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
    private static GoogleSheetsClient GoogleSheetsClient;
    private static ulong ChannelToJoinId = 1344347126330560625;
    private static Timer Timer;

    public static async Task Main()
    {
        var discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };
        DiscordBotClient = new DiscordSocketClient(discordConfig);
        AppSettings.Initialize();
        GoogleSheetsClient = new GoogleSheetsClient();
        DiscordBotClient.Log += Log;
        DiscordBotClient.MessageReceived += MonitorMessages;
        // DiscordBotClient.UserVoiceStateUpdated += OnUserVoiceStateUpdated;

        await DiscordBotClient.LoginAsync(TokenType.Bot, AppSettings.Discord.Token);
        await DiscordBotClient.StartAsync();
        DiscordBotClient.Ready += OnReady;

        //await ReplyToSpecificMessage(840082901890629644, 1340060583533346908, "https://tenor.com/view/who-cares-gif-24186436");

        //Thread.Sleep(5000);

        //await GoogleSheetsClient.UpdateSheet(await RaidBotsClient.GetItemUpgrades(""));

        await Task.Delay(-1);
    }

    private static async Task OnReady()
    {
        CheckScheduleLoop();
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
        // Application messages

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

                        var response = await WoWAuditClient.UpdateWishlist(raidBotsUrl.Split('/').Last(), wowAudit.Guild);
                        validWoWAuditReport = bool.Parse(response.Created);
                        if (response.Base != null)
                        {
                            errors += response.Base[0];
                        }

                        validGoogleSheetsReport = await RaidBotsClient.IsValidReport(raidBotsUrl);

                        if (wowAudit.Guild == "REFINED" && validGoogleSheetsReport && !Constants.ERROR_MESSAGES.Any(em => errors.Contains(em)))
                        {
                            itemUpgrades.AddRange(await RaidBotsClient.GetItemUpgrades(raidBotsUrl.Split('/').Last()));
                        }
                    }

                    if (itemUpgrades.Count > 0)
                    {
                        uploadedToGoogleSheets = await GoogleSheetsClient.UpdateSheet(itemUpgrades);
                    }

                    if (string.IsNullOrWhiteSpace(errors) || uploadedToGoogleSheets)
                    {
                        await message.AddReactionAsync(new Emoji("✅"));
                    }
                    else
                    {
                        await message.Author.SendMessageAsync($"You did not send a valid droptimizer {errors}");
                        await message.DeleteAsync();
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

    private static async Task CheckScheduleLoop()
    {
        var eastern = TZConvert.GetTimeZoneInfo("Eastern Standard Time");

        while (true)
        {
            var now = TimeZoneInfo.ConvertTime(DateTime.UtcNow, eastern);
            if (now.DayOfWeek == DayOfWeek.Monday && now.Hour == 17 && now.Minute == 00)
            {
                var channel = DiscordBotClient.GetChannel(1338053649531928587) as IMessageChannel;
                if (channel != null)
                {
                    await channel.SendMessageAsync("@here Make sure to post droptimizers or you're not getting loot");
                }

                await Task.Delay(TimeSpan.FromMinutes(61)); // Skip past this hour
            }
            else
            {
                await Task.Delay(TimeSpan.FromMinutes(1)); // Check again in a minute
            }
        }
    }
}