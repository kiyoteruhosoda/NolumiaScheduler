# Git Rules

## Commit Format

```
<type>(<scope>): <short summary>

[optional body]
```

Types: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`

Examples:
```
feat(domain): add Word entity and WordId value object
fix(infrastructure): correct WordRow mapper null handling
test(application): add CreateWordUseCase unit tests
```

- Summary line: imperative mood, lowercase, no period, max 72 chars.
- Body: explain *why*, not *what*, when non-obvious.

## Branching

- Feature branches: `feature/<short-description>`
- Fix branches: `fix/<short-description>`
- Never commit directly to `main` or `master`.

## Before Committing

1. `dart format lib/`
2. `dart analyze`
3. `flutter test`
4. All checks must pass — do not bypass with `--no-verify`.

## Pull Requests

- One PR per logical change.
- PR description must include: what changed, why, and how to test.
- Self-review before requesting review.
