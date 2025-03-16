# Project Structure

Wabbit/
├── BotClient/                # Discord bot implementation
│   ├── Commands/            # Bot command groups
│   │   ├── BasicGroup.cs              # Basic utility commands
│   │   ├── ConfigGroup.cs             # Configuration commands
│   │   ├── MapManagementGroup.cs      # Map pool management
│   │   ├── TournamentGroup.cs         # Tournament participant commands
│   │   └── TournamentManagementGroup.cs # Tournament admin commands
│   │
│   ├── Config/             # Bot configuration
│   │   ├── BotConfig.cs              # Bot settings model
│   │   └── ConfigManager.cs          # Configuration management
│   │
│   └── Events/             # Event handling system
│       ├── Components/     # UI component handlers
│       │   ├── Base/       # Base component handling
│       │   │   ├── ComponentHandlerBase.cs    # Abstract base for component handlers
│       │   │   └── DefaultComponentHandler.cs  # Default component handling logic
│       │   ├── Factory/    # Component handler creation
│       │   │   └── ComponentHandlerFactory.cs  # Factory for component handlers
│       │   └── Tournament/ # Tournament-specific components
│       │       ├── AdminThirdPlaceMatchHandler.cs  # Third place match management
│       │       ├── DeckSubmissionHandler.cs        # Deck submission processing
│       │       ├── GameResultHandler.cs            # Match result handling
│       │       ├── MapBanHandler.cs                # Map banning system
│       │       └── TournamentSignupHandler.cs      # Tournament registration
│       │
│       ├── MainHandlers/   # Primary event handlers
│       │   ├── ComponentInteractionHandler.cs  # Component interaction processing
│       │   └── ModalInteractionHandler.cs      # Modal form processing
│       │
│       ├── Messages/       # Message event handlers
│       │   └── Base/     # Base message handling
│       │
│       ├── Modals/        # Modal form handlers
│       │   ├── Base/     # Base modal handling
│       │   │   └── ModalHandlerBase.cs         # Abstract base for modal handlers
│       │   ├── Factory/  # Modal handler creation
│       │   │   └── ModalHandlerFactory.cs      # Factory for modal handlers
│       │   └── Tournament/ # Tournament-specific modals
│       │       ├── DeckSubmissionModalHandler.cs    # Deck submission forms
│       │       ├── DefaultModalHandler.cs           # Default modal processing
│       │       ├── MapBanModalHandler.cs           # Map ban selection forms
│       │       └── TournamentCreationModalHandler.cs # Tournament setup forms
│       │
│       ├── DependencyInjectionStandards.md
│       ├── Event_MessageCreated.cs
│       ├── EventsRefactoringPlan.md
│       ├── HandlerTemplate.md
│       └── ServiceProviderFactoryPattern.md
│
├── Data/                   # Data storage
│   ├── dev/               # Development environment data
│   │   ├── signups.json
│   │   └── tournament_state.json
│   ├── images/           # Image resources
│   └── Maps.cs          # Map definitions
│
├── Documentation/        # Project documentation
│   ├── PlayoffAdjustmentFeature.md
│   ├── ThirdPlaceMatchFeature.md
│   ├── TournamentCodeImprovements.md
│   └── TournamentSystemDesign.md
│
├── Misc/                # Miscellaneous utilities
│   ├── OngoingRounds.cs        # Active rounds tracking
│   └── TournamentVisualization.cs # Tournament visualization
│
├── Models/              # Data models
│   ├── Map.cs          # Map data model
│   ├── MatchStage.cs   # Match stage model
│   ├── Regular1v1.cs   # 1v1 match model
│   ├── Round.cs        # Round model
│   ├── Tournament.cs   # Core tournament model
│   ├── TournamentSignup.cs # Signup management
│   └── TournamentState.cs  # State representation
│
├── ReplayParser/       # Replay parsing functionality
│
├── Services/           # Core business logic
│   ├── Interfaces/    # Service interfaces
│   │   ├── IMapBanExt.cs
│   │   ├── IMatchStatusService.cs
│   │   ├── IRandomProvider.cs
│   │   ├── ITournamentGameService.cs
│   │   ├── ITournamentGroupService.cs
│   │   ├── ITournamentManagerService.cs
│   │   ├── ITournamentMatchService.cs
│   │   ├── ITournamentPlayoffService.cs
│   │   ├── ITournamentRepositoryService.cs
│   │   ├── ITournamentService.cs
│   │   ├── ITournamentStateManager.cs
│   │   └── ITournamentStateService.cs
│   │
│   ├── ServiceHelpers/
│   │   ├── TournamentBracketManager.cs
│   │   ├── TournamentProgressTracker.cs
│   │   ├── TournamentScoreManager.cs
│   │   └── TournamentStateManager.cs
│   │
│   ├── DiscordUtilities.cs
│   ├── MapBanExt.cs
│   ├── MatchStatusService.cs
│   ├── RandomMapExt.cs
│   ├── RandomProvider.cs
│   ├── TournamentGameService.cs
│   ├── TournamentGroupService.cs
│   ├── TournamentManagerService.cs
│   ├── TournamentMatchService.cs
│   ├── TournamentPlayoffService.cs
│   ├── TournamentRepositoryService.cs
│   ├── TournamentService.cs
│   ├── TournamentStateManager.cs
│   └── TournamentStateService.cs
│
├── .env               # Environment variables
├── CodeStandards.md  # Coding standards
├── ConfigFile.json   # Main configuration
├── GroupStageFormat.md # Tournament format documentation
├── Maps.json         # Map data
├── MatchStatusImprovementPlan.md # UI/UX improvements
└── Program.cs        # Application entry point