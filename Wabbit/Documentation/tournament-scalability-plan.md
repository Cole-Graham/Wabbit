# Tournament Scalability and Resilience Improvement Plan

## Overview

This document outlines a comprehensive plan to enhance Wabbit's tournament system for better handling of concurrent matches. The primary goal is to reduce race conditions, improve resource management, and ensure a smooth experience for tournament participants, especially in high-load scenarios with 4-6+ concurrent matches.

## Current Issues

1. **Global Lock Bottleneck**: Using a static semaphore across all matches creates unnecessary waiting
2. **Resource Contention**: Shared non-thread-safe collections cause race conditions
3. **Discord Rate Limiting**: Too many concurrent API calls during critical moments
4. **Fire-and-Forget Tasks**: Unhandled exceptions in background tasks
5. **Message State Management**: Orphaned state when messages are deleted
6. **Memory Growth**: Unbounded collections without proper cleanup

## Improvement Plan

### 1. Implement Per-Match Locking

- [x] Replace global `_deckConfirmationLock` with per-match locks
- [x] Use `ConcurrentDictionary<string, SemaphoreSlim>` to store locks by match ID
- [ ] Add timeout handling to prevent deadlocks
- [ ] Implement cleanup mechanism for removing stale locks

```csharp
// Example implementation
private readonly ConcurrentDictionary<string, SemaphoreSlim> _matchLocks = new();

// Usage
var matchLock = _matchLocks.GetOrAdd(round.Id, _ => new SemaphoreSlim(1, 1));
try
{
    if (!await matchLock.WaitAsync(TimeSpan.FromSeconds(10)))
    {
        _logger.LogWarning($"Timed out waiting for match lock: {round.Id}");
        return;
    }
    
    // Critical section here
}
finally
{
    matchLock.Release();
}
```

### 2. Improve State Management

- [ ] Make `_channelToMessageMap` thread-safe with `ConcurrentDictionary`
- [ ] Add message ID validation to ensure correct updates
- [ ] Implement message recreation with exponential backoff on failure
- [ ] Add transaction-like operations for critical state changes
- [ ] Consider persistent storage for message mappings

```csharp
// Instead of Dictionary<ulong, ulong>
private readonly ConcurrentDictionary<ulong, ulong> _channelToMessageMap = new();

// Example: Atomic update with fallback
public async Task<bool> TryUpdateMessageAsync(DiscordChannel channel, DiscordEmbed embed)
{
    if (_channelToMessageMap.TryGetValue(channel.Id, out ulong messageId))
    {
        try
        {
            var message = await channel.GetMessageAsync(messageId);
            await message.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed));
            return true;
        }
        catch (NotFoundException)
        {
            _channelToMessageMap.TryRemove(channel.Id, out _);
            return false;
        }
    }
    return false;
}
```

### 3. Implement Resource Cleanup

- [ ] Add background service for cleaning up stale resources
- [ ] Implement time-based expiration for cached resources
- [ ] Add proper tracking of all background tasks
- [ ] Implement periodic health checks for orphaned states

```csharp
// Example background service
public class ResourceCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupStaleResources();
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
    
    private Task CleanupStaleResources()
    {
        // Clean up stale locks, cooldowns, etc.
    }
}
```

### 4. Discord API Resilience

- [ ] Implement rate limit tracking and throttling
- [ ] Add exponential backoff for failed API calls
- [ ] Group and batch updates when possible
- [ ] Prioritize critical updates during high-load periods

```csharp
// Example exponential backoff implementation
public async Task<T> ExecuteWithBackoffAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
{
    int attempt = 0;
    while (true)
    {
        try
        {
            return await operation();
        }
        catch (RateLimitException)
        {
            if (++attempt >= maxRetries)
                throw;
                
            int delay = (int)Math.Pow(2, attempt) * 1000; // Exponential backoff
            await Task.Delay(delay);
        }
    }
}
```

### 5. Improve Task Management

- [ ] Replace fire-and-forget patterns with proper tracked tasks
- [ ] Implement a task registry for in-flight operations
- [ ] Add circuit breakers for repeated failures
- [ ] Implement proper cancellation token support

```csharp
// Task registry example
public class TaskRegistry
{
    private readonly ConcurrentDictionary<string, TaskInfo> _tasks = new();
    
    public Task RegisterTask(string key, Func<CancellationToken, Task> taskFunc)
    {
        var cts = new CancellationTokenSource();
        var task = taskFunc(cts.Token).ContinueWith(t => {
            _tasks.TryRemove(key, out _);
            if (t.IsFaulted)
                _logger.LogError(t.Exception, $"Task {key} failed");
        });
        
        _tasks[key] = new TaskInfo { Task = task, CancellationTokenSource = cts };
        return task;
    }
    
    public bool TryCancelTask(string key)
    {
        if (_tasks.TryGetValue(key, out var info))
        {
            info.CancellationTokenSource.Cancel();
            return true;
        }
        return false;
    }
    
    private class TaskInfo
    {
        public Task Task { get; set; }
        public CancellationTokenSource CancellationTokenSource { get; set; }
    }
}
```

### 6. Exception Handling and Logging

- [ ] Implement structured logging for better error analysis
- [ ] Add correlation IDs for tracing requests across components
- [ ] Create automated error reporting for critical failures
- [ ] Implement health monitoring dashboards
- [ ] Add telemetry for performance bottlenecks

```csharp
// Example correlation ID usage
public async Task ProcessMatchAsync(Round round, string correlationId = null)
{
    correlationId ??= Guid.NewGuid().ToString();
    
    using (_logger.BeginScope(new Dictionary<string, object> {
        ["CorrelationId"] = correlationId,
        ["RoundId"] = round.Id,
        ["MatchType"] = round.Type
    }))
    {
        _logger.LogInformation("Processing match");
        // Implementation
    }
}
```

### 7. UI/UX Improvements

- [ ] Add loading indicators for long operations
- [ ] Implement progressive UI updates to show progress
- [ ] Add better error messaging for users
- [ ] Implement auto-recovery for common failure scenarios
- [ ] Add client-side validation to reduce server load

## Implementation Phases

### Phase 1: Critical Fixes (1-2 weeks)

- [ ] Implement per-match locking
- [ ] Make shared collections thread-safe
- [ ] Add basic retry mechanisms
- [ ] Fix message handling race conditions

### Phase 2: Resilience Improvements (2-3 weeks)

- [ ] Implement proper task tracking
- [ ] Add structured logging with correlation IDs
- [ ] Implement resource cleanup service
- [ ] Add comprehensive error handling

### Phase 3: Scalability Enhancements (3-4 weeks)

- [ ] Optimize Discord API usage
- [ ] Implement performance monitoring
- [ ] Add circuit breakers and load management
- [ ] Implement advanced retry strategies

### Phase 4: UI/UX and Testing (2-3 weeks)

- [ ] Improve user feedback during operations
- [ ] Implement comprehensive load testing
- [ ] Add automated recovery for edge cases
- [ ] Create monitoring dashboards

## Metrics for Success

- [ ] Zero tournament-breaking bugs in production
- [ ] Successfully handle 10+ concurrent matches without issues
- [ ] 99.9% success rate for all tournament operations
- [ ] Mean time to recovery < 5 seconds for non-critical failures
- [ ] Average response time < 2 seconds for all user interactions 