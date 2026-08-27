import { CommandAction } from '@logitech/plugin-sdk';

import { switchChatGptDesktopMode } from '../codex/mode-switch';

export class SwitchToChatGptAction extends CommandAction {
  name = 'switch_to_chatgpt';
  displayName = 'ChatGPT';
  description = 'Switches the ChatGPT desktop app to ChatGPT mode.';
  groupName = 'Codex Desktop';

  async onKeyDown(): Promise<void> {
    await switchChatGptDesktopMode('ChatGPT');
  }
}
