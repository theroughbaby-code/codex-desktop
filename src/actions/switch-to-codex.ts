import { CommandAction } from '@logitech/plugin-sdk';

import { switchChatGptDesktopMode } from '../codex/mode-switch';

export class SwitchToCodexAction extends CommandAction {
  name = 'switch_to_codex';
  displayName = 'Codex';
  description = 'Switches the ChatGPT desktop app to Codex mode.';
  groupName = 'Codex Desktop';

  async onKeyDown(): Promise<void> {
    await switchChatGptDesktopMode('Codex');
  }
}
