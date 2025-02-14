
using dev_library.Data;
using dev_refined.Clients;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json;
using Serilog;

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
        if (message.Channel.Id == AppSettings.DroptimizerChannelId && !message.Author.IsBot && message.Content.StartsWith("https://www.raidbots.com/simbot/report/"))
        {
            var updatedWishlist = await WoWAuditClient.UpdateWishlist(message.Content.Split('/').Last());

            if (updatedWishlist)
            {
                await message.AddReactionAsync(new Emoji("✅"));
            }
        }
    }

    private static Task Log(LogMessage msg)
    {
        Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
}