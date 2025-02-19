
using dev_library.Data;
using dev_refined.Clients;
using Discord;
using Discord.WebSocket;
using System.Text.RegularExpressions;

public class Program
{
    private static DiscordSocketClient DiscordBotClient;
    private static WoWAuditClient WoWAuditClient = new();

    public static async Task Main()
    {
        var discordConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent
        };
        DiscordBotClient = new DiscordSocketClient(discordConfig);
        AppSettings.Initialize();
        DiscordBotClient.Log += Log;
        DiscordBotClient.MessageReceived += MonitorMessages;

        await DiscordBotClient.LoginAsync(TokenType.Bot, AppSettings.DiscordBotToken);
        await DiscordBotClient.StartAsync();

        //Thread.Sleep(5000);

        //await ReplyToSpecificMessage(840082901890629644, 1340060583533346908, "https://tenor.com/view/who-cares-gif-24186436");

        //await JoinAndLeaveVoiceChannel(933433126200443001);

        await Task.Delay(-1);
    }

    public static async Task JoinAndLeaveVoiceChannel(ulong channelId)
    {
        var channel = DiscordBotClient.GetChannel(channelId) as SocketVoiceChannel;
        if (channel == null)
        {
            Console.WriteLine("Voice channel not found.");
            return;
        }

        var audioClient = await channel.ConnectAsync(); // Join the voice channel
        Console.WriteLine($"Joined voice channel: {channel.Name}");

        //await Task.Delay(5000); // Stay in channel for 5 seconds (adjust as needed)

        //await audioClient.StopAsync(); // Proper way to leave
        //Console.WriteLine("Disconnected from voice channel.");
    }

    public static async Task MonitorMessages(SocketMessage message)
    {
        // Raidbots messages
        if (AppSettings.WoWAudit.Any(wa => wa.ChannelId == message.Channel.Id))
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
        var raidBotsUrls = ExtractUrls(message.Content);
        var wowAudit = AppSettings.WoWAudit.First(wa => wa.ChannelId == message.Channel.Id);

        if (!message.Author.IsBot)
        {
            var validDroptimizers = false;
            var errors = string.Empty;

            if (raidBotsUrls.Count > 0)
            {
                foreach (var raidBotsUrl in raidBotsUrls)
                {
                    var response = await WoWAuditClient.UpdateWishlist(message.Content.Split('/').Last(), wowAudit.Guild);
                    validDroptimizers = bool.Parse(response.Created);
                    if (response.Base != null)
                    {
                        errors += response.Base[0];
                    }
                }

                if (validDroptimizers)
                {
                    await message.AddReactionAsync(new Emoji("✅"));
                }
                else
                {
                    await message.Author.SendMessageAsync($"You did not send a valid droptimizer {errors}");
                    await message.DeleteAsync();
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

    static List<string> ExtractUrls(string text)
    {
        var pattern = @"https:\/\/(www\.raidbots\.com\/simbot\/report|questionablyepic\.com\/live\/upgradereport)[^\s]*";
        var matches = Regex.Matches(text, pattern);

       var urls = new List<string>();
        foreach (Match match in matches)
        {
            urls.Add(match.Value);
        }

        return urls;
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
}