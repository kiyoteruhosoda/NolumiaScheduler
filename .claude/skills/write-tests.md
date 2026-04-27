# Skill: Write Tests

## Priority Order

1. Domain tests (highest — always required)
2. UseCase tests
3. Infrastructure tests

## Domain Tests

Path: `test/domain/`

What to test:
- Entity construction and validation
- Value Object equality and invalid input rejection
- Domain Service logic

```dart
void main() {
  group('WordId', () {
    test('is equal when value matches', () {
      expect(WordId('abc'), equals(WordId('abc')));
    });

    test('throws on empty value', () {
      expect(() => WordId(''), throwsA(isA<AssertionError>()));
    });
  });
}
```

## UseCase Tests

Path: `test/application/`

Pattern:
- Mock the repository with `mocktail` or `mockito`
- Test: happy path, domain error, unexpected error

```dart
void main() {
  late MockWordRepository repository;
  late CreateWordUseCase useCase;

  setUp(() {
    repository = MockWordRepository();
    useCase = CreateWordUseCase(repository);
  });

  test('saves word to repository', () async {
    when(() => repository.save(any())).thenAnswer((_) async {});
    await useCase.execute(CreateWordInput(id: 'id1', text: 'hello'));
    verify(() => repository.save(any())).called(1);
  });
}
```

## Infrastructure Tests

Path: `test/infrastructure/`

Pattern:
- Use an in-memory SQLite database (`:memory:`)
- Test actual SQL through the DAO and Repository implementation
- Test mapper round-trips (domain → row → domain)

## Rules

- No business logic tested only through widget/integration tests
- Every new UseCase must have a corresponding test file
- Every new Entity / Value Object must have a corresponding test file
