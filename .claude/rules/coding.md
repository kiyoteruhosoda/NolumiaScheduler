# Coding Rules

Applies to all Dart files.

## General

- Prefer `final` over `var` everywhere possible.
- Prefer named parameters for constructors with 2+ parameters.
- Never suppress lint warnings without a comment explaining why.
- No dead code; remove unused imports, fields, and methods.

## Error Handling

- Separate expected domain errors from unexpected system failures.
- Domain errors: typed exceptions or sealed classes defined in `domain/` or `shared/errors/`.
- Domain errors must not contain UI strings (no `'Something went wrong'` in domain layer).
- Unexpected errors bubble up and are handled at the ViewModel or app boundary.

## ViewModels

- One ViewModel per page/screen.
- ViewModel responsibilities: call UseCases, manage loading state, handle errors, expose display data.
- ViewModels must not contain business logic.
- ViewModels must not import infrastructure classes directly.

## Testing

- Domain layer tests are highest priority — always write them.
- UseCase tests use mocked repositories.
- Infrastructure tests use integration tests against a real (in-memory) SQLite database.
- No business logic may be tested only through UI tests.

## Formatting

- Run `dart format` before committing.
- Run `dart analyze` and fix all errors and warnings before committing.
