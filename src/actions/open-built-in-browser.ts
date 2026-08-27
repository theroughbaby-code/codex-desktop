import { CommandAction } from '@logitech/plugin-sdk';

import { triggerChatGptShortcut } from '../codex/shortcut';

export class OpenBuiltInBrowserAction extends CommandAction {
  name = 'open_built_in_browser';
  displayName = 'Browser';
  description = 'Focuses ChatGPT/Codex Desktop and opens the built-in browser with Ctrl+Shift+B.';
  groupName = 'Codex Desktop';

  async onKeyDown(): Promise<void> {
    await triggerChatGptShortcut('Browser');
  }
}
