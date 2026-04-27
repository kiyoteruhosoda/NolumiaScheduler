# Skill: Add a Domain Entity and Value Objects

## Steps

1. **Define Value Objects first**
   - Path: `lib/domain/value_objects/<name>.dart`
   - Must be immutable (`final` fields, no setters)
   - Override `==` and `hashCode`
   - Validate in constructor; throw a domain exception on invalid input

   ```dart
   final class WordId {
     const WordId(this.value) : assert(value.length > 0);
     final String value;

     @override
     bool operator ==(Object other) =>
         other is WordId && other.value == value;

     @override
     int get hashCode => value.hashCode;
   }
   ```

2. **Define the Entity**
   - Path: `lib/domain/entities/<name>.dart`
   - Entity has identity via its ID value object
   - Fields are `final`; produce a new instance for mutations (`copyWith`)

   ```dart
   final class Word {
     const Word({required this.id, required this.text});
     final WordId id;
     final String text;

     Word copyWith({WordId? id, String? text}) =>
         Word(id: id ?? this.id, text: text ?? this.text);
   }
   ```

3. **No Flutter / infrastructure imports** in either file

4. **Tests**
   - Path: `test/domain/entities/<name>_test.dart`
   - Test: construction, equality, validation errors, copyWith
