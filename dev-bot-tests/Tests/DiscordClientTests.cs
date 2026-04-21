using dev_library.Data;
using dev_refined.Clients;

namespace dev_bot_tests.Tests
{
    public class DiscordClientTests
    {
        [Fact]
        public async Task PostToChannel_WhenDelegateIsSet_InvokesDelegateWithCorrectArgs()
        {
            ulong? receivedChannelId = null;
            string? receivedMessage = null;

            DiscordClient.SendMessageAsync = (channelId, message) =>
            {
                receivedChannelId = channelId;
                receivedMessage = message;
                return Task.CompletedTask;
            };

            var sut = new DiscordClient();
            await sut.PostToChannel(12345ul, "Hello world");

            Assert.Equal(12345ul, receivedChannelId);
            Assert.Equal("Hello world", receivedMessage);
        }

        [Fact]
        public async Task PostToChannel_WhenDelegateIsNull_DoesNotThrow()
        {
            DiscordClient.SendMessageAsync = null;

            var sut = new DiscordClient();
            var ex = await Record.ExceptionAsync(() => sut.PostToChannel(99ul, "test"));

            Assert.Null(ex);
        }

        [Fact]
        public async Task PostWebHook_WhenDelegateIsSet_PostsGroupedByStore()
        {
            AppSettings.Guilds = new[]
            {
                new GuildSettings
                {
                    Name = "POKEMON",
                    Channels = new Dictionary<string, ulong> { ["general"] = 777ul },
                    Features = new GuildFeatures()
                }
            };

            ulong? postedChannelId = null;
            string? postedMessage = null;

            DiscordClient.SendMessageAsync = (channelId, message) =>
            {
                postedChannelId = channelId;
                postedMessage = message;
                return Task.CompletedTask;
            };

            var searchResults = new List<Search>
            {
                new Search(
                    keyword: "PS5",
                    store: "BestBuy",
                    products: new List<Product>
                    {
                        new Product("PlayStation 5", "699", "https://example.com/ps5")
                    })
            };

            var sut = new DiscordClient();
            await sut.PostWebHook(searchResults);

            Assert.Equal(777ul, postedChannelId);
            Assert.Contains("BestBuy", postedMessage);
            Assert.Contains("PS5", postedMessage);
        }
    }
}
