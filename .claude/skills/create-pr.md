# Skill: Create a Pull Request

## Steps

1. **Pre-flight checks**
   ```bash
   dart format lib/
   dart analyze
   flutter test
   ```
   All must pass.

2. **Review your diff**
   ```bash
   git diff main...HEAD
   ```
   Confirm only intended changes are included.

3. **Commit (if not already done)**
   ```bash
   git add <specific files>
   git commit -m "feat(scope): summary"
   ```

4. **Push branch**
   ```bash
   git push -u origin <branch-name>
   ```

5. **Create PR with `gh`**
   ```bash
   gh pr create \
     --title "<type>(<scope>): <summary>" \
     --body "$(cat <<'EOF'
   ## What changed
   - ...

   ## Why
   - ...

   ## How to test
   - [ ] ...
   EOF
   )"
   ```

## PR Checklist

- [ ] Title follows commit format: `type(scope): summary`
- [ ] All four UI states handled (if UI changes)
- [ ] Domain tests written
- [ ] UseCase tests written
- [ ] `dart analyze` clean
- [ ] `flutter test` passing
- [ ] No hardcoded spacing or color values
- [ ] No infrastructure imports in domain or application layers
