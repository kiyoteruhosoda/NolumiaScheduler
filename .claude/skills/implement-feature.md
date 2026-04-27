# Skill: Implement Feature End-to-End

Use this skill when adding a complete new feature that touches multiple layers.

## Steps

1. **Clarify scope**
   - What is the business operation? (e.g., "user adds a vocabulary word")
   - What domain entity is involved?
   - What does the user see?

2. **Domain layer first**
   - Create or update the Entity in `domain/entities/`
   - Create required Value Objects in `domain/value_objects/`
   - Define or extend the Repository interface in `domain/repositories/`
   - Add Domain Services in `domain/services/` if logic spans multiple entities

3. **Application layer**
   - Create a UseCase in `application/usecases/` (one class, one operation)
   - Define input/output DTOs in `application/dto/` if needed
   - UseCase receives repository via constructor; calls domain, returns result

4. **Infrastructure layer**
   - Create or update `*Row` model in `infrastructure/db/sqlite/rows/`
   - Create or update DAO in `infrastructure/db/sqlite/dao/`
   - Create or update mapper in `infrastructure/mappers/`
   - Implement the Repository interface in `infrastructure/repositories/`
   - Add a versioned migration in `infrastructure/db/sqlite/migrations/` if schema changed

5. **DI wiring**
   - Register new implementations in `app/di/`

6. **Presentation layer**
   - Create or update ViewModel in `presentation/viewmodels/`
   - Create or update Page in `presentation/pages/`
   - Create reusable widgets in `presentation/widgets/ui/` if needed
   - Implement all four UI states: Loading, Empty, Error, Normal

7. **Tests**
   - Domain: unit test entities and value objects
   - UseCase: unit test with mocked repository
   - Infrastructure: integration test with in-memory SQLite

8. **Pre-commit checks**
   ```
   dart format lib/
   dart analyze
   flutter test
   ```
