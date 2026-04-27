# Skill: Add a Repository Interface + SQLite Implementation

## Steps

1. **Define the interface in Domain**
   - Path: `lib/domain/repositories/<name>_repository.dart`
   - Only domain types in the signature; no SQLite types

   ```dart
   abstract interface class WordRepository {
     Future<void> save(Word word);
     Future<Word?> findById(WordId id);
     Future<List<Word>> findAll();
     Future<void> delete(WordId id);
   }
   ```

2. **Create the Row model**
   - Path: `lib/infrastructure/db/sqlite/rows/<name>_row.dart`
   - Plain Dart class matching the SQLite table columns
   - Has `fromMap(Map<String, dynamic>)` and `toMap()` methods

3. **Create the DAO**
   - Path: `lib/infrastructure/db/sqlite/dao/<name>_dao.dart`
   - Handles all SQL for this entity
   - Returns `*Row` objects; no domain types

4. **Create the Mapper**
   - Path: `lib/infrastructure/mappers/<name>_mapper.dart`
   - Converts `*Row` ↔ domain Entity

5. **Create the Repository implementation**
   - Path: `lib/infrastructure/repositories/sqlite_<name>_repository.dart`
   - Implements the domain interface
   - Uses DAO + Mapper; no raw SQL here

   ```dart
   final class SqliteWordRepository implements WordRepository {
     const SqliteWordRepository(this._dao, this._mapper);
     final WordDao _dao;
     final WordMapper _mapper;

     @override
     Future<Word?> findById(WordId id) async {
       final row = await _dao.findById(id.value);
       return row != null ? _mapper.toDomain(row) : null;
     }
     // ...
   }
   ```

6. **Add migration if schema changed**
   - Path: `lib/infrastructure/db/sqlite/migrations/v<N>_<description>.dart`
   - Increment the database version

7. **Wire in DI**
   - Bind `WordRepository` → `SqliteWordRepository` in `app/di/`
