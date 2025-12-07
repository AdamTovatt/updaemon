---
name: Command Pattern Refactoring
overview: Refactor the command architecture to use a proper command pattern with interface-based design, separating argument parsing from command execution, and making the system more maintainable and testable.
todos:
  - id: create-infrastructure
    content: Create ICommand, ICommandFactory, CommandFactory, ArgumentParser, UnknownCommandException
    status: completed
  - id: refactor-commands
    content: Refactor all 8 commands to implement ICommand interface
    status: completed
    dependencies:
      - create-infrastructure
  - id: refactor-executor
    content: Simplify CommandExecutor to thin routing layer using factory
    status: completed
    dependencies:
      - refactor-commands
  - id: update-di
    content: Update Program.cs DI registration for new architecture
    status: completed
    dependencies:
      - refactor-executor
  - id: update-tests
    content: Update all command tests and CommandExecutorTests for new signatures
    status: completed
    dependencies:
      - update-di
  - id: verify
    content: Run full test suite and verify CLI functionality
    status: completed
    dependencies:
      - update-tests
---

# Command Pattern Refactoring - Phase 1

## Overview

Refactor all 8 commands to implement a unified ICommand interface, extract argument parsing from CommandExecutor into individual commands, and introduce a factory pattern for command discovery. This is a breaking change that will make the codebase cleaner and more maintainable.

## 1. Create New Infrastructure

### Create ICommand Interface

**File:** [`Updaemon/Commands/ICommand.cs`](Updaemon/Commands/ICommand.cs) (new)

```csharp
public interface ICommand
{
    string Name { get; }
    string Description { get; }
    string Usage { get; }
    Task<int> ExecuteAsync(string[] args, CancellationToken cancellationToken);
    string GetDetailedHelp();
}
```

### Create Command Factory

**Files:**

- [`Updaemon/Commands/ICommandFactory.cs`](Updaemon/Commands/ICommandFactory.cs) (new)
- [`Updaemon/Commands/CommandFactory.cs`](Updaemon/Commands/CommandFactory.cs) (new)

Factory with explicit AOT-friendly command registration (no reflection).

### Create ArgumentParser Helper

**File:** [`Updaemon/Commands/ArgumentParser.cs`](Updaemon/Commands/ArgumentParser.cs) (new)

Helper class with methods like `GetPositional()`, `TryGetRequiredPositional()`, `GetFlag()`, `TryGetRequiredFlag()`, `ValidateMinimumArgs()` to reduce boilerplate in commands.

### Create UnknownCommandException

**File:** [`Updaemon/Commands/UnknownCommandException.cs`](Updaemon/Commands/UnknownCommandException.cs) (new)

Simple exception type for unknown commands.

## 2. Refactor CommandExecutor

**File:** [`Updaemon/Commands/CommandExecutor.cs`](Updaemon/Commands/CommandExecutor.cs)

Transform from 200+ line switch statement to thin routing layer:

- Remove all command instance fields (8 fields)
- Remove all command dependencies from constructor
- Add `ICommandFactory` and version extraction
- Replace switch statement with factory lookup
- Add help command support (`updaemon help <command>`)
- Generate help text dynamically from registered commands
- Display version (v0.6.0) at top of help output

## 3. Refactor All Commands to Implement ICommand

All 8 commands need updates:

### [`Updaemon/Commands/NewCommand.cs`](Updaemon/Commands/NewCommand.cs)

- Implement ICommand
- Add Name, Description, Usage, GetDetailedHelp properties/methods
- Change `ExecuteAsync(string, string, ...)` to `ExecuteAsync(string[], ...)`
- Parse `args[0] `(app-name) and `--from` flag internally
- Use ArgumentParser helper

### [`Updaemon/Commands/UpdateCommand.cs`](Updaemon/Commands/UpdateCommand.cs)

- Implement ICommand
- Add required properties/methods
- Change `ExecuteAsync(string?, ...)` to `ExecuteAsync(string[], ...)`
- Parse optional `args[0]` for app name

### [`Updaemon/Commands/SetRemoteCommand.cs`](Updaemon/Commands/SetRemoteCommand.cs)

- Implement ICommand
- Add required properties/methods
- Change `ExecuteAsync(string, string, ...)` to `ExecuteAsync(string[], ...)`
- Parse `args[0]` and `args[1]` with validation

### [`Updaemon/Commands/SetExecNameCommand.cs`](Updaemon/Commands/SetExecNameCommand.cs)

- Implement ICommand
- Add required properties/methods
- Change `ExecuteAsync(string, string, ...)` to `ExecuteAsync(string[], ...)`
- Parse `args[0]` and `args[1]` with validation

### [`Updaemon/Commands/DistInstallCommand.cs`](Updaemon/Commands/DistInstallCommand.cs)

- Implement ICommand
- Add required properties/methods
- Change `ExecuteAsync(string?, string, ...)` to `ExecuteAsync(string[], ...)`
- Parse optional `--as` flag and required URL/name from args
- Move URL resolution logic from CommandExecutor into the command

### [`Updaemon/Commands/DistListCommand.cs`](Updaemon/Commands/DistListCommand.cs)

- Implement ICommand
- Add required properties/methods
- Signature already takes no args, minimal changes needed

### [`Updaemon/Commands/SecretSetCommand.cs`](Updaemon/Commands/SecretSetCommand.cs)

- Implement ICommand
- Add required properties/methods
- Change `ExecuteAsync(string, string, string, ...)` to `ExecuteAsync(string[], ...)`
- Parse `args[0]`, `args[1]`, `args[2]` with validation

### [`Updaemon/Commands/TimerCommand.cs`](Updaemon/Commands/TimerCommand.cs)

- Implement ICommand
- Add required properties/methods
- Change `ExecuteAsync(string?, ...)` to `ExecuteAsync(string[], ...)`
- Parse optional `args[0]` for interval

## 4. Update Dependency Injection

**File:** [`Updaemon/Program.cs`](Updaemon/Program.cs)

- Change all 8 command registrations from `AddSingleton<>` to `AddTransient<>`
- Add `services.AddSingleton<ICommandFactory, CommandFactory>();`
- Remove individual command injections from services setup

## 5. Update Tests

### [`Updaemon.Tests/Commands/CommandExecutorTests.cs`](Updaemon.Tests/Commands/CommandExecutorTests.cs)

Major simplification:

- Remove `CreateCommandExecutor` helper (or simplify dramatically)
- Remove argument parsing tests (move to individual command tests)
- Keep routing tests (NoArgs, UnknownCommand)
- Keep error handling tests
- Update to use MockCommandFactory instead of real commands

### Individual Command Tests

Update all 8 test files:

- [`NewCommandTests.cs`](Updaemon.Tests/Commands/NewCommandTests.cs)
- [`UpdateCommandTests.cs`](Updaemon.Tests/Commands/UpdateCommandTests.cs)
- [`SetRemoteCommandTests.cs`](Updaemon.Tests/Commands/SetRemoteCommandTests.cs)
- [`SetExecNameCommandTests.cs`](Updaemon.Tests/Commands/SetExecNameCommandTests.cs)
- [`DistInstallCommandTests.cs`](Updaemon.Tests/Commands/DistInstallCommandTests.cs)
- [`DistListCommandTests.cs`](Updaemon.Tests/Commands/DistListCommandTests.cs)
- [`SecretSetCommandTests.cs`](Updaemon.Tests/Commands/SecretSetCommandTests.cs)
- No existing TimerCommandTests file - may need to create one

Changes per test file:

- Add argument parsing tests (missing args, invalid args)
- Update `ExecuteAsync` calls to pass `string[]` instead of parsed args
- Keep existing behavior validation tests

### Create Test Helpers

**Files:**

- [`Updaemon.Tests/Mocks/MockCommandFactory.cs`](Updaemon.Tests/Mocks/MockCommandFactory.cs) (new)
- [`Updaemon.Tests/Mocks/MockCommand.cs`](Updaemon.Tests/Mocks/MockCommand.cs) (new)

For testing CommandExecutor in isolation.

## Testing Strategy

1. Run existing tests before changes to establish baseline
2. Make infrastructure changes first (ICommand, Factory, ArgumentParser)
3. Refactor commands one at a time, running tests after each
4. Update CommandExecutor last
5. Update all tests
6. Final full test run to ensure no regressions