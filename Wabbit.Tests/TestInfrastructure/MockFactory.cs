using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Moq;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Factory for creating and configuring mocks in a way that avoids expression tree issues
    /// </summary>
    public static class MockFactory
    {
        /// <summary>
        /// Creates a pre-configured DiscordMessage mock
        /// </summary>
        public static Mock<DiscordMessage> CreateDiscordMessageMock()
        {
            var mock = new Mock<DiscordMessage>();

            // Use simple, non-optional parameters for setup to avoid expression tree issues
            mock.Setup(m => m.ModifyAsync(It.IsAny<DiscordEmbed>()))
                .Returns(Task.FromResult(mock.Object));

            return mock;
        }

        /// <summary>
        /// Creates a pre-configured DiscordChannel mock
        /// </summary>
        public static Mock<DiscordChannel> CreateDiscordChannelMock(Mock<DiscordMessage> messageMock)
        {
            var mock = new Mock<DiscordChannel>();

            // Use simple, non-optional parameters for setup to avoid expression tree issues
            mock.Setup(c => c.SendMessageAsync(It.IsAny<string>()))
                .Returns(Task.FromResult(messageMock.Object));

            // Use callback for GetMessageAsync to avoid expression tree issues with optional params
            mock.Setup(c => c.GetMessageAsync(It.IsAny<ulong>()))
                .Returns((ulong id) => Task.FromResult(messageMock.Object));

            return mock;
        }

        /// <summary>
        /// Creates a pre-configured DiscordClient mock
        /// </summary>
        public static Mock<DiscordClient> CreateDiscordClientMock()
        {
            return new Mock<DiscordClient>();
        }
    }
}