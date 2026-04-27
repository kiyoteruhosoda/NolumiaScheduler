# Skill: Add a Reusable UI Widget

## Steps

1. **Confirm the Figma component exists**
   - Widget must correspond to a component defined in Figma (`01_Components` or `02_Patterns`)
   - If it doesn't exist in Figma, create it there first

2. **Create the file**
   - Path: `lib/presentation/widgets/ui/<widget_name>.dart`
   - Name pattern: `App<ComponentName>` (e.g., `AppPrimaryButton`, `AppCard`)

3. **Implement using design tokens only**
   - Colors: `AppColors.*`
   - Spacing: `AppSpacing.*`
   - Radius: `AppRadius.*`
   - Typography: `AppTextStyles.*`
   - No hardcoded values

4. **Respect tap target minimum**
   - Interactive widgets must have a minimum touch area of 48dp

5. **Support dark mode**
   - Use `Theme.of(context)` or token classes that resolve per theme
   - Test in both light and dark mode

6. **Widget skeleton**
   ```dart
   class AppPrimaryButton extends StatelessWidget {
     const AppPrimaryButton({
       super.key,
       required this.label,
       required this.onPressed,
       this.isLoading = false,
     });

     final String label;
     final VoidCallback? onPressed;
     final bool isLoading;

     @override
     Widget build(BuildContext context) {
       return SizedBox(
         height: 48,
         child: ElevatedButton(
           onPressed: isLoading ? null : onPressed,
           child: isLoading
               ? const CircularProgressIndicator()
               : Text(label, style: AppTextStyles.button),
         ),
       );
     }
   }
   ```

7. **No business logic in widgets**
   - Widgets receive data and callbacks; they do not call UseCases directly
