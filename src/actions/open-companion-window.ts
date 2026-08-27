import { CommandAction } from '@logitech/plugin-sdk';

import { triggerChatGptShortcut } from '../codex/shortcut';

export class OpenCompanionWindowAction extends CommandAction {
  name = 'open_companion_window';
  displayName = 'Companion';
  description = 'Opens the ChatGPT companion window with Alt+Space.';
  groupName = 'Codex Desktop';

  async onKeyDown(): Promise<void> {
    await triggerChatGptShortcut('Companion');
  }
}
