using DSharpPlus;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Wabbit.Misc;
using Wabbit.Models;
using Wabbit.Services;
using Wabbit.Services.Interfaces;

namespace Wabbit.Tests.TestInfrastructure
{
    /// <summary>
    /// Base class for all test fixtures providing common mock setup and utilities
    /// </summary>
    public abstract class TestBase
    {
        // Common mocks
        protected Mock<ILogger<TournamentService>> MockTournamentServiceLogger { get; private set; }
        protected Mock<ILogger<TournamentGroupService>> MockTournamentGroupServiceLogger { get; private set; }
        protected Mock<ILogger<TournamentMapService>> MockTournamentMapServiceLogger { get; private set; }
        protected Mock<ILogger<MatchStatusService>> MockMatchStatusServiceLogger { get; private set; }

        protected Mock<ITournamentManagerService> MockTournamentManagerService { get; private set; }
        protected Mock<ITournamentGroupService> MockTournamentGroupService { get; private set; }
        protected Mock<ITournamentStateService> MockTournamentStateService { get; private set; }
        protected Mock<ITournamentPlayoffService> MockTournamentPlayoffService { get; private set; }
        protected Mock<ITournamentStateValidator> MockTournamentStateValidator { get; private set; }
        protected Mock<ITournamentMatchOperationsService> MockTournamentMatchOperationsService { get; private set; }
        protected Mock<ITournamentScoreManager> MockTournamentScoreManager { get; private set; }
        protected Mock<IMapService> MockMapService { get; private set; }
        protected Mock<IRandomProvider> MockRandomProvider { get; private set; }
        protected Mock<Random> MockRandom { get; private set; }

        protected Mock<DiscordClient> MockDiscordClient { get; private set; }
        protected Mock<DiscordChannel> MockDiscordChannel { get; private set; }
        protected Mock<DiscordMessage> MockDiscordMessage { get; private set; }

        protected TestBase()
        {
            // Initialize all common mocks
            MockTournamentServiceLogger = new Mock<ILogger<TournamentService>>();
            MockTournamentGroupServiceLogger = new Mock<ILogger<TournamentGroupService>>();
            MockTournamentMapServiceLogger = new Mock<ILogger<TournamentMapService>>();
            MockMatchStatusServiceLogger = new Mock<ILogger<MatchStatusService>>();

            MockTournamentManagerService = new Mock<ITournamentManagerService>();
            MockTournamentGroupService = new Mock<ITournamentGroupService>();
            MockTournamentStateService = new Mock<ITournamentStateService>();
            MockTournamentPlayoffService = new Mock<ITournamentPlayoffService>();
            MockTournamentStateValidator = new Mock<ITournamentStateValidator>();
            MockTournamentMatchOperationsService = new Mock<ITournamentMatchOperationsService>();
            MockTournamentScoreManager = new Mock<ITournamentScoreManager>();
            MockMapService = new Mock<IMapService>();

            MockRandom = new Mock<Random>();
            MockRandomProvider = new Mock<IRandomProvider>();
            MockRandomProvider.Setup(rp => rp.Instance).Returns(MockRandom.Object);

            MockDiscordClient = new Mock<DiscordClient>();
            MockDiscordChannel = new Mock<DiscordChannel>();
            MockDiscordMessage = new Mock<DiscordMessage>();

            // Set up default Discord message behavior
            MockDiscordMessage.Setup(m => m.ModifyAsync(It.IsAny<Action<DSharpPlus.Entities.DiscordMessageBuilder>>()))
                .Returns(Task.FromResult(MockDiscordMessage.Object));

            MockDiscordChannel.Setup(c => c.SendMessageAsync(It.IsAny<DSharpPlus.Entities.DiscordMessageBuilder>()))
                .Returns(Task.FromResult(MockDiscordMessage.Object));

            MockDiscordChannel.Setup(c => c.GetMessageAsync(It.IsAny<ulong>()))
                .Returns(Task.FromResult(MockDiscordMessage.Object));

            // Set up mock Random behavior
            MockRandom.Setup(r => r.Next(It.IsAny<int>()))
                .Returns((int max) => max > 0 ? max / 2 : 0); // Predictable "random" results
            MockRandom.Setup(r => r.Next(It.IsAny<int>(), It.IsAny<int>()))
                .Returns((int min, int max) => min + ((max - min) / 2));
        }

        /// <summary>
        /// Creates a TournamentService instance with the mocked dependencies
        /// </summary>
        protected TournamentService CreateTournamentService()
        {
            return new TournamentService(
                MockTournamentServiceLogger.Object,
                MockTournamentManagerService.Object,
                MockTournamentGroupService.Object,
                MockTournamentStateService.Object,
                MockTournamentPlayoffService.Object,
                MockTournamentStateValidator.Object);
        }

        /// <summary>
        /// Creates a TournamentGroupService instance with the mocked dependencies
        /// </summary>
        protected TournamentGroupService CreateTournamentGroupService()
        {
            return new TournamentGroupService(
                MockRandomProvider.Object,
                MockTournamentGroupServiceLogger.Object,
                MockTournamentMatchOperationsService.Object,
                MockTournamentScoreManager.Object,
                MockTournamentStateValidator.Object);
        }

        /// <summary>
        /// Creates a TournamentMapService instance with the mocked dependencies
        /// </summary>
        protected TournamentMapService CreateTournamentMapService()
        {
            return new TournamentMapService(
                MockTournamentMapServiceLogger.Object,
                MockRandomProvider.Object,
                MockMapService.Object);
        }

        /// <summary>
        /// Creates a MatchStatusService instance with the mocked dependencies
        /// </summary>
        protected MatchStatusService CreateMatchStatusService()
        {
            var mapService = CreateTournamentMapService();

            return new MatchStatusService(
                MockMatchStatusServiceLogger.Object,
                mapService);
        }

        /// <summary>
        /// Helper method to set up mock map service with default maps
        /// </summary>
        protected void SetupDefaultMapService()
        {
            // Create a default list of maps for testing
            var defaultMaps = new List<string> { "Map1", "Map2", "Map3", "Map4", "Map5", "Map6", "Map7", "Map8" };

            // Set up the map service to return these maps
            MockMapService.Setup(m => m.GetAllMaps())
                .Returns(defaultMaps.ConvertAll(name => new Map { Name = name, IsInTournamentPool = true }));
        }
    }
}