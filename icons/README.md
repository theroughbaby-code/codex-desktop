# Editable SVG Icons

These folders are the source of truth for the plugin's SVG artwork:

- `actionicons`: 144 x 144 Keypad action artwork with transparent backgrounds.
- `actionsymbols`: 24 x 24 symbols shown in the Options+ action picker.

Keep each filename unchanged because the Logitech SDK discovers artwork from the full C# action class name. Future builds copy these editable files into the standalone plugin package without regenerating or overwriting existing artwork.
