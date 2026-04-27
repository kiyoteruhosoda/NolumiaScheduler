# Architecture Rules

Applies to all tasks touching `lib/`.

## Layer Boundaries

Dependencies must always point inward:
```
Presentation → Application → Domain
Infrastructure → Domain (implements interfaces)
```

**Domain must never import:**
- Flutter (`package:flutter`)
- SQLite packages
- HTTP packages
- JSON serialization libraries
- State management libraries

**Presentation must never:**
- Write SQL
- Instantiate repositories directly
- Contain business rules

**Infrastructure must never:**
- Contain business logic
- Be imported by Domain or Application

## Naming Conventions

| Type | Example |
|------|---------|
| Entity | `Word`, `StudySession` |
| Value Object | `WordId`, `PointAmount` |
| Repository interface | `WordRepository` |
| Repository implementation | `SqliteWordRepository` |
| UseCase | `CreateWordUseCase`, `DeleteWordUseCase` |
| DAO | `WordDao` |
| Row model | `WordRow` |
| ViewModel | `WordListViewModel` |

Forbidden names: `Helper`, `Manager`, `CommonService`, `Util` (as class names).

## DDD Rules

- Entities have identity (ID field).
- Value Objects are immutable; equality by value, not reference.
- Repository interfaces live in `domain/repositories/`.
- Repository implementations live in `infrastructure/repositories/`.
- One UseCase per business operation.

## Dependency Injection

- All wiring happens in `app/di/`.
- Never use `new RepositoryImpl()` inside UI classes or UseCases.
- UseCases receive repositories via constructor injection.

## SQLite

- All SQLite access lives exclusively in `infrastructure/`.
- DB row model (`*Row`) and domain entity (`*`) are always separate classes.
- Use a mapper class to convert between them.
- All schema changes require a versioned migration in `infrastructure/db/sqlite/migrations/`.
- DAOs handle SQL; Repositories handle domain abstraction.

## Offline-First

Include these fields on entities that sync:
- `createdAt`, `updatedAt`
- `syncStatus`: `pendingCreate` | `pendingUpdate` | `pendingDelete` | `synced` | `conflict`
- `version`

## OOP / SOLID

- One class, one responsibility.
- Prefer composition and interfaces over inheritance.
- Domain models are immutable by default.
- Use polymorphism (strategy interfaces) for variant behavior (quiz types, sync strategies, notification strategies).
- Repository interfaces must be narrow — no "god interfaces".
