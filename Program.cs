
using dev_library.Data;
using dev_refined.Clients;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using Serilog;
using System.Text.RegularExpressions;

public class Program {
    private static DiscordSocketClient DiscordBotClient;
    private static WoWAuditClient WoWAuditClient = new();

    public static async Task Main()
    {
        var discordConfig = new DiscordSocketConfig();
        discordConfig.GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent;
        DiscordBotClient = new DiscordSocketClient(discordConfig);
        AppSettings.Initialize();
        DiscordBotClient.Log += Log;
        DiscordBotClient.MessageReceived += MonitorDroptimizers;
        
        await DiscordBotClient.LoginAsync(TokenType.Bot, AppSettings.DiscordBotToken);
        await DiscordBotClient.StartAsync();

        await Task.Delay(-1);
    }

    public static async Task MonitorDroptimizers(SocketMessage message)
    {
        var raidBotsUrls = ExtractUrls(message.Content);

        if (message.Channel.Id == AppSettings.DroptimizerChannelId && !message.Author.IsBot)
        {
            var validDroptimizers = false;

            if (raidBotsUrls.Count > 0)
            {
                foreach (var raidBotsUrl in raidBotsUrls)
                {
                    validDroptimizers = await WoWAuditClient.UpdateWishlist(message.Content.Split('/').Last());
                }

                if (validDroptimizers)
                {
                    //await ((SocketUserMessage)message).ReplyAsync("I have updated your wishlist pookie.");
                    //await message.AddReactionAsync(new Emoji("✅"));
                    await message.Author.SendMessageAsync("I have updated your [wishlist](https://wowaudit.com/us/zuljin/refined/main/wishlists/personal).");
                    await message.DeleteAsync();
                }
                else
                {
                    await message.Author.SendMessageAsync("You did not send a valid droptimizer");
                    await message.DeleteAsync();
                    //await ((SocketUserMessage)message).ReplyAsync("You did not send a valid droptimizer you fucking retard");
                    //await ((SocketUserMessage)message).ReplyAsync("https://tenor.com/view/idiots-idiot-no-intelligent-life-buzz-lightyear-toy-story-gif-12637964375483433713");
                    // await message.AddReactionAsync(new Emoji("❌"));
                }
            }
            else
            {
                await message.DeleteAsync();
                //await ((SocketUserMessage)message).ReplyAsync("https://tenor.com/view/cat-meme-flying-cat-fling-shut-up-gif-8931012358356675065");
                //await ((SocketUserMessage)message).ReplyAsync("This channel is for droptimizers only. Do not YAP.");
            }
        }
    }

    static List<string> ExtractUrls(string text)
    {
        var pattern  = @"https:\/\/www\.raidbots\.com\/simbot\/report\/[^\s]+";
        var matches = Regex.Matches(text, pattern);

        List<string> urls = new List<string>();
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
}