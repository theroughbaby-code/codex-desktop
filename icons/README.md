# Editable SVG Icons

These folders are the source of truth for the plugin's SVG artwork:

- `actionicons`: 144 x 144 Keypad action artwork with transparent backgrounds.
- `actionsymbols`: 24 x 24 symbols shown in the Options+ action picker.
- `plugin`: Editable 256 x 256 source artwork for the plugin and application-profile badge.

Keep action filenames unchanged because the Logitech SDK discovers them from the full C# action class name. Builds copy the action SVGs into the package and render the plugin badge to the required 256 x 256 PNG asset.
