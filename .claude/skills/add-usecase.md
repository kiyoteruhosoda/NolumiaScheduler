# Skill: Add a UseCase

Use this skill when adding a single business operation to the Application layer.

## Steps

1. **Name the UseCase**
   - Pattern: `<Verb><Noun>UseCase` (e.g., `CreateWordUseCase`, `DeleteStudySessionUseCase`)

2. **Create the file**
   - Path: `lib/application/usecases/<name_use_case>.dart`

3. **Define DTOs if needed**
   - Input DTO: `lib/application/dto/<noun>_input.dart`
   - Output DTO: `lib/application/dto/<noun>_output.dart`
   - DTOs are plain Dart classes; no Flutter or infrastructure imports

4. **Implement the UseCase class**
   ```dart
   final class CreateWordUseCase {
     const CreateWordUseCase(this._repository);
     final WordRepository _repository;

     Future<void> execute(CreateWordInput input) async {
       final word = Word(
         id: WordId(input.id),
         // ...
       );
       await _repository.save(word);
     }
   }
   ```
   - Constructor-inject all dependencies
   - Single public method: `execute(input)`
   - No UI imports, no infrastructure imports

5. **Wire in DI**
   - Add to `app/di/`

6. **Test**
   - Path: `test/application/usecases/<name_use_case>_test.dart`
   - Mock the repository using `mockito` or `mocktail`
   - Test: happy path, domain error path, unexpected error path
